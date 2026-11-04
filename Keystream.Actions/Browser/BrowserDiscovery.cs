using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Config;

namespace Keystream.Actions.Browser;

/// <summary>A browser that can open profiles: an installed browser found
/// by scanning known paths, or a custom browser the user added.
/// Engine is "Chromium" or "Firefox". UserDataDir is the Chromium
/// "User Data" folder, or the Firefox folder containing profiles.ini.</summary>
public sealed record DetectedBrowser(
    string Id, string Name, string ExePath, string Engine, string UserDataDir, bool IsCustom)
{
    // Kotak pilihan yang tertutup di template ComboBox global (App.xaml)
    // menampilkan SelectionBoxItem apa adanya, bukan DisplayMemberPath -
    // jadi nama harus berasal dari ToString, bukan hanya dari template.
    public override string ToString() => Name;
}

/// <summary>One profile of a browser. Key is what the launcher passes:
/// the Chromium profile directory name ("Default", "Profile 1") or the
/// Firefox profile name from profiles.ini. Display is the friendly name.</summary>
public sealed record BrowserProfile(string Key, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Discovers installed browsers and their profiles from disk.
/// Pure file-system reads - no registry, no network - so a missing or
/// moved browser simply yields no entry instead of an error. Profile
/// display names are best-effort: a Preferences/profiles.ini entry that
/// cannot be parsed falls back to the folder/profile key.</summary>
public static class BrowserDiscovery
{
    private sealed record KnownBrowser(
        string Id, string Name, string Engine, string[] ExeRelPaths, string UserDataRelPath);

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string ProgramFiles => Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
    private static string ProgramFilesX86 =>
        Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";

    private static IReadOnlyList<KnownBrowser> KnownBrowsers() => new[]
    {
        new KnownBrowser("chrome", Localizer.Get("S1147"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(ProgramFiles, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(ProgramFilesX86, @"Google\Chrome\Application\chrome.exe"),
            },
            Path.Combine(LocalAppData, @"Google\Chrome\User Data")),
        new KnownBrowser("thorium", Localizer.Get("S1148"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"Thorium\Application\thorium.exe"),
                Path.Combine(ProgramFiles, @"Thorium\Application\thorium.exe"),
                Path.Combine(ProgramFilesX86, @"Thorium\Application\thorium.exe"),
            },
            Path.Combine(LocalAppData, @"Thorium\User Data")),
        new KnownBrowser("edge", Localizer.Get("S1149"), "Chromium",
            new[]
            {
                Path.Combine(ProgramFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(ProgramFiles, @"Microsoft\Edge\Application\msedge.exe"),
            },
            Path.Combine(LocalAppData, @"Microsoft\Edge\User Data")),
        new KnownBrowser("brave", Localizer.Get("S1150"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(ProgramFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Path.Combine(ProgramFilesX86, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            },
            Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data")),
        new KnownBrowser("firefox", Localizer.Get("S1151"), "Firefox",
            new[]
            {
                Path.Combine(ProgramFiles, @"Mozilla Firefox\firefox.exe"),
                Path.Combine(ProgramFilesX86, @"Mozilla Firefox\firefox.exe"),
            },
            Path.Combine(AppData, @"Mozilla\Firefox")),
        // Perluasan 25 Okt 2026 - aplikasi kini dipakai lebih dari satu
        // orang, jadi deteksi mencakup browser Chromium/Firefox populer
        // lain. Semua entri dijaga File.Exists di DetectBrowsers, jadi
        // path yang meleset untuk satu varian instalasi hanya berarti
        // browser itu tidak muncul - tidak pernah error. Browser yang
        // tidak ada di sini tetap bisa dipakai lewat browser kustom +
        // pindai folder User Data.
        new KnownBrowser("vivaldi", Localizer.Get("S1152"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"Vivaldi\Application\vivaldi.exe"),
                Path.Combine(ProgramFiles, @"Vivaldi\Application\vivaldi.exe"),
                Path.Combine(ProgramFilesX86, @"Vivaldi\Application\vivaldi.exe"),
            },
            Path.Combine(LocalAppData, @"Vivaldi\User Data")),
        new KnownBrowser("opera", Localizer.Get("S1153"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"Programs\Opera\opera.exe"),
                Path.Combine(ProgramFiles, @"Opera\opera.exe"),
                Path.Combine(ProgramFilesX86, @"Opera\opera.exe"),
            },
            Path.Combine(AppData, @"Opera Software\Opera Stable")),
        new KnownBrowser("opera-gx", Localizer.Get("S1154"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"Programs\Opera GX\opera.exe"),
                Path.Combine(ProgramFiles, @"Opera GX\opera.exe"),
            },
            Path.Combine(AppData, @"Opera Software\Opera GX Stable")),
        new KnownBrowser("arc", Localizer.Get("S1155"), "Chromium",
            new[]
            {
                Path.Combine(LocalAppData, @"Arc\Application\Arc.exe"),
                Path.Combine(ProgramFiles, @"Arc\Application\Arc.exe"),
            },
            Path.Combine(LocalAppData, @"Arc\User Data")),
        new KnownBrowser("zen", Localizer.Get("S1156"), "Firefox",
            new[]
            {
                Path.Combine(LocalAppData, @"Zen\zen.exe"),
                Path.Combine(ProgramFiles, @"Zen\zen.exe"),
            },
            Path.Combine(AppData, @"Zen")),
        new KnownBrowser("librewolf", Localizer.Get("S1157"), "Firefox",
            new[]
            {
                Path.Combine(ProgramFiles, @"LibreWolf\librewolf.exe"),
                Path.Combine(ProgramFilesX86, @"LibreWolf\librewolf.exe"),
                Path.Combine(LocalAppData, @"Programs\LibreWolf\librewolf.exe"),
            },
            Path.Combine(AppData, @"librewolf")),
        new KnownBrowser("floorp", Localizer.Get("S1158"), "Firefox",
            new[]
            {
                Path.Combine(ProgramFiles, @"Floorp\floorp.exe"),
                Path.Combine(ProgramFilesX86, @"Floorp\floorp.exe"),
            },
            Path.Combine(AppData, @"Floorp")),
    };

    /// <summary>Nama + engine browser yang dikenal (untuk tes dan
    /// dokumentasi). Daftar deteksi itu sendiri tetap dijaga File.Exists
    /// di DetectBrowsers.</summary>
    public static IReadOnlyList<(string Name, string Engine)> KnownBrowserNames()
        => KnownBrowsers().Select(k => (k.Name, k.Engine)).ToList();

    public static IReadOnlyList<DetectedBrowser> DetectBrowsers(IEnumerable<CustomBrowserConfig>? customs = null)
    {
        var result = new List<DetectedBrowser>();
        var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var known in KnownBrowsers())
        {
            string? exe = known.ExeRelPaths.FirstOrDefault(File.Exists);
            if (exe is null) continue;
            if (!seenExe.Add(exe)) continue;
            result.Add(new DetectedBrowser(known.Id, known.Name, exe, known.Engine, known.UserDataRelPath, IsCustom: false));
        }
        if (customs is not null)
        {
            foreach (var custom in customs)
            {
                if (string.IsNullOrWhiteSpace(custom.ExePath)) continue;
                if (!seenExe.Add(custom.ExePath)) continue;
                result.Add(new DetectedBrowser(
                    custom.Id,
                    string.IsNullOrWhiteSpace(custom.Name) ? Path.GetFileNameWithoutExtension(custom.ExePath) : custom.Name,
                    custom.ExePath,
                    NormalizeEngine(custom.Engine),
                    custom.UserDataDir ?? "",
                    IsCustom: true));
            }
        }
        return result;
    }

    public static string NormalizeEngine(string? engine)
        => string.Equals(engine, "Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox" : "Chromium";

    /// <summary>Hasil pindai profil: daftar + apakah pencatatan folder
    /// gagal karena izin. Dulu keduanya menyatu (§45): folder yang
    /// tidak bisa dibaca dilaporkan sebagai "bukan folder User Data".</summary>
    public sealed record ProfileScan(IReadOnlyList<BrowserProfile> Profiles, bool Unreadable);

    public static IReadOnlyList<BrowserProfile> GetProfiles(DetectedBrowser browser)
        => GetProfilesDetailed(browser).Profiles;

    public static ProfileScan GetProfilesDetailed(DetectedBrowser browser)
    {
        if (browser is null) return new(Array.Empty<BrowserProfile>(), false);
        return NormalizeEngine(browser.Engine) == "Firefox"
            ? new(GetFirefoxProfiles(browser.UserDataDir), false)
            : GetChromiumProfilesDetailed(browser.UserDataDir);
    }

    private static ProfileScan GetChromiumProfilesDetailed(string userDataDir)
    {
        var result = new List<BrowserProfile>();
        if (string.IsNullOrWhiteSpace(userDataDir) || !Directory.Exists(userDataDir))
            return new(result, false);
        var dirs = new List<string>();
        string def = Path.Combine(userDataDir, "Default");
        if (Directory.Exists(def)) dirs.Add(def);
        bool unreadable = false;
        try
        {
            dirs.AddRange(Directory.GetDirectories(userDataDir, "Profile *")
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException) { unreadable = true; }
        catch { /* unreadable user-data dir: return what we have */ }
        foreach (string dir in dirs)
        {
            string key = Path.GetFileName(dir);
            result.Add(new BrowserProfile(key, ReadChromiumProfileName(dir) ?? key));
        }
        // Profil yang dibuat tanpa diganti namanya semuanya bernama sama
        // ("Person 1"). Tanpa pembeda, dropdown tidak bisa dipakai - jadi
        // nama yang duplikat diberi akhiran foldernya ("Person 1 (Profile 3)").
        var counts = result.GroupBy(pr => pr.Display).ToDictionary(g => g.Key, g => g.Count());
        var final = result.Select(pr => counts[pr.Display] > 1
            ? new BrowserProfile(pr.Key, $"{pr.Display} ({pr.Key})")
            : pr).ToList();
        return new(final, unreadable);
    }

    /// <summary>Detects what a folder is, by its contents: a
    /// "Preferences" file means a Chromium profile (display name read
    /// from it when possible), "prefs.js" means a Firefox profile
    /// (display name = folder name). Anything else is not a profile
    /// folder - this catches picking the "User Data" folder by mistake.
    /// Used by the editor's manual-path section.</summary>
    public static bool TryDetectProfileFolder(string folder, out string engine, out string display)
    {
        engine = "Chromium";
        display = "";
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (File.Exists(Path.Combine(folder, "Preferences")))
        {
            engine = "Chromium";
            display = ReadChromiumProfileName(folder) ?? name;
            return true;
        }
        if (File.Exists(Path.Combine(folder, "prefs.js")))
        {
            engine = "Firefox";
            display = name;
            return true;
        }
        return false;
    }

    private static string? ReadChromiumProfileName(string profileDir)
    {
        try
        {
            string prefsPath = Path.Combine(profileDir, "Preferences");
            if (!File.Exists(prefsPath)) return null;
            var root = JsonNode.Parse(File.ReadAllText(prefsPath)) as JsonObject;
            return root?["profile"]?["name"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<BrowserProfile> GetFirefoxProfiles(string firefoxDir)
    {
        var result = new List<BrowserProfile>();
        if (string.IsNullOrWhiteSpace(firefoxDir)) return result;
        string iniPath = Path.Combine(firefoxDir, "profiles.ini");
        if (!File.Exists(iniPath)) return result;
        try
        {
            string? name = null;
            bool inProfile = false;
            void Flush()
            {
                if (inProfile && !string.IsNullOrWhiteSpace(name)) result.Add(new BrowserProfile(name, name));
                name = null;
            }
            foreach (string rawLine in File.ReadLines(iniPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    Flush();
                    inProfile = line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inProfile) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = value;
            }
            Flush();
        }
        catch
        {
            // A half-readable profiles.ini yields whatever parsed so far.
        }
        return result;
    }
}
