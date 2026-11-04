using System.IO;
using System.Text;
using Keystream.Core.Config;

namespace Keystream.App.Host;

/// <summary>
/// Last-resort crash recording (added UI v2.8.1 after a silent
/// won't-open report): every unhandled exception and every startup
/// failure is appended to %AppData%\Keystream\crash.log, and
/// startup failures are also shown in a message box. A silent death with
/// no evidence is the one unforgivable failure mode for this app - the
/// user runs it from a tray, so there is no console to catch anything.
///
/// §137: one file, one writer, level-gated. The active level comes
/// from DeckSettings.LogLevel (default Information), applied by
/// AppHost on load and on every successful Save - never a restart,
/// never a second file, never a second writer. Gating lives in
/// <see cref="LogLevel"/> (Core, WPF-free, harness-tested); this
/// class only holds the active level and the file I/O:
/// <see cref="Write"/> (errors/exceptions) is ALWAYS written at every
/// level; <see cref="WriteInfo"/> at Information and Debug only;
/// <see cref="WriteWarning"/> at Warning, Information, and Debug;
/// <see cref="WriteDebug"/> at Debug only. <see cref="WriteResetFailure"/>
/// (§151) is the single documented ungated warning: the destructive
/// data-reset failure line. <see cref="SuppressWrites"/> (§151 P5)
/// suspends ALL appends for the duration of a data reset. Rotation: when the file
/// exceeds 1 MiB it is rewritten in place as a marker line + the
/// newest 512 KiB (see <see cref="LogRotation"/>); the marker names
/// the truncation, so old error evidence is never deleted silently.
/// Logging must never throw: every path below swallows its own
/// failures, and a failed rotation never loses the entry being
/// written. Writes before AppHost applies the level (unhandled
/// exceptions during very early startup) use the Information default.
/// </summary>
public static class CrashLog
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Keystream", "crash.log");

    /// <summary>Active level, canonical name from
    /// <see cref="LogLevel"/>. Volatile: written once per apply on
    /// the UI thread, read from timers/workers; a stale read only
    /// lasts until the next apply and can never corrupt a write.</summary>
    private static volatile string _activeLevel = LogLevel.Default;

    /// <summary>Serializes in-process appends so a rotation rewrite
    /// can never interleave with another thread's append.</summary>
    private static readonly object _writeGate = new();

    /// <summary>§151 P5 option (b): while true, <see cref="AppendText"/>
    /// writes nothing. Set ONLY by <see cref="SuppressWrites"/> for
    /// the synchronous duration of AppDataReset.TryDelete (the data
    /// reset deletes crash.log itself; a background write in that
    /// window would recreate the file - and with it the root - and
    /// fail the reset). The reset-result line is written only AFTER
    /// the suppression scope ends, so suppression can never swallow
    /// the one line the reset exists to produce. Semantik §151 P5:
    /// delete all, then record the result (frasa yang sama juga
    /// didokumentasikan di AppDataReset). Volatile: set on the UI
    /// thread, read from any writer thread; a stale read only
    /// lasts until the scope ends.</summary>
    private static volatile bool _writesSuppressed;

    /// <summary>§151 P5: suspends crash.log appends until the
    /// returned scope is disposed, restoring the previous state
    /// (nested scopes restore correctly; disposing twice is a
    /// no-op). The caller MUST dispose on every path - use a
    /// <c>using</c> block - or logging stays off for the rest of
    /// the process. Only App.ResetDataAndRestart uses this, around
    /// TryDeleteAppData; <see cref="Clear"/> is unaffected (it is
    /// never called during a reset).</summary>
    public static IDisposable SuppressWrites()
    {
        bool previous = _writesSuppressed;
        _writesSuppressed = true;
        return new WriteSuppressionScope(previous);
    }

    private sealed class WriteSuppressionScope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public WriteSuppressionScope(bool previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writesSuppressed = _previous;
        }
    }

    /// <summary>§151: the single formatter for the <c>[Warning]</c>
    /// line shape, so the ungated reset-failure line and the gated
    /// warning lines can never drift apart in format. Public and
    /// pure so the wb-harness can pin the exact bytes (timestamp
    /// format, tag, line ending) without touching the real log
    /// file.</summary>
    public static string FormatWarningLine(string message, DateTime timestamp)
        => $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [Warning] {message}\r\n";

    /// <summary>Applies a persisted/free-form level value. Unknown
    /// values fall back to Information via
    /// <see cref="LogLevel.NormalizeLevel"/>; never throws.</summary>
    public static void SetLevel(string? level)
    {
        try { _activeLevel = LogLevel.NormalizeLevel(level); }
        catch { _activeLevel = LogLevel.Default; }
    }

    /// <summary>Active level for diagnostics (canonical name).</summary>
    public static string ActiveLevel => _activeLevel;

    public static void Write(string context, Exception? ex)
    {
        // Errors/exceptions: ALWAYS written, at every level - the
        // gating predicate would say the same (Error severity is the
        // maximum), but no gate is consulted here so a future
        // ordering change can never silence error evidence.
        AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\r\n{ex}\r\n\r\n");
    }

    public static void WriteInfo(string message)
    {
        if (!LogLevel.ShouldWrite(LogLevel.Information, _activeLevel)) return;
        AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
    }

    /// <summary>§137: degraded-operation notices (recovery fallbacks,
    /// faulted service, failed destructive op) - written at Warning,
    /// Information, and Debug; suppressed only at Error, where the
    /// same condition is already visible in UI status.</summary>
    public static void WriteWarning(string message)
    {
        if (!LogLevel.ShouldWrite(LogLevel.Warning, _activeLevel)) return;
        AppendText(FormatWarningLine(message, DateTime.Now));
    }

    /// <summary>§151 P1b - the data-reset failure line, DELIBERATELY
    /// UNGATED: written at EVERY Log Level, including Error. The
    /// audit (§150 F6) proved the old gated WriteWarning call hid
    /// the one diagnostic line a failed destructive op produces
    /// whenever the user had chosen Error. This is the ONLY ungated
    /// warning in the app - do not reuse it for other warnings;
    /// every other warning keeps its §137 gating. The line format
    /// is byte-identical to the old WriteWarning line
    /// (<c>[yyyy-MM-dd HH:mm:ss] [Warning] Reset Data gagal:
    /// &lt;detail&gt;</c>) via <see cref="FormatWarningLine"/>;
    /// only the caller changes (App.ResetDataAndRestart). The
    /// detail is written verbatim; the caller guarantees it is
    /// non-empty.</summary>
    public static void WriteResetFailure(string detail)
    {
        AppendText(FormatWarningLine("Reset Data gagal: " + detail, DateTime.Now));
    }

    /// <summary>§137: diagnostics for the §137 seams only (level
    /// load/apply, and - from Worker A/B files - master-shortcut
    /// fire, recording, playback, registration failures). Written
    /// at Debug only. NEVER per-keystroke/per-deck-event: deck
    /// events fire at input rate and would flood the file.</summary>
    public static void WriteDebug(string message)
    {
        if (!LogLevel.ShouldWrite(LogLevel.Debug, _activeLevel)) return;
        AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Debug] {message}\r\n");
    }

    private static void AppendText(string text)
    {
        // §151 P5: suppressed during TryDelete (see _writesSuppressed).
        // Checked before AND inside the lock: a write that entered
        // before suppression still completes (it predates the delete
        // window); a write that arrives after the scope started is
        // dropped even if it waited on the gate.
        if (_writesSuppressed) return;
        try
        {
            lock (_writeGate)
            {
                if (_writesSuppressed) return;
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                RotateIfNeeded();
                File.AppendAllText(LogPath, text);
            }
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>§139: truncates crash.log to an empty file (the
    /// "Bersihkan Log" button in Settings). Follows the
    /// <see cref="AppendText"/> pattern: serialized on
    /// <see cref="_writeGate"/>, directory created if needed, and
    /// every failure swallowed - but unlike the appends it REPORTS
    /// success as a bool so the caller can show an honest result
    /// message. Never writes a marker or "log cleared" line after
    /// truncating: the file must end up empty. A missing file is
    /// created empty and counts as success (nothing to clear).</summary>
    public static bool Clear()
    {
        try
        {
            lock (_writeGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.WriteAllText(LogPath, "");
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Single-threshold rotation guard: if crash.log exceeds
    /// <see cref="LogRotation.MaxBytes"/> (1 MiB), rewrite it in
    /// place as a marker line + the newest
    /// <see cref="LogRotation.KeepBytes"/> (512 KiB), dropping a
    /// partial first line. Best-effort: any failure here is swallowed
    /// and the pending append still runs, so rotation can never lose
    /// the entry being written. No second file is ever created.</summary>
    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || !LogRotation.ShouldRotate(info.Length)) return;
            long start = LogRotation.KeepStartOffset(info.Length);
            byte[] tail;
            using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(start, SeekOrigin.Begin);
                tail = new byte[fs.Length - start];
                int read = 0;
                while (read < tail.Length)
                {
                    int n = fs.Read(tail, read, tail.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read < tail.Length) Array.Resize(ref tail, read);
            }
            string kept = Encoding.UTF8.GetString(tail);
            int newline = kept.IndexOf('\n');
            if (newline >= 0) kept = kept[(newline + 1)..];
            string marker = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Log rotated: file exceeded 1 MiB; oldest entries discarded, newest 512 KiB kept]\r\n";
            File.WriteAllText(LogPath, marker + kept);
        }
        catch { /* rotation is best-effort; the append still runs */ }
    }
}
