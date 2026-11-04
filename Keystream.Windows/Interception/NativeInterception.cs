using System.Runtime.InteropServices;

namespace Keystream.Windows.Interception;

/// <summary>
/// P/Invoke for interception.dll (official Interception C API by Francisco
/// Lopes). Declarations match the vendor samples. Two audit lessons are
/// applied here (findings B1-10 and B1-12 from the codebase audit):
///  - the filter predicate delegate is stored in a STATIC ROOTED field -
///    the native library keeps the function pointer for the context's whole
///    lifetime, so the delegate must never be collectable;
///  - hardware-id strings are NUL-trimmed before any comparison.
/// </summary>
public static class NativeInterception
{
    public const ushort FILTER_KEY_ALL = 0xFFFF;
    public const int KEY_STATE_UP = 0x01;   // InterceptionKeyState: UP bit
    public const int KEY_STATE_E0 = 0x02;
    public const int KEY_STATE_E1 = 0x04;

    public delegate int InterceptionPredicate(int device);

    // ROOTED for the process lifetime (B1-10). Never a local, never a lambda
    // that only the native side references.
    public static readonly InterceptionPredicate KeyboardPredicate = device => interception_is_keyboard(device);

    [StructLayout(LayoutKind.Sequential)]
    public struct InterceptionKeyStroke
    {
        public ushort code;
        public ushort state;
        public uint information;
    }

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr interception_create_context();

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_destroy_context(IntPtr context);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, ushort filter);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_wait_with_timeout(IntPtr context, uint milliseconds);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_send(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_receive(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint interception_get_hardware_id(IntPtr context, int device, IntPtr buffer, uint bufferSize);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_is_keyboard(int device);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint code, uint mapType);

    public const uint MAPVK_VSC_TO_VK_EX = 3;

    /// <summary>Reads a device's hardware id. Returns "" when the driver
    /// reports none - callers must treat "" as UNKNOWN, never as an identity
    /// (B1-12: the audited code cached "" permanently).</summary>
    public static string GetHardwareId(IntPtr context, int device)
    {
        IntPtr buffer = Marshal.AllocHGlobal(2048);
        try
        {
            uint bytes = interception_get_hardware_id(context, device, buffer, 2048);
            if (bytes == 0) return "";
            string raw = Marshal.PtrToStringUni(buffer, (int)bytes / 2) ?? "";
            return raw.TrimEnd('\0').Trim();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
