using System.Text.Json.Nodes;
using Keystream.Actions.Common;
using Keystream.Core.Actions;
using Keystream.Core.Localization;

namespace Keystream.Actions.StreamerBot;

/// <summary>
/// §147 - Streamer.bot control through <see cref="StreamerBotClient"/>:
/// run one action by name or ID, with optional JSON-object arguments.
/// Outbound only: no event subscription code exists in this file (or in
/// the client). Connection settings come from the host via a provider
/// delegate; if Streamer.bot is not connected the action connects once
/// with the CURRENT settings and reports honestly, mirroring
/// <see cref="Obs.ObsAction"/>. Params: "action" (name or ID, required)
/// and "args" (a JSON object string, optional; empty = no arguments).
/// </summary>
public sealed class StreamerBotAction : IAction
{
    private readonly StreamerBotClient _client;
    private readonly Func<(string Host, int Port, string Endpoint, string Password)> _settings;

    public StreamerBotAction(
        StreamerBotClient client,
        Func<(string Host, int Port, string Endpoint, string Password)> settings)
    {
        _client = client;
        _settings = settings;
    }

    public ActionDescriptor Descriptor => new("streamerbot", Localizer.Get("S1743"), Localizer.Get("S1089"),
        Localizer.Get("S1750"));

    private static string? P(JsonObject p, params string[] keys) => ParamReader.GetString(p, keys);

    /// <summary>Parses the free-text "args" field. Null/whitespace means
    /// no arguments (args null, true). Anything else must be a JSON
    /// object; invalid JSON, arrays, and scalars fail with the parse
    /// error text in <paramref name="error"/>. Never throws.</summary>
    public static bool TryParseArgs(string? raw, out JsonObject? args, out string? error)
    {
        args = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        try
        {
            JsonNode? parsed = JsonNode.Parse(raw);
            if (parsed is JsonObject obj)
            {
                args = obj;
                return true;
            }
            error = "Argumen harus berupa objek JSON.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public ValidationResult Validate(JsonObject parameters)
    {
        string? action = P(parameters, "action", "Action");
        if (string.IsNullOrWhiteSpace(action))
            return ValidationResult.Invalid("Nama atau ID aksi wajib diisi.");
        if (!TryParseArgs(P(parameters, "args", "Args"), out _, out string? argsError))
            return ValidationResult.Invalid("Argumen JSON tidak valid: " + argsError);
        return ValidationResult.Valid;
    }

    public async Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        var validation = Validate(parameters);
        if (!validation.IsValid)
            return ActionResult.Fail(string.Join("; ", validation.Errors));

        string action = P(parameters, "action", "Action")!.Trim();
        TryParseArgs(P(parameters, "args", "Args"), out JsonObject? args, out _);

        if (!_client.IsConnected)
        {
            var (host, port, endpoint, password) = _settings();
            bool connected;
            try
            {
                connected = await _client
                    .ConnectAsync(host, port, endpoint, password, context.Cancellation)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A throwing ConnectAsync is still a failed connect:
                // report it honestly instead of letting it escape
                // (the IAction contract forbids throwing for expected
                // failures). LastError carries the cause when the
                // client set it; S1757 is the fallback.
                connected = false;
            }
            if (!connected || !_client.IsConnected)
                return ActionResult.Fail(_client.LastError ?? Localizer.Get("S1757"));
        }

        StreamerBotResponse response;
        try
        {
            response = await _client.DoActionAsync(action, args, context.Cancellation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
        return response.Success
            ? ActionResult.Ok()
            : ActionResult.Fail(response.Error ?? "Streamer.bot gagal menjalankan aksi.");
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
