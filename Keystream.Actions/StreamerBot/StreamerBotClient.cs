using System.Collections.Concurrent;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;

namespace Keystream.Actions.StreamerBot;

/// <summary>Transport seam so the Streamer.bot protocol state machine can
/// be tested with a scripted fake (the real adapter is
/// <see cref="StreamerBotWebSocketTransport"/>). Mirrors
/// <c>Keystream.Actions.Obs.IObsTransport</c>.</summary>
public interface IStreamerBotTransport : IDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellation);
    Task SendAsync(string json, CancellationToken cancellation);
    Task<string?> ReceiveAsync(CancellationToken cancellation);
    Task CloseAsync();
}

public sealed record StreamerBotResponse(bool Success, string? Error);

public sealed record StreamerBotActionInfo(string Name, string? Id);

/// <summary>
/// §147 - an outbound-only Streamer.bot WebSocket client (DoAction /
/// GetActions). Written against the official docs, not against hope:
///
/// Docs (verified 2026-10-28, see ~/workspace/keystream-147-audit.md):
/// - Requests: https://docs.streamer.bot/api/websocket/requests -
///   base schema {"request", "id"}; "id" identifies request/response
///   pairs; DoAction {"request":"DoAction","action":{"id"|"name"},
///   "args":{...},"id"}; GetActions {"request":"GetActions","id"}.
/// - Client guide: https://docs.streamer.bot/api/websocket/guide/client
///   - defaults host 127.0.0.1, port 8080, endpoint "/"; doAction
///   accepts a bare Guid string (id) or {name}.
/// - Authentication: https://docs.streamer.bot/api/websocket/guide/authentication
///   - on connect the server sends a "Hello" message; when auth is
///   enabled it carries an "authentication" object with salt +
///   challenge, and the client must answer with an "Authenticate"
///   request whose string is Base64(SHA256(Base64(SHA256(password +
///   salt)) + challenge)).
///
/// HONEST-FAIL AUTH (v1): the docs prove the Authenticate framing and
/// the hash, but they document NO reply to Authenticate - nothing
/// proves what confirms (or rejects) authentication. Guessing that
/// reply would let a wrong password look connected, so v1 sends NO
/// Authenticate frame at all: when the Hello message carries an
/// "authentication" object, ConnectAsync returns false with S1759
/// (turn auth off in Streamer.bot's WebSocket Server settings). The
/// password parameter is accepted for API symmetry with the settings
/// and is never sent, logged, or included in any error text. The
/// common no-auth case (no "authentication" object in Hello) connects
/// normally. Do NOT "complete" this handshake without a PC proving
/// the real server's Authenticate reply against a real Streamer.bot.
///
/// REPLY SEMANTICS (RISKY, PC-unproven): the docs document NO response
/// shape for DoAction/GetActions either - no status field, no field
/// names for the GetActions list. This client therefore treats a
/// correlated reply (same "id") as success UNLESS it carries an
/// explicit failure signal (status "error"/"failed"/"fail", success
/// false, or a non-empty "error" string), and ParseActions accepts
/// "actions" / "data" containers with per-item "name"/"id" (also
/// "Name"/"Id"). If the real server uses other field names, PC
/// verification must extend ParseActions - never assume a list.
///
/// Behaviour (mirrors ObsClient): IsConnected is true ONLY after the
/// Hello handshake completes; every request is correlated by "id"
/// and times out honestly; a dropped connection faults all pending
/// requests. No Subscribe/event handling anywhere (§147 is outbound
/// only); unsolicited messages without a pending "id" are ignored.
/// </summary>
public sealed class StreamerBotClient : IDisposable
{
    private readonly Func<IStreamerBotTransport> _transportFactory;
    private IStreamerBotTransport? _transport;
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoop;

    private TaskCompletionSource<JsonObject>? _helloTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();

    private bool _isConnected;
    public bool IsConnected => _isConnected;
    public string? LastError { get; private set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>§148: raised exactly when <see cref="IsConnected"/>
    /// changes (false→true on successful connect, true→false on
    /// disconnect and on every fault/transport-death path). Raised
    /// on the thread that changed the state (the receive loop for
    /// transport death); subscribers that touch UI must marshal to
    /// the UI thread. No event fires when the value does not change
    /// - including the §147 honest-auth-refusal path, which leaves
    /// IsConnected false throughout.</summary>
    public event Action? ConnectionChanged;

    /// <summary>Single place that mutates the connected flag, so the
    /// transition rule above cannot drift between call sites.</summary>
    private void SetConnected(bool value)
    {
        if (_isConnected == value) return;
        _isConnected = value;
        ConnectionChanged?.Invoke();
    }

    public StreamerBotClient() : this(() => new StreamerBotWebSocketTransport()) { }

    public StreamerBotClient(Func<IStreamerBotTransport> transportFactory) => _transportFactory = transportFactory;

    /// <summary>Builds the DoAction request frame. A value that parses
    /// as a Guid is sent as {"id": value}; anything else as
    /// {"name": value} - the docs' client guide shows doAction with a
    /// bare Guid string and with {name: ...}, and the requests page
    /// shows an "action" object carrying either key (its worked
    /// example sends name only). Args are deep-cloned so a caller
    /// object that already has a parent never throws on assignment;
    /// null args become an empty object.</summary>
    public static JsonObject BuildDoActionRequest(string id, string actionNameOrId, JsonObject? args)
    {
        string value = actionNameOrId ?? "";
        JsonObject action = Guid.TryParse(value, out _)
            ? new JsonObject { ["id"] = value }
            : new JsonObject { ["name"] = value };
        return new JsonObject
        {
            ["request"] = "DoAction",
            ["id"] = id,
            ["action"] = action,
            ["args"] = args is null ? new JsonObject() : (JsonNode)args.DeepClone(),
        };
    }

    /// <summary>Builds the GetActions request frame:
    /// {"request":"GetActions","id": id}.</summary>
    public static JsonObject BuildGetActionsRequest(string id) => new()
    {
        ["request"] = "GetActions",
        ["id"] = id,
    };

    /// <summary>Parses a GetActions payload into (Name, Id?) pairs.
    /// NEVER throws and NEVER invents entries: null, a missing array,
    /// wrong types, or a malformed item all yield an empty list (the
    /// call-site error, if any, comes from the reply's failure signal
    /// in <see cref="RequestAsync"/>, never from here). Accepted
    /// containers: a bare array, an object with an "actions" array,
    /// or an object with a "data" array / "data" object holding an
    /// "actions" array. Items without a non-empty "name" are skipped;
    /// a missing/blank "id" becomes a null Id.</summary>
    public static IReadOnlyList<StreamerBotActionInfo> ParseActions(JsonNode? payload)
    {
        try
        {
            JsonArray? array = payload switch
            {
                JsonArray a => a,
                JsonObject o => FindActionsArray(o),
                _ => null,
            };
            if (array is null) return Array.Empty<StreamerBotActionInfo>();
            var list = new List<StreamerBotActionInfo>();
            foreach (var item in array)
            {
                if (item is not JsonObject obj) continue;
                string? name = TryString(obj, "name") ?? TryString(obj, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                string? id = TryString(obj, "id") ?? TryString(obj, "Id");
                list.Add(new StreamerBotActionInfo(name, string.IsNullOrWhiteSpace(id) ? null : id));
            }
            return list;
        }
        catch
        {
            // Malformed payload: honest empty list, never a throw.
            return Array.Empty<StreamerBotActionInfo>();
        }
    }

    private static JsonArray? FindActionsArray(JsonObject obj)
    {
        if (obj["actions"] is JsonArray actions) return actions;
        if (obj["data"] is JsonArray dataArray) return dataArray;
        if (obj["data"] is JsonObject dataObj && dataObj["actions"] is JsonArray nested) return nested;
        return null;
    }

    private static string? TryString(JsonObject obj, string property)
        => obj[property] is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;

    /// <summary>Connects to ws://host:port + endpoint. Endpoint "/"
    /// by default; a value with or without a leading slash is
    /// tolerated and the result never contains "//". Returns false
    /// (never throws) with <see cref="LastError"/> set on every
    /// failure: S1758 on timeout, S1759 when the server demands
    /// authentication (see the class doc - honest-fail, v1), S1752
    /// for any other connect failure. <see cref="LastError"/> never
    /// contains the password.</summary>
    public async Task<bool> ConnectAsync(string host, int port, string endpoint, string password,
        CancellationToken cancellation = default)
    {
        if (IsConnected) return true;
        // The password is deliberately never sent: v1 has no proven
        // Authenticate reply to wait for (see class doc). It is
        // accepted only so the frozen signature matches the settings.
        _ = password;
        LastError = null;
        DisconnectInternal(faultPending: false);

        _transport = _transportFactory();
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = _loopCts.Token;

        _helloTcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ConnectTimeout);

            await _transport.ConnectAsync(BuildUri(host, port, endpoint), timeout.Token).ConfigureAwait(false);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(token), token);

            JsonObject hello = await _helloTcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

            // Auth required: the docs' Hello carries an "authentication"
            // object (salt + challenge) only when auth is enabled.
            // No proven reply exists for Authenticate, so refuse
            // honestly instead of guessing the handshake (S1759).
            if (hello["authentication"] is JsonObject)
            {
                LastError = Localizer.Get("S1759");
                DisconnectInternal(faultPending: true);
                return false;
            }

            SetConnected(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            LastError = Localizer.Get("S1758");
            DisconnectInternal(faultPending: true);
            return false;
        }
        catch (Exception)
        {
            LastError = Localizer.Get("S1752");
            DisconnectInternal(faultPending: true);
            return false;
        }
    }

    private static Uri BuildUri(string host, int port, string endpoint)
    {
        string cleanHost = (host ?? "").Trim().TrimEnd('/');
        if (cleanHost.Length == 0) throw new ArgumentException("Host kosong.", nameof(host));
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        string path = string.IsNullOrWhiteSpace(endpoint) ? "/" : endpoint.Trim();
        while (path.StartsWith("//", StringComparison.Ordinal)) path = path[1..];
        if (!path.StartsWith('/')) path = "/" + path;
        return new UriBuilder(Uri.UriSchemeWs, cleanHost, port, path).Uri;
    }

    /// <summary>Runs one action by name or Guid id. Success requires a
    /// correlated reply with no explicit failure signal (see class
    /// doc); timeout / disconnect / not-connected all return
    /// Success=false with a named, localized error and set
    /// <see cref="LastError"/>.</summary>
    public async Task<StreamerBotResponse> DoActionAsync(string actionNameOrId, JsonObject? args,
        CancellationToken cancellation = default)
    {
        if (!IsConnected || _transport is null)
        {
            LastError = Localizer.Get("S1757");
            return new StreamerBotResponse(false, LastError);
        }
        if (string.IsNullOrWhiteSpace(actionNameOrId))
        {
            LastError = Localizer.Get("S1752");
            return new StreamerBotResponse(false, LastError);
        }

        var request = BuildDoActionRequest(Guid.NewGuid().ToString("N"), actionNameOrId, args);
        var (_, error) = await RequestAsync(request, cancellation).ConfigureAwait(false);
        if (error is not null)
        {
            LastError = error;
            return new StreamerBotResponse(false, error);
        }
        return new StreamerBotResponse(true, null);
    }

    /// <summary>Fetches the action list. The list itself comes from
    /// <see cref="ParseActions"/> (empty on malformed payloads); the
    /// Error, when present, comes from the reply's failure signal or
    /// from the transport (timeout / disconnect / not connected).</summary>
    public async Task<(IReadOnlyList<StreamerBotActionInfo> Actions, string? Error)> GetActionsAsync(
        CancellationToken cancellation = default)
    {
        if (!IsConnected || _transport is null)
        {
            LastError = Localizer.Get("S1757");
            return (Array.Empty<StreamerBotActionInfo>(), LastError);
        }

        var request = BuildGetActionsRequest(Guid.NewGuid().ToString("N"));
        var (reply, error) = await RequestAsync(request, cancellation).ConfigureAwait(false);
        if (error is not null)
        {
            LastError = error;
            return (Array.Empty<StreamerBotActionInfo>(), error);
        }
        return (ParseActions(reply), null);
    }

    /// <summary>One correlated request: send, wait for the reply with
    /// the same "id" (or fault on disconnect), reject explicit
    /// failure signals. Returns (reply, null) on success,
    /// (null, error) on every failure - never throws for transport
    /// reasons.</summary>
    private async Task<(JsonObject? Reply, string? Error)> RequestAsync(JsonObject request,
        CancellationToken cancellation)
    {
        if (!IsConnected || _transport is null)
            return (null, Localizer.Get("S1757"));

        string id = TryString(request, "id") is { Length: > 0 } existing
            ? existing
            : Guid.NewGuid().ToString("N");
        request["id"] = id;

        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await SendRawAsync(request, cancellation).ConfigureAwait(false);
            JsonObject reply = await tcs.Task.WaitAsync(RequestTimeout, cancellation).ConfigureAwait(false);
            if (IsFailureReply(reply, out string? failure))
                return (null, failure);
            return (reply, null);
        }
        catch (TimeoutException)
        {
            return (null, Localizer.Get("S1758"));
        }
        catch (OperationCanceledException)
        {
            return (null, Localizer.Get("S1758"));
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            return (null, string.IsNullOrWhiteSpace(message) ? Localizer.Get("S1752") : message);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Explicit failure signals only (the docs define no
    /// status vocabulary - this is the RISKY assumption recorded in
    /// the class doc). Anything else counts as acceptance.</summary>
    private static bool IsFailureReply(JsonObject reply, out string? error)
    {
        error = null;
        string? status = TryString(reply, "status");
        bool statusFailed = status is not null
            && (status.Equals("error", StringComparison.OrdinalIgnoreCase)
                || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                || status.Equals("fail", StringComparison.OrdinalIgnoreCase));
        bool successFalse = reply["success"] is JsonValue successValue
            && successValue.TryGetValue<bool>(out bool success) && !success;
        string? errorText = TryString(reply, "error");
        if (!statusFailed && !successFalse && string.IsNullOrWhiteSpace(errorText)) return false;
        error = !string.IsNullOrWhiteSpace(errorText)
            ? errorText
            : TryString(reply, "message") ?? TryString(reply, "reason") ?? Localizer.Get("S1752");
        return true;
    }

    private async Task SendRawAsync(JsonObject message, CancellationToken cancellation)
    {
        if (_transport is null) throw new InvalidOperationException(Localizer.Get("S1757"));
        await _transport.SendAsync(message.ToJsonString(), cancellation).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? json = await _transport!.ReceiveAsync(token).ConfigureAwait(false);
                if (json is null) break; // closed by peer
                JsonNode? node;
                try { node = JsonNode.Parse(json); }
                catch { continue; } // malformed frame: ignore, keep listening
                if (node is not JsonObject root) continue;

                // Hello has no "id" - it is the handshake, not a reply.
                if (TryString(root, "request") == "Hello")
                {
                    _helloTcs?.TrySetResult(root);
                    continue;
                }

                string? id = TryString(root, "id");
                if (id is not null && _pending.TryRemove(id, out var tcs))
                    tcs.TrySetResult(root);
                // Anything else (unsolicited / event-shaped) is
                // ignored: §147 never subscribes.
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception)
        {
            LastError = Localizer.Get("S1757");
        }
        finally
        {
            SetConnected(false);
            _helloTcs?.TrySetException(new InvalidOperationException(Localizer.Get("S1757")));
            foreach (var kv in _pending)
                kv.Value.TrySetException(new InvalidOperationException(Localizer.Get("S1757")));
            _pending.Clear();
        }
    }

    public void Disconnect() => DisconnectInternal(faultPending: true);

    private void DisconnectInternal(bool faultPending)
    {
        SetConnected(false);
        try { _loopCts?.Cancel(); } catch { }
        try { _transport?.Dispose(); } catch { }
        _transport = null;
        if (!faultPending) _pending.Clear();
    }

    public void Dispose() => DisconnectInternal(faultPending: true);
}
