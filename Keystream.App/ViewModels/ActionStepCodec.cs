using System.IO;
using System.Text.Json.Nodes;
using Keystream.Actions.Browser;
using Keystream.Actions.Common;
using Keystream.Actions.Windows;
using Keystream.Core.Config;

namespace Keystream.App.ViewModels;

/// <summary>§158 (Worker A) - the PURE mapping between one <see cref="ActionStep"/>
/// and the ~25 plain editor fields that describe it. Host-free by design:
/// no <c>AppHost</c>, no live lists, no <see cref="ActionCatalog"/> (its
/// <c>AppIcons</c> dependency is WPF-tainted and cannot link into the
/// plain net8.0 wb-harness), no <c>Localizer</c>, no WPF. The two inputs
/// that live in host-owned lists are therefore passed in as plain data:
/// the Windows command values (from <c>ActionCatalog.WindowsCommands</c>)
/// for <see cref="NormalizeWindows"/>, and the OBS param spec + resolved
/// display names carried on <see cref="StepParamFields"/> itself, filled
/// by the owning view-model from its own live lists just before
/// <see cref="BuildStep"/> runs.
/// <para>Both the main editor (<c>BindingEditorViewModel</c>) and the
/// combo step editor (<c>ComboStepViewModel</c>) delegate to this codec,
/// so a step built from a combo row is byte-equivalent to the step the
/// main editor would build from the same fields. The engine, resolver,
/// and <c>KeyCombinationConfig.ToStep()</c> are untouched.</para></summary>
public static class ActionStepCodec
{
    /// <summary>Sentinel <c>BrowserExe</c> value for a manually-entered
    /// browser (profilePath branch). Single source: the editor view-model
    /// exposes the same value through its own const.</summary>
    public const string CustomBrowserSentinel = "CUSTOM";

    /// <summary>One OBS command's parameter slots, projected from
    /// <c>ActionCatalog.ObsSpec</c> by the owning view-model. Empty
    /// strings mean the slot does not exist for that command.</summary>
    public sealed record ObsParamSpec(string NameParam, string SecondParam, string ValueParam)
    {
        public static readonly ObsParamSpec None = new("", "", "");
    }

    /// <summary>Loads one step's parameters into editor fields, applying
    /// exactly the fallbacks the main editor's ctor used before §158:
    /// <see cref="ParamReader"/> legacy-name fallbacks, the
    /// <c>Normalize*</c> value canonicalizations, the browser
    /// custom/manual migration (a stored <c>profilePath</c> moves the
    /// exe into the manual field and selects the CUSTOM sentinel),
    /// the multi-profile <c>profileKeys</c> array, and the
    /// launcher↔link split (a launcher target that looks like a URL
    /// presents as a link). The resolved action id (after the
    /// launcher↔link split) is returned on the bag itself, in
    /// <see cref="StepParamFields.SelectedActionId"/>.</summary>
    public static StepParamFields LoadFields(
        ActionStep step, string selectedActionId, IReadOnlyList<string> windowsCommandValues)
    {
        var f = new StepParamFields();
        var p = step.Params;
        f.HotkeyText = ParamReader.GetString(p, "keys", "Key") ?? "";
        f.MediaValue = NormalizeMedia(ParamReader.GetString(p, "command", "Command"));
        f.LauncherTarget = ParamReader.GetString(p, "target", "Target") ?? "";
        f.ObsCommandValue = NormalizeObs(ParamReader.GetString(p, "command", "Command"));
        f.ObsNameText = ParamReader.GetString(p, "sceneName", "SceneName", "sourceName", "SourceName",
            "inputName", "collectionName", "profileName") ?? "";
        f.ObsName2Text = ParamReader.GetString(p, "itemName", "filterName") ?? "";
        f.ObsValueText = ParamReader.GetString(p, "percent", "text", "filePath", "hotkeyName") ?? "";
        f.StreamerBotActionText = ParamReader.GetString(p, "action", "Action") ?? "";
        f.StreamerBotArgsText = ParamReader.GetString(p, "args", "Args") ?? "";
        string rawSteamCmd = ParamReader.GetString(p, "command", "Command") ?? "launch";
        string bpMode = ParamReader.GetString(p, "subCommand", "SubCommand", "mode", "Mode") ?? "";
        if (rawSteamCmd == "openbigpicture")
        {
            f.SteamCommandValue = "bigpicture";
            bpMode = "open";
        }
        else if (rawSteamCmd == "closebigpicture")
        {
            f.SteamCommandValue = "bigpicture";
            bpMode = "close";
        }
        else
        {
            f.SteamCommandValue = rawSteamCmd;
            if (string.IsNullOrEmpty(bpMode))
            {
                bpMode = ParamReader.GetBool(p, false, "isToggle", "IsToggle") ? "toggle" : "open";
            }
        }
        f.SteamBigPictureMode = bpMode;
        f.SteamAppId = ParamReader.GetString(p, "appId", "AppId") ?? "";
        f.SteamGameName = ParamReader.GetString(p, "gameName", "GameName") ?? "";
        f.SteamStatusValue = ParamReader.GetString(p, "status", "Status") ?? "online";
        f.SteamStatusSecondValue = ParamReader.GetString(p, "secondStatus", "SecondStatus") ?? "invisible";
        f.SteamIsToggle = ParamReader.GetBool(p, false, "isToggle", "IsToggle");
        f.SteamRunningGameAction = ParamReader.GetString(p, "runningAction", "RunningAction") ?? "focus";
        f.SoundFile = ParamReader.GetString(p, "file", "File", "Path") ?? "";
        string deckCmd = (ParamReader.GetString(p, "command", "Command") ?? "goto").ToLowerInvariant();
        f.DeckCommandValue = deckCmd == "gotopage" ? "goto" : deckCmd;
        f.DeckPageId = ParamReader.GetString(p, "pageId") ?? "";
        f.BasicValue = NormalizeBasic(ParamReader.GetString(p, "command", "Command"));
        f.WindowsValue = NormalizeWindows(ParamReader.GetString(p, "command", "Command"), windowsCommandValues);
        f.BrowserExe = ParamReader.GetString(p, "browserExe", "BrowserExe") ?? "";
        f.BrowserEngine = ParamReader.GetString(p, "engine", "Engine") ?? "Chromium";
        f.BrowserProfileKey = ParamReader.GetString(p, "profileKey", "ProfileKey") ?? "";
        // Tile multi-profil: daftar kunci dari param sendiri.
        // Bukan grup: tidak ada groupId yang dibaca di sini.
        if (p.TryGetPropertyValue("profileKeys", out var multiKeys)
            && multiKeys is JsonArray multiArr && multiArr.Count > 0)
        {
            f.BrowserProfileMulti = true;
            foreach (var n in multiArr)
            {
                string k = n?.GetValue<string>() ?? "";
                if (k.Length > 0) f.BrowserProfileKeys.Add(k);
            }
            if (f.BrowserProfileKeys.Count == 0) f.BrowserProfileMulti = false;
        }
        f.BrowserOpenMode = BrowserAction.NormalizeOpenMode(ParamReader.GetString(p, "openMode", "OpenMode"));
        f.BrowserUrl = ParamReader.GetString(p, "url", "Url") ?? "";
        f.BrowserUserDataDir = ParamReader.GetString(p, "userDataDir", "UserDataDir") ?? "";
        string manualPath = ParamReader.GetString(p, "profilePath", "ProfilePath") ?? "";
        if (manualPath.Length > 0)
        {
            // Tile manual dari paket sebelumnya dimigrasikan ke CUSTOM:
            // exe manual pindah ke field manual, dropdown memilih CUSTOM.
            f.ManualBrowserExe = f.BrowserExe;
            f.ManualEngine = f.BrowserEngine;
            f.ManualProfileDisplay = ParamReader.GetString(p, "profileName", "ProfileName") ?? "";
            f.BrowserExe = CustomBrowserSentinel;
            f.ManualProfilePath = manualPath;
        }
        f.BrowserGroupId = ParamReader.GetString(p, "groupId", "GroupId") ?? "";
        f.MacroId = ParamReader.GetString(p, "macroId", "MacroId") ?? "";
        f.ProfileId = ParamReader.GetString(p, "profileId") ?? "";
        f.LinkUrl = ParamReader.GetString(p, "url", "Url") ?? "";
        // Split (v2.9.24): a legacy "launcher" binding whose target is
        // a web address belongs to "Buka Link" - present it there.
        string lt = f.LauncherTarget.Trim();
        string resolved = selectedActionId;
        if (selectedActionId == "launcher" && LooksLikeUrl(lt))
        {
            resolved = "link";
            f.LinkUrl = lt;
        }
        else if (selectedActionId == "link" && f.LinkUrl.Length == 0)
        {
            f.LinkUrl = lt;
        }
        f.SelectedActionId = resolved;
        return f;
    }

    /// <summary>Builds the step the editor would persist from the given
    /// fields - the single funnel the main editor's <c>BuildStep</c>
    /// and the combo step's <c>ToStep</c> both delegate to. Returns
    /// null for the no-action id (empty string), exactly as the main
    /// editor did. The legacy "windows" id migrates on save to its
    /// split id by command mechanism (<see cref="WindowsAction.ResolveSplitActionId"/>),
    /// but only for a session that opened as a legacy "windows" card/step
    /// (<paramref name="openedAsLegacyWindows"/>) - the migration key
    /// is the stored id itself, so the flag is redundant for every id
    /// reachable through the UI and exists to keep the session scope
    /// explicit at the call site.</summary>
    public static ActionStep? BuildStep(StepParamFields bag, string selectedActionId, bool openedAsLegacyWindows)
    {
        if (selectedActionId.Length == 0) return null;
        var p = new JsonObject();
        switch (selectedActionId)
        {
            case "hotkey": p["keys"] = bag.HotkeyText.Trim(); break;
            case "media": p["command"] = bag.MediaValue; break;
            case "basic": p["command"] = bag.BasicValue; break;
            case "windows":
            case "windows-command":
            case "windows-tools": p["command"] = bag.WindowsValue; break;
            case "browser":
                if (bag.BrowserExe == CustomBrowserSentinel)
                {
                    // CUSTOM: dropdown profil disembunyikan, path manual
                    // yang dipakai; validasi aksi browser menolak pasangan
                    // yang tidak lengkap.
                    string manualPath = bag.ManualProfilePath.Trim();
                    string manualFolder = Path.GetFileName(manualPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    p["browserExe"] = bag.ManualBrowserExe.Trim();
                    p["engine"] = bag.ManualEngine;
                    p["profilePath"] = manualPath;
                    p["profileKey"] = manualFolder;
                    p["profileName"] = bag.ManualProfileDisplay.Length > 0 ? bag.ManualProfileDisplay : manualFolder;
                }
                else
                {
                    p["browserExe"] = bag.BrowserExe;
                    p["engine"] = bag.BrowserEngine;
                    if (bag.BrowserProfileMulti && bag.BrowserProfileKeys.Count > 0)
                    {
                        // Multi: daftar disimpan di param tile sendiri.
                        // profileKey/profileName pertama dipertahankan
                        // sebagai kompatibilitas tampilan lama.
                        var keys = new JsonArray();
                        var names = new JsonArray();
                        for (int i = 0; i < bag.BrowserProfileKeys.Count; i++)
                        {
                            string k = bag.BrowserProfileKeys[i];
                            keys.Add(JsonValue.Create(k));
                            string display = i < bag.BrowserProfileNames.Count ? bag.BrowserProfileNames[i] : "";
                            names.Add(JsonValue.Create(display.Length > 0 ? display : k));
                        }
                        p["profileKeys"] = keys;
                        p["profileNames"] = names;
                        p["profileKey"] = bag.BrowserProfileKeys[0];
                        p["profileName"] = $"{bag.BrowserProfileKeys.Count} profil";
                    }
                    else
                    {
                        p["profileKey"] = bag.BrowserProfileKey;
                        p["profileName"] = bag.BrowserProfileDisplay.Length > 0 ? bag.BrowserProfileDisplay : bag.BrowserProfileKey;
                    }
                    p["userDataDir"] = bag.BrowserUserDataDir;
                }
                p["openMode"] = bag.BrowserOpenMode;
                // Di luar cabang CUSTOM/dropdown: URL berlaku untuk keduanya.
                p["url"] = bag.BrowserUrl.Trim();
                break;
            case "browsergroup":
                p["groupId"] = bag.BrowserGroupId;
                p["groupName"] = bag.BrowserGroupDisplay;
                break;
            case "macro":
                // Execution uses macroId only; macroName is a readable
                // label for the saved JSON (same pattern as groupName).
                p["macroId"] = bag.MacroId;
                p["macroName"] = bag.MacroDisplay;
                break;
            case "launcher":
                p["target"] = bag.LauncherTarget.Trim();
                break;
            case "link": p["url"] = bag.LinkUrl.Trim(); break;
            case "obs":
                p["command"] = bag.ObsCommandValue;
                var spec = bag.ObsSpec;
                if (spec.NameParam.Length > 0) p[spec.NameParam] = bag.ObsNameText.Trim();
                if (spec.SecondParam.Length > 0) p[spec.SecondParam] = bag.ObsName2Text.Trim();
                if (spec.ValueParam.Length > 0) p[spec.ValueParam] = bag.ObsValueText.Trim();
                break;
            case "streamerbot":
                if (!string.IsNullOrWhiteSpace(bag.StreamerBotActionText)) p["action"] = bag.StreamerBotActionText.Trim();
                if (!string.IsNullOrWhiteSpace(bag.StreamerBotArgsText)) p["args"] = bag.StreamerBotArgsText.Trim();
                break;
            case "steam":
                p["command"] = bag.SteamCommandValue;
                if (bag.SteamCommandValue == "launch")
                {
                    p["appId"] = bag.SteamAppId;
                    p["gameName"] = bag.SteamGameName;
                    p["runningAction"] = bag.SteamRunningGameAction;
                    p["isToggle"] = bag.SteamIsToggle;
                }
                else if (bag.SteamCommandValue == "bigpicture")
                {
                    p["subCommand"] = bag.SteamBigPictureMode;
                    p["isToggle"] = (bag.SteamBigPictureMode == "toggle");
                }
                else if (bag.SteamCommandValue == "status")
                {
                    p["status"] = bag.SteamStatusValue;
                    p["secondStatus"] = bag.SteamStatusSecondValue;
                    p["isToggle"] = bag.SteamIsToggle;
                }
                else
                {
                    p["isToggle"] = bag.SteamIsToggle;
                }
                break;
            case "sound": p["file"] = bag.SoundFile.Trim(); break;
            case "deck":
                p["command"] = bag.DeckCommandValue;
                if (bag.DeckCommandValue is "goto" or "momentary")
                {
                    p["pageId"] = bag.DeckPageId;
                    p["pageName"] = bag.DeckPageDisplay;
                }
                break;
            case "profile":
                p["profileId"] = bag.ProfileId;
                p["profileName"] = bag.ProfileDisplay;
                break;
        }
        string effectiveId = openedAsLegacyWindows
            ? WindowsAction.ResolveSplitActionId(selectedActionId, bag.WindowsValue) ?? selectedActionId
            : selectedActionId;
        return new ActionStep { ActionId = effectiveId, Params = p };
    }

    /// <summary>Reverse of <see cref="KeyCombinationConfig.ToStep"/> for
    /// the six mapped kinds: extracts exactly the param key
    /// <c>ToStep</c> wrote (hotkey→keys, launcher→target, link→url,
    /// media/obs/basic→command). Every other kind keeps its own id
    /// with an empty target - the legacy fields are a display/summary
    /// pair, never a second source of truth for the step itself.</summary>
    public static (string ActionId, string ActionTarget) LegacyFields(ActionStep step)
    {
        string key = step.ActionId switch
        {
            "hotkey" => "keys",
            "launcher" => "target",
            "link" => "url",
            "steam" => "gameName",
            "media" or "obs" or "basic" => "command",
            _ => "",
        };
        if (key.Length == 0) return (step.ActionId, "");
        string target = step.ActionId switch
        {
            "hotkey" => ParamReader.GetString(step.Params, "keys", "Key") ?? "",
            "launcher" => ParamReader.GetString(step.Params, "target", "Target") ?? "",
            "link" => ParamReader.GetString(step.Params, "url", "Url") ?? "",
            "steam" => ParamReader.GetString(step.Params, "gameName", "GameName") ?? "Steam",
            _ => ParamReader.GetString(step.Params, "command", "Command") ?? "",
        };
        return (step.ActionId, target);
    }

    /// <summary>Whether a launcher target is a web address (the
    /// launcher↔link split test). Verbatim from the main editor.</summary>
    public static bool LooksLikeUrl(string target)
        => target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || target.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    /// <summary>§159 - the chord a virtual-key picker button writes:
    /// the active modifiers in fixed order (Ctrl, Shift, Alt, Win)
    /// plus the picked token, joined with "+". Verbatim rule from
    /// the main editor's <c>PickVirtualKey</c>; both the main editor
    /// and every combination step compose through here, so a step
    /// pick can never spell a chord differently from a main pick.
    /// An empty token contributes nothing (no trailing "+").</summary>
    public static string ComposeHotkey(bool ctrl, bool shift, bool alt, bool win, string? token)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        if (!string.IsNullOrEmpty(token)) parts.Add(token);
        return string.Join("+", parts);
    }

    /// <summary>Canonical basic command, falling back to Copy for
    /// anything unknown. Verbatim switch from the main editor.</summary>
    public static string NormalizeBasic(string? raw)
    {
        string normalized = (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        return normalized switch
        {
            "cut" => "Cut",
            "copy" => "Copy",
            "paste" => "Paste",
            "undo" => "Undo",
            "redo" => "Redo",
            "all" => "All",
            "find" => "Find",
            "new" => "New",
            "open" => "Open",
            "print" => "Print",
            "save" => "Save",
            "websearch" => "WebSearch",
            _ => "Copy",
        };
    }

    /// <summary>Canonical media command, falling back to PlayPause.
    /// Verbatim switch from the main editor (incl. the §156
    /// VolumeDial line, without which reopening a dial card would
    /// fall back and re-saving would overwrite the command).</summary>
    public static string NormalizeMedia(string? raw)
    {
        string normalized = (raw ?? "").Replace(" ", "").Replace("_", "").ToLowerInvariant();
        return normalized switch
        {
            "playpause" => "PlayPause",
            "stop" => "Stop",
            "next" => "Next",
            "prev" or "previous" => "Prev",
            "volumeup" => "VolumeUp",
            "volumedown" => "VolumeDown",
            "mute" => "Mute",
            "volumedial" => "VolumeDial",
            _ => "PlayPause",
        };
    }

    /// <summary>Canonical OBS command, falling back to SwitchScene.
    /// Verbatim switch from the main editor.</summary>
    public static string NormalizeObs(string? raw)
    {
        string normalized = (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        return normalized switch
        {
            "switchscene" => "SwitchScene",
            "setpreviewscene" => "SetPreviewScene",
            "togglestream" or "startstream" or "stopstream" => "ToggleStream",
            "togglerecord" or "startrecord" or "stoprecord" => "ToggleRecord",
            "togglerecordpause" => "ToggleRecordPause",
            "savereplay" or "savereplaybuffer" => "SaveReplay",
            "togglereplaybuffer" => "ToggleReplayBuffer",
            "togglevirtualcam" => "ToggleVirtualCam",
            "togglestudiomode" => "ToggleStudioMode",
            "togglemute" => "ToggleMute",
            "setinputvolume" => "SetInputVolume",
            "togglesceneitem" => "ToggleSceneItem",
            "togglefilter" => "ToggleFilter",
            "refreshbrowser" => "RefreshBrowser",
            "mediatoggle" => "MediaToggle",
            "settext" => "SetText",
            "screenshot" => "Screenshot",
            "setscenecollection" => "SetSceneCollection",
            "setprofile" => "SetProfile",
            "triggerhotkey" => "TriggerHotkey",
            _ => "SwitchScene",
        };
    }

    /// <summary>Canonical Windows command value from the catalog's
    /// value list (matched ignoring case and underscores), falling
    /// back to SwitchWindows. The candidate list is a parameter -
    /// not read from <see cref="ActionCatalog"/> - because the
    /// catalog cannot link into the harness (WPF-tainted
    /// <c>AppIcons</c>); the owning view-model passes
    /// <c>ActionCatalog.WindowsCommands</c> values. Verbatim matching
    /// rule from the main editor's <c>NormalizeWindows</c>.</summary>
    public static string NormalizeWindows(string? raw, IEnumerable<string> windowsCommandValues)
    {
        string normalized = (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        return windowsCommandValues
            .FirstOrDefault(o => o.Replace("_", "").ToLowerInvariant() == normalized)
            ?? "SwitchWindows";
    }
}

/// <summary>Plain bag of the ~25 per-action editor fields for one step,
/// plus the plain inputs <see cref="ActionStepCodec.BuildStep"/> needs
/// from the owning view-model's live lists: the OBS param spec
/// (<see cref="ObsSpec"/>) and the resolved display names
/// (<c>*Display</c> / <see cref="BrowserProfileNames"/>). Display
/// fields are filled by the owner just before building; the codec
/// never reads a live list itself. No behaviour, no host, no WPF -
/// safe to construct in the harness.</summary>
public sealed class StepParamFields
{
    /// <summary>Action id after load, incl. the launcher↔link split.
    /// Set by <see cref="ActionStepCodec.LoadFields"/>; the owning
    /// view-model copies it into its own SelectedActionId.</summary>
    public string SelectedActionId { get; set; } = "";
    public string HotkeyText { get; set; } = "";
    public string MediaValue { get; set; } = "PlayPause";
    public string BasicValue { get; set; } = "Copy";
    public string WindowsValue { get; set; } = "SwitchWindows";
    public string BrowserExe { get; set; } = "";
    public string BrowserEngine { get; set; } = "Chromium";
    public string BrowserProfileKey { get; set; } = "";
    public List<string> BrowserProfileKeys { get; set; } = new();
    public bool BrowserProfileMulti { get; set; }
    public string BrowserGroupId { get; set; } = "";
    public string MacroId { get; set; } = "";
    public string BrowserOpenMode { get; set; } = "New";
    public string BrowserUrl { get; set; } = "";
    public string BrowserUserDataDir { get; set; } = "";
    public string ManualBrowserExe { get; set; } = "";
    public string ManualProfilePath { get; set; } = "";
    /// <summary>Engine detected for the manual profile folder
    /// (owner-maintained while editing; copied from the stored step
    /// on load).</summary>
    public string ManualEngine { get; set; } = "Chromium";
    /// <summary>Display name detected for the manual profile folder
    /// (owner-maintained while editing; copied from the stored step
    /// on load).</summary>
    public string ManualProfileDisplay { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string LauncherTarget { get; set; } = "";
    public string LinkUrl { get; set; } = "";
    public string ObsCommandValue { get; set; } = "SwitchScene";
    public string ObsNameText { get; set; } = "";
    public string ObsName2Text { get; set; } = "";
    public string ObsValueText { get; set; } = "";
    public string StreamerBotActionText { get; set; } = "";
    public string StreamerBotArgsText { get; set; } = "";
    public string SteamCommandValue { get; set; } = "launch";
    public string SteamBigPictureMode { get; set; } = "toggle";
    public string SteamAppId { get; set; } = "";
    public string SteamGameName { get; set; } = "";
    public string SteamStatusValue { get; set; } = "online";
    public string SteamStatusSecondValue { get; set; } = "invisible";
    public bool SteamIsToggle { get; set; }
    public string SteamRunningGameAction { get; set; } = "focus";
    public string SoundFile { get; set; } = "";
    public string DeckCommandValue { get; set; } = "goto";
    public string DeckPageId { get; set; } = "";

    /// <summary>OBS param slots for <see cref="ObsCommandValue"/>,
    /// projected by the owner from <c>ActionCatalog.ObsSpec</c>.
    /// Load leaves this at <see cref="ObsParamSpec.None"/> (load
    /// reads every candidate key generically and needs no spec).</summary>
    public ActionStepCodec.ObsParamSpec ObsSpec { get; set; } = ActionStepCodec.ObsParamSpec.None;
    /// <summary>Resolved display name for the single browser profile
    /// (owner-filled from its profile list; empty = fall back to the
    /// profile key, as the main editor did).</summary>
    public string BrowserProfileDisplay { get; set; } = "";
    /// <summary>Resolved display names parallel to
    /// <see cref="BrowserProfileKeys"/> (owner-filled; a missing or
    /// empty entry falls back to its key).</summary>
    public List<string> BrowserProfileNames { get; set; } = new();
    public string BrowserGroupDisplay { get; set; } = "";
    public string MacroDisplay { get; set; } = "";
    public string DeckPageDisplay { get; set; } = "";
    public string ProfileDisplay { get; set; } = "";
}
