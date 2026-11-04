namespace Keystream.Core.Input;

/// <summary>§167a: pure accumulation state machine for press-to-map
/// chord capture. The capture branch in the main view-model delegates
/// to it; the harness pins it here because the view-model needs
/// AppHost. The rules are the §167 D2 rule plus the user's modifier-alone rule
/// (19:41 WIB): only modifier DOWNs seen while capture is armed
/// count; the mask is the set of modifiers HELD when the main key's
/// DOWN arrives; if every held modifier is released with no main key
/// having arrived, the capture completes with the FIRST modifier
/// pressed as a single key (mask 0, no chord entry). No timers, no
/// ordering: a modifier pressed after the main key's DOWN never
/// joins the chord, and a released modifier never counts.</summary>
public sealed class CaptureChordAccumulator
{
    /// <summary>Modifiers currently held (bits from
    /// <see cref="ModifierMask"/>). 0 while no chord is pending.</summary>
    public byte Mask { get; private set; }

    /// <summary>The first modifier whose DOWN started the pending
    /// state, or null when nothing is pending. Its release — once the
    /// mask empties — completes the capture as a single key.</summary>
    public KeyIdentity? FirstModifier { get; private set; }

    /// <summary>Virtual key of <see cref="FirstModifier"/> (kept so a
    /// modifier-alone completion binds the same key the driver
    /// reported, not a reconstructed one).</summary>
    public uint FirstModifierVk { get; private set; }

    /// <summary>Feed a key DOWN (non-repeat, capture armed). Returns
    /// true when the key is a modifier absorbed into the pending chord
    /// (the caller stays armed); false when it is the main key (the
    /// caller completes the capture with the current
    /// <see cref="Mask"/>). State is untouched for a main key.</summary>
    public bool AbsorbDown(KeyIdentity key, uint virtualKey)
    {
        byte bit = ModifierMask.BitFor(key);
        if (bit == ModifierMask.None) return false;
        if (Mask == ModifierMask.None)
        {
            FirstModifier = key;
            FirstModifierVk = virtualKey;
        }
        Mask |= bit;
        return true;
    }

    /// <summary>Feed a key UP (capture armed). Returns the first-pressed
    /// modifier (with its virtual key) when this release empties the
    /// pending mask with no main key having arrived: the caller
    /// completes the capture with it as a SINGLE key. Returns null in
    /// every other case (the caller stays armed). A release whose bit
    /// was never set — e.g. a modifier held before arming — never
    /// completes. Resets itself on completion, so the snapshot the
    /// caller takes afterwards is always mask 0.</summary>
    public (KeyIdentity Key, uint VirtualKey)? ReleaseUp(KeyIdentity key)
    {
        byte bit = ModifierMask.BitFor(key);
        if (bit == ModifierMask.None) return null;
        bool wasSet = (Mask & bit) != 0;
        Mask = (byte)(Mask & ~bit);
        if (!wasSet || Mask != ModifierMask.None || FirstModifier is not KeyIdentity first) return null;
        uint vk = FirstModifierVk;
        Reset();
        return (first, vk);
    }

    /// <summary>Clear all pending state: arming, cancel, and every
    /// completion path. A leaked mask would corrupt the next capture.</summary>
    public void Reset()
    {
        Mask = ModifierMask.None;
        FirstModifier = null;
        FirstModifierVk = 0;
    }
}
