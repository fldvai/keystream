using System.Diagnostics;
using System.Runtime.InteropServices;
using Keystream.Core.Config;

namespace Keystream.Actions.Browser;

/// <summary>
/// Tracks the window Keystream opened for each (browser, profile), so a
/// later press can focus or close it instead of opening a duplicate.
///
/// Why a snapshot diff: Chromium delegates a launch to its already-running
/// instance, so the process Keystream started usually exits at once and
/// its PID is useless. The window itself is the stable identity. After
/// launching, the tracker polls for a NEW visible top-level window owned
/// by the browser's process and records its handle, owner PID and owner
/// start time. A record is only trusted while all three still match, so
/// a recycled handle value can never focus or close a stranger window.
///
/// Known limits, stated plainly: (1) if the browser is slow to show its
/// window (cold start beyond the poll window), tracking is missed and
/// the next press opens a new window; (2) when a group launches several
/// profiles of the same browser nearly at once, a tracked handle can
/// occasionally attach to a sibling window of the same browser - focus
/// may then land on the wrong profile. Single-profile tiles, the main
/// use case, do not have problem (2): only one new window appears.
/// Tracking runs in the background and never delays the tile's result:
/// the press returns as soon as the browser process starts.</summary>
public static class BrowserWindowTracker
{
    private const uint WM_CLOSE = 0x0010;
    private const int SW_RESTORE = 9;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static BrowserWindowTracking? Find(IReadOnlyList<BrowserWindowTracking> list, string exePath, string profileKey)
        => list?.FirstOrDefault(t => t is not null
            && string.Equals(t.BrowserExe, exePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ProfileKey, profileKey, StringComparison.Ordinal));

    public static void Upsert(List<BrowserWindowTracking> list, BrowserWindowTracking record)
    {
        if (list is null || record is null) return;
        list.RemoveAll(t => t is not null
            && string.Equals(t.BrowserExe, record.BrowserExe, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ProfileKey, record.ProfileKey, StringComparison.Ordinal));
        list.Add(record);
    }

    public static void Remove(List<BrowserWindowTracking> list, string exePath, string profileKey)
    {
        if (list is null) return;
        list.RemoveAll(t => t is not null
            && string.Equals(t.BrowserExe, exePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ProfileKey, profileKey, StringComparison.Ordinal));
    }

    /// <summary>True only if the tracked window still exists AND belongs
    /// to the same process that owned it when tracked.</summary>
    public static bool IsValid(BrowserWindowTracking tracking)
    {
        try
        {
            var hwnd = new IntPtr(tracking.Hwnd);
            if (!IsWindow(hwnd)) return false;
            var process = Process.GetProcessById(tracking.ProcessId);
            return string.Equals(process.ProcessName,
                    Path.GetFileNameWithoutExtension(tracking.BrowserExe),
                    StringComparison.OrdinalIgnoreCase)
                && process.StartTime.Ticks == tracking.ProcessStartTicks;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Brings the tracked window forward, restoring it first if
    /// minimized. Uses the attach-thread-input trick so a background
    /// caller (a deck key press) can actually take the foreground;
    /// without it Windows would only flash the taskbar button.</summary>
    public static bool Focus(BrowserWindowTracking tracking)
    {
        if (tracking is null) return false;
        var hwnd = new IntPtr(tracking.Hwnd);
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread
            && AttachThreadInput(foregroundThread, currentThread, true);
        try
        {
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(foregroundThread, currentThread, false);
        }
    }

    /// <summary>Graceful close (WM_CLOSE), deliberately not a kill: the
    /// browser process is shared by every profile, and killing it risks
    /// unsaved forms and running downloads in other windows.</summary>
    public static void Close(BrowserWindowTracking tracking)
    {
        if (tracking is null) return;
        SendMessageTimeout(new IntPtr(tracking.Hwnd), WM_CLOSE, UIntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, 2000, out _);
    }

    public static HashSet<IntPtr> SnapshotWindows()
    {
        var handles = new HashSet<IntPtr>();
        try
        {
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0) handles.Add(hwnd);
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Bukan Windows, atau EnumWindows gagal: tracking dilewati
            // saja - aksi pembuka profil tidak boleh gagal karenanya.
            return new HashSet<IntPtr>();
        }
        return handles;
    }

    /// <summary>Polls (up to ~4 s) for a new visible top-level window
    /// owned by the browser's process, then returns its tracking record.
    /// Returns null when nothing appears in time - the launch itself
    /// still succeeded; only tracking is missed.</summary>
    public static async Task<BrowserWindowTracking?> TrackNewWindowAsync(
        string exePath, string profileKey, HashSet<IntPtr> before, CancellationToken cancellation)
    {
        string processName = Path.GetFileNameWithoutExtension(exePath);
        for (int i = 0; i < 16 && !cancellation.IsCancellationRequested; i++)
        {
            BrowserWindowTracking? found = null;
            EnumWindows((hwnd, _) =>
            {
                if (found is not null) return false;
                if (before.Contains(hwnd) || !IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) <= 0) return true;
                try
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    var process = Process.GetProcessById((int)pid);
                    if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                        found = new BrowserWindowTracking
                        {
                            BrowserExe = exePath,
                            ProfileKey = profileKey,
                            ProcessId = (int)pid,
                            ProcessStartTicks = process.StartTime.Ticks,
                            Hwnd = hwnd.ToInt64(),
                        };
                }
                catch
                {
                    // Process vanished mid-enumeration; keep polling.
                }
                return true;
            }, IntPtr.Zero);
            if (found is not null) return found;
            try { await Task.Delay(250, cancellation); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
}
