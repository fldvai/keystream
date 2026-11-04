using Microsoft.Win32;
using Keystream.Core.Localization;

namespace Keystream.Windows.Interception;

public enum DriverState
{
    /// <summary>The deck service is running - strongest possible proof.</summary>
    Working,
    /// <summary>Driver service registered, deck service not running.</summary>
    Installed,
    /// <summary>Driver registered in registry, but context creation failed (requires Windows reboot).</summary>
    PendingRestart,
    NotDetected,
    Unknown,
}

/// <summary>
/// Honest Interception driver presence check (UI v2.6). Proof levels, in
/// order: (1) if the deck service is RUNNING the driver necessarily
/// works; (2) the Interception installer registers a kernel service named
/// "keyboard" - its registry key is a reliable passive signal; (3)
/// otherwise report not-detected with the remedy, never a guess dressed
/// as a fact.
/// </summary>
public static class DriverStatus
{
    public static (DriverState State, string Text) Check(bool serviceRunning)
    {
        if (serviceRunning)
            return (DriverState.Working, Localizer.Get("S1212"));
        if (!OperatingSystem.IsWindows())
            return (DriverState.Unknown, Localizer.Get("S1213"));
        try
        {
            var kernelState = ProbeKernelState();
            if (kernelState == DriverState.PendingRestart)
            {
                return (DriverState.PendingRestart, Localizer.Get("S1922"));
            }
            if (kernelState == DriverState.Working)
                return (DriverState.Installed, Localizer.Get("S1214"));

            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\keyboard");
            if (key is not null)
                return (DriverState.Installed, Localizer.Get("S1214"));
        }
        catch
        {
            return (DriverState.Unknown, Localizer.Get("S1215"));
        }
        return (DriverState.NotDetected,
            Localizer.Get("S1216"));
    }

    /// <summary>
    /// Probe langsung komunikasi ke kernel filter driver Interception.
    /// Membedakan dengan presisi antara driver yang sudah aktif (Working),
    /// driver yang baru terpasang di registry tapi belum reboot (PendingRestart),
    /// atau driver yang belum terpasang (NotDetected).
    /// </summary>
    public static DriverState ProbeKernelState()
    {
        if (!OperatingSystem.IsWindows()) return DriverState.Unknown;

        try
        {
            IntPtr ctx = NativeInterception.interception_create_context();
            if (ctx != IntPtr.Zero)
            {
                NativeInterception.interception_destroy_context(ctx);
                return DriverState.Working;
            }
        }
        catch (DllNotFoundException)
        {
            return DriverState.NotDetected;
        }
        catch
        {
            // Context call threw exception
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\keyboard");
            if (key is not null)
            {
                // Terdaftar di Services tapi context = 0 => Windows butuh reboot!
                return DriverState.PendingRestart;
            }
        }
        catch
        {
            return DriverState.Unknown;
        }

        return DriverState.NotDetected;
    }
}
