using Keystream.Actions.Common;

namespace Keystream.Actions.Media;

/// <summary>Dial-volume plan (§156): the pure, state-free translation
/// of a signed wheel delta into master-volume key strokes. Kept apart
/// from <see cref="MediaAction"/> so the harness can test the mapping
/// without touching real system volume: the action itself is a
/// no-op-safe consumer of <c>ActionContext.WheelDelta</c> - it holds no
/// state of its own and never decides anything the plan does not.
/// One notch (WHEEL_DELTA, 120 units) = one volume step; a remainder
/// below one notch produces no step here (the engine retains the
/// remainder across ticks). Zero delta = no plan (no-op).</summary>
public static class DialVolumePlan
{
    public const ushort VolumeUpVk = 0xAF; // VK_VOLUME_UP
    public const ushort VolumeDownVk = 0xAE; // VK_VOLUME_DOWN
    public const int WheelDeltaPerNotch = 120; // WHEEL_DELTA

    /// <summary>Maps a signed wheel delta to (virtual key, step count),
    /// or null when the delta holds less than one whole notch in
    /// either direction. Positive delta (scroll up) raises the volume;
    /// negative delta lowers it.</summary>
    public static (ushort VirtualKey, int Steps)? Plan(int wheelDelta)
    {
        int steps = Math.Abs(wheelDelta) / WheelDeltaPerNotch;
        if (steps == 0) return null;
        return (wheelDelta > 0 ? VolumeUpVk : VolumeDownVk, steps);
    }

    /// <summary>Builds the one-batch SendInput stroke list for a plan:
    /// one down+up pair per step, using EXACTLY the shared
    /// <see cref="SendInputNative"/> key-stroke shape MediaAction uses
    /// for its other volume commands. No P/Invoke lives here.</summary>
    public static IReadOnlyList<SendInputNative.KeyStroke> BuildStrokes((ushort VirtualKey, int Steps) plan)
    {
        var strokes = new SendInputNative.KeyStroke[plan.Steps * 2];
        for (int i = 0; i < plan.Steps; i++)
        {
            strokes[i * 2] = new SendInputNative.KeyStroke(plan.VirtualKey, KeyUp: false, Extended: false);
            strokes[i * 2 + 1] = new SendInputNative.KeyStroke(plan.VirtualKey, KeyUp: true, Extended: false);
        }
        return strokes;
    }
}
