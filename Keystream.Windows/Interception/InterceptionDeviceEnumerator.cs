using System;
using Keystream.Core.Localization;
using System.Collections.Generic;

namespace Keystream.Windows.Interception;

/// <summary>One-shot keyboard enumeration for the deck picker modal.
/// Creates its own Interception context, lists keyboards 1..20, destroys
/// the context. No filter is set on this context, so it never receives
/// or disturbs input - a running service keeps its own context.
/// Mirrors the service worker's failure modes: missing dll, wrong arch,
/// context == 0 (driver not installed / no reboot yet).
/// A device that errors mid-scan is skipped, never fatal to the list.</summary>
public static class InterceptionDeviceEnumerator
{
    public sealed record DeviceInfo(int DeviceNumber, string HardwareId);

    public static (List<DeviceInfo> Devices, string? Error) Enumerate()
    {
        var devices = new List<DeviceInfo>();
        IntPtr context;
        try
        {
            context = NativeInterception.interception_create_context();
        }
        catch (DllNotFoundException)
        {
            return (devices, Localizer.Get("S1220"));
        }
        catch (BadImageFormatException)
        {
            return (devices, Localizer.Get("S1221"));
        }
        catch (Exception ex)
        {
            return (devices, Localizer.Format("S1222", ex.Message));
        }
        if (context == IntPtr.Zero)
            return (devices, Localizer.Get("S1223"));

        try
        {
            for (int device = 1; device <= 20; device++)
            {
                try
                {
                    if (NativeInterception.interception_is_keyboard(device) == 0) continue;
                    string hwId = NativeInterception.GetHardwareId(context, device);
                    if (hwId.Length == 0) continue;
                    devices.Add(new DeviceInfo(device, hwId));
                }
                catch (Exception)
                {
                    // Satu perangkat bermasalah tidak boleh menggagalkan daftar.
                    continue;
                }
            }
        }
        finally
        {
            try { NativeInterception.interception_destroy_context(context); }
            catch (Exception) { }
        }
        return (devices, null);
    }
}
