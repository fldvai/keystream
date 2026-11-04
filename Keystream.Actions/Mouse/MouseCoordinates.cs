using System.Runtime.InteropServices;

namespace Keystream.Actions.Mouse;

/// <summary>A snapshot of the virtual screen (all monitors, in the
/// coordinate space mouse playback uses): its origin, its size, and
/// the system DPI captured with it. Dpi is fingerprint data only -
/// scaling uses the bounds, never the DPI - so a stale DPI can never
/// move a click.</summary>
public sealed record VirtualScreenSnapshot(int Left, int Top, int Width, int Height, int Dpi);

/// <summary>Pure, testable coordinate math for mouse playback (§107),
/// plus the one live read (the current virtual screen). Nothing here
/// sends input, so it can be exercised on any machine, including the
/// Linux harness: only TryCaptureCurrent touches user32.dll, and it
/// reports failure honestly instead of throwing.
///
/// Fallback rule, stated once here and relied on by MouseAction: when
/// TryCaptureCurrent fails, the caller falls back to the recorded
/// bounds (scale 1:1) - a recorded point is replayed verbatim rather
/// than dropped or clicked blindly against unknown bounds.</summary>
public static class MouseCoordinates
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int FallbackDpi = 96;

    /// <summary>Scales a recorded point onto the current virtual
    /// screen, per axis, against the virtual origin: x' = current.Left
    /// + (x - recorded.Left) * current.Width / recorded.Width (same
    /// for y). Integer math with long intermediates, so a recorded
    /// coordinate near int.MaxValue cannot overflow. A recorded
    /// snapshot with a zero width or height cannot scale anything, so
    /// the point passes through unchanged (1:1); a current snapshot
    /// with a zero width or height does the same - callers never feed
    /// such a snapshot here, because TryCaptureCurrent rejects it,
    /// but a direct caller must still get a sane answer, never a
    /// divide-by-zero.</summary>
    public static (int X, int Y) ScalePoint(int x, int y, VirtualScreenSnapshot recorded, VirtualScreenSnapshot current)
    {
        if (recorded.Width <= 0 || recorded.Height <= 0 || current.Width <= 0 || current.Height <= 0)
            return (x, y);
        long sx = (long)current.Left + ((long)x - recorded.Left) * current.Width / recorded.Width;
        long sy = (long)current.Top + ((long)y - recorded.Top) * current.Height / recorded.Height;
        return ((int)sx, (int)sy);
    }

    /// <summary>Converts a virtual-screen point to SendInput absolute
    /// coordinates (0..65535 per axis): dx = (x - current.Left) *
    /// 65535 / (current.Width - 1), same for y, clamped to 0..65535.
    /// The -1 keeps the last pixel on each axis reachable (x =
    /// Left + Width - 1 maps to 65535). A snapshot a single pixel
    /// wide (or narrower) has no span to map, so that axis maps to 0.</summary>
    public static (int Dx, int Dy) ToAbsolute(int x, int y, VirtualScreenSnapshot current)
    {
        int dx = current.Width <= 1
            ? 0
            : (int)((long)(x - current.Left) * 65535 / (current.Width - 1));
        int dy = current.Height <= 1
            ? 0
            : (int)((long)(y - current.Top) * 65535 / (current.Height - 1));
        return (Math.Clamp(dx, 0, 65535), Math.Clamp(dy, 0, 65535));
    }

    /// <summary>True when the point is a real pixel of the snapshot:
    /// [Left, Left + Width) × [Top, Top + Height). Long comparisons,
    /// so Left + Width can never overflow past int.MaxValue.</summary>
    public static bool IsInside(int x, int y, VirtualScreenSnapshot s)
        => (long)x >= s.Left && (long)x < (long)s.Left + s.Width
            && (long)y >= s.Top && (long)y < (long)s.Top + s.Height;

    /// <summary>Reads the current virtual screen via GetSystemMetrics
    /// (SM_X/Y/CX/CYVIRTUALSCREEN) plus GetDpiForSystem. Returns false
    /// with snapshot = default when the metrics are unreadable or
    /// unusable (Width/Height &lt;= 0) - including off Windows, where
    /// user32.dll does not exist and the P/Invoke throws. The DPI
    /// falls back to 96 on any failure of its own call; a failed DPI
    /// read never fails the capture, because scaling never uses it.
    /// On capture failure the caller falls back to the recorded
    /// bounds (scale 1:1); see the class comment.</summary>
    public static bool TryCaptureCurrent(out VirtualScreenSnapshot snapshot)
    {
        snapshot = default!;
        try
        {
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (width <= 0 || height <= 0)
                return false;
            int dpi = FallbackDpi;
            try
            {
                uint captured = GetDpiForSystem();
                if (captured > 0)
                    dpi = (int)captured;
            }
            catch
            {
                // GetDpiForSystem missing (old Windows) or unreadable:
                // keep the 96 fallback - DPI never affects scaling.
                dpi = FallbackDpi;
            }
            snapshot = new VirtualScreenSnapshot(left, top, width, height, dpi);
            return true;
        }
        catch
        {
            // Metrics unreadable (not Windows, or user32.dll failed):
            // report failure honestly; the caller falls back to the
            // recorded bounds instead of guessing.
            snapshot = default!;
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
