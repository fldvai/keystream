using System;
using System.Runtime.InteropServices;

namespace Keystream.App.Host;

/// <summary>F-C6 - per-monitor DPI queries for the floating panel's
/// position clamps. The panel must be clamped against the work area
/// of the monitor that actually contains the target position, using
/// THAT monitor's DPI - never the DPI of the monitor the panel
/// window happens to sit on (the old code read the panel's own
/// PresentationSource transform for every screen, which is wrong by
/// up to 640 DIP on a 100% + 150% pair). These queries need no
/// window and no PresentationSource: they work before the first
/// Show, on any thread that can call Win32. WPF-FREE (only System
/// types), so the wb-harness can link this file directly; the calls
/// themselves are Windows-only and must never be made on Linux.
/// Never throws: every failure (no monitor, failed HRESULT, a
/// missing shcore.dll on an old Windows, DllNotFound on Linux)
/// returns false and the caller falls back to its previous
/// behaviour. No process-wide DPI-awareness change is made here:
/// this file only READS each monitor's effective DPI.</summary>
public static class MonitorDpi
{
    /// <summary>MonitorFromPoint flag: return the nearest monitor
    /// when the point is on no monitor (the point is always inside
    /// some monitor's bounds in practice; nearest is the safe
    /// fallback, never null for a real desktop).</summary>
    private const uint MonitorDefaultToNearest = 2;

    /// <summary>MONITOR_DPI_TYPE: the effective DPI (what the user
    /// sees, including any DPI virtualization overrides).</summary>
    private const int MonitorDpiTypeEffective = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(Point pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Effective DPI of the monitor containing the given
    /// device-pixel point (virtual-screen pixel coordinates, the
    /// same space as System.Windows.Forms.Screen bounds). Returns
    /// false (outs left at 0) on any failure, including when the
    /// native calls are unavailable. Never throws.</summary>
    public static bool TryGetDpiForPixelPoint(int pixelX, int pixelY, out uint dpiX, out uint dpiY)
    {
        dpiX = 0;
        dpiY = 0;
        try
        {
            IntPtr monitor = MonitorFromPoint(new Point { X = pixelX, Y = pixelY }, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;
            int hr = GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out uint x, out uint y);
            if (hr != 0 || x == 0 || y == 0) return false;
            dpiX = x;
            dpiY = y;
            return true;
        }
        catch
        {
            dpiX = 0;
            dpiY = 0;
            return false;
        }
    }
}
