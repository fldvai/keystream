using System.Runtime.InteropServices;

namespace Keystream.Actions.Common;

/// <summary>
/// Shared SendInput interop for HotkeyAction, MediaAction, and (§107)
/// MouseAction.
///
/// BUG FIXED (found by the user on hardware, 23 Oct 2026): the first
/// version defined the INPUT union with only KEYBDINPUT, making the
/// struct 32 bytes on x64. The real INPUT is 40 bytes (the union is sized
/// by MOUSEINPUT, its largest member), so SendInput rejected every call
/// with ERROR_INVALID_PARAMETER (errno 87) and "0/N inputs sent". The
/// union below includes MOUSEINPUT to force the correct size, and
/// InputStructSize exposes it so the self-check can pin it forever.
/// The mouse path (§107) reuses that same union - it does not redefine
/// INPUT - and mirrors SendKeys exactly, including errno handling.
/// </summary>
public static class SendInputNative
{
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint INPUT_KEYBOARD = 1;

    // §107: MOUSEEVENTF flags for the mouse path. Callers pass Dx/Dy
    // already normalized to 0..65535 (see MouseCoordinates.ToAbsolute)
    // and set MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK when the
    // coordinates are virtual-screen absolute.
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint INPUT_MOUSE = 0;

    public static int InputStructSize => Marshal.SizeOf<NativeInput>();

    public readonly record struct KeyStroke(ushort VirtualKey, bool KeyUp, bool Extended);

    /// <summary>One mouse input. Dx/Dy are pre-normalized 0..65535
    /// absolute coordinates; Flags carries the MOUSEEVENTF bits;
    /// MouseData is the wheel delta (multiples of WHEEL_DELTA 120)
    /// for wheel strokes and 0 otherwise.</summary>
    public readonly record struct MouseStroke(int Dx, int Dy, uint Flags, int MouseData);

    /// <summary>Sends the strokes as one batch. Returns (sent, errno).</summary>
    public static (uint Sent, int Errno) SendMouse(IReadOnlyList<MouseStroke> strokes)
    {
        var inputs = new NativeInput[strokes.Count];
        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            inputs[i] = new NativeInput
            {
                type = INPUT_MOUSE,
                data = new InputUnion
                {
                    mi = new MouseInput
                    {
                        dx = s.Dx,
                        dy = s.Dy,
                        mouseData = unchecked((uint)s.MouseData),
                        dwFlags = s.Flags,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero,
                    },
                },
            };
        }
        uint sent = SendInput((uint)inputs.Length, inputs, InputStructSize);
        int errno = sent == (uint)inputs.Length ? 0 : Marshal.GetLastWin32Error();
        return (sent, errno);
    }

    /// <summary>Sends the strokes as one batch. Returns (sent, errno).</summary>
    public static (uint Sent, int Errno) SendKeys(IReadOnlyList<KeyStroke> strokes)
    {
        var inputs = new NativeInput[strokes.Count];
        for (int i = 0; i < strokes.Count; i++)
        {
            var s = strokes[i];
            uint flags = 0;
            if (s.KeyUp) flags |= KEYEVENTF_KEYUP;
            if (s.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
            inputs[i] = new NativeInput
            {
                type = INPUT_KEYBOARD,
                data = new InputUnion
                {
                    ki = new KeyboardInput
                    {
                        wVk = s.VirtualKey,
                        wScan = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero,
                    },
                },
            };
        }
        uint sent = SendInput((uint)inputs.Length, inputs, InputStructSize);
        int errno = sent == (uint)inputs.Length ? 0 : Marshal.GetLastWin32Error();
        return (sent, errno);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput ki;
        [FieldOffset(0)] public MouseInput mi; // sizes the union to the real INPUT layout
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint type;
        public InputUnion data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);
}
