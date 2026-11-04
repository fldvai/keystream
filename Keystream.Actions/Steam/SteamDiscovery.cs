using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Keystream.Actions.Steam;

public sealed record SteamInstallInfo(bool IsInstalled, string? SteamPath, string? SteamExe);

public sealed record SteamGame(
    string AppId,
    string Name,
    string InstallDir,
    string LibraryPath,
    string? ExecutablePath,
    string? IconPath)
{
    public override string ToString() => Name;
}

public static class SteamDiscovery
{
    private static readonly Regex LibraryPathRegex = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AppIdRegex = new("\"appid\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NameRegex = new("\"name\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InstallDirRegex = new("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StateFlagsRegex = new("\"StateFlags\"\\s+\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> IgnoredAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "228980", // Steamworks Common Redistributables
        "1070560", // Steam Linux Runtime
        "1391110", // Steam Linux Runtime - Soldier
        "1628350", // Steam Linux Runtime - Sniper
    };

    public static SteamInstallInfo GetInstallInfo()
    {
        try
        {
            // 1. Current user registry
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key != null)
                {
                    string? steamPath = key.GetValue("SteamPath") as string;
                    string? steamExe = key.GetValue("SteamExe") as string;

                    if (!string.IsNullOrWhiteSpace(steamPath))
                    {
                        steamPath = NormalizePath(steamPath);
                        if (string.IsNullOrWhiteSpace(steamExe))
                            steamExe = Path.Combine(steamPath, "steam.exe");
                        else
                            steamExe = NormalizePath(steamExe);

                        if (File.Exists(steamExe) || Directory.Exists(steamPath))
                        {
                            return new SteamInstallInfo(true, steamPath, steamExe);
                        }
                    }
                }
            }

            // 2. Local machine 64-bit/32-bit registry
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam") ??
                             Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                if (key != null)
                {
                    string? installPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(installPath))
                    {
                        installPath = NormalizePath(installPath);
                        string steamExe = Path.Combine(installPath, "steam.exe");
                        if (File.Exists(steamExe) || Directory.Exists(installPath))
                        {
                            return new SteamInstallInfo(true, installPath, steamExe);
                        }
                    }
                }
            }

            // 3. Known default disk paths
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string defPath86 = Path.Combine(pf86, "Steam");
            if (File.Exists(Path.Combine(defPath86, "steam.exe")))
            {
                return new SteamInstallInfo(true, defPath86, Path.Combine(defPath86, "steam.exe"));
            }

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string defPath = Path.Combine(pf, "Steam");
            if (File.Exists(Path.Combine(defPath, "steam.exe")))
            {
                return new SteamInstallInfo(true, defPath, Path.Combine(defPath, "steam.exe"));
            }
        }
        catch
        {
            // Fail safely without throwing
        }

        return new SteamInstallInfo(false, null, null);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\').Trim();
    }

    public static IReadOnlyList<string> GetLibraryFolders(string steamPath)
    {
        var list = new List<string>();
        if (Directory.Exists(steamPath))
        {
            list.Add(Path.GetFullPath(steamPath));
        }

        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                string content = File.ReadAllText(vdfPath);
                var matches = LibraryPathRegex.Matches(content);
                foreach (Match m in matches)
                {
                    if (m.Groups.Count > 1)
                    {
                        string p = NormalizePath(m.Groups[1].Value.Replace(@"\\", @"\"));
                        if (Directory.Exists(p) && !list.Any(existing => string.Equals(existing, p, StringComparison.OrdinalIgnoreCase)))
                        {
                            list.Add(p);
                        }
                    }
                }
            }
            catch
            {
                // Ignore corrupt VDF safely
            }
        }

        return list;
    }

    public static IReadOnlyList<SteamGame> DiscoverGames()
    {
        var games = new List<SteamGame>();
        var install = GetInstallInfo();
        if (!install.IsInstalled || string.IsNullOrWhiteSpace(install.SteamPath))
            return games;

        var libraries = GetLibraryFolders(install.SteamPath);
        var seenAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string lib in libraries)
        {
            string steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            string[] manifestFiles;
            try
            {
                manifestFiles = Directory.GetFiles(steamapps, "appmanifest_*.acf");
            }
            catch
            {
                continue;
            }

            foreach (string manifest in manifestFiles)
            {
                try
                {
                    string content = File.ReadAllText(manifest);
                    var appMatch = AppIdRegex.Match(content);
                    if (!appMatch.Success) continue;

                    string appId = appMatch.Groups[1].Value.Trim();
                    if (IgnoredAppIds.Contains(appId) || !seenAppIds.Add(appId))
                        continue;

                    var nameMatch = NameRegex.Match(content);
                    string name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : $"Steam App {appId}";

                    // Filter out internal tools
                    if (name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Steam Linux", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dirMatch = InstallDirRegex.Match(content);
                    string installDir = dirMatch.Success ? dirMatch.Groups[1].Value.Trim() : "";

                    string? gameExe = null;
                    string? iconPath = null;

                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        string commonGameDir = Path.Combine(steamapps, "common", installDir);
                        if (Directory.Exists(commonGameDir))
                        {
                            gameExe = FindMainExecutable(commonGameDir, installDir, name);
                            if (gameExe != null && File.Exists(gameExe))
                            {
                                iconPath = gameExe;
                            }
                        }
                    }

                    // Fallback to Steam icon cache / shortcuts if no direct exe icon
                    if (string.IsNullOrEmpty(iconPath))
                    {
                        iconPath = FindGameIconPath(appId, install.SteamPath);
                    }

                    games.Add(new SteamGame(appId, name, installDir, lib, gameExe, iconPath));
                }
                catch
                {
                    // Ignore individual corrupt manifest
                }
            }
        }

        return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindMainExecutable(string dir, string installDir, string gameName)
    {
        try
        {
            var files = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                // Look 1 level deeper (e.g. bin/ or game/)
                files = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories).Take(15).ToArray();
            }

            if (files.Length == 0) return null;
            if (files.Length == 1) return files[0];

            // Filter out obvious crash reporters, installers, unity crash handlers
            var filtered = files.Where(f =>
            {
                string fn = Path.GetFileName(f).ToLowerInvariant();
                return !fn.Contains("crash") &&
                       !fn.Contains("report") &&
                       !fn.Contains("install") &&
                       !fn.Contains("setup") &&
                       !fn.Contains("config") &&
                       !fn.Contains("update") &&
                       !fn.Contains("dxsetup");
            }).ToList();

            if (filtered.Count == 1) return filtered[0];

            var pool = filtered.Count > 0 ? filtered : files.ToList();

            // Match by name similarity or largest exe
            string normDir = installDir.Replace(" ", "").ToLowerInvariant();
            string normName = gameName.Replace(" ", "").ToLowerInvariant();

            var exactMatch = pool.FirstOrDefault(f =>
            {
                string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                return fn == normDir || fn == normName;
            });
            if (exactMatch != null) return exactMatch;

            // Otherwise pick largest file size (usually the game binary)
            return pool.OrderByDescending(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0; }
            }).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static string? FindGameIconPath(string appId, string? steamPath = null)
    {
        try
        {
            // 1. Check Steam start menu and desktop shortcuts (.url files with IconFile=...)
            var shortcutDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Steam"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (var dir in shortcutDirs)
            {
                if (!Directory.Exists(dir)) continue;
                string[] urlFiles;
                try { urlFiles = Directory.GetFiles(dir, "*.url", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (var urlFile in urlFiles)
                {
                    try
                    {
                        string content = File.ReadAllText(urlFile);
                        if (content.IndexOf($"rungameid/{appId}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            content.IndexOf($"rungameid\\{appId}", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var match = Regex.Match(content, @"IconFile=([^\r\n]+)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string ico = match.Groups[1].Value.Trim();
                                if (File.Exists(ico)) return ico;
                            }
                        }
                    }
                    catch { }
                }
            }

            // 2. Check <SteamPath>\steam\games\
            string? sp = steamPath;
            if (string.IsNullOrEmpty(sp))
            {
                var install = GetInstallInfo();
                if (install.IsInstalled) sp = install.SteamPath;
            }

            if (!string.IsNullOrWhiteSpace(sp) && Directory.Exists(sp))
            {
                string gamesDir = Path.Combine(sp, "steam", "games");
                string appInfoPath = Path.Combine(sp, "appcache", "appinfo.vdf");

                if (Directory.Exists(gamesDir) && File.Exists(appInfoPath))
                {
                    try
                    {
                        var icoFiles = Directory.GetFiles(gamesDir, "*.ico");
                        if (icoFiles.Length > 0)
                        {
                            byte[] bytes = File.ReadAllBytes(appInfoPath);
                            string text = System.Text.Encoding.ASCII.GetString(bytes);
                            int idx = text.IndexOf(appId, StringComparison.Ordinal);
                            if (idx >= 0)
                            {
                                int window = Math.Min(5000, text.Length - idx);
                                string chunk = text.Substring(idx, window);
                                foreach (var ico in icoFiles)
                                {
                                    string hash = Path.GetFileNameWithoutExtension(ico);
                                    if (chunk.IndexOf(hash, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        return ico;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 3. Check librarycache for app logos or icons
                string libCacheAppDir = Path.Combine(sp, "appcache", "librarycache", appId);
                if (Directory.Exists(libCacheAppDir))
                {
                    try
                    {
                        var imgFiles = Directory.GetFiles(libCacheAppDir, "*.*", SearchOption.AllDirectories)
                            .Where(f =>
                            {
                                string ext = Path.GetExtension(f).ToLowerInvariant();
                                return ext is ".png" or ".jpg" or ".ico";
                            }).ToList();

                        var logo = imgFiles.FirstOrDefault(f => f.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? imgFiles.FirstOrDefault(f => f.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? imgFiles.FirstOrDefault();

                        if (logo != null && File.Exists(logo)) return logo;
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }

    public static bool IsSteamRunning()
    {
        try
        {
            return Process.GetProcessesByName("steam").Any(p => !p.HasExited);
        }
        catch
        {
            return false;
        }
    }

    public static Process? FindGameProcess(string appId, string? installDir = null)
    {
        // 1. Resolve installDir and executable if not provided
        string? targetDir = installDir;
        string? exeName = null;

        if (string.IsNullOrEmpty(targetDir) || string.IsNullOrEmpty(exeName))
        {
            var game = DiscoverGames().FirstOrDefault(g => string.Equals(g.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (game != null)
            {
                if (string.IsNullOrEmpty(targetDir)) targetDir = game.InstallDir;
                if (!string.IsNullOrEmpty(game.ExecutablePath))
                    exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
            }
        }

        // 2. Direct fast lookup by process name if known
        if (!string.IsNullOrEmpty(exeName))
        {
            try
            {
                var procs = Process.GetProcessesByName(exeName);
                var active = procs.FirstOrDefault(p => !p.HasExited);
                if (active != null) return active;
            }
            catch { }
        }

        // 3. Scan running processes by module path
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.HasExited) continue;

                    string? mainModulePath = null;
                    try
                    {
                        mainModulePath = proc.MainModule?.FileName;
                    }
                    catch
                    {
                        // Protected process, skip safely
                    }

                    if (!string.IsNullOrEmpty(mainModulePath))
                    {
                        if (!string.IsNullOrEmpty(targetDir) &&
                            mainModulePath.IndexOf(targetDir, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return proc;
                        }
                    }
                }
                catch
                {
                    // Ignore per-process access exception
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    public static bool IsGameRunning(string appId, string? installDir = null)
    {
        // 1. Check Steam Registry RunningAppId (real-time official Steam state)
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key != null)
                {
                    object? runningId = key.GetValue("RunningAppId");
                    if (runningId != null && string.Equals(runningId.ToString(), appId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        // 2. Check process directly
        return FindGameProcess(appId, installDir) != null;
    }

    public static bool BringGameToFront(string appId, string? installDir = null)
    {
        var proc = FindGameProcess(appId, installDir);
        if (proc != null && proc.MainWindowHandle != IntPtr.Zero)
        {
            ShowWindow(proc.MainWindowHandle, SW_RESTORE);
            SetForegroundWindow(proc.MainWindowHandle);
            return true;
        }
        return false;
    }

    public static bool CloseGame(string appId, string? installDir = null)
    {
        bool anyClosed = false;

        // Resolve installDir if missing
        if (string.IsNullOrEmpty(installDir))
        {
            var game = DiscoverGames().FirstOrDefault(g => string.Equals(g.AppId, appId, StringComparison.OrdinalIgnoreCase));
            if (game != null) installDir = game.InstallDir;
        }

        // 1. Try closing main process gracefully first, then kill
        var proc = FindGameProcess(appId, installDir);
        if (proc != null)
        {
            try
            {
                if (proc.CloseMainWindow())
                {
                    if (proc.WaitForExit(1500))
                    {
                        anyClosed = true;
                    }
                }
                if (!proc.HasExited)
                {
                    proc.Kill(true);
                    anyClosed = true;
                }
            }
            catch
            {
                try { proc.Kill(); anyClosed = true; } catch { }
            }
        }

        // 2. Also terminate any child/related processes running in the game's directory
        if (!string.IsNullOrEmpty(installDir))
        {
            try
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.HasExited) continue;
                        string? path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (!string.IsNullOrEmpty(path) && path.IndexOf(installDir, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try
                            {
                                p.Kill(true);
                                anyClosed = true;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return anyClosed;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public const uint WM_CLOSE = 0x0010;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const int SC_CLOSE = 0xF060;
    public const int SC_MINIMIZE = 0xF020;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    public static HashSet<uint> GetSteamProcessIds()
    {
        var pids = new HashSet<uint>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.HasExited) continue;
                    string name = p.ProcessName;
                    if (name.Equals("steam", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase))
                    {
                        pids.Add((uint)p.Id);
                    }
                }
                catch { }
            }
        }
        catch { }
        return pids;
    }

    public static bool IsFriendsWindowTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Indonesian
        if (title.IndexOf("Daftar Teman", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (title.IndexOf("Teman & Obrolan", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (title.IndexOf("Teman", StringComparison.OrdinalIgnoreCase) >= 0 && !title.Equals("Steam", StringComparison.OrdinalIgnoreCase)) return true;
        if (title.IndexOf("Obrolan", StringComparison.OrdinalIgnoreCase) >= 0) return true;

        // English
        if (title.IndexOf("Friends List", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (title.IndexOf("Friends & Chat", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (title.IndexOf("Friends", StringComparison.OrdinalIgnoreCase) >= 0 && !title.Equals("Steam", StringComparison.OrdinalIgnoreCase)) return true;
        if (title.IndexOf("Chat", StringComparison.OrdinalIgnoreCase) >= 0 && !title.Equals("Steam", StringComparison.OrdinalIgnoreCase)) return true;

        // Other Steam localizations
        string[] knownTitles =
        {
            "freundesliste", "liste d'amis", "lista de amigos", "lista amici",
            "список друзей", "好友列表", "好友名單", "フレンドリスト", "친구 목록"
        };

        foreach (var k in knownTitles)
        {
            if (title.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    public static IntPtr FindFriendsWindow()
    {
        var steamPids = GetSteamProcessIds();
        if (steamPids.Count == 0) return IntPtr.Zero;

        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!steamPids.Contains(pid)) return true;

            GetWindowRect(hWnd, out RECT rect);
            if (rect.Width <= 50 || rect.Height <= 50) return true;

            var sbTitle = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sbTitle, 256);
            string title = sbTitle.ToString().Trim();

            if (title.IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (IsFriendsWindowTitle(title))
            {
                foundHwnd = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    public static IntPtr FindSteamMainWindow()
    {
        var steamPids = GetSteamProcessIds();
        if (steamPids.Count == 0) return IntPtr.Zero;

        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!steamPids.Contains(pid)) return true;

            GetWindowRect(hWnd, out RECT rect);
            if (rect.Width <= 150 || rect.Height <= 150) return true;

            var sbTitle = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sbTitle, 256);
            string title = sbTitle.ToString().Trim();

            if (string.IsNullOrEmpty(title)) return true;
            if (IsFriendsWindowTitle(title)) return true;
            if (title.IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (title.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("Steam ", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("Steam -", StringComparison.OrdinalIgnoreCase))
            {
                foundHwnd = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    public static IntPtr FindBigPictureWindow()
    {
        var steamPids = GetSteamProcessIds();
        if (steamPids.Count == 0) return IntPtr.Zero;

        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!steamPids.Contains(pid)) return true;

            var sbTitle = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sbTitle, 256);
            string title = sbTitle.ToString().Trim();

            if (title.IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foundHwnd = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    public static void CloseWindowGracefully(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        PostMessage(hWnd, WM_SYSCOMMAND, (IntPtr)SC_CLOSE, IntPtr.Zero);
        ShowWindow(hWnd, SW_HIDE);
    }

    public static void MinimizeOrHideWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        ShowWindow(hWnd, SW_MINIMIZE);
        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public static void RestoreAndBringToFront(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
        }
        else
        {
            ShowWindow(hWnd, SW_SHOWNORMAL);
        }
        SetForegroundWindow(hWnd);
    }

    public static bool IsWindowOrChildFocused(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero) return false;
        IntPtr fg = GetForegroundWindow();
        if (fg == targetHwnd) return true;

        GetWindowThreadProcessId(targetHwnd, out uint targetPid);
        GetWindowThreadProcessId(fg, out uint fgPid);
        return targetPid != 0 && targetPid == fgPid;
    }
}

