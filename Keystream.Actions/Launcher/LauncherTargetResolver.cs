using System;
using System.Collections.Generic;
using System.IO;

namespace Keystream.Actions.Launcher;

/// <summary>Outcome of the §170 launcher pre-check: Found (launch it),
/// NotFound (fail with the dedicated S1810 message, no OS exception
/// text), Unknown (cannot determine - proceed to Process.Start exactly
/// as before, keeping the existing S1161 exception path).</summary>
public enum LauncherTargetStatus
{
    Found,
    NotFound,
    Unknown,
}

/// <summary>Explicit search locations for <see cref="LauncherTargetResolver.CheckTarget"/>.
/// Production passes null (locations are read from the environment);
/// tests pass explicit locations so no process state is mutated.</summary>
public sealed record LauncherSearchEnvironment(
    string? PathEnv,
    string? CurrentDirectory,
    string? WindowsDirectory,
    string? SystemDirectory,
    string? PathExtEnv);

/// <summary>§170 Task 3: before Process.Start, check whether a launcher
/// target exists - so a missing .exe fails with a dedicated message
/// naming the file (S1810) instead of raw OS exception text. URL-like
/// targets are never checked. The check is deliberately conservative:
/// anything it cannot determine (UNC paths, unresolvable %vars%,
/// unreadable locations, any exception) returns Unknown and the launch
/// proceeds exactly as before. Resolution order for bare names mirrors
/// the shell: current directory, Windows directory, System32, PATH
/// dirs, then the App Paths registry (Windows only) - plus PATHEXT
/// for extensionless names, so "chrome.exe" (App Paths) and "notepad"
/// (PATHEXT) keep working.</summary>
public static class LauncherTargetResolver
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    /// <summary>True when the target is a URL/URI, not a file: contains
    /// "://" or starts with a known scheme or "www.". Such targets skip
    /// the existence check entirely.</summary>
    public static bool IsUrlLike(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        string t = target.Trim();
        return t.Contains("://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Production entry point: locations from the environment.</summary>
    public static LauncherTargetStatus CheckTarget(string target)
        => CheckTarget(target, null);

    /// <summary>Check whether <paramref name="target"/> exists. Returns
    /// Unknown for URL-like targets, UNC paths, unresolvable %vars%,
    /// and any failure while probing - the caller must then proceed to
    /// Process.Start as before.</summary>
    public static LauncherTargetStatus CheckTarget(string target, LauncherSearchEnvironment? environment)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)) return LauncherTargetStatus.Unknown;
            string t = target.Trim();
            if (IsUrlLike(t)) return LauncherTargetStatus.Unknown;

            // §175-sweep: Explorer "Copy as path" wraps the path in
            // double quotes. File.Exists on a quoted path is always
            // false, so a quoted existing file was misreported
            // NotFound (S1810) although Process.Start launches it.
            // Strip ONE balanced pair of surrounding quotes (and any
            // inner padding) before probing; the caller still launches
            // the original target string unchanged.
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                t = t[1..^1].Trim();

            // %VAR% expansion: unresolvable leftovers mean ambiguity.
            if (t.Contains('%'))
            {
                t = Environment.ExpandEnvironmentVariables(t);
                if (t.Contains('%')) return LauncherTargetStatus.Unknown;
            }

            // UNC paths: never probe the network from the dispatcher thread.
            if (t.StartsWith(@"\\", StringComparison.Ordinal)) return LauncherTargetStatus.Unknown;

            // Path-like target (separator or rooted): direct existence check.
            if (t.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0
                || Path.IsPathRooted(t))
            {
                return File.Exists(t) || Directory.Exists(t)
                    ? LauncherTargetStatus.Found
                    : LauncherTargetStatus.NotFound;
            }

            // Bare name: shell-style search.
            var dirs = SearchDirectories(environment);
            if (dirs.Count == 0) return LauncherTargetStatus.Unknown;
            foreach (string candidate in CandidateNames(t, environment))
            {
                foreach (string dir in dirs)
                {
                    string full;
                    try { full = Path.Combine(dir, candidate); }
                    catch { continue; }
                    if (File.Exists(full) || Directory.Exists(full))
                        return LauncherTargetStatus.Found;
                }
            }

            // App Paths registry (production Windows only): the shell
            // resolves "chrome.exe" through it even when it is not on PATH.
            // A test-supplied environment skips this for determinism.
            if (environment is null && OperatingSystem.IsWindows()
                && IsRegisteredInAppPaths(t))
            {
                return LauncherTargetStatus.Found;
            }

            return LauncherTargetStatus.NotFound;
        }
        catch
        {
            return LauncherTargetStatus.Unknown;
        }
    }

    private static List<string> SearchDirectories(LauncherSearchEnvironment? environment)
    {
        var dirs = new List<string>();
        void Add(string? dir)
        {
            if (!string.IsNullOrWhiteSpace(dir)) dirs.Add(dir);
        }

        string? cwd;
        try { cwd = environment?.CurrentDirectory ?? Directory.GetCurrentDirectory(); }
        catch { cwd = null; }
        Add(cwd);
        Add(environment?.WindowsDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(environment?.SystemDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.System));

        string? path = environment?.PathEnv ?? Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string entry in path.Split(Path.PathSeparator))
            {
                string dir = entry.Trim().Trim('"');
                Add(dir);
            }
        }
        return dirs;
    }

    private static IEnumerable<string> CandidateNames(string fileName, LauncherSearchEnvironment? environment)
    {
        yield return fileName;
        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            string pathext = environment?.PathExtEnv
                ?? Environment.GetEnvironmentVariable("PATHEXT")
                ?? "";
            if (string.IsNullOrWhiteSpace(pathext)) pathext = DefaultPathExt;
            foreach (string ext in pathext.Split(';'))
            {
                string e = ext.Trim();
                if (e.Length > 1 && e[0] == '.') yield return fileName + e;
            }
        }
    }

    // Registry is Windows-only: the caller is IsWindows-guarded; the
    // pragma only silences the cross-platform analyzer on Linux builds.
#pragma warning disable CA1416
    private static bool IsRegisteredInAppPaths(string fileName)
    {
        try
        {
            const string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
            foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                using var key = hive.OpenSubKey(subKey + fileName, writable: false);
                if (key?.GetValue(null) is string installed && !string.IsNullOrWhiteSpace(installed))
                {
                    string path = Environment.ExpandEnvironmentVariables(installed.Trim().Trim('"'));
                    if (File.Exists(path)) return true;
                }
            }
        }
        catch
        {
            // Registry unreadable: not a finding, just no evidence.
        }
        return false;
    }
#pragma warning restore CA1416
}
