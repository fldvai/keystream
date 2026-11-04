using System.Globalization;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Obs;

/// <summary>
/// OBS control through <see cref="ObsClient"/> - full command vocabulary
/// (UI v2.2, added at the user's request). Connection settings come from
/// the host via a provider delegate; if OBS is not connected the action
/// connects once with the CURRENT settings and reports honestly.
/// Toggle-style commands that have no native toggle request (studio
/// mode, scene-item visibility, filter enabled) are implemented as
/// read-current-state then set-the-opposite, so their result is still a
/// single honest outcome. Params per command:
///   SwitchScene / SetPreviewScene : sceneName
///   ToggleMute                    : sourceName (legacy) / inputName
///   ToggleSceneItem               : sceneName + itemName
///   RefreshBrowser / MediaToggle  : inputName (sourceName accepted)
///   SetInputVolume                : inputName + percent (0-100)
///   ToggleFilter                  : inputName + filterName
///   SetText                       : inputName + text
///   Screenshot                    : sourceName (scene or source) + filePath (optional)
///   SetSceneCollection            : collectionName
///   SetProfile                    : profileName
///   TriggerHotkey                 : hotkeyName
///   (stream/record/replay/virtual-cam/record-pause/studio toggles need none)
/// </summary>
public sealed class ObsAction : IAction
{
    private readonly ObsClient _client;
    private readonly Func<(string Host, string Password)> _settings;

    public ObsAction(ObsClient client, Func<(string Host, string Password)> settings)
    {
        _client = client;
        _settings = settings;
    }

    public ActionDescriptor Descriptor => new("obs", Localizer.Get("S1088"), Localizer.Get("S1089"),
        Localizer.Get("S1090"));

    private static string Normalize(string? raw)
        => (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

    private static string? P(JsonObject p, params string[] keys) => ParamReader.GetString(p, keys);

    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "switchscene", "togglestream", "startstream", "stopstream",
        "togglerecord", "startrecord", "stoprecord", "togglemute",
        "savereplay", "savereplaybuffer", "togglereplaybuffer", "togglevirtualcam",
        "togglerecordpause", "togglestudiomode", "setpreviewscene", "screenshot",
        "togglesceneitem", "refreshbrowser", "setinputvolume", "togglefilter",
        "mediatoggle", "settext", "setscenecollection", "setprofile", "triggerhotkey",
    };

    public ValidationResult Validate(JsonObject parameters)
    {
        string command = Normalize(P(parameters, "command", "Command"));
        if (!KnownCommands.Contains(command))
            return ValidationResult.Invalid(Localizer.Format("S1182", command));

        string? Need(string label, params string[] keys)
            => string.IsNullOrWhiteSpace(P(parameters, keys)) ? $"Perintah ini membutuhkan '{label}'." : null;

        string? error = command switch
        {
            "switchscene" or "setpreviewscene" => Need("sceneName", "sceneName", "SceneName"),
            "togglemute" => Need("sourceName", "sourceName", "SourceName", "inputName"),
            "togglesceneitem" => Need("sceneName", "sceneName", "SceneName") ?? Need("itemName", "itemName"),
            "refreshbrowser" or "mediatoggle" => Need("inputName", "inputName", "sourceName", "SourceName"),
            "setinputvolume" => Need("inputName", "inputName", "sourceName", "SourceName") ?? Need("percent", "percent"),
            "togglefilter" => Need("inputName", "inputName", "sourceName", "SourceName") ?? Need("filterName", "filterName"),
            "settext" => Need("inputName", "inputName", "sourceName", "SourceName")
                         ?? (P(parameters, "text") is null ? Localizer.Get("S1183") : null),
            "screenshot" => Need("sourceName", "sourceName", "SourceName", "sceneName", "SceneName"),
            "setscenecollection" => Need("collectionName", "collectionName"),
            "setprofile" => Need("profileName", "profileName"),
            "triggerhotkey" => Need("hotkeyName", "hotkeyName"),
            _ => null,
        };
        if (error is not null) return ValidationResult.Invalid(error);

        if (command == "setinputvolume")
        {
            string raw = P(parameters, "percent") ?? "";
            if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct)
                || pct < 0 || pct > 100)
                return ValidationResult.Invalid(Localizer.Format("S1184", raw));
        }
        return ValidationResult.Valid;
    }

    private async Task<ActionResult> ReqAsync(string type, JsonObject? data, ActionContext context)
    {
        var r = await _client.RequestAsync(type, data, context.Cancellation).ConfigureAwait(false);
        return r.Success ? ActionResult.Ok() : ActionResult.Fail(r.Error ?? Localizer.Format("S1185", type));
    }

    private async Task<ObsResponse> ReqRawAsync(string type, JsonObject? data, ActionContext context)
        => await _client.RequestAsync(type, data, context.Cancellation).ConfigureAwait(false);

    public async Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        var validation = Validate(parameters);
        if (!validation.IsValid)
            return ActionResult.Fail(string.Join("; ", validation.Errors));

        string command = Normalize(P(parameters, "command", "Command"));

        if (!_client.IsConnected)
        {
            var (host, password) = _settings();
            bool connected = await _client.ConnectAsync(host, password, context.Cancellation).ConfigureAwait(false);
            if (!connected)
                return ActionResult.Fail(_client.LastError ?? Localizer.Get("S1186"));
        }

        string? input = P(parameters, "inputName", "sourceName", "SourceName");
        string? scene = P(parameters, "sceneName", "SceneName");

        switch (command)
        {
            case "switchscene":
                return await ReqAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = scene }, context);
            case "setpreviewscene":
                return await ReqAsync("SetCurrentPreviewScene", new JsonObject { ["sceneName"] = scene }, context);
            case "togglestream": return await ReqAsync("ToggleStream", null, context);
            case "startstream": return await ReqAsync("StartStream", null, context);
            case "stopstream": return await ReqAsync("StopStream", null, context);
            case "togglerecord": return await ReqAsync("ToggleRecord", null, context);
            case "startrecord": return await ReqAsync("StartRecord", null, context);
            case "stoprecord": return await ReqAsync("StopRecord", null, context);
            case "togglemute":
                return await ReqAsync("ToggleInputMute", new JsonObject { ["inputName"] = input }, context);
            case "savereplay": case "savereplaybuffer":
                return await ReqAsync("SaveReplayBuffer", null, context);
            case "togglereplaybuffer": return await ReqAsync("ToggleReplayBuffer", null, context);
            case "togglevirtualcam": return await ReqAsync("ToggleVirtualCam", null, context);
            case "togglerecordpause": return await ReqAsync("ToggleRecordPause", null, context);

            case "togglestudiomode":
            {
                var cur = await ReqRawAsync("GetStudioModeEnabled", null, context);
                if (!cur.Success) return ActionResult.Fail(cur.Error ?? Localizer.Get("S1187"));
                bool enabled = cur.Data?["studioModeEnabled"]?.GetValue<bool>() ?? false;
                return await ReqAsync("SetStudioModeEnabled", new JsonObject { ["studioModeEnabled"] = !enabled }, context);
            }

            case "togglesceneitem":
            {
                string item = P(parameters, "itemName")!;
                var idResp = await ReqRawAsync("GetSceneItemId",
                    new JsonObject { ["sceneName"] = scene, ["sourceName"] = item }, context);
                if (!idResp.Success) return ActionResult.Fail(idResp.Error ?? Localizer.Get("S1188"));
                long itemId = idResp.Data?["sceneItemId"]?.GetValue<long>() ?? -1;
                var enResp = await ReqRawAsync("GetSceneItemEnabled",
                    new JsonObject { ["sceneName"] = scene, ["sceneItemId"] = itemId }, context);
                if (!enResp.Success) return ActionResult.Fail(enResp.Error ?? Localizer.Get("S1189"));
                bool enabled = enResp.Data?["sceneItemEnabled"]?.GetValue<bool>() ?? true;
                return await ReqAsync("SetSceneItemEnabled", new JsonObject
                {
                    ["sceneName"] = scene, ["sceneItemId"] = itemId, ["sceneItemEnabled"] = !enabled,
                }, context);
            }

            case "togglefilter":
            {
                string filter = P(parameters, "filterName")!;
                var enResp = await ReqRawAsync("GetSourceFilterEnabled",
                    new JsonObject { ["sourceName"] = input, ["filterName"] = filter }, context);
                if (!enResp.Success) return ActionResult.Fail(enResp.Error ?? Localizer.Get("S1190"));
                bool enabled = enResp.Data?["filterEnabled"]?.GetValue<bool>() ?? true;
                return await ReqAsync("SetSourceFilterEnabled", new JsonObject
                {
                    ["sourceName"] = input, ["filterName"] = filter, ["filterEnabled"] = !enabled,
                }, context);
            }

            case "refreshbrowser":
                return await ReqAsync("PressInputPropertiesButton", new JsonObject
                {
                    ["inputName"] = input, ["propertyName"] = "refreshnocache",
                }, context);

            case "mediatoggle":
                return await ReqAsync("TriggerMediaInputAction", new JsonObject
                {
                    ["inputName"] = input,
                    ["mediaAction"] = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_TOGGLE_PLAY_PAUSE",
                }, context);

            case "setinputvolume":
            {
                double pct = double.Parse((P(parameters, "percent") ?? "0").Replace(',', '.'),
                    CultureInfo.InvariantCulture);
                return await ReqAsync("SetInputVolume", new JsonObject
                {
                    ["inputName"] = input, ["inputVolumeMul"] = pct / 100.0,
                }, context);
            }

            case "settext":
                return await ReqAsync("SetInputSettings", new JsonObject
                {
                    ["inputName"] = input,
                    ["inputSettings"] = new JsonObject { ["text"] = P(parameters, "text") ?? "" },
                    ["overlay"] = true,
                }, context);

            case "screenshot":
            {
                string source = P(parameters, "sourceName", "SourceName", "sceneName", "SceneName")!;
                string? path = P(parameters, "filePath");
                if (string.IsNullOrWhiteSpace(path))
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Keystream");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, $"obs-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                }
                var r = await ReqAsync("SaveSourceScreenshot", new JsonObject
                {
                    ["sourceName"] = source, ["imageFormat"] = "png", ["imageFilePath"] = path,
                }, context);
                // Path dikembalikan ke user lewat Detail, dan dicatat di parameter
                // (editor membaca filePath) hanya bila OBS sukses; bila gagal, path
                // tak tercatat dan hasil Fail dari ReqAsync yang bicara.
                if (r.Success)
                {
                    parameters["filePath"] = path;
                    return ActionResult.Ok(0, path);
                }
                return r;
            }

            case "setscenecollection":
                return await ReqAsync("SetCurrentSceneCollection",
                    new JsonObject { ["sceneCollectionName"] = P(parameters, "collectionName") }, context);

            case "setprofile":
                return await ReqAsync("SetCurrentProfile",
                    new JsonObject { ["profileName"] = P(parameters, "profileName") }, context);

            case "triggerhotkey":
                return await ReqAsync("TriggerHotkeyByName",
                    new JsonObject { ["hotkeyName"] = P(parameters, "hotkeyName") }, context);

            default:
                return ActionResult.Fail(Localizer.Format("S1182", command));
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
