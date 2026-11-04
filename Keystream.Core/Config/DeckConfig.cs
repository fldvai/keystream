using System.Linq;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Core.Devices;
using Keystream.Core.Icons;

namespace Keystream.Core.Config;

/// <summary>Config schema versions (B-06: CurrentVersion stays 1).
/// Bump <see cref="CurrentVersion"/> ONLY when a persisted-shape change
/// needs a migration step: a field removed, renamed, or retyped, or a
/// new field whose meaning an old loader would misread. A purely
/// ADDITIVE change - a new property whose initializer supplies the old
/// behaviour for files that lack it - does NOT bump: System.Text.Json
/// keeps the initializer for an absent property, so old files load
/// unchanged and new files stay readable by the old loader. Bumping
/// for an additive change would make the gate in ConfigStore.Load
/// reject a byte-compatible file on downgrade and fall back through
/// the backups to FreshDefault. Precedents, all without a bump:
/// BrowserProfileGroups, Macros, ChordMask, IsActive,
/// SwitchBackOnAutoSwitch, the additive DeckSettings fields, and IsE1
/// (Batch 1). When a bump IS needed, add the migration step in Load
/// (there is no version-gated migration today) and keep the gate:
/// loaders must refuse versions newer than they understand instead
/// of silently dropping fields (the legacy schema had no version at
/// all, finding B3-3). Every shape change since v1 has been additive
/// in the sense above, so no migration step exists to attach to v2.</summary>
public static class ConfigSchema
{
    public const int CurrentVersion = 1;
}

public enum TriggerKind
{
    Tap,
    LongPress,
    DoubleTap,
    /// <summary>Fires on key-DOWN and on every auto-repeat DOWN while
    /// held - how a real volume key behaves. Added in UI v2.3 after the
    /// user found Tap (fires on UP, once) miserable for volume control.
    /// When Repeat is bound on a key it owns the press entirely.</summary>
    Repeat,
    /// <summary>Fires once on key-DOWN; the matching key-UP fires
    /// MomentaryRelease. The pair expresses hold-to-activate semantics -
    /// the Layer Modifier (v3.3.0): hold a key to visit a layer, release
    /// to return. Momentary owns the press entirely, like Repeat.</summary>
    Momentary,
    /// <summary>The key-UP counterpart of Momentary. Never bound
    /// directly; the resolver/dispatcher pair it with the Momentary
    /// binding that started the hold.</summary>
    MomentaryRelease,
    /// <summary>Fires once on key-DOWN; the matching key-UP fires
    /// ScrollDialRelease. The pair expresses hold-to-capture semantics -
    /// the scroll dial (§156): hold a key to capture the mouse wheel
    /// (wheel deltas are coalesced and delivered as ScrollDial jobs),
    /// release to restore the wheel. ScrollDial owns the press entirely,
    /// like Momentary, and wins over Momentary when both are bound on
    /// one key (checked first in TriggerEngine).</summary>
    ScrollDial,
    /// <summary>The key-UP counterpart of ScrollDial. Never bound
    /// directly; the resolver/dispatcher pair it with the ScrollDial
    /// binding that started the hold.</summary>
    ScrollDialRelease,
}

/// <summary>K4 - the versioned persisted configuration (PRD §9 shape).
/// Plain serializable DTOs: NO UI/observable types in the persisted model
/// (the legacy config serialized CommunityToolkit ObservableObjects, tying
/// the file format to the view-model layer).</summary>
public sealed class DeckConfig
{
    public int SchemaVersion { get; set; } = ConfigSchema.CurrentVersion;

    public DeviceBinding DeckDevice { get; set; } = new();

    public List<ProfileConfig> Profiles { get; set; } = new();

    public string ActiveProfileId { get; set; } = "";

    public DeckSettings Settings { get; set; } = new();

    public ProfileConfig? ActiveProfile
        => Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();

    /// <summary>The GLOBAL page (UI v2.8, the user's scope design): one
    /// page living at config level - outside every profile. Its bindings
    /// overlay every page of every profile (key-level priority in the
    /// resolver). Edited exactly like a profile page; not part of profile
    /// exports. Replaces the v2.7 per-profile nav slots: those migrate
    /// here on first load (see GlobalMigration).</summary>
    public PageConfig GlobalPage { get; set; } = new() { Name = Localizer.Get("S1015") };

    /// <summary>Browser profile groups (playlists, v4.5.0): named sets of
    /// browser profiles opened with one tile press. Entries reference a
    /// browser by exe path + engine + profile key, so a group survives
    /// renames of the profile's display name.</summary>
    public List<BrowserProfileGroup> BrowserProfileGroups { get; set; } = new();

    /// <summary>Macro library (§62): named step sequences stored ONCE,
    /// globally - every profile's cards reference a macro by Id, so a
    /// macro created in the sidebar MACRO page appears in every
    /// profile's card action picker. Cards store only the macro Id
    /// (see MacroAction); a rename never breaks a card. Macros are NOT
    /// part of profile exports (global library, like icon files) - v1
    /// limitation, documented in CARA_JALANKAN. Additive property, no
    /// schema bump (same precedent as BrowserProfileGroups).</summary>
    public List<MacroConfig> Macros { get; set; } = new();

    /// <summary>Browsers the user added manually (portable installs and
    /// browsers the auto-detector does not know). Merged with detected
    /// browsers in the editor and the group manager.</summary>
    public List<CustomBrowserConfig> CustomBrowsers { get; set; } = new();

    /// <summary>Tracked browser windows, one per (browser, profile): the
    /// window Keystream opened for that profile, so a later tile press
    /// can focus or close it instead of opening a duplicate. Persisted
    /// so tracking survives a Keystream restart; every entry is
    /// revalidated (window still exists, same process, same start time)
    /// before use, and a stale entry simply means "open a new window".</summary>
    public List<BrowserWindowTracking> BrowserWindowTracking { get; set; } = new();
}

/// <summary>The saved deck identity. Kind is stored as the DeviceIdKind
/// name so a file can never be misread across identity namespaces;
/// <see cref="RequiresReidentification"/> is set by the migrator for legacy
/// values whose namespace cannot be proven.</summary>
public sealed class DeviceBinding
{
    public string Kind { get; set; } = nameof(DeviceIdKind.LegacyUnverified);
    public string Value { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public bool RequiresReidentification { get; set; } = true;

    public DeviceId ToDeviceId()
    {
        var kind = Enum.TryParse<DeviceIdKind>(Kind, ignoreCase: true, out var parsed)
            ? parsed
            : DeviceIdKind.LegacyUnverified;
        return kind switch
        {
            DeviceIdKind.InterceptionHardwareId => DeviceId.FromInterceptionHardwareId(Value),
            DeviceIdKind.RawInputDevicePath => DeviceId.FromRawInputPath(Value),
            _ => DeviceId.FromLegacyUnverified(Value),
        };
    }
}

public sealed class DeckSettings
{
    public string PauseHotkey { get; set; } = "F9";
    public bool IsGlobalEnabled { get; set; } = true;
    public string ToggleGlobalHotkey { get; set; } = "";
    public string NextProfileHotkey { get; set; } = "";
    public string PrevProfileHotkey { get; set; } = "";
    /// <summary>§137 (Worker A): master shortcuts for macro recording /
    /// playback. "" = unassigned (never a default chord - an invented
    /// default could collide with the user's apps); older configs
    /// deserialize to "" via this initializer, like the hotkeys above.
    /// The LogLevel field in this class is Worker C's seam.</summary>
    public string ToggleRecordingHotkey { get; set; } = "";
    public string TogglePlaybackHotkey { get; set; } = "";
    public string PauseResumePlaybackHotkey { get; set; } = "";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool PlaySoundOnPress { get; set; } = true;
    public string ObsHost { get; set; } = "ws://127.0.0.1:4455"; // Literal — never localized (S0827 is display text only).
    public string ObsPassword { get; set; } = "";

    /// <summary>§147 (Worker A): Streamer.bot WebSocket Server settings.
    /// Additive only: older configs deserialize to these defaults
    /// (127.0.0.1:8080, endpoint "/"), like the OBS fields above.
    /// The password is stored here like ObsPassword and is NEVER
    /// written to logs or UI text (see StreamerBotClient).</summary>
    public string StreamerBotHost { get; set; } = "127.0.0.1";
    public int StreamerBotPort { get; set; } = 8080;
    public string StreamerBotEndpoint { get; set; } = "/";
    public string StreamerBotPassword { get; set; } = "";
    public string ActivePageId { get; set; } = "";

    /// <summary>When true, the editor's "key already used" popup no longer
    /// appears (the user ticked "jangan tampilkan lagi"); the ⚠ tile badge
    /// remains. Can be re-enabled in Settings.</summary>
    public bool SuppressConflictWarning { get; set; }

    /// <summary>Upload-icon size limit in kilobytes (v3.4.0, editable in
    /// Settings → Ikon Upload). Files larger than this are refused at
    /// import, before any conversion runs.</summary>
    public int IconUploadMaxKb { get; set; } = 2048;

    /// <summary>When true (v3.5.0, opt-in; §150: default ON - checked
    /// by default, an explicit stored false is still honoured), the
    /// editor automatically fetches a tile's icon - the website
    /// favicon for Buka URL, the application's own icon for Buka
    /// Aplikasi - but only while the tile has no manually picked
    /// icon. Manual "Ambil ikon" buttons in the editor work
    /// regardless of this setting.</summary>
    public bool AutoFetchIcons { get; set; } = true;

    /// <summary>UI language (§86, wave-1 multi-language): "en" (default),
    /// "id", "es", "pt", "fr", "de", "ru", "zh", "ja", "ko", "ar", or "tr".
    /// Toggled by the title-bar globe button; applied at startup before any
    /// window is created so the first frame already uses it. Additive:
    /// older configs without this member deserialize to "en". No migration.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Dashboard view mode (§90): "grid" (default) or "list".
    /// Segmented toggle in the title row; global (not per-profile) and
    /// persisted like Language. Additive: older configs deserialize
    /// to "grid".</summary>
    public string DashboardViewMode { get; set; } = "grid";

    /// <summary>Diagnostic log level (§137): "Debug", "Information"
    /// (default), "Warning", or "Error" - the canonical names in
    /// <see cref="LogLevel"/>. Additive: older configs without this
    /// member deserialize to "Information". Unknown persisted values
    /// fall back to Information at apply time, never throw.</summary>
    public string LogLevel { get; set; } = "Information";

    // Pemeriksaan pembaruan otomatis saat startup. Default AKTIF:
    // memeriksa rilis baru secara berkala tanpa mengganggu bila sudah terbaru.
    public bool AutoCheckUpdates { get; set; } = true;

    // §171: hasil pemeriksaan rilis terakhir yang dicatat.
    // Tag terakhir yang dilihat (ditampilkan/dicatat) - dipakai
    // pemeriksaan otomatis untuk membedakan rilis baru dari yang
    // sudah diketahui; "baru" berarti tag berubah saja, karena
    // aplikasi tidak punya penomoran versi formal (selalu v1.0.0).
    public string LastSeenReleaseTag { get; set; } = "";

    public string LastReleasePageUrl { get; set; } = "";

    // Tanggal terbit rilis (ISO 8601 apa adanya dari GitHub API),
    // ditampilkan sebagai tanggal saja di Pengaturan.
    public string LastReleasePublishedAt { get; set; } = "";

    // Kutipan catatan rilis, dipotong 500 karakter seperti dialog
    // (§166) agar config.json tidak membengkak.
    public string LastReleaseNotes { get; set; } = "";

    // Waktu pemeriksaan terakhir (ISO 8601 UTC); kosong = belum
    // pernah diperiksa.
    public string LastUpdateCheckUtc { get; set; } = "";
}

public sealed class ProfileConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Localizer.Get("S1016");

    /// <summary>Any UI rendering of a profile shows its name, never the
    /// raw type name (seen leaking into the profile dropdown).</summary>
    public override string ToString() => Name;
    public List<string> AutoSwitchExe { get; set; } = new();
    /// <summary>Switch-back (opt-in, default OFF): when an auto-switch
    /// TO this profile fires and this flag is ON, the host remembers
    /// the (profile, page) active immediately before the switch and
    /// returns to it when the trigger app leaves the foreground,
    /// unless the user switched manually meanwhile. Missing in old
    /// configs → false (no migration): System.Text.Json leaves the
    /// initializer in place for absent properties. In-memory armed
    /// state is never serialized (see SwitchBackState).</summary>
    public bool SwitchBackOnAutoSwitch { get; set; } = false;
    public List<PageConfig> Pages { get; set; } = new();

    /// <summary>Global navigation bindings (UI v2.7): profile-level, NOT
    /// part of any page. The resolver overlays them on every page with
    /// key-level priority - a nav key shadows any page binding for that
    /// key on every page. This fixes the stranded-navigation flaw: a
    /// page-local "next page" binding dies the moment the page changes;
    /// navigation must live outside the thing it leaves.</summary>
    public List<BindingConfig> NavBindings { get; set; } = new();
}

public sealed class PageConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Localizer.Get("S1017");
    /// <summary>§75: cap template asal halaman ini ("media",
    /// "produktivitas", "navigasi", "stream"). Kosong = bukan hasil
    /// template (atau hasil template lama sebelum cap ada - aturan
    /// tolak-duplikat mencocokkan "Template Stream" lama lewat nama).
    /// Cap bertahan walau halaman diganti namanya oleh user.</summary>
    public string TemplateId { get; set; } = "";
    /// <summary>Mode-level auto-switch triggers (Edit Mode modal):
    /// foreground process names that switch to THIS mode, mirroring
    /// <see cref="ProfileConfig.AutoSwitchExe"/> at profile level.
    /// Missing in old configs → empty (no migration): System.Text.Json
    /// leaves the initializer in place for absent properties, so old
    /// configs load with zero mode triggers.</summary>
    public List<string> AutoSwitchExe { get; set; } = new();
    /// <summary>Switch-back (opt-in, default OFF): same rule as
    /// <see cref="ProfileConfig.SwitchBackOnAutoSwitch"/>, read from
    /// THIS page when an auto-switch fires to it. Missing in old
    /// configs → false (no migration). Duplicates copy it verbatim
    /// with the triggers (CardTransferService.DuplicatePage).</summary>
    public bool SwitchBackOnAutoSwitch { get; set; } = false;
    public List<BindingConfig> Bindings { get; set; } = new();

    public override string ToString() => Name;
}

/// <summary>A key binding. The PRD's own PANIC example is a multi-step
/// sequence; the legacy model (single ActionType + flat params) could not
/// express it, so steps are a first-class list here from schema v1.</summary>
public sealed class BindingConfig
{
    public string KeyId { get; set; } = "";
    /// <summary>Physical key name as reported by the OS at capture time
    /// (e.g. "Num 7", "Enter"). Kept separate from Label so renaming a tile
    /// never loses which physical key it is (UI v2 tile badge).</summary>
    public string KeyName { get; set; } = "";
    public ushort ScanCode { get; set; }
    public bool IsE0 { get; set; }
    /// <summary>A-07: E1 prefix bit, part of this binding's KEY together
    /// with ScanCode/IsE0 (Pause/Break is the classic E1 key). Default
    /// false, so legacy configs without this member deserialize as
    /// non-E1 (the property initializer is kept by System.Text.Json);
    /// no schema bump, same precedent as ChordMask/IsActive.</summary>
    public bool IsE1 { get; set; }
    /// <summary>§168: chord-modifier mask, part of this binding's KEY
    /// together with ScanCode/IsE0. 0 = plain single key; non-zero =
    /// the card fires only while these modifiers (Ctrl=1, Alt=2,
    /// Shift=4, see <see cref="Input.ChordMask"/>) are held when the
    /// main key goes down. Default 0, so legacy configs without this
    /// member deserialize as plain single keys (the property
    /// initializer is kept by System.Text.Json).</summary>
    public byte ChordMask { get; set; }
    public uint VirtualKey { get; set; }
    /// <summary>§132: false = card temporarily inactive - it does not
    /// execute and is not counted as a duplicate (see
    /// <see cref="Engine.BindingResolver"/>). Default true so legacy
    /// configs without this member deserialize as active (the
    /// property initializer is kept by System.Text.Json).</summary>
    public bool IsActive { get; set; } = true;
    public string Label { get; set; } = "Unassigned"; // Literal — never localized; compared literally by BindingEditorViewModel auto-naming.
    public string Icon { get; set; } = "";
    /// <summary>Icon fetched automatically (website favicon / app
    /// icon), stored separately from Icon since v3.6.0 so the editor
    /// can tell an auto icon from a user upload. Render priority:
    /// upload file (Icon) > auto file (AutoIcon) > catalog key
    /// (Icon) > default. Empty = none.</summary>
    public string AutoIcon { get; set; } = "";
    public string BackgroundColor { get; set; } = "#1E222D";
    public TriggerKind Trigger { get; set; } = TriggerKind.Tap;
    public List<ActionStep> Steps { get; set; } = new();

    /// <summary>Multi-key combinations on this deck keyboard (e.g. Hold F1 + Key C).
    /// Fires a separate action while this key is held down and another deck key is pressed.</summary>
    public List<KeyCombinationConfig> Combinations { get; set; } = new();

    public bool IsAssigned => Steps.Count > 0 || Combinations.Count > 0;

    /// <summary>Menyalin semua field dari binding lain KE objek ini
    /// (tanpa ganti referensi - tile di UI membungkus objek ini).
    /// Dipakai pemulihan snapshot editor/reset: objek live harus
    /// kembali ke nilai lama, bukan diganti objek baru.</summary>
    public void CopyFrom(BindingConfig source)
    {
        foreach (var prop in typeof(BindingConfig).GetProperties())
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(this, prop.GetValue(source));
    }

    /// <summary>One-time migration for bindings written before v3.6.0,
    /// when auto-fetched icons lived in Icon and were indistinguishable
    /// from uploads: a file icon on a launcher/link binding moves to
    /// AutoIcon - the tile looks identical, and future app switches
    /// refresh it. Uploads on other action types are untouched
    /// (auto-fetch never wrote to those). Idempotent: re-running
    /// finds Icon no longer a file icon and returns false.</summary>
    public bool MigrateLegacyAutoIcon()
    {
        if (!string.IsNullOrWhiteSpace(AutoIcon)) return false;
        if (!IconReference.IsFileIcon(Icon)) return false;
        string? actionId = Steps.FirstOrDefault()?.ActionId;
        if (actionId != "launcher" && actionId != "link") return false;
        AutoIcon = Icon;
        Icon = "";
        return true;
    }
}

/// <summary>Defines a combination where this parent key is held, and a second deck key is pressed.</summary>
public sealed class KeyCombinationConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ushort SecondScanCode { get; set; }
    public bool SecondIsE0 { get; set; }
    public string SecondKeyName { get; set; } = "";
    public string Label { get; set; } = "";
    public string ActionId { get; set; } = "hotkey";
    public string ActionTarget { get; set; } = "";
    public List<ActionStep> Steps { get; set; } = new();

    public ActionStep ToStep()
    {
        var p = new JsonObject();
        switch (ActionId)
        {
            case "hotkey": p["keys"] = ActionTarget; break;
            case "launcher": p["target"] = ActionTarget; break;
            case "link": p["url"] = ActionTarget; break;
            case "media": p["command"] = ActionTarget; break;
            case "obs": p["command"] = ActionTarget; break;
            case "basic": p["command"] = ActionTarget; break;
            default: p["target"] = ActionTarget; break;
        }
        return new ActionStep { ActionId = ActionId, Params = p };
    }
}

public sealed class ActionStep
{
    public string ActionId { get; set; } = "";
    public JsonObject Params { get; set; } = new();
    public int DelayAfterMs { get; set; }
}

/// <summary>One saved macro (§62): a named, repeatable list of action
/// steps plus its repeat count. v1 steps are hotkey chords created in
/// the MACRO page, but the step list is List&lt;ActionStep&gt; from day
/// one so general-action steps (Fase 3) need no data migration.
/// Cycles lives on the macro (like the example dialog), not on the
/// card - one macro, one repeat count, everywhere it is used.</summary>
public sealed class MacroConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Cycles { get; set; } = 1;
    // §69: playback speed multiplier. 1 = recorded timing, 2 = twice
    // as fast (every delay halved), 0.5 = half speed (every delay
    // doubled). Old configs lack the field → System.Text.Json keeps
    // this initializer (1.0), so pre-§69 macros play unchanged.
    public double SpeedMultiplier { get; set; } = 1.0;
    public List<ActionStep> Steps { get; set; } = new();

    /// <summary>Deep copy (Params JsonObjects are cloned) - the macro
    /// manager edits the copy; the library entry only changes on
    /// Simpan, so Batal never needs a restore.</summary>
    public MacroConfig Copy() => new()
    {
        Id = Id,
        Name = Name,
        Cycles = Cycles,
        SpeedMultiplier = SpeedMultiplier,
        Steps = Steps.Select(s => new ActionStep
        {
            ActionId = s.ActionId,
            Params = (JsonObject)s.Params.DeepClone(),
            DelayAfterMs = s.DelayAfterMs,
        }).ToList(),
    };
}

/// <summary>Pure lookup for the macro manager's delete guard: every card
/// whose steps reference a macro, as "Profil / Mode / Tile" labels.
/// Covers profile pages, the Global page, and per-profile nav bindings.
/// No UI here so the rule is unit-testable.</summary>
public static class MacroUsage
{
    public static IReadOnlyList<string> FindUsages(DeckConfig config, string macroId)
    {
        var found = new List<string>();
        void Scan(IEnumerable<BindingConfig> bindings, string where)
        {
            foreach (var b in bindings)
            {
                foreach (var step in b.Steps)
                {
                    if (!string.Equals(step.ActionId, "macro", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!step.Params.TryGetPropertyValue("macroId", out var node) || node is null)
                        continue;
                    string? id = null;
                    if (node is JsonValue v) v.TryGetValue<string>(out id);
                    if (id == macroId)
                        found.Add($"{where} / {b.Label}");
                }
            }
        }
        foreach (var profile in config.Profiles)
        {
            foreach (var page in profile.Pages)
                Scan(page.Bindings, $"{profile.Name} / {page.Name}");
            Scan(profile.NavBindings, $"{profile.Name} / {Localizer.Get("S1020")}");
        }
        Scan(config.GlobalPage.Bindings, $"Global / {config.GlobalPage.Name}");
        return found;
    }
}

/// <summary>One browser profile group (playlist): an ordered list of
/// profiles opened by one tile press. Mode is "Simultaneous" (all at once,
/// staggered 150 ms) or "Sequential" (one by one, DelayMs apart).</summary>
public sealed class BrowserProfileGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "Simultaneous"; // Literal engine value — never localized (display label is S0790).
    public int DelayMs { get; set; } = 800;
    /// <summary>What a group press does with already-open profiles:
    /// "New" (always open a new window) or "Focus" (focus the tracked
    /// window, open only the missing ones). No "Toggle": closing a
    /// whole group at once is ambiguous and destructive.</summary>
    /// Literal engine value — never localized (the display label is S0605).
    public string OpenMode { get; set; } = "New";
    public List<BrowserGroupEntry> Entries { get; set; } = new();
}

/// <summary>One entry of a browser profile group. Engine is "Chromium"
/// or "Firefox". ProfileKey is the Chromium profile directory name
/// ("Default", "Profile 1") or the Firefox profile name from profiles.ini.
/// ProfileDisplay is only a label for the manager; launches use the key.
/// ProfilePath is empty for registered profiles and holds the full folder
/// path for manual (CUSTOM) entries; a non-empty value marks the entry
/// as manual and is also its window-tracking key. UserDataDir is the
/// Chromium "User Data" folder for custom-browser entries (scanned
/// portables); empty means "the exe's default location". Firefox
/// entries ignore it (see BrowserLauncher).</summary>
public sealed class BrowserGroupEntry
{
    public string BrowserExe { get; set; } = "";
    public string Engine { get; set; } = "Chromium"; // Literal engine value — never localized (S0813/S0814 are display labels only).
    public string ProfileKey { get; set; } = "";
    public string ProfileDisplay { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public string UserDataDir { get; set; } = "";
    /// <summary>URL opsional yang dibuka bersama profil ini (fase 2 dari
    /// riset temuan 1). Kosong = halaman awal profil. Aturan sama seperti
    /// tile tunggal: hanya dipakai saat benar-benar membuka; pada Focus
    /// yang mengenai jendela yang ada, URL diabaikan.</summary>
    public string Url { get; set; } = "";
}

/// <summary>One tracked browser window. Hwnd is the top-level window
/// handle (IntPtr bits); ProcessId + ProcessStartTicks identify the
/// owning process, because a handle value can be recycled by Windows
/// after its window closes.</summary>
public sealed class BrowserWindowTracking
{
    public string BrowserExe { get; set; } = "";
    public string ProfileKey { get; set; } = "";
    public long Hwnd { get; set; }
    public int ProcessId { get; set; }
    public long ProcessStartTicks { get; set; }
}

/// <summary>A browser the user added manually. Engine is "Chromium" or
/// "Firefox". UserDataDir is the Chromium "User Data" folder or the
/// Firefox folder containing profiles.ini; empty means "try the defaults
/// next to the exe" at discovery time.</summary>
public sealed class CustomBrowserConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Engine { get; set; } = "Chromium"; // Literal engine value — never localized (S0813/S0814 are display labels only).
    public string UserDataDir { get; set; } = "";
}
