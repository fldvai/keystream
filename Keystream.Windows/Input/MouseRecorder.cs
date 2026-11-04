using System.Runtime.InteropServices;

namespace Keystream.Windows.Input;

/// <summary>Mouse recorder for macros (§107): a WH_MOUSE_LL hook that
/// reports each real mouse move, button down/up and wheel event while
/// recording, so a recording is a faithful event list instead of one
/// step per click. Deliberately narrow, mirroring KeyboardRecorder:
/// <list type="bullet">
/// <item>NEVER swallows an event: the hook always calls CallNextHookEx,
/// so a recorded click still reaches the app under the cursor.</item>
/// <item>Injected input is IGNORED (LLMHF_INJECTED) for ALL messages:
/// input sent by SendInput - including macro playback - never becomes
/// a recorded step, so recording can never feed back into itself.</item>
/// <item>Button downs are collapsed per button: holding a button fires
/// one down event, not one per repeat. A button-up fires only for a
/// button whose down was seen during this recording - a button
/// already held when Rekam is clicked raises no down, so its later
/// release is ignored too (no phantom unmatched up step; press
/// buttons AFTER Rekam for a faithful recording).</item>
/// <item>Moves and wheel events always fire: they carry no down/up
/// state, so the down-set never filters them.</item>
/// </list>
/// Threading: the hook proc runs on the thread that installed the hook
/// (the UI thread, since the modal starts recording from a button
/// click), but MouseRecord carries its own timestamp so a consumer
/// that marshals to the dispatcher still computes inter-event gaps
/// correctly. The proc does the minimum only - parse the struct,
/// filter injected input, raise the event last - and never runs user
/// logic: the subscriber marshals to the UI thread, exactly like
/// KeyboardRecorder's KeyEvent.
/// The delegate MUST stay alive as a field: if it is garbage-collected,
/// Windows calls into freed code and the process crashes.
/// This class is Windows-only and cannot be proven on the Linux VM;
/// PC test is the proof.</summary>
public sealed class MouseRecorder
{
    /// <summary>One recorded mouse event: what happened (move, down,
    /// up, wheel), which button it concerns (None for moves and wheel),
    /// the cursor position in screen coordinates, the wheel delta
    /// (meaningful only for wheel events: positive for scroll up,
    /// negative for scroll down, in WHEEL_DELTA units), plus the
    /// proc-time timestamp (UTC) used for Record Delay gaps.</summary>
    public sealed record MouseRecord(MouseEventKind Kind, MouseButton Button, int X, int Y, int Delta, DateTimeOffset At);

    public enum MouseEventKind { Move, Down, Up, Wheel }

    public enum MouseButton { None, Left, Right, Middle }

    public event Action<MouseRecord>? MouseEvent;

    public bool IsRecording => _hookId != IntPtr.Zero;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelMouseProc _proc;
    // Buttons currently down, seen during this recording: collapses
    // repeats (second down ignored) and filters pre-held releases
    // (up without a seen down ignored). Moves and wheel never touch
    // this set.
    private readonly HashSet<MouseButton> _down = new();
    private IntPtr _hookId = IntPtr.Zero;

    private const int WhMouseLl = 14;
    private const int WmMousemove = 0x0200;
    private const int WmLbuttondown = 0x0201;
    private const int WmLbuttonup = 0x0202;
    private const int WmRbuttondown = 0x0204;
    private const int WmRbuttonup = 0x0205;
    private const int WmMbuttondown = 0x0207;
    private const int WmMbuttonup = 0x0208;
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

    public MouseRecorder()
    {
        _proc = HookProc;
    }

    /// <summary>Installs the hook. Returns false when already recording
    /// or when Windows refuses the hook (hook stays uninstalled, so
    /// IsRecording stays false and the caller must not pretend to
    /// record - the modal aborts Rekam with an error dialog on false,
    /// mirroring the KeyboardRecorder S0939 path).</summary>
    public bool Start()
    {
        if (IsRecording) return false;
        _down.Clear();
        _hookId = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        return IsRecording;
    }

    /// <summary>Uninstalls the hook. Idempotent: safe to call twice
    /// (window Closing + Closed both call it). _hookId is cleared
    /// before UnhookWindowsHookEx so IsRecording is truthful even if
    /// the unhook itself fails.</summary>
    public void Stop()
    {
        if (!IsRecording) return;
        IntPtr id = _hookId;
        _hookId = IntPtr.Zero;
        _down.Clear();
        UnhookWindowsHookEx(id);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            var info = Marshal.PtrToStructure<Msllhookstruct>(lParam);
            // Injected input (SendInput, incl. macro playback) is
            // skipped for ALL messages, so the down-set never goes stale.
            if ((info.Flags & LlmhfInjected) == 0)
            {
                int msg = wParam.ToInt32();
                int x = info.Pt.X;
                int y = info.Pt.Y;
                if (msg == WmMousemove)
                {
                    MouseEvent?.Invoke(new MouseRecord(MouseEventKind.Move, MouseButton.None, x, y, 0, DateTimeOffset.UtcNow));
                }
                else if (msg == WmMousewheel)
                {
                    // Wheel delta is the signed high word of mouseData.
                    int delta = (short)(info.MouseData >> 16);
                    MouseEvent?.Invoke(new MouseRecord(MouseEventKind.Wheel, MouseButton.None, x, y, delta, DateTimeOffset.UtcNow));
                }
                else if (msg is WmLbuttondown or WmRbuttondown or WmMbuttondown)
                {
                    MouseButton button = msg == WmLbuttondown ? MouseButton.Left
                        : msg == WmRbuttondown ? MouseButton.Right : MouseButton.Middle;
                    // First down only: holding a button raises one down
                    // event, not one per repeat.
                    if (_down.Add(button))
                        MouseEvent?.Invoke(new MouseRecord(MouseEventKind.Down, button, x, y, 0, DateTimeOffset.UtcNow));
                }
                else if (msg is WmLbuttonup or WmRbuttonup or WmMbuttonup)
                {
                    MouseButton button = msg == WmLbuttonup ? MouseButton.Left
                        : msg == WmRbuttonup ? MouseButton.Right : MouseButton.Middle;
                    // Up only for a down seen during this recording:
                    // a button already held when Rekam started has no
                    // matching down, so its release is ignored rather
                    // than recorded as a phantom unmatched up step.
                    if (_down.Remove(button))
                        MouseEvent?.Invoke(new MouseRecord(MouseEventKind.Up, button, x, y, 0, DateTimeOffset.UtcNow));
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
