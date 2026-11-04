using Keystream.App.Views;
using Keystream.Core.Localization;

namespace Keystream.App.ViewModels;

/// <summary>Friendly presentation data for the simple binding editor:
/// action names, the media/OBS option lists shown as dropdowns (never raw
/// parameter text), and the tile color presets. The engine and actions
/// keep their stable English ids; only the presentation is localized.
/// Presentation texts are stored as localization KEYS and resolved lazily
/// (§86): the lists are static readonly, so resolving at construction
/// would freeze them in the startup language.</summary>
public static class ActionCatalog
{
    // Display/ Hint are KEYS, resolved lazily: these lists are static
    // readonly (built once at startup), so resolving at construction
    // would freeze them in the startup language. Computed properties
    // keep the same member names for all consumers.
    public sealed record ActionOption(string ActionId, string DisplayKey, string HintKey)
    {
        public string Display => Localizer.Get(DisplayKey);
        public string Hint => Localizer.Get(HintKey);
        // A record's generated ToString dumps every property - exactly what
        // leaked into the dropdowns. The display text is the only sane
        // rendering of an option, in any template.
        public override string ToString() => Display;
    }

    // DisplayKey holds a localization key; dynamic names (page/profile
    // names) pass through verbatim because Localizer.Get falls back to its
    // input when it is not a key. The one pathological collision is a user
    // name exactly equal to a key (e.g. "S0093"), which would render as
    // that key's translation in pickers only.
    // §155: optional HintKey - the per-option mechanism text shown as a
    // ComboBoxItem tooltip and as the persistent hint under the combo.
    // Null = no hint (every pre-§155 site). Resolved lazily like Display
    // so a language switch is not frozen at construction; a resolved
    // literal passed as the key passes through Localizer.Get verbatim.
    public sealed record ValueOption(string Value, string DisplayKey, string? HintKey = null)
    {
        public string Display => Localizer.Get(DisplayKey);
        public string Hint => HintKey is null ? "" : Localizer.Get(HintKey);
        public override string ToString() => Display;
    }

    public const string NoActionId = "";

    public static readonly IReadOnlyList<ActionOption> Actions = new[]
    {
        new ActionOption(NoActionId, "S0573", "S0574"),
        new ActionOption("basic", "S0575", "S0576"),
        new ActionOption("windows-command", "S1681", "S1682"),
        new ActionOption("windows-tools", "S1683", "S1684"),
        new ActionOption("media", "S0583", "S0584"),
        new ActionOption("hotkey", "S0585", "S0586"),
        new ActionOption("macro", "S0093", "S0587"),
        new ActionOption("launcher", "S0106", "S0588"),
        new ActionOption("link", "S0589", "S0590"),
        // §129 (pekerja B): browser/browsergroup pindah ke tepat
        // setelah link. Urutan daftar ini hanya urutan tampil
        // dropdown - tidak ada logika yang memakai indeksnya
        // (satu-satunya konsumen urutan adalah ItemsSource ComboBox
        // editor; FriendlyName lookup berdasarkan ActionId).
        new ActionOption("browser", "S0579", "S0580"),
        new ActionOption("browsergroup", "S0581", "S0582"),
        new ActionOption("sound", "S0591", "S0592"),
        new ActionOption("obs", "S0241", "S0593"),
        new ActionOption("streamerbot", "S1743", "S1750"),
        new ActionOption("steam", "S1848", "S1849"),
        new ActionOption("profile", "S0594", "S0595"),
        new ActionOption("deck", "S0596", "S0597"),
    };

    public static readonly IReadOnlyList<ValueOption> BasicCommands = new[]
    {
        new ValueOption("Cut", "S0598"),
        new ValueOption("Copy", "S0599"),
        new ValueOption("Paste", "S0600"),
        new ValueOption("Undo", "S0601"),
        new ValueOption("Redo", "S0602"),
        new ValueOption("All", "S0603"),
        new ValueOption("Find", "S0604"),
        new ValueOption("New", "S0605"),
        new ValueOption("Open", "S0606"),
        new ValueOption("Print", "S0607"),
        new ValueOption("Save", "S0608"),
        new ValueOption("WebSearch", "S0609"),
    };

    /// <summary>§129 - Windows Command (52): chord, sequence, virtual-key, and lock commands. Partition of WindowsCommands by mechanism (WindowsAction.Chords/Sequences/VirtualKeys + Lock).</summary>
    public static readonly IReadOnlyList<ValueOption> WindowsCommandCommands = new[]
    {
        // Jendela
        new ValueOption("SwitchWindows", "S0610"),
        new ValueOption("SwitchWindowsBack", "S0611"),
        new ValueOption("TaskView", "S0612"),
        new ValueOption("NextWindow", "S0613"),
        new ValueOption("CloseWindow", "S0614"),
        new ValueOption("WindowMenu", "S0615"),
        new ValueOption("Maximize", "S0616"),
        new ValueOption("Minimize", "S0617"),
        new ValueOption("SnapLeft", "S0618"),
        new ValueOption("SnapRight", "S0619"),
        new ValueOption("SnapTopLeft", "S0620"),
        new ValueOption("SnapTopRight", "S0621"),
        new ValueOption("SnapBottomLeft", "S0622"),
        new ValueOption("SnapBottomRight", "S0623"),
        new ValueOption("MoveMonitorLeft", "S0624"),
        new ValueOption("MoveMonitorRight", "S0625"),
        new ValueOption("MinimizeAll", "S0626"),
        new ValueOption("RestoreAll", "S0627"),
        new ValueOption("MinimizeOthers", "S0628"),
        new ValueOption("ShowDesktop", "S0629"),
        new ValueOption("NewDesktop", "S0630"),
        new ValueOption("CloseDesktop", "S0631"),
        new ValueOption("PrevDesktop", "S0632"),
        new ValueOption("NextDesktop", "S0633"),
        // Sistem & shell
        new ValueOption("LockPC", "S0445"),
        new ValueOption("Run", "S0634"),
        new ValueOption("Explorer", "S0635"),
        new ValueOption("Settings", "S0636"),
        new ValueOption("Search", "S0637"),
        new ValueOption("ActionCenter", "S0638"),
        new ValueOption("NotificationCenter", "S0639"),
        new ValueOption("QuickLink", "S0640"),
        new ValueOption("Project", "S0641"),
        new ValueOption("Clipboard", "S0642"),
        new ValueOption("Emoji", "S0643"),
        new ValueOption("Snip", "S0644"),
        new ValueOption("GameBar", "S0645"),
        new ValueOption("Taskbar", "S0646"),
        new ValueOption("Tray", "S0647"),
        new ValueOption("TaskManager", "S0648"),
        new ValueOption("MagnifyIn", "S0649"),
        new ValueOption("MagnifyOut", "S0650"),
        new ValueOption("Narrator", "S0651"),
        new ValueOption("OnScreenKeyboard", "S0652"),
        new ValueOption("Dictation", "S0653"),
        // Browser (tombol virtual)
        new ValueOption("BrowserBack", "S0662"),
        new ValueOption("BrowserForward", "S0663"),
        new ValueOption("BrowserRefresh", "S0664"),
        new ValueOption("BrowserStop", "S0665"),
        new ValueOption("BrowserFavorites", "S0666"),
        new ValueOption("BrowserHome", "S0667"),
        // Luncurkan
        new ValueOption("Mail", "S0668"),
    };

    /// <summary>§129 - Windows Tools (42): ms-settings: pages (Uris) and system-tool launches (Launches). Partition of WindowsCommands by mechanism.</summary>
    public static readonly IReadOnlyList<ValueOption> WindowsToolsCommands = new[]
    {
        // Pengaturan cepat
        new ValueOption("SettingsDisplay", "S0654"),
        new ValueOption("SettingsSound", "S0655"),
        new ValueOption("SettingsNetwork", "S0656"),
        new ValueOption("SettingsBluetooth", "S0657"),
        new ValueOption("SettingsUpdate", "S0658"),
        new ValueOption("SettingsApps", "S0659"),
        new ValueOption("SettingsPersonalization", "S0660"),
        new ValueOption("SettingsPower", "S0661"),
        // §126 Windows Tools (exe/msc/cpl/cmd - peluncuran via mekanisme
        // pekerja C; alfabetis berdasarkan nama tampilan EN)
        new ValueOption("BackupRestore", "S1647"),
        new ValueOption("CommandPrompt", "S1648"),
        new ValueOption("ComputerManagement", "S1649"),
        new ValueOption("ControlPanel", "S1650"),
        new ValueOption("DiskDefragmenter", "S1651"),
        new ValueOption("DeviceManager", "S1652"),
        new ValueOption("DirectXDiagnostic", "S1653"),
        new ValueOption("DiskCleanup", "S1654"),
        new ValueOption("DiskManagement", "S1655"),
        new ValueOption("EventViewer", "S1656"),
        new ValueOption("GroupPolicyEditor", "S1657"),
        new ValueOption("Netstat", "S1658"),
        new ValueOption("NetworkConnections", "S1659"),
        new ValueOption("NetworkInformation", "S1660"),
        new ValueOption("PerformanceMonitor", "S1661"),
        new ValueOption("PowerOptions", "S1662"),
        new ValueOption("ProgramsAndFeatures", "S1663"),
        new ValueOption("ResourceMonitor", "S1664"),
        new ValueOption("SecurityMaintenance", "S1665"),
        new ValueOption("Services", "S1666"),
        new ValueOption("SharedFolders", "S1667"),
        new ValueOption("SystemConfiguration", "S1668"),
        new ValueOption("SystemInformation", "S1669"),
        new ValueOption("SystemProperties", "S1670"),
        new ValueOption("SystemRestore", "S1671"),
        new ValueOption("TaskScheduler", "S1672"),
        new ValueOption("WindowsFeatures", "S1673"),
        new ValueOption("WindowsPowerShell", "S1674"),
        // §127 admin tools (appended after the 28 §126 entries,
        // alphabetical by EN display name among the six; ordinary
        // Launches in WindowsAction.cs, elevation inherited from
        // the elevated app process - no runas verb)
        new ValueOption("CheckDisk", "S1675"),
        new ValueOption("DISM", "S1676"),
        new ValueOption("FlushDNS", "S1677"),
        new ValueOption("MSRT", "S1678"),
        new ValueOption("RegistryEditor", "S1679"),
        new ValueOption("SystemFileChecker", "S1680"),
    };

    /// <summary>Legacy union of the two §129 partitions (94), in grouped order: every Windows Command first, then every Windows Tool. Kept for legacy "windows" cards (editor panel + NormalizeWindows); new cards use one partition list. Derived, never hand-edited separately, so the union cannot drift from the partitions.</summary>
    public static readonly IReadOnlyList<ValueOption> WindowsCommands =
        WindowsCommandCommands.Concat(WindowsToolsCommands).ToList();

    public static readonly IReadOnlyList<ValueOption> DeckCommands = new[]
    {
        new ValueOption("next", "S0669"),
        new ValueOption("prev", "S0670"),
        new ValueOption("momentary", "S0671"),
        new ValueOption("goto", "S0672"),
        new ValueOption("home", "S0673"),
    };

    public static string FriendlyName(string actionId)
        // §129: legacy "windows" is not offered for new cards, so it is
        // not in Actions - but old cards still display its name, never
        // the raw id, on the dashboard second line.
        => actionId == "windows" ? Localizer.Get("S0577")
            : Actions.FirstOrDefault(a => a.ActionId == actionId)?.Display ?? actionId;

    public static readonly IReadOnlyList<ValueOption> MediaCommands = new[]
    {
        new ValueOption("PlayPause", "S0408"),
        new ValueOption("Stop", "S0674"),
        new ValueOption("Next", "S0675"),
        new ValueOption("Prev", "S0676"),
        new ValueOption("VolumeUp", "S0416"),
        new ValueOption("VolumeDown", "S0418"),
        new ValueOption("Mute", "S0677"),
        new ValueOption("MicMute", "S0678"),
        new ValueOption("MediaPlayer", "S0679"),
        // §156: dial volume - consumes the ScrollDial wheel delta; not
        // a fixed key like VolumeUp/VolumeDown. Appended last so the
        // existing order (and every index-free consumer) is untouched.
        new ValueOption("VolumeDial", "S1773"),
    };

    public static readonly IReadOnlyList<ValueOption> SteamCommands = new[]
    {
        new ValueOption("launch", "S1857"),          // Jalankan Game
        new ValueOption("bigpicture", "S1858"),      // Mode Big Picture
        new ValueOption("status", "S1862"),          // Status Profil
        new ValueOption("downloads", "S1863"),       // Halaman Unduhan
        new ValueOption("friends", "S1864"),         // Daftar Teman
        new ValueOption("openclose", "S1865"),       // Buka / Tutup Steam (Toggle)
        new ValueOption("exit", "S1866"),            // Tutup Steam
    };

    public static readonly IReadOnlyList<ValueOption> SteamBigPictureOptions = new[]
    {
        new ValueOption("toggle", "S1883"),          // Toggle (Buka / Keluar)
        new ValueOption("open", "S1859"),            // Masuk Big Picture
        new ValueOption("close", "S1860"),           // Keluar Big Picture
    };

    public static readonly IReadOnlyList<ValueOption> SteamStatuses = new[]
    {
        new ValueOption("online", "S1868"),       // Online
        new ValueOption("invisible", "S1869"),    // Gaib (Invisible)
        new ValueOption("away", "S1870"),         // Pergi (Away)
        new ValueOption("busy", "S1871"),         // Sibuk (Busy)
    };

    public static readonly IReadOnlyList<ValueOption> SteamRunningGameActions = new[]
    {
        new ValueOption("focus", "S1874"),        // Bawa ke Layar Depan
        new ValueOption("close", "S1875"),        // Tutup Game
    };

    /// <summary>Where an editor dropdown's items come from.</summary>
    public enum ObsNameSource { None, Scenes, Inputs, TextInputs, ScenesAndInputs, Collections, Profiles }
    /// <summary>Second-level dropdown, fed by the primary choice.</summary>
    public enum ObsSecondSource { None, SceneItems, Filters }

    /// <summary>One OBS command and the (up to) three editor slots it
    /// needs: primary name (dropdown), second name (cascading dropdown),
    /// free value (percent / text / path / hotkey name).</summary>
    public sealed record ObsCommandSpec(
        string Value, string DisplayKey,
        ObsNameSource NameSource, string NameLabelKey, string NameParam,
        ObsSecondSource SecondSource, string SecondLabelKey, string SecondParam,
        string? ValueLabelKey, string ValueParam, string ValueHintKey)
    {
        public string Display => Localizer.Get(DisplayKey);
        public string NameLabel => Localizer.Get(NameLabelKey);
        public string SecondLabel => Localizer.Get(SecondLabelKey);
        public string? ValueLabel => ValueLabelKey is null ? null : Localizer.Get(ValueLabelKey);
        public string ValueHint => Localizer.Get(ValueHintKey);
    }

    private static ObsCommandSpec Spec(
        string value, string display,
        ObsNameSource nameSource = ObsNameSource.None, string nameLabel = "", string nameParam = "",
        ObsSecondSource secondSource = ObsSecondSource.None, string secondLabel = "", string secondParam = "",
        string? valueLabel = null, string valueParam = "", string valueHint = "") =>
        new(value, display, nameSource, nameLabel, nameParam, secondSource, secondLabel, secondParam, valueLabel, valueParam, valueHint);

    public static readonly IReadOnlyList<ObsCommandSpec> ObsSpecs = new[]
    {
        Spec("SwitchScene", "S0680", ObsNameSource.Scenes, "S0681", "sceneName"),
        Spec("SetPreviewScene", "S0682", ObsNameSource.Scenes, "S0681", "sceneName"),
        Spec("ToggleStream", "S0683"),
        Spec("ToggleRecord", "S0684"),
        Spec("ToggleRecordPause", "S0685"),
        Spec("SaveReplay", "S0686"),
        Spec("ToggleReplayBuffer", "S0687"),
        Spec("ToggleVirtualCam", "S0688"),
        Spec("ToggleStudioMode", "S0689"),
        Spec("ToggleMute", "S0690", ObsNameSource.Inputs, "S0691", "sourceName"),
        Spec("SetInputVolume", "S0692", ObsNameSource.Inputs, "S0693", "inputName",
            valueLabel: "S0694", valueParam: "percent", valueHint: "S0695"),
        Spec("ToggleSceneItem", "S0696", ObsNameSource.Scenes, "S0681", "sceneName",
            ObsSecondSource.SceneItems, "S0697", "itemName"),
        Spec("ToggleFilter", "S0698", ObsNameSource.Inputs, "S0693", "inputName",
            ObsSecondSource.Filters, "S0699", "filterName"),
        Spec("RefreshBrowser", "S0700", ObsNameSource.Inputs, "S0701", "inputName"),
        Spec("MediaToggle", "S0702", ObsNameSource.Inputs, "S0703", "inputName"),
        Spec("SetText", "S0704", ObsNameSource.TextInputs, "S0705", "inputName",
            valueLabel: "S0706", valueParam: "text", valueHint: "S0707"),
        Spec("Screenshot", "S0708", ObsNameSource.ScenesAndInputs, "S0709", "sourceName",
            valueLabel: "S0710", valueParam: "filePath", valueHint: "S0711"),
        Spec("SetSceneCollection", "S0712", ObsNameSource.Collections, "S0713", "collectionName"),
        Spec("SetProfile", "S0714", ObsNameSource.Profiles, "S0715", "profileName"),
        Spec("TriggerHotkey", "S0716", valueLabel: "S0717", valueParam: "hotkeyName",
            valueHint: "S0718"),
    };

    public static ObsCommandSpec ObsSpec(string value)
        => ObsSpecs.FirstOrDefault(s => s.Value == value) ?? ObsSpecs[0];

    public static readonly IReadOnlyList<ValueOption> ObsCommands =
        ObsSpecs.Select(s => new ValueOption(s.Value, s.DisplayKey)).ToList();

    public static readonly IReadOnlyList<ValueOption> TriggerOptions = new[]
    {
        new ValueOption("Tap", "S0719", "S1766"),
        new ValueOption("Momentary", "S0720", "S1767"),
        new ValueOption("Repeat", "S0721", "S1768"),
        new ValueOption("LongPress", "S0722", "S1769"),
        new ValueOption("DoubleTap", "S0723", "S1770"),
        // §156: appended LAST, mirroring the TriggerKind enum append -
        // the editor round-trips the Value string through Enum.Parse,
        // so this entry is what makes a ScrollDial card buildable
        // without hand-edited JSON. Label/hint follow the §155 family.
        new ValueOption("ScrollDial", "S1771", "S1772"),
    };

    /// <summary>Visual key picker (editor): a full keyboard - main rows in
    /// physical order, navigation cluster, and the numpad. Each entry is
    /// (token inserted into the field, short label on the key cap).</summary>
    public static readonly IReadOnlyList<string> PickerFunctionKeys = new[]
    {
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
    };

    private static ValueOption K(string token, string label) => new(token, label);
    private static ValueOption[] Letters(string row) =>
        row.Select(c => K(c.ToString(), c.ToString())).ToArray();

    public static readonly IReadOnlyList<ValueOption> PickerRowNumbers = new[]
    {
        K("Backtick", "S0748"), K("1", "S0749"), K("2", "S0750"), K("3", "S0751"), K("4", "S0752"),
        K("5", "S0753"), K("6", "S0754"), K("7", "S0755"), K("8", "S0756"), K("9", "S0757"), K("0", "S0486"),
        K("Minus", "S0366"), K("Equals", "S0758"), K("Backspace", "S0759"),
    };

    public static readonly IReadOnlyList<ValueOption> PickerRowTop =
        new[] { K("Tab", "S0760") }
            .Concat(Letters("QWERTYUIOP"))
            .Concat(new[] { K("LeftBracket", "S0762"), K("RightBracket", "S0763"), K("Backslash", "S0764") })
            .ToArray();

    public static readonly IReadOnlyList<ValueOption> PickerNavKeys = new[]
    {
        K("PrintScreen", "S0765"), K("ScrollLock", "S0766"), K("Pause", "S0767"),
        K("Insert", "S0768"), K("Delete", "S0769"), K("Home", "S0770"), K("End", "S0771"),
        K("PageUp", "S0772"), K("PageDown", "S0773"),
        K("Up", "S0774"), K("Down", "S0775"), K("Left", "S0776"), K("Right", "S0777"),
    };

    public static readonly IReadOnlyList<ValueOption> PickerNumpadKeys = new[]
    {
        K("NumLock", "S0778"), K("Divide", "S0779"), K("Multiply", "S0268"), K("Subtract", "S0780"),
        K("Numpad7", "S0755"), K("Numpad8", "S0756"), K("Numpad9", "S0757"), K("Add", "S0100"),
        K("Numpad4", "S0752"), K("Numpad5", "S0753"), K("Numpad6", "S0754"), K("NumpadEnter", "S0031"),
        K("Numpad1", "S0749"), K("Numpad2", "S0750"), K("Numpad3", "S0751"), K("Decimal", "S0040"),
    };

    public static readonly IReadOnlyList<string> ColorPresets = new[]
    {
        "#2A2F3A", "#3090E0", "#2456C8", "#D92027",
        "#E85D04", "#1FA84A", "#0E7C86", "#B5179E",
    };

    public static readonly IReadOnlyList<string> IconPresets = AppIcons.TileKeys;
}
