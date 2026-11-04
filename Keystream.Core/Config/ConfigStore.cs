using System.Text.Json;
using System.Text.Json.Nodes;
using Keystream.Core.Input;
using Keystream.Core.Localization;
using System.Text.Json.Serialization;

namespace Keystream.Core.Config;

public enum LoadSource
{
    Main,
    Backup1,
    Backup2,
    Backup3,
    MigratedLegacy,
    FreshDefault,
}

public sealed record LoadResult(DeckConfig Config, LoadSource Source, string? Note);

/// <summary>
/// K4 (storage part) - durable config persistence. Fixes audit findings
/// B3-1/B3-2: the legacy service rotated .bak1-3 on every save but its Load
/// NEVER read them, so one corrupt byte cost the user everything; and the
/// legacy save was a plain overwrite (a crash mid-save truncated the only
/// copy). Here: saves go to a temp file, are flushed to disk, the temp file replaces the main file, and only then are backups
/// rotated from a snapshot of the previous main file (B-07). Loads walk
/// main → bak1 → bak2 → bak3 and report honestly where the config came
/// from.
/// </summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    public string MainPath => Path.Combine(_directory, "config.json");
    private string BackupPath(int n) => Path.Combine(_directory, $"config.json.bak{n}");

    public ConfigStore(string directory) => _directory = directory;

    public static ConfigStore ForCurrentUser()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(root, "Keystream");
        return new ConfigStore(dir);
    }

    public void Save(DeckConfig config)
    {
        Directory.CreateDirectory(_directory);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        string tempPath = MainPath + ".tmp";
        // B-07: snapshot of the pre-save main file (see Save below).
        // Not a Load candidate: Load only reads config.json / .bak1-3,
        // so a leftover staging file can never be mistaken for a backup.
        string stagingPath = MainPath + ".bakstage";

        // Write + fsync the temp file BEFORE touching the live one.
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(fs, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
            }
            fs.Flush(flushToDisk: true);
        }

        // B-07: snapshot the live main BEFORE committing. If the
        // Move below throws, backups must be untouched (previously
        // RotateBackups ran first, so a failed commit had already
        // discarded bak3 and shifted the chain). A failed snapshot
        // likewise aborts before any live file is touched.
        if (File.Exists(MainPath))
        {
            try { File.Copy(MainPath, stagingPath, overwrite: true); }
            catch { try { File.Delete(stagingPath); } catch { } throw; }
        }
        else if (File.Exists(stagingPath))
        {
            // No live main: no predecessor to preserve, and a stale
            // staging file from an interrupted earlier Save must not
            // become bak1 on this Save. A failed delete aborts here,
            // before the commit, rather than promoting stale data.
            File.Delete(stagingPath);
        }

        File.Move(tempPath, MainPath, overwrite: true);
        // Commit succeeded: only now rotate, from the snapshot.
        RotateBackups(stagingPath);
    }

    // B-07: <paramref name="previousMainPath"/> is the staging snapshot
    // of the main file as it was BEFORE this Save's commit (MainPath
    // itself is already the new content by the time this runs, so
    // copying it would back up the just-written file). Moving the
    // snapshot consumes it, leaving no staging file behind.
    private void RotateBackups(string previousMainPath)
    {
        if (File.Exists(BackupPath(2))) File.Move(BackupPath(2), BackupPath(3), overwrite: true);
        if (File.Exists(BackupPath(1))) File.Move(BackupPath(1), BackupPath(2), overwrite: true);
        if (File.Exists(previousMainPath)) File.Move(previousMainPath, BackupPath(1), overwrite: true);
    }

    public LoadResult Load()
    {
        var attempts = new (string Path, LoadSource Source)[]
        {
            (MainPath, LoadSource.Main),
            (BackupPath(1), LoadSource.Backup1),
            (BackupPath(2), LoadSource.Backup2),
            (BackupPath(3), LoadSource.Backup3),
        };

        // Versi-lebih-baru dulu melempar dan mengunci pengguna dari
        // aplikasinya sendiri padahal backup lama yang valid tersedia
        // (§45 SIM-01). Kini diperlakukan seperti kandidat corrupt:
        // lanjut ke backup berikutnya, dengan catatan yang jujur.
        bool newerVersionSeen = false;
        foreach (var (path, source) in attempts)
        {
            if (!File.Exists(path)) continue;
            string text;
            try { text = File.ReadAllText(path); }
            catch { continue; }

            if (LegacyMigrator.LooksLegacy(text))
            {
                var (migrated, report) = LegacyMigrator.Migrate(text);
                Normalize(migrated);
                return new LoadResult(migrated, LoadSource.MigratedLegacy,
                    Localizer.Format("S1011", Path.GetFileName(path), report.ProfilesMigrated, report.BindingsMigrated, report.Warnings.Count));
            }

            try
            {
                var config = JsonSerializer.Deserialize<DeckConfig>(text, JsonOptions);
                if (config is null) continue;
                if (config.SchemaVersion > ConfigSchema.CurrentVersion)
                {
                    newerVersionSeen = true;
                    continue;
                }
                Normalize(config);
                string? note = source == LoadSource.Main ? null
                    : newerVersionSeen
                        ? Localizer.Format("S1012", Path.GetFileName(path))
                        : Localizer.Format("S1013", Path.GetFileName(path));
                return new LoadResult(config, source, note);
            }
            catch { /* corrupt candidate: try the next backup */ }
        }

        return new LoadResult(CreateDefault(), LoadSource.FreshDefault,
            Localizer.Get("S1014"));
    }

    /// <summary>JSON null eksplisit menimpa initializer (System.Text.Json):
    /// {"profiles": null} dulu lolos Load lalu NullReference di ctor
    /// AppHost (§45 SIM-02). Normalisasi mengembalikan daftar kosong
    /// agar aplikasi tetap dibuka, bukan shutdown tanpa petunjuk.</summary>
    private static void Normalize(DeckConfig config)
    {
        config.Profiles ??= new();
        config.Profiles.RemoveAll(p => p is null);
        config.Settings ??= new();
        config.GlobalPage ??= new();
        config.BrowserProfileGroups ??= new();
        config.BrowserProfileGroups.RemoveAll(g => g is null);
        config.DeckDevice ??= new();
        config.DeckDevice.Value ??= "";
        config.DeckDevice.FriendlyName ??= "";
        foreach (var profile in config.Profiles)
            NormalizeProfile(profile);
        NormalizePage(config.GlobalPage);
        foreach (var group in config.BrowserProfileGroups)
            NormalizeGroup(group);
        config.Macros ??= new();
        config.Macros.RemoveAll(m => m is null);
        foreach (var macro in config.Macros)
            NormalizeMacro(macro);
        config.CustomBrowsers ??= new();
        config.CustomBrowsers.RemoveAll(c => c is null);
        config.BrowserWindowTracking ??= new();
        config.BrowserWindowTracking.RemoveAll(t => t is null);
    }

    /// <summary>A-02: satu-satunya normalisasi profil — dipakai jalur
    /// Load (via Normalize) dan jalur impor (via ProfilePackage.Parse),
    /// agar keduanya tidak bisa divergen.</summary>
    internal static void NormalizeProfile(ProfileConfig profile)
    {
        profile.Pages ??= new();
        profile.Pages.RemoveAll(p => p is null);
        profile.AutoSwitchExe ??= new();
        profile.AutoSwitchExe.RemoveAll(s => s is null);
        profile.NavBindings ??= new();
        profile.NavBindings.RemoveAll(b => b is null);
        foreach (var page in profile.Pages)
            NormalizePage(page);
        foreach (var binding in profile.NavBindings)
            NormalizeBinding(binding);
    }

    /// <summary>A-02: satu-satunya normalisasi halaman — dipakai jalur
    /// Load dan impor (profil + bagian "global" paket).</summary>
    internal static void NormalizePage(PageConfig page)
    {
        page.Bindings ??= new();
        page.Bindings.RemoveAll(b => b is null);
        page.AutoSwitchExe ??= new();
        page.AutoSwitchExe.RemoveAll(s => s is null);
        foreach (var binding in page.Bindings)
            NormalizeBinding(binding);
    }

    private static void NormalizeBinding(BindingConfig binding)
    {
        binding.Steps ??= new();
        binding.Steps.RemoveAll(s => s is null);
        foreach (var step in binding.Steps)
            NormalizeStep(step);
        binding.Combinations ??= new();
        binding.Combinations.RemoveAll(c => c is null);
        foreach (var combo in binding.Combinations)
        {
            combo.Steps ??= new();
            combo.Steps.RemoveAll(s => s is null);
            foreach (var step in combo.Steps)
                NormalizeStep(step);
        }
    }

    private static void NormalizeStep(ActionStep step)
    {
        step.Params ??= new JsonObject();
    }

    private static void NormalizeMacro(MacroConfig macro)
    {
        macro.Steps ??= new();
        macro.Steps.RemoveAll(s => s is null);
        foreach (var step in macro.Steps)
            NormalizeStep(step);
    }

    private static void NormalizeGroup(BrowserProfileGroup group)
    {
        group.Entries ??= new();
        group.Entries.RemoveAll(e => e is null);
    }

    public static DeckConfig CreateDefault()
    {
        var profile = new ProfileConfig();
        return new DeckConfig
        {
            Profiles = { profile },
            ActiveProfileId = profile.Id,
            Settings = { ActivePageId = "" },
            GlobalPage = DefaultGlobalPage(),
        };
    }

    /// <summary>Default Global cards (first start only - CreateDefault
    /// runs solely when Load finds no config/backups, so an existing
    /// config, even one with an empty Global page, NEVER gains these).
    /// Order is the user's order. KeyName is hardcoded, not read from
    /// KeyNames.GetDisplayName: that helper is user32 GetKeyNameText in
    /// Keystream.Windows, unreachable from Core - and these ARE its
    /// outputs for these keys ("Left"/"Right"/"Space"/"Alt"; the tile's
    /// KeyCapBadge uppercases them to LEFT/RIGHT/SPACE/ALT, and chord
    /// badges to CTRL+SPACE/ALT+SPACE, exactly like captured cards).
    /// Labels are FIXED ENGLISH (card labels are user data, not UI
    /// strings - no localization, no new strings). Icon starts empty:
    /// AppHost imports the 5 user-supplied default icons into the Icons
    /// folder on first run and fills Icon for the 5 window/desktop
    /// cards; the 2 Mode cards keep the default tile icon. Triggers
    /// are Tap (the default); BackgroundColor is the default #1E222D.
    /// Keys are distinct card identities (ScanCode, IsE0, ChordMask):
    /// Left Alt (0x38, no E0) vs Right Alt (0x38+E0) do not clash, and
    /// plain Space vs Ctrl+Space vs Alt+Space coexist (§168: a chord
    /// never fires the plain single).</summary>
    private static PageConfig DefaultGlobalPage() => new()
    {
        Name = Localizer.Get("S1015"),
        Bindings =
        {
            DefaultGlobalCard("K-4BE", "Left", 0x4B, true, ChordMask.None,
                "Previous Page", "deck", "prev"),
            DefaultGlobalCard("K-4DE", "Right", 0x4D, true, ChordMask.None,
                "Next Page", "deck", "next"),
            DefaultGlobalCard("K-39", "Space", 0x39, false, ChordMask.None,
                "Change Windows", "windows-command", "switchwindows"),
            DefaultGlobalCard("K-39", "Space", 0x39, false, ChordMask.Ctrl,
                "Change Windows Back", "windows-command", "switchwindowsback"),
            DefaultGlobalCard("K-38E", "Alt", 0x38, true, ChordMask.None,
                "New Desktop", "windows-command", "newdesktop"),
            DefaultGlobalCard("K-38", "Alt", 0x38, false, ChordMask.None,
                "Previous Desktop", "windows-command", "prevdesktop"),
            DefaultGlobalCard("K-39", "Space", 0x39, false, ChordMask.Alt,
                "Next Desktop", "windows-command", "nextdesktop"),
        },
    };

    private static BindingConfig DefaultGlobalCard(string keyId, string keyName,
        ushort scan, bool isE0, byte chordMask, string label,
        string actionId, string command) => new()
    {
        KeyId = keyId,
        KeyName = keyName,
        ScanCode = scan,
        IsE0 = isE0,
        ChordMask = chordMask,
        Label = label,
        Steps =
        {
            new ActionStep
            {
                ActionId = actionId,
                Params = new JsonObject { ["command"] = JsonValue.Create(command) },
            },
        },
    };
}
