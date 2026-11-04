using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Keystream.Core.Config;

/// <summary>
/// §130 - the file-system half of a data reset, deliberately free of WPF
/// and of any store instance: it takes plain paths, so the real code can
/// be linked directly into the plain net8.0 wb-harness and exercised
/// against temporary directories. AppHost.TryDeleteAppData is a thin
/// wrapper that supplies Store.MainPath / IconStore.Folder / the
/// verified %AppData%\Keystream root.
///
/// The set deleted is everything under the verified root: config.json +
/// config.json.bak1–3 (+ a .tmp left by an interrupted save), the Icons
/// folder, crash.log, and any other entry under the root. Nothing outside
/// the root is ever enumerated. The root itself is verified to be exactly
/// the expected root before anything is deleted, so a future config-root
/// change can never turn this into a wrong-directory delete.
/// Returns false (with <paramref name="failureDetail"/>) when any entry
/// survives, so the caller must NOT restart: a partial delete followed
/// by a restart would resurrect the surviving config as if the reset
/// had worked.
///
/// §151 P5 option (b) - "delete all, then record the result": this
/// helper deletes crash.log like any other file under the root and
/// never writes a log line itself. The caller (App.ResetDataAndRestart)
/// suppresses CrashLog writes for the duration of this call via
/// CrashLog.SuppressWrites(), so no background writer can recreate
/// crash.log (and with it the root) mid-deletion; the reset-result
/// line is written only AFTER suppression ends, recreating root +
/// crash.log on failure - exactly the evidence the next diagnosis
/// needs. On success nothing is written after the delete, so the
/// root stays gone for the fresh process.
/// </summary>
public static class AppDataReset
{
    /// <summary>§151 P4b - attempts per file/directory delete: the
    /// first attempt plus this many retries. A transient lock
    /// (Explorer thumbnail, antivirus scan, a just-finished Save)
    /// usually clears within one retry; a persistent lock still
    /// fails honestly after the third attempt.</summary>
    public const int DeleteAttemptsPerFile = 3;

    /// <summary>§151 P4b - delay between attempts. Worst-case added
    /// latency is (DeleteAttemptsPerFile − 1) × this delay = 200 ms
    /// per entry that fails every attempt, i.e. 200 ms × F
    /// persistently-failing entries, plus the OS delete calls
    /// themselves. The call runs synchronously on the UI thread
    /// (§130), so the bound is deliberately per-entry and small;
    /// there is no global cap because every failing entry must be
    /// attempted and reported, not skipped after a time budget.</summary>
    public const int DeleteRetryDelayMs = 100;

    /// <summary>§151 P4d - cap on the joined failure text: the first
    /// 2000 characters of joined messages are kept, plus an
    /// ellipsis marker when truncation cut more off. The cap keeps
    /// a pathological root (hundreds of locked files) from
    /// producing a multi-kilobyte footer/log line; ordering is
    /// first-error-first, nothing is reordered or deduplicated.</summary>
    public const int MaxFailureDetailLength = 2000;

    /// <summary>§151 P4c - deletion depth of a path: the number of
    /// directory separators it contains. Deeper directories sort
    /// first, so every subfolder is empty when its turn comes.
    /// (Path length, the §130 approximation, happens to order
    /// ancestors correctly because a descendant's path is always
    /// longer than its ancestor's - but depth is the property that
    /// actually matters, and it stays correct under path forms
    /// where length does not track nesting.)</summary>
    public static int DirectoryDepth(string path)
        => path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

    /// <summary>§151 P4d - joins collected failure messages,
    /// first-error-first, capped at
    /// <see cref="MaxFailureDetailLength"/> characters (plus an
    /// ellipsis when truncated). Null when there are no messages.
    /// Public so the wb-harness can pin the format without forcing
    /// a real delete failure (unlink semantics differ per OS).</summary>
    public static string? JoinFailureMessages(IEnumerable<string> messages)
    {
        string joined = string.Join("; ", messages);
        if (joined.Length == 0) return null;
        if (joined.Length > MaxFailureDetailLength)
            joined = joined[..MaxFailureDetailLength] + "…";
        return joined;
    }

    public static bool TryDelete(string mainPath, string iconFolder, string expectedRoot,
        out string? failureDetail)
    {
        failureDetail = null;
        string? directory = Path.GetDirectoryName(mainPath);
        if (string.IsNullOrEmpty(directory)
            || !string.Equals(directory, expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            failureDetail = "App data directory did not match the expected %AppData%\\Keystream root.";
            return false;
        }
        if (!Directory.Exists(directory)) return true;

        bool ok = true;
        var failures = new List<string>();
        void NoteFailure(Exception ex)
        {
            ok = false;
            failures.Add(string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message);
        }
        string? JoinedFailures() => JoinFailureMessages(failures);

        // One delete attempt, retried: returns true when the entry is
        // gone, false after the last attempt (failure already noted
        // by the caller from the returned exception).
        bool TryWithRetry(Action delete, out Exception? lastError)
        {
            lastError = null;
            for (int attempt = 0; attempt < DeleteAttemptsPerFile; attempt++)
            {
                try
                {
                    delete();
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt + 1 < DeleteAttemptsPerFile)
                        Thread.Sleep(DeleteRetryDelayMs);
                }
            }
            return false;
        }

        try
        {
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                // §151 P4a: clear ReadOnly before deleting. The
                // attribute clear is best-effort inside the retried
                // action: if it throws (file vanished between
                // enumeration and delete, or the file is locked),
                // File.Delete below is the arbiter - a vanished file
                // deletes as a no-op, a locked one throws and is
                // retried/noted like any other failure.
                if (!TryWithRetry(() =>
                    {
                        try { File.SetAttributes(file, FileAttributes.Normal); }
                        catch { /* File.Delete decides; see above */ }
                        File.Delete(file);
                    }, out Exception? fileError))
                    NoteFailure(fileError!);
            }
            // Deepest first (by depth, §151 P4c; length-descending as
            // the deterministic tie-break) so every subfolder is
            // empty when deleted.
            foreach (string dir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(DirectoryDepth)
                         .ThenByDescending(d => d.Length))
            {
                if (!TryWithRetry(() => Directory.Delete(dir, recursive: false),
                        out Exception? dirError))
                    NoteFailure(dirError!);
            }
            if (!TryWithRetry(() => Directory.Delete(directory, recursive: false),
                    out Exception? rootError))
                NoteFailure(rootError!);
        }
        catch (Exception ex)
        {
            failureDetail = JoinedFailures() ?? ex.Message;
            return false;
        }

        // Verification: the load-bearing entries must be gone. A
        // stray file that survived without an exception above (e.g.
        // created concurrently) still fails the reset honestly.
        if (File.Exists(mainPath)
            || File.Exists(mainPath + ".bak1")
            || File.Exists(mainPath + ".bak2")
            || File.Exists(mainPath + ".bak3")
            || Directory.Exists(iconFolder))
        {
            failureDetail = JoinedFailures() ?? "Config file or Icons folder survived deletion.";
            return false;
        }
        // §151 P4e: the root itself must be gone, and no stray file
        // may survive under it. A root that still exists - even
        // empty, even with no recorded exception - is a failed
        // reset; the surviving entries are named (up to 5) so the
        // footer/log line says WHAT survived, not just that
        // something did.
        if (Directory.Exists(directory))
        {
            string survivorNote = "App data directory survived deletion.";
            try
            {
                string[] survivors = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                if (survivors.Length > 0)
                    survivorNote += " Surviving: " + string.Join(", ", survivors.Take(5))
                        + (survivors.Length > 5 ? ", …" : "");
            }
            catch { /* the root's existence already fails the reset */ }
            string? joined = JoinedFailures();
            failureDetail = joined is null ? survivorNote : joined + " " + survivorNote;
            return false;
        }
        if (!ok)
        {
            failureDetail = JoinedFailures();
            return false;
        }
        return true;
    }
}
