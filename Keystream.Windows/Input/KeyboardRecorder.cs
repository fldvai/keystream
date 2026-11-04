using System.Runtime.InteropServices;

namespace Keystream.Windows.Input;

/// <summary>Macro recorder, Fase 2 (§62) + down/up detail (§65): a
/// WH_KEYBOARD_LL hook that reports each real key DOWN and key UP while
/// recording, so a recording is a faithful event list (Ctrl+C becomes
/// Ctrl↓, C↓, C↑, Ctrl↑) instead of one tap step per key. Deliberately
/// narrow:
/// <list type="bullet">
/// <item>NEVER swallows a key: the hook always calls CallNextHookEx, so a
/// recorded key still reaches the focused app (recording Ctrl+C in
/// Notepad still copies there).</item>
/// <item>Injected input is IGNORED (LLKHF_INJECTED): keys sent by SendInput
/// - including macro playback and deck keys forwarded by the
/// Interception service - never become recorded steps, so recording can
/// never feed back into itself. Caveat: when the deck service is STOPPED,
/// deck keys flow as plain hardware input and ARE recorded like any
/// other keyboard, because at that point nothing distinguishes them.</item>
/// <item>Key repeats are collapsed: holding a key fires one down event,
/// not one per repeat. A key-up fires only for a key whose down was
/// seen during this recording - a key already held when Rekam is
/// clicked raises no down, so its later release is ignored too (no
/// phantom unmatched up step; press modifiers AFTER Rekam for a
/// faithful recording).</item>
/// <item>Alt+key arrives as WM_SYSKEYDOWN/WM_SYSKEYUP and is handled like
/// WM_KEYDOWN/WM_KEYUP. Esc is an ordinary event here (unlike chord
/// capture, where Esc cancels): recording stops only via the Stop
/// button, the step cap, or the window closing.</item>
/// </list>
/// Threading: the hook proc runs on the thread that installed the hook
/// (the UI thread, since the modal starts recording from a button click),
/// but KeyEvent carries its own timestamp so a consumer that marshals
/// to the dispatcher still computes inter-key gaps correctly.
/// The delegate MUST stay alive as a field: if it is garbage-collected,
/// Windows calls into freed code and the process crashes.
/// This class is Windows-only and cannot be proven on the Linux VM;
/// PC test is the proof.</summary>
public sealed class KeyboardRecorder
{
    /// <summary>One recorded key event: the virtual-key code, whether it
    /// is a key-down (true) or a key-up (false), the modifier snapshot
    /// at that instant (used by tap-mode recording to rebuild chords;
    /// ignored by down/up mode, where each event is one key), plus the
    /// proc-time timestamp (UTC) used for Record Delay gaps.</summary>
    public sealed record KeyRecord(int VirtualKey, bool Down, bool Ctrl, bool Shift, bool Alt, bool Win, DateTimeOffset At);

    public event Action<KeyRecord>? KeyEvent;

    public bool IsRecording => _hookId != IntPtr.Zero;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelKeyboardProc _proc;
    // Keys currently down, seen during this recording: collapses
    // repeats (second down ignored) and filters pre-held releases
    // (up without a seen down ignored).
    private readonly HashSet<int> _down = new();
    private IntPtr _hookId = IntPtr.Zero;
    // Live modifier snapshot (§66, for tap-mode chords): seeded at
    // Start (a modifier already held when Rekam is clicked still
    // counts toward the first tap) and updated on every modifier
    // down/up - including a pre-held modifier's release, which raises
    // no event but must still clear the snapshot, or later taps would
    // wrongly keep its modifier.
    private bool _ctrl;
    private bool _shift;
    private bool _alt;
    private bool _win;

    // Virtual-key codes for modifiers (left + right variants).
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const uint LlkfInjected = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Kbdllhookstruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public KeyboardRecorder()
    {
        _proc = HookProc;
    }

    /// <summary>Installs the hook. Returns false when already recording
    /// or when Windows refuses the hook (hook stays uninstalled).
    /// Modifier state is seeded from the live keyboard: a modifier
    /// already held when Rekam is clicked (hook missed its key-down)
    /// still counts toward the first tap-mode chord.</summary>
    public bool Start()
    {
        if (IsRecording) return false;
        _down.Clear();
        _ctrl = IsDown(VkLControl) || IsDown(VkRControl);
        _shift = IsDown(VkLShift) || IsDown(VkRShift);
        _alt = IsDown(VkLMenu) || IsDown(VkRMenu);
        _win = IsDown(VkLWin) || IsDown(VkRWin);
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        return IsRecording;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Uninstalls the hook. Idempotent: safe to call twice
    /// (window Closing + Closed both call it).</summary>
    public void Stop()
    {
        if (!IsRecording) return;
        IntPtr id = _hookId;
        _hookId = IntPtr.Zero;
        _down.Clear();
        UnhookWindowsHookEx(id);
    }

    private static bool IsModifier(int vk) => vk is VkLShift or VkRShift
        or VkLControl or VkRControl or VkLMenu or VkRMenu or VkLWin or VkRWin;

    private void SetModifier(int vk, bool down)
    {
        switch (vk)
        {
            case VkLShift: case VkRShift: _shift = down; break;
            case VkLControl: case VkRControl: _ctrl = down; break;
            case VkLMenu: case VkRMenu: _alt = down; break;
            case VkLWin: case VkRWin: _win = down; break;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsRecording)
        {
            var info = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            // Injected input (SendInput, incl. deck forwarding) is skipped
            // for BOTH down and up, so the down-set never goes stale.
            if ((info.Flags & LlkfInjected) == 0)
            {
                int vk = (int)info.VkCode;
                int msg = wParam.ToInt32();
                if (msg is WmKeydown or WmSyskeydown)
                {
                    // Snapshot first: a modifier's own down event must
                    // already carry it (tap mode skips modifier events
                    // but uses the snapshot for the next chord).
                    if (IsModifier(vk)) SetModifier(vk, true);
                    // First down only: holding a key (repeats) raises
                    // one down event, not one per repeat.
                    if (_down.Add(vk))
                        KeyEvent?.Invoke(new KeyRecord(vk, true, _ctrl, _shift, _alt, _win, DateTimeOffset.UtcNow));
                }
                else if (msg is WmKeyup or WmSyskeyup)
                {
                    // Snapshot always updated, even for a pre-held key
                    // whose release raises no event (see Start seeding).
                    if (IsModifier(vk)) SetModifier(vk, false);
                    // Up only for a down seen during this recording:
                    // a key already held when Rekam started has no
                    // matching down, so its release is ignored rather
                    // than recorded as a phantom unmatched up step.
                    if (_down.Remove(vk))
                        KeyEvent?.Invoke(new KeyRecord(vk, false, _ctrl, _shift, _alt, _win, DateTimeOffset.UtcNow));
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
