namespace Keystream.Core.Config;

/// <summary>
/// §137 - log-level policy: pure, WPF-free ordering + gating so the
/// plain net8.0 wb-harness can link this file directly and test it
/// (same precedent as AppDataReset). CrashLog (Keystream.App) is the
/// only writer and delegates every gating decision here; no other
/// file may invent its own ordering.
///
/// Canonical level names are the persisted strings (DeckSettings.
/// LogLevel) and the CrashLog entry-point names: Debug, Information,
/// Warning, Error. Ordering is Debug (most verbose) &lt; Information
/// &lt; Warning &lt; Error (least). An entry is written when its
/// severity is at or above the active level's severity, so an Error
/// entry (severity 3, the maximum) is written at every level with no
/// special case, a Debug entry only at Debug, and so on. Unknown,
/// null, empty, or whitespace-only persisted values fall back to
/// Information and never throw - a hand-edited config.json must not
/// silence error evidence or crash startup.
/// </summary>
public static class LogLevel
{
    public const string Debug = "Debug";
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";

    /// <summary>Fallback for unknown/invalid values (also the default
    /// of DeckSettings.LogLevel).</summary>
    public const string Default = Information;

    /// <summary>Canonical levels in verbosity order: most verbose
    /// first. The Settings dropdown lists them in exactly this order.</summary>
    public static readonly string[] All = { Debug, Information, Warning, Error };

    /// <summary>Severity rank: higher = less verbose. Unknown values
    /// rank as Information (the fallback), never throw.</summary>
    public static int Severity(string? levelName) => NormalizeLevel(levelName) switch
    {
        Debug => 0,
        Information => 1,
        Warning => 2,
        Error => 3,
        _ => 1, // unreachable: NormalizeLevel only returns canonical names
    };

    /// <summary>Canonical name for a persisted/free-form value:
    /// trimmed, case-insensitive match against the four levels;
    /// anything else (null, empty, "Info", typos) becomes
    /// <see cref="Default"/>.</summary>
    public static string NormalizeLevel(string? levelName)
    {
        if (string.IsNullOrWhiteSpace(levelName)) return Default;
        string trimmed = levelName.Trim();
        if (string.Equals(trimmed, Debug, StringComparison.OrdinalIgnoreCase)) return Debug;
        if (string.Equals(trimmed, Information, StringComparison.OrdinalIgnoreCase)) return Information;
        if (string.Equals(trimmed, Warning, StringComparison.OrdinalIgnoreCase)) return Warning;
        if (string.Equals(trimmed, Error, StringComparison.OrdinalIgnoreCase)) return Error;
        return Default;
    }

    /// <summary>Whether an entry of <paramref name="entryLevel"/>
    /// severity is written while <paramref name="activeLevel"/> is
    /// active. Entry levels use the same canonical names; an entry
    /// named "Error" is always written (severity 3 &gt;= every active
    /// severity, including Error itself).</summary>
    public static bool ShouldWrite(string entryLevel, string? activeLevel)
        => Severity(entryLevel) >= Severity(activeLevel);
}

/// <summary>
/// §137 - pure rotation arithmetic for the single crash.log file.
/// CrashLog owns the file I/O; this helper only answers "rotate?"
/// and "where does the kept tail start?" so the harness can test the
/// numbers without touching a real log file. Thresholds live here,
/// next to the policy, so the writer and the tests cannot drift.
/// </summary>
public static class LogRotation
{
    /// <summary>Rotate only once the file exceeds 1 MiB. A normal
    /// crash.log is kilobytes; the threshold is high enough that
    /// rotation is exceptional, low enough that the file can never
    /// grow without bound from a chatty fault loop.</summary>
    public const long MaxBytes = 1024L * 1024;

    /// <summary>Bytes of newest content kept by a rotation. The
    /// rotation rewrites the file in place (no second file) as a
    /// marker line + the newest 512 KiB, dropping a partial first
    /// line so no half-entry survives.</summary>
    public const long KeepBytes = 512L * 1024;

    public static bool ShouldRotate(long length) => length > MaxBytes;

    /// <summary>Byte offset where the kept tail starts: 0 for files
    /// at or under <see cref="KeepBytes"/>, otherwise length minus
    /// <see cref="KeepBytes"/>.</summary>
    public static long KeepStartOffset(long length, long keepBytes = KeepBytes)
        => length <= keepBytes ? 0 : length - keepBytes;
}
