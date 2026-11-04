using NAudio.CoreAudioApi;

namespace Keystream.Actions.Media;

/// <summary>
/// True device-level microphone mute: toggles the mute state of the
/// default capture endpoint through Core Audio - the same switch the
/// Windows Sound settings flip - so it applies to every application at
/// once. Unlike Win+Alt+K (which only mutes cooperating apps while the
/// mic is in use) and unlike a sent F13 (which needs a listening app),
/// this mutes the device itself, and its state can be read back.
/// When the default communications device is a different endpoint, it
/// is set to the same state so "the microphone" means one thing.
/// </summary>
public static class MicMute
{
    public static (bool Ok, bool NowMuted, string? Error) Toggle()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var primary = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            bool target = !primary.AudioEndpointVolume.Mute;
            primary.AudioEndpointVolume.Mute = target;
            try
            {
                using var comms = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                if (comms.ID != primary.ID)
                    comms.AudioEndpointVolume.Mute = target;
            }
            catch { /* no separate communications device - nothing more to do */ }
            return (true, target, null);
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    /// <summary>Reads the current mute state of the default capture endpoint.</summary>
    public static bool TryGetMuted(out bool muted)
    {
        muted = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            muted = device.AudioEndpointVolume.Mute;
            return true;
        }
        catch { return false; }
    }
}
