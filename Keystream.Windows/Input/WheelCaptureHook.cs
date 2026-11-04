using System.Runtime.InteropServices;

namespace Keystream.Windows.Input;

/// <summary>Dial-scroll capture hook (§156): a WH_MOUSE_LL hook that,
/// while a ScrollDial key is held, SWALLOWS the vertical mouse wheel
/// (WM_MOUSEWHEEL) so the wheel adjusts the master volume instead of
/// scrolling whatever is under the cursor, and releases the wheel
/// the instant the key is released. Deliberately the opposite of
/// <see cref="MouseRecorder"/> in exactly one respect - the recorder
/// NEVER swallows; this hook swallows ONLY the wheel, ONLY while
/// capture is active. Every other message (moves, button downs/ups,
/// horizontal tilt WM_MOUSEHWHEEL) passes through untouched, and
/// injected input (LLMHF_INJECTED) is ignored for the wheel too, so
/// macro playback can never feed the dial.
///
/// Threading: the hook is installed on the UI thread (see AppHost -
/// installing from the driver thread stalls delivery, there is no
/// message pump there), so the proc runs on the UI thread. Even so
/// the proc does the minimum only: parse the wheel delta, accumulate
/// it with a lock-free Interlocked add, return. It never calls the
/// engine, the dispatcher, or any action - the AppHost tick drains
/// the pending delta into DeckEngine.AddDialDelta. The delegate MUST
/// stay alive as a field: if it is garbage-collected, Windows calls
/// into freed code and the process crashes.
/// This class is Windows-only and cannot be proven on the Linux VM;
/// PC test is the proof.</summary>
public sealed class WheelCaptureHook
{
    /// <summary>Pure swallow/pass decision, separated from the Win32
    /// proc so the harness can test it without a hook: swallow only a
    /// real (non-injected), in-range vertical-wheel message while
    /// capture is active, and report its signed delta in WHEEL_DELTA
    /// units. Everything else passes through with delta 0.</summary>
    public static (bool Swallow, int Delta) Decide(int nCode, int message, uint flags, uint mouseData, bool capturing)
    {
        if (nCode < 0 || !capturing || message != WmMousewheel)
            return (false, 0);
        // Injected input (SendInput, incl. macro playback) is skipped
        // for the wheel too: playback must never become dial input.
        if ((flags & LlmhfInjected) != 0)
            return (false, 0);
        // Wheel delta is the signed high word of mouseData.
        return (true, (short)(mouseData >> 16));
    }

    /// <summary>True while the hook is installed (capture active).</summary>
    public bool IsCapturing => _hookId != IntPtr.Zero;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    // Lock-free handoff: the proc only Interlocked-adds here; the
    // AppHost tick drains it (Interlocked.Exchange to 0) into
    // DeckEngine.AddDialDelta. No lock, no I/O on the hook path.
    private int _pendingDelta;

    private const int WhMouseLl = 14;
    private const int WmMousewheel = 0x020A;
    private const uint LlmhfInjected = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msllhookstruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public WheelCaptureHook()
    {
        _proc = HookProc;
    }

    /// <summary>Installs the hook. MUST be called on the UI thread (see
    /// AppHost). Returns false when already capturing or when Windows
    /// refuses the hook (hook stays uninstalled, so IsCapturing stays
    /// false and the caller must not pretend to capture).</summary>
    public bool Start()
    {
        if (IsCapturing) return false;
        Interlocked.Exchange(ref _pendingDelta, 0);
        _hookId = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        return IsCapturing;
    }

    /// <summary>Uninstalls the hook and discards any not-yet-drained
    /// delta, so a stale remainder can never leak into the next dial
    /// hold. Idempotent and exception-safe: safe to call twice, safe
    /// to call when never started. _hookId is cleared before
    /// UnhookWindowsHookEx so IsCapturing is truthful even if the
    /// unhook itself fails.</summary>
    public void Stop()
    {
        if (!IsCapturing) return;
        IntPtr id = _hookId;
        _hookId = IntPtr.Zero;
        Interlocked.Exchange(ref _pendingDelta, 0);
        try { UnhookWindowsHookEx(id); }
        catch { /* unhook failure must never crash the caller */ }
    }

    /// <summary>Atomically takes the accumulated wheel delta (WHEEL_DELTA
    /// units, signed) and resets the slot to zero. Called only from the
    /// AppHost tick on the UI thread.</summary>
    public int DrainPendingDelta() => Interlocked.Exchange(ref _pendingDelta, 0);

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsCapturing && wParam.ToInt32() == WmMousewheel)
        {
            // Only wheel messages need the struct (flags + delta).
            // Every other message passes through below without being
            // parsed at all - moves, buttons, and horizontal tilt
            // (WM_MOUSEHWHEEL) are never swallowed, never accumulated.
            var info = Marshal.PtrToStructure<Msllhookstruct>(lParam);
            var (swallow, delta) = Decide(nCode, WmMousewheel, info.Flags, info.MouseData, capturing: true);
            if (swallow)
            {
                Interlocked.Add(ref _pendingDelta, delta);
                // Swallow: nonzero return, CallNextHookEx NOT called -
                // the wheel event reaches no application.
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
