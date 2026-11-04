using System.Collections.Generic;

namespace Keystream.Core.Input;

/// <summary>§168: the chord-modifier mask — the ONE new concept for
/// button mapping with chords. A binding's key is (ScanCode, IsE0,
/// ChordMask): mask 0 is a plain single key, a non-zero mask means the
/// card fires only while those modifiers are held when the main key
/// goes down. Left/right variants of one modifier are the SAME bit.
/// The Win key is NOT mapped. Scancodes are standard PS/2 set-1 values:
/// R-Ctrl / R-Alt arrive with the E0 prefix on the same base scancode
/// as their left twins; L-Shift / R-Shift have distinct scancodes and
/// no E0 prefix. Pure and unit-testable; display names go through the
/// OS (KeyNames) everywhere else — this table is the input-decision
/// table only.</summary>
public static class ChordMask
{
    public const byte None = 0;
    public const byte Ctrl = 1;
    public const byte Alt = 2;
    public const byte Shift = 4;

    /// <summary>The single mask bit for a physical key, or
    /// <see cref="None"/> when the key is not a Ctrl/Alt/Shift
    /// modifier (including the Win key, which is unmapped).</summary>
    public static byte BitFor(KeyIdentity key)
    {
        if (!key.IsE0)
        {
            return key.ScanCode switch
            {
                0x1D => Ctrl,
                0x38 => Alt,
                0x2A => Shift,
                0x36 => Shift,
                _ => None,
            };
        }
        return key.ScanCode switch
        {
            0x1D => Ctrl,
            0x38 => Alt,
            _ => None,
        };
    }

    /// <summary>ORs <see cref="BitFor"/> over the held keys, so the
    /// mask is the set of modifiers held when the main key's DOWN
    /// arrives — decision-at-DOWN, computed on the spot from the
    /// engine's held-key set, with no new engine state.</summary>
    public static byte FromHeldKeys(IEnumerable<KeyIdentity> held)
    {
        byte mask = None;
        foreach (var key in held)
            mask |= BitFor(key);
        return mask;
    }

    /// <summary>Display name of a mapped key: modifier names in the
    /// conventional Ctrl, Alt, Shift order, then the main-key name
    /// ("Ctrl+S", "Ctrl+Alt+X"). A mask of <see cref="None"/> yields
    /// the main-key name alone, unchanged. A leading "Key " prefix
    /// on the main-key name ("Key S", the OS display form) is
    /// stripped so the chord reads "Ctrl+S", never "Ctrl+Key S".</summary>
    public static string Format(byte mask, string mainKeyName)
    {
        if (mask == None) return mainKeyName;
        string main = mainKeyName.Trim();
        if (main.StartsWith("Key ", System.StringComparison.OrdinalIgnoreCase))
            main = main.Substring(4);
        var parts = new List<string>(4);
        if ((mask & Ctrl) != 0) parts.Add("Ctrl");
        if ((mask & Alt) != 0) parts.Add("Alt");
        if ((mask & Shift) != 0) parts.Add("Shift");
        parts.Add(main);
        return string.Join("+", parts);
    }

    /// <summary>§168a: pending-capture cap text — the modifiers held so
    /// far only, uppercase ("CTRL", "CTRL+SHIFT"), in the conventional
    /// Ctrl, Alt, Shift order. Empty when no modifier is held; the
    /// caller shows its idle cap ("…") in that case.</summary>
    public static string FormatModifiers(byte mask)
    {
        if (mask == None) return "";
        var parts = new List<string>(3);
        if ((mask & Ctrl) != 0) parts.Add("CTRL");
        if ((mask & Alt) != 0) parts.Add("ALT");
        if ((mask & Shift) != 0) parts.Add("SHIFT");
        return string.Join("+", parts);
    }
}

/// <summary>§168: pure accumulation state machine for press-to-map
/// chord capture, shared by the dashboard capture
/// (MainViewModel) and the in-editor reassign capture
/// (BindingEditorViewModel) so both sites obey exactly one rule.
/// Only modifier DOWNs seen while capture is armed count; the mask
/// is the set of modifiers HELD when the main key's DOWN arrives;
/// if every held modifier is released with no main key having
/// arrived, the capture completes with the FIRST modifier pressed
/// as a single key (mask 0, no chord). No timers, no ordering: a
/// modifier pressed after the main key's DOWN never joins, and a
/// released modifier never counts.</summary>
public sealed class PendingChord
{
    /// <summary>Modifiers currently held (bits from
    /// <see cref="ChordMask"/>). 0 while no chord is pending.</summary>
    public byte Mask { get; private set; }

    /// <summary>The first modifier whose DOWN started the pending
    /// state, or null when nothing is pending. Its release — once
    /// the mask empties — completes the capture as a single key.</summary>
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
        byte bit = ChordMask.BitFor(key);
        if (bit == ChordMask.None) return false;
        if (Mask == ChordMask.None)
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
        byte bit = ChordMask.BitFor(key);
        if (bit == ChordMask.None) return null;
        bool wasSet = (Mask & bit) != 0;
        Mask = (byte)(Mask & ~bit);
        if (!wasSet || Mask != ChordMask.None || FirstModifier is not KeyIdentity first) return null;
        uint vk = FirstModifierVk;
        Reset();
        return (first, vk);
    }

    /// <summary>Clear all pending state: arming, cancel, and every
    /// completion path. A leaked mask would corrupt the next capture.</summary>
    public void Reset()
    {
        Mask = ChordMask.None;
        FirstModifier = null;
        FirstModifierVk = 0;
    }
}
