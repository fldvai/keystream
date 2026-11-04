using System.Diagnostics;
using Keystream.Core.Localization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Keystream.Core.Actions;
using Keystream.Actions.Common;
using Keystream.Actions.Hotkey;

namespace Keystream.Actions.Windows;

/// <summary>
/// "windows" - Windows shell, window-management, quick-settings, browser
/// APPCOMMAND and app-launch commands as one-press deck actions, in the
/// same shape as "basic": one action id, Params["command"], normalized
/// case/space-insensitively. Five mechanisms, chosen per command:
///   - Chord: an ordinary key chord sent through HotkeyAction, so there
///     is still exactly one chord-sending path in the app.
///   - Sequence: two chords in order (quadrant snaps), 60 ms apart.
///   - VirtualKey: a browser/app-launch VK sent directly via SendInput,
///     like the media keys (no chord exists for these).
///   - Lock: LockWorkStation via P/Invoke. Win+L deliberately NOT sent
///     as a chord: Windows blocks synthetic Win+L, so it would silently
///     do nothing. LockWorkStation needs no privilege.
///   - Uri: an ms-settings: page (or control panel) opened with
///     UseShellExecute, like LinkAction.
///   - Launch (§126 + §127): a system tool opened with UseShellExecute, like
///     LauncherAction - bare exe/msc/cpl name (+ optional arguments),
///     no elevation verb. §126 tools run as the app's user; the §127
///     admin tools inherit elevation from the elevated app process
///     (requireAdministrator manifest), never from a verb here.
///     A failed launch (tool absent,
///     e.g. gpedit.msc on Windows Home) throws from Process.Start and
///     becomes an honest Fail (S1211), never a raw popup.
/// Params: "command": see Commands/Chords/Sequences/VirtualKeys/Uris/Launches.
///
/// §129 - split, same executor: the single legacy id "windows" keeps
/// accepting all 94 commands forever. Two new ids, "windows-command"
/// (Chords + Sequences + VirtualKeys + Lock = 52) and "windows-tools"
/// (Uris + Launches = 42), are the SAME class with a different
/// Descriptor.Id - one shared Validate/Execute path, no new execution
/// branch, no change to any dictionary. Which split id a legacy card
/// migrates to on editor save is decided purely by mechanism:
/// <see cref="ResolveSplitActionId"/>.
/// </summary>
public sealed class WindowsAction : IAction
{
    /// <summary>Legacy id: valid forever, all 94 commands.</summary>
    public const string LegacyId = "windows";
    /// <summary>Split id for the chord/sequence/virtual-key/lock mechanisms (52).</summary>
    public const string CommandId = "windows-command";
    /// <summary>Split id for the URI/launch mechanisms (42).</summary>
    public const string ToolsId = "windows-tools";

    private readonly string _descriptorId;

    /// <summary>One executor for three ids. An unknown id falls back to
    /// the legacy id so a mis-registered instance can never register
    /// a stray id in ActionsById.</summary>
    public WindowsAction(string descriptorId = LegacyId)
    {
        _descriptorId = descriptorId is CommandId or ToolsId or LegacyId ? descriptorId : LegacyId;
    }

    public ActionDescriptor Descriptor => _descriptorId switch
    {
        CommandId => new(CommandId, Localizer.Get("S1681"), Localizer.Get("S1071"), Localizer.Get("S1682")),
        ToolsId => new(ToolsId, Localizer.Get("S1683"), Localizer.Get("S1071"), Localizer.Get("S1684")),
        _ => new(LegacyId, Localizer.Get("S0577"), Localizer.Get("S1071"), Localizer.Get("S1095")),
    };

    private readonly HotkeyAction _hotkey = new();

    public static readonly IReadOnlyDictionary<string, string> Chords =
        new Dictionary<string, string>
        {
            // Jendela
            ["switchwindows"] = "Alt+Tab",
            ["switchwindowsback"] = "Alt+Shift+Tab",
            ["taskview"] = "Win+Tab",
            ["nextwindow"] = "Alt+Esc",
            ["closewindow"] = "Alt+F4",
            ["windowmenu"] = "Alt+Space",
            ["maximize"] = "Win+Up",
            ["minimize"] = "Win+Down",
            ["snapleft"] = "Win+Left",
            ["snapright"] = "Win+Right",
            ["movemonitorleft"] = "Win+Shift+Left",
            ["movemonitorright"] = "Win+Shift+Right",
            ["minimizeall"] = "Win+M",
            ["restoreall"] = "Win+Shift+M",
            ["minimizeothers"] = "Win+Home",
            ["showdesktop"] = "Win+D",
            ["newdesktop"] = "Win+Ctrl+D",
            ["closedesktop"] = "Win+Ctrl+F4",
            ["prevdesktop"] = "Win+Ctrl+Left",
            ["nextdesktop"] = "Win+Ctrl+Right",
            // Sistem & shell
            ["run"] = "Win+R",
            ["explorer"] = "Win+E",
            ["settings"] = "Win+I",
            ["search"] = "Win+S",
            ["actioncenter"] = "Win+A",
            ["notificationcenter"] = "Win+N",
            ["quicklink"] = "Win+X",
            ["project"] = "Win+P",
            ["clipboard"] = "Win+V",
            ["emoji"] = "Win+Period",
            ["snip"] = "Win+Shift+S",
            ["gamebar"] = "Win+G",
            ["taskbar"] = "Win+T",
            ["tray"] = "Win+B",
            ["taskmanager"] = "Ctrl+Shift+Esc",
            ["magnifyin"] = "Win+Add",
            ["magnifyout"] = "Win+Subtract",
            ["narrator"] = "Win+Ctrl+Enter",
            ["onscreenkeyboard"] = "Win+Ctrl+O",
            ["dictation"] = "Win+H",
        };

    /// <summary>Quadrant snaps: two chords in order (snap to a side,
    /// then to a vertical half). Sent 60 ms apart so the shell applies
    /// the first snap before the second arrives.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Sequences =
        new Dictionary<string, string[]>
        {
            ["snaptopleft"] = new[] { "Win+Left", "Win+Up" },
            ["snaptopright"] = new[] { "Win+Right", "Win+Up" },
            ["snapbottomleft"] = new[] { "Win+Left", "Win+Down" },
            ["snapbottomright"] = new[] { "Win+Right", "Win+Down" },
        };

    public static readonly IReadOnlyDictionary<string, ushort> VirtualKeys =
        new Dictionary<string, ushort>
        {
            ["browserback"] = 0xA6,      // VK_BROWSER_BACK
            ["browserforward"] = 0xA7,   // VK_BROWSER_FORWARD
            ["browserrefresh"] = 0xA8,   // VK_BROWSER_REFRESH
            ["browserstop"] = 0xA9,      // VK_BROWSER_STOP
            ["browserfavorites"] = 0xAB, // VK_BROWSER_FAVORITES
            ["browserhome"] = 0xAC,      // VK_BROWSER_HOME
            ["mail"] = 0xB4,             // VK_LAUNCH_MAIL
        };

    public static readonly IReadOnlyDictionary<string, string> Uris =
        new Dictionary<string, string>
        {
            ["settingsdisplay"] = "ms-settings:display",
            ["settingssound"] = "ms-settings:sound",
            ["settingsnetwork"] = "ms-settings:network",
            ["settingsbluetooth"] = "ms-settings:bluetooth",
            ["settingsupdate"] = "ms-settings:windowsupdate",
            ["settingsapps"] = "ms-settings:apps",
            ["settingspersonalization"] = "ms-settings:personalization",
            ["settingspower"] = "ms-settings:powersleep",
        };

    public const string LockCommand = "lockpc";

    /// <summary>§126 system tools, keyed by normalized command (same
    /// normalization as the catalog Values: "GroupPolicyEditor" →
    /// "grouppolicyeditor"). FileName is a bare system name resolved
    /// by the shell (System32 / PATH); Arguments is empty except the
    /// cmd.exe tools, which need "/k ..." to stay open showing
    /// their output. Keys are disjoint from Chords/Sequences/
    /// VirtualKeys/Uris/Lock - verified by the wb + SelfCheck count
    /// asserts (94 total). §126 entries need no elevation to OPEN
    /// their UI; operations inside a tool may raise Windows' own UAC,
    /// honestly. §127 appends 6 admin tools as ordinary Launches
    /// (same shape, NO runas verb anywhere): their elevation comes
    /// solely from the app itself running elevated (requireAdministrator
    /// manifest, worker A) and is inherited by the child process -
    /// never from a per-launch verb here.</summary>
    public static readonly IReadOnlyDictionary<string, (string FileName, string Arguments)> Launches =
        new Dictionary<string, (string, string)>
        {
            ["backuprestore"] = ("sdclt.exe", ""),
            ["commandprompt"] = ("cmd.exe", ""),
            ["computermanagement"] = ("compmgmt.msc", ""),
            ["controlpanel"] = ("control.exe", ""),
            ["diskdefragmenter"] = ("dfrgui.exe", ""),
            ["devicemanager"] = ("devmgmt.msc", ""),
            ["directxdiagnostic"] = ("dxdiag.exe", ""),
            ["diskcleanup"] = ("cleanmgr.exe", ""),
            ["diskmanagement"] = ("diskmgmt.msc", ""),
            ["eventviewer"] = ("eventvwr.msc", ""),
            ["grouppolicyeditor"] = ("gpedit.msc", ""),
            ["netstat"] = ("cmd.exe", "/k netstat -a"),
            ["networkconnections"] = ("ncpa.cpl", ""),
            ["networkinformation"] = ("cmd.exe", "/k ipconfig /all"),
            ["performancemonitor"] = ("perfmon.msc", ""),
            ["poweroptions"] = ("powercfg.cpl", ""),
            ["programsandfeatures"] = ("appwiz.cpl", ""),
            ["resourcemonitor"] = ("resmon.exe", ""),
            ["securitymaintenance"] = ("wscui.cpl", ""),
            ["services"] = ("services.msc", ""),
            ["sharedfolders"] = ("fsmgmt.msc", ""),
            ["systemconfiguration"] = ("msconfig.exe", ""),
            ["systeminformation"] = ("msinfo32.exe", ""),
            ["systemproperties"] = ("sysdm.cpl", ""),
            ["systemrestore"] = ("rstrui.exe", ""),
            ["taskscheduler"] = ("taskschd.msc", ""),
            ["windowsfeatures"] = ("OptionalFeatures.exe", ""),
            ["windowspowershell"] = ("powershell.exe", ""),
            // §127 admin tools (appended after the 28 §126 entries,
            // alphabetical by EN display name among the six):
            // ordinary Launches, no runas verb - elevation is inherited
            // from the elevated app process (manifest, worker A).
            ["checkdisk"] = ("cmd.exe", "/k chkdsk.exe"),
            ["dism"] = ("cmd.exe", "/k dism.exe /Online /Cleanup-Image /RestoreHealth"),
            ["flushdns"] = ("cmd.exe", "/k ipconfig.exe /flushdns"),
            ["msrt"] = ("mrt.exe", ""),
            ["registryeditor"] = ("regedit.exe", ""),
            ["systemfilechecker"] = ("cmd.exe", "/k sfc.exe /scannow"),
        };

    public static string NormalizeCommand(string? raw)
        => (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

    public static bool IsKnownCommand(string? raw)
    {
        string command = NormalizeCommand(raw);
        return command == LockCommand
            || Chords.ContainsKey(command)
            || Sequences.ContainsKey(command)
            || VirtualKeys.ContainsKey(command)
            || Uris.ContainsKey(command)
            || Launches.ContainsKey(command);
    }

    /// <summary>True when the command runs through a keyboard mechanism
    /// (chord, sequence, virtual key, or lock) - the "windows-command"
    /// partition. Pure mechanism test; no per-item judgement.</summary>
    public static bool IsCommandMechanism(string? raw)
    {
        string command = NormalizeCommand(raw);
        return command == LockCommand
            || Chords.ContainsKey(command)
            || Sequences.ContainsKey(command)
            || VirtualKeys.ContainsKey(command);
    }

    /// <summary>True when the command opens something (an ms-settings:
    /// page or a system tool launch) - the "windows-tools" partition.</summary>
    public static bool IsToolsMechanism(string? raw)
    {
        string command = NormalizeCommand(raw);
        return Uris.ContainsKey(command) || Launches.ContainsKey(command);
    }

    /// <summary>§129 migrate-on-save: the split id a legacy "windows"
    /// card migrates to when the editor SAVES it, decided only by the
    /// command's mechanism. Returns null for every other input: an
    /// already-split id (no-op), an unknown command (no silent
    /// retarget - the caller keeps the id it was given), or a null
    /// action id. The editor calls this only from BuildStep (the save
    /// path); loading a card never calls it, so a legacy card keeps
    /// "windows" in the file until it is saved.</summary>
    public static string? ResolveSplitActionId(string? actionId, string? rawCommand)
    {
        if (!string.Equals(actionId, LegacyId, StringComparison.OrdinalIgnoreCase)) return null;
        if (IsCommandMechanism(rawCommand)) return CommandId;
        if (IsToolsMechanism(rawCommand)) return ToolsId;
        return null;
    }

    public ValidationResult Validate(JsonObject parameters)
    {
        string command = NormalizeCommand(ParamReader.GetString(parameters, "command", "Command"));
        return IsKnownCommand(command)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(Localizer.Format("S1208", command));
    }

    public async Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string command = NormalizeCommand(ParamReader.GetString(parameters, "command", "Command"));

        if (command == LockCommand)
        {
            if (!LockWorkStation())
                return ActionResult.Fail(Localizer.Format("S1209", Marshal.GetLastWin32Error()));
            return ActionResult.Ok();
        }
        if (Chords.TryGetValue(command, out string? chord))
        {
            var p = new JsonObject { ["keys"] = chord };
            return await _hotkey.ExecuteAsync(p, context);
        }
        if (Sequences.TryGetValue(command, out string[]? steps))
        {
            ActionResult? last = null;
            foreach (string step in steps)
            {
                var p = new JsonObject { ["keys"] = step };
                last = await _hotkey.ExecuteAsync(p, context);
                if (!last.Success) return last;
                await Task.Delay(60, context.Cancellation);
            }
            return last ?? ActionResult.Ok();
        }
        if (VirtualKeys.TryGetValue(command, out ushort vk))
        {
            var (sent, errno) = SendInputNative.SendKeys(new[]
            {
                new SendInputNative.KeyStroke(vk, KeyUp: false, Extended: false),
                new SendInputNative.KeyStroke(vk, KeyUp: true, Extended: false),
            });
            return sent == 2
                ? ActionResult.Ok()
                : ActionResult.Fail(Localizer.Format("S1210", command, sent, errno));
        }
        if (Uris.TryGetValue(command, out string? uri))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
                return ActionResult.Ok();
            }
            catch (Exception ex)
            {
                return ActionResult.Fail(Localizer.Format("S1211", uri, ex.Message));
            }
        }
        if (Launches.TryGetValue(command, out var launch))
        {
            // Same shape as the Uri branch and LauncherAction: shell
            // launch, no elevation verb; a missing tool (gpedit.msc on
            // Home, a deprecated tool removed by a future Windows)
            // fails honestly with the OS message (S1211), surfaced by
            // the dispatcher like every other action failure (§105).
            string target = string.IsNullOrEmpty(launch.Arguments)
                ? launch.FileName
                : launch.FileName + " " + launch.Arguments;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launch.FileName,
                    Arguments = launch.Arguments,
                    UseShellExecute = true,
                });
                return ActionResult.Ok();
            }
            catch (Exception ex)
            {
                return ActionResult.Fail(Localizer.Format("S1211", target, ex.Message));
            }
        }
        return ActionResult.Fail(Localizer.Format("S1208", command));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
