using System;
using System.Runtime.InteropServices;

namespace Keystream.Windows.Input;

/// <summary>§113 - minimal global ESC listener, active ONLY during
/// playback/countdown (panic stop). Mirrors KeyboardRecorder's
/// WH_KEYBOARD_LL pattern (same P/Invoke shapes, same
/// never-swallow rule): the hook ALWAYS calls CallNextHookEx, so
/// ESC keeps working everywhere else, and it never interferes with
/// the WPF-level §110 Esc field-revert or chord capture (both live
/// above this hook and see the key normally). Watches VK_ESCAPE
/// key-down only, collapsing auto-repeat (first down fires once
/// until key-up). The event fires on the hook thread (the UI thread
/// that installed the hook); handlers must be idempotent and must
/// never throw - the proc swallows handler exceptions so a faulty
/// handler can never break the hook chain. Start() installs,
/// Stop() uninstalls (both idempotent); Dispose() = Stop(). A
/// failed install returns false from Start() - the caller proceeds
/// without panic (and logs it); playback must never depend on this
/// hook to run.</summary>
public sealed class EscPanicHook : IDisposable
{
    /// <summary>Raised once per ESC press while the hook is active.</summary>
    public event Action? EscapePressed;

    /// <summary>True while the hook is installed.</summary>
    public bool IsActive => _hookId != IntPtr.Zero;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _escDown;

    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const int VkEscape = 0x1B;
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

    public EscPanicHook()
    {
        _proc = HookProc;
    }

    /// <summary>Installs the hook. Returns false when Windows refuses
    /// it (hook stays uninstalled). Idempotent: a second Start while
    /// active is a no-op returning true.</summary>
    public bool Start()
    {
        if (IsActive) return true;
        _escDown = false;
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        return IsActive;
    }

    /// <summary>Uninstalls the hook. Idempotent.</summary>
    public void Stop()
    {
        if (!IsActive) return;
        IntPtr id = _hookId;
        _hookId = IntPtr.Zero;
        _escDown = false;
        UnhookWindowsHookEx(id);
    }

    /// <summary>Stops the hook.</summary>
    public void Dispose() => Stop();

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive)
        {
            var info = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
            // B-11: input injeksi (SendInput, termasuk macro playback
            // dan deck forwarding) dilewati untuk down DAN up, supaya
            // _escDown tidak basi: ESC injeksi tidak boleh memicu
            // panic, dan key-up injeksi tidak boleh menghapus ESC
            // fisik yang masih ditahan. Pola persis KeyboardRecorder
            // (LLKHF_INJECTED, 0x10); CallNextHookEx tetap dipanggil.
            if (info.VkCode == VkEscape && (info.Flags & LlkfInjected) == 0)
            {
                int msg = wParam.ToInt32();
                if (msg is WmKeydown or WmSyskeydown)
                {
                    if (!_escDown)
                    {
                        _escDown = true;
                        try { EscapePressed?.Invoke(); }
                        catch { /* a handler must never break the chain */ }
                    }
                }
                else if (msg is WmKeyup or WmSyskeyup)
                {
                    _escDown = false;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
