using System.Collections.Concurrent;
using Keystream.Core.Localization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Keystream.Actions.Obs;

/// <summary>Transport seam so the obs-websocket protocol state machine can
/// be tested with a scripted fake (the real adapter is
/// <see cref="ClientWebSocketTransport"/>).</summary>
public interface IObsTransport : IDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellation);
    Task SendAsync(string json, CancellationToken cancellation);
    Task<string?> ReceiveAsync(CancellationToken cancellation);
    Task CloseAsync();
}

public sealed record ObsResponse(bool Success, JsonObject? Data, string? Error);

/// <summary>
/// K6 - a real obs-websocket v5 client. Written against the protocol, not
/// against hope (the legacy ObsService, finding B2-3: computed no auth at
/// all, announced "connected" before Identify, and never read a single
/// response). This client:
///  - completes the Hello → Identify → Identified handshake, including the
///    two-stage SHA-256 authentication when the server requires it;
///  - reports IsConnected ONLY after Identified arrives;
///  - correlates every request with its response by requestId and times
///    out honestly instead of assuming success;
///  - faults all pending requests if the connection drops.
/// Protocol: op 0 Hello, op 1 Identify, op 2 Identified, op 5 Event,
/// op 6 Request, op 7 RequestResponse.
/// </summary>
public sealed class ObsClient : IDisposable
{
    private readonly Func<IObsTransport> _transportFactory;
    private IObsTransport? _transport;
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoop;

    private TaskCompletionSource<JsonObject>? _helloTcs;
    private TaskCompletionSource<bool>? _identifiedTcs;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();

    private bool _isConnected;
    public bool IsConnected => _isConnected;
    public string? LastError { get; private set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(6);

    public event Action<string, JsonObject>? ObsEventReceived;

    /// <summary>§148: raised exactly when <see cref="IsConnected"/>
    /// changes (false→true on successful connect, true→false on
    /// disconnect and on every fault/transport-death path). Raised
    /// on the thread that changed the state (the receive loop for
    /// transport death); subscribers that touch UI must marshal to
    /// the UI thread. No event fires when the value does not
    /// change.</summary>
    public event Action? ConnectionChanged;

    /// <summary>Single place that mutates the connected flag, so the
    /// transition rule above cannot drift between call sites.</summary>
    private void SetConnected(bool value)
    {
        if (_isConnected == value) return;
        _isConnected = value;
        ConnectionChanged?.Invoke();
    }

    public ObsClient() : this(() => new ClientWebSocketTransport()) { }

    public ObsClient(Func<IObsTransport> transportFactory) => _transportFactory = transportFactory;

    /// <summary>The obs-websocket authentication string:
    /// secret = Base64(SHA256(password + salt));
    /// auth   = Base64(SHA256(secret + challenge)).
    /// Concatenations are plain string concatenations of the base64/utf8
    /// forms, per the protocol specification.</summary>
    public static string ComputeAuthentication(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public async Task<bool> ConnectAsync(string host, string password, CancellationToken cancellation = default)
    {
        if (IsConnected) return true;
        LastError = null;
        DisconnectInternal(faultPending: false);

        _transport = _transportFactory();
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = _loopCts.Token;

        _helloTcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _identifiedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ConnectTimeout);

            await _transport.ConnectAsync(new Uri(host), timeout.Token).ConfigureAwait(false);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(token), token);

            JsonObject hello = await _helloTcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

            var identify = new JsonObject
            {
                ["rpcVersion"] = 1,
                ["eventSubscriptions"] = 0,
            };
            if (hello["authentication"] is JsonObject auth
                && auth["challenge"]?.GetValue<string>() is string challenge
                && auth["salt"]?.GetValue<string>() is string salt)
            {
                if (string.IsNullOrEmpty(password))
                    throw new InvalidOperationException(Localizer.Get("S1191"));
                identify["authentication"] = ComputeAuthentication(password, salt, challenge);
            }

            await SendRawAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, timeout.Token).ConfigureAwait(false);
            await _identifiedTcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

            SetConnected(true);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex is OperationCanceledException
                ? Localizer.Get("S1192")
                : Localizer.Format("S1193", ex.Message);
            DisconnectInternal(faultPending: true);
            return false;
        }
    }

    public async Task<ObsResponse> RequestAsync(string requestType, JsonObject? requestData = null,
        CancellationToken cancellation = default)
    {
        if (!IsConnected || _transport is null)
            return new ObsResponse(false, null, Localizer.Get("S1194"));

        string requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        var message = new JsonObject
        {
            ["op"] = 6,
            ["d"] = new JsonObject
            {
                ["requestType"] = requestType,
                ["requestId"] = requestId,
                ["requestData"] = requestData ?? new JsonObject(),
            },
        };

        try
        {
            await SendRawAsync(message, cancellation).ConfigureAwait(false);
            JsonObject d = await tcs.Task.WaitAsync(RequestTimeout, cancellation).ConfigureAwait(false);
            var status = d["requestStatus"] as JsonObject;
            bool result = status?["result"]?.GetValue<bool>() ?? false;
            if (!result)
            {
                string comment = status?["comment"]?.GetValue<string>() ?? Localizer.Get("S1195");
                int code = status?["code"]?.GetValue<int>() ?? -1;
                return new ObsResponse(false, null, Localizer.Format("S1196", requestType, code, comment));
            }
            return new ObsResponse(true, d["responseData"] as JsonObject, null);
        }
        catch (TimeoutException)
        {
            return new ObsResponse(false, null, Localizer.Format("S1197", RequestTimeout.TotalSeconds, requestType));
        }
        catch (Exception ex)
        {
            return new ObsResponse(false, null, Localizer.Format("S1198", requestType, ex.Message));
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    // --- Live discovery (UI v2.1): real lists from the connected OBS, so
    // the tile editor can offer dropdowns instead of free-typed names. ---

    /// <summary>Scene names from GetSceneList, in the order OBS returns.</summary>
    public static IReadOnlyList<string> ParseSceneNames(JsonObject? responseData)
    {
        var names = new List<string>();
        if (responseData?["scenes"] is JsonArray scenes)
            foreach (var item in scenes)
                if (item is JsonObject scene && scene["sceneName"]?.GetValue<string>() is { Length: > 0 } name)
                    names.Add(name);
        return names;
    }

    /// <summary>Input ("source") names from GetInputList - the valid
    /// targets for ToggleMute and friends.</summary>
    public static IReadOnlyList<string> ParseInputNames(JsonObject? responseData)
    {
        var names = new List<string>();
        if (responseData?["inputs"] is JsonArray inputs)
            foreach (var item in inputs)
                if (item is JsonObject input && input["inputName"]?.GetValue<string>() is { Length: > 0 } name)
                    names.Add(name);
        return names;
    }

    public async Task<(IReadOnlyList<string> Names, string? Error)> GetSceneNamesAsync(
        CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetSceneList", null, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseSceneNames(r.Data), null) : (Array.Empty<string>(), r.Error);
    }

    public async Task<(IReadOnlyList<string> Names, string? Error)> GetInputNamesAsync(
        CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetInputList", null, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseInputNames(r.Data), null) : (Array.Empty<string>(), r.Error);
    }

    /// <summary>Inputs with their kinds - the editor filters text inputs
    /// (kinds containing "text") for the overlay-text command.</summary>
    public static IReadOnlyList<(string Name, string Kind)> ParseInputs(JsonObject? responseData)
    {
        var list = new List<(string, string)>();
        if (responseData?["inputs"] is JsonArray inputs)
            foreach (var item in inputs)
                if (item is JsonObject input && input["inputName"]?.GetValue<string>() is { Length: > 0 } name)
                    list.Add((name, input["inputKind"]?.GetValue<string>() ?? ""));
        return list;
    }

    public async Task<(IReadOnlyList<(string Name, string Kind)> Inputs, string? Error)> GetInputsAsync(
        CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetInputList", null, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseInputs(r.Data), null) : (Array.Empty<(string, string)>(), r.Error);
    }

    /// <summary>Source names inside one scene (GetSceneItemList) - the
    /// valid second-level targets for show/hide scene item.</summary>
    public static IReadOnlyList<string> ParseSceneItemNames(JsonObject? responseData)
    {
        var names = new List<string>();
        if (responseData?["sceneItems"] is JsonArray items)
            foreach (var item in items)
                if (item is JsonObject sceneItem && sceneItem["sourceName"]?.GetValue<string>() is { Length: > 0 } name)
                    names.Add(name);
        return names;
    }

    public async Task<(IReadOnlyList<string> Names, string? Error)> GetSceneItemNamesAsync(
        string sceneName, CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = sceneName }, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseSceneItemNames(r.Data), null) : (Array.Empty<string>(), r.Error);
    }

    /// <summary>Filter names on one input (GetSourceFilterList).</summary>
    public static IReadOnlyList<string> ParseFilterNames(JsonObject? responseData)
    {
        var names = new List<string>();
        if (responseData?["filters"] is JsonArray filters)
            foreach (var item in filters)
                if (item is JsonObject filter && filter["filterName"]?.GetValue<string>() is { Length: > 0 } name)
                    names.Add(name);
        return names;
    }

    public async Task<(IReadOnlyList<string> Names, string? Error)> GetFilterNamesAsync(
        string inputName, CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetSourceFilterList", new JsonObject { ["sourceName"] = inputName }, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseFilterNames(r.Data), null) : (Array.Empty<string>(), r.Error);
    }

    public static IReadOnlyList<string> ParseSceneCollectionNames(JsonObject? responseData)
    {
        var names = new List<string>();
        if (responseData?["sceneCollections"] is JsonArray collections)
            foreach (var item in collections)
                if (item is JsonObject collection && collection["sceneCollectionName"]?.GetValue<string>() is { Length: > 0 } name)
                    names.Add(name);
        return names;
    }

    public async Task<(IReadOnlyList<string> Names, string? Error)> GetSceneCollectionNamesAsync(
        CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetSceneCollectionList", null, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseSceneCollectionNames(r.Data), null) : (Array.Empty<string>(), r.Error);
    }

    public static IReadOnlyList<string> ParseProfileNames(JsonObject? responseData)
    {
        var names = new List<string>();
        if (responseData?["profiles"] is JsonArray profiles)
            foreach (var item in profiles)
                if (item?.GetValue<string>() is { Length: > 0 } name)
                    names.Add(name);
        return names;
    }

    public async Task<(IReadOnlyList<string> Names, string? Error)> GetProfileNamesAsync(
        CancellationToken cancellation = default)
    {
        var r = await RequestAsync("GetProfileList", null, cancellation).ConfigureAwait(false);
        return r.Success ? (ParseProfileNames(r.Data), null) : (Array.Empty<string>(), r.Error);
    }

    private async Task SendRawAsync(JsonObject message, CancellationToken cancellation)
    {
        if (_transport is null) throw new InvalidOperationException("Transport belum ada.");
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
                if (JsonNode.Parse(json) is not JsonObject root) continue;
                int op = root["op"]?.GetValue<int>() ?? -1;
                var d = root["d"] as JsonObject ?? new JsonObject();

                switch (op)
                {
                    case 0: _helloTcs?.TrySetResult(d); break;
                    case 2:
                        _identifiedTcs?.TrySetResult(true);
                        break;
                    case 5:
                        ObsEventReceived?.Invoke(d["eventType"]?.GetValue<string>() ?? "", d["eventData"] as JsonObject ?? new JsonObject());
                        break;
                    case 7:
                        string? id = d["requestId"]?.GetValue<string>();
                        if (id is not null && _pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(d);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            LastError = Localizer.Format("S1199", ex.Message);
        }
        finally
        {
            SetConnected(false);
            _helloTcs?.TrySetException(new InvalidOperationException(Localizer.Get("S1200")));
            _identifiedTcs?.TrySetException(new InvalidOperationException(Localizer.Get("S1200")));
            foreach (var kv in _pending)
                kv.Value.TrySetException(new InvalidOperationException(Localizer.Get("S1200")));
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
