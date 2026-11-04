using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Keystream.Windows.Input;

/// <summary>Window helpers for mouse recording (§107), for the App
/// layer only: which window sits under a recorded point, and what the
/// foreground window is at record time. P/Invoke only - no UI, no
/// user-facing strings, no logging. Windows-only; the VM cannot prove
/// any of these calls, PC test is the proof.</summary>
public static class MouseWindowHelper
{
    /// <summary>Snapshot of the foreground window: its handle, title,
    /// window class, owning process name, and screen rectangle.
    /// Title, class name and process name are best-effort and empty
    /// when their lookup fails; the rectangle is always usable when
    /// TryGetForeground returns true.</summary>
    public sealed record ForegroundWindowInfo(IntPtr Handle, string Title, string ClassName, string ProcessName, int Left, int Top, int Width, int Height);

    /// <summary>True only if the window at the given screen point
    /// belongs to the current process.
    /// Fail-open, by design: FALSE on ANY API failure (no window at
    /// the point, thread/process lookup failure, or user32
    /// unavailable). False means "not proven to be ours", so the
    /// caller records the event instead of dropping it - a failure
    /// must never silently swallow a real mouse step. The cost of a
    /// false negative is one extra recorded click on Keystream's own
    /// window; the cost of a false positive would be a lost step.</summary>
    public static bool IsOwnProcessPoint(int x, int y)
    {
        try
        {
            IntPtr hwnd = WindowFromPoint(new NativePoint { X = x, Y = y });
            if (hwnd == IntPtr.Zero) return false;
            if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return false;
            return pid == (uint)Environment.ProcessId;
        }
        catch
        {
            // Fail-open (see doc comment): any failure is "not ours",
            // so the event is recorded rather than dropped.
            return false;
        }
    }

    /// <summary>Reads the current foreground window. Returns false
    /// when there is no foreground window or its rectangle is
    /// unusable (GetWindowRect failed, or zero/negative size); info
    /// is then the empty sentinel (zero handle, empty strings, zero
    /// rectangle). Title, class name and process name are filled
    /// best-effort and left empty when their individual lookup
    /// fails - that alone never makes the call return false.</summary>
    public static bool TryGetForeground(out ForegroundWindowInfo info)
    {
        info = new ForegroundWindowInfo(IntPtr.Zero, string.Empty, string.Empty, string.Empty, 0, 0, 0, 0);
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(hwnd, out NativeRect rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return false;

        string title = GetText(hwnd, GetWindowText, 512);
        string className = GetText(hwnd, GetClassName, 256);

        string processName = string.Empty;
        if (GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid != 0)
        {
            try
            {
                processName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                // Best-effort: the process vanished or cannot be read;
                // the window snapshot is still usable without its name.
                processName = string.Empty;
            }
        }

        info = new ForegroundWindowInfo(hwnd, title, className, processName,
            rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    private static string GetText(IntPtr hwnd, Func<IntPtr, StringBuilder, int, int> getter, int capacity)
    {
        var sb = new StringBuilder(capacity);
        return getter(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint pt);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
