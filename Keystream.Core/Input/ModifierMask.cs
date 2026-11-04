namespace Keystream.Core.Input;

/// <summary>§167: modifier-chord mask helper — the sanctioned exception
/// to the tree's "no hardcoded scancode tables" rule (display names go
/// through the OS; this table is the input-decision table, pure and
/// unit-testable). Mask bits: Ctrl = 1, Alt = 2, Shift = 4; left and
/// right variants of one modifier are the SAME bit. Mask 0 (no
/// modifier held) is NOT a chord. The Win key is NOT mapped in v1.
/// Scancodes are standard PS/2 set-1 values: R-Ctrl / R-Alt arrive
/// with the E0 prefix on the same base scancode as their left twins;
/// L-Shift / R-Shift have distinct scancodes and no E0 prefix.</summary>
public static class ModifierMask
{
    public const byte None = 0;
    public const byte Ctrl = 1;
    public const byte Alt = 2;
    public const byte Shift = 4;

    /// <summary>The single mask bit for a physical key, or
    /// <see cref="None"/> when the key is not a Ctrl/Alt/Shift
    /// modifier (including the Win key, which is unmapped in v1).</summary>
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
    /// arrives — decision-at-DOWN (§167 D2), computed on the spot
    /// from the engine's held-key set, with no new engine state.</summary>
    public static byte FromHeldKeys(IEnumerable<KeyIdentity> held)
    {
        byte mask = None;
        foreach (var key in held)
            mask |= BitFor(key);
        return mask;
    }

    /// <summary>Display name of a chord: modifier names in the
    /// conventional Ctrl, Alt, Shift order, then the main-key name
    /// ("Ctrl+S", "Ctrl+Alt+X"). A mask of <see cref="None"/> yields
    /// the main-key name alone.</summary>
    public static string Format(byte mask, string mainKeyName)
    {
        if (mask == None) return mainKeyName;
        var parts = new List<string>(4);
        if ((mask & Ctrl) != 0) parts.Add("Ctrl");
        if ((mask & Alt) != 0) parts.Add("Alt");
        if ((mask & Shift) != 0) parts.Add("Shift");
        parts.Add(mainKeyName);
        return string.Join("+", parts);
    }

    /// <summary>§167b: the modifier names in a mask, in the same
    /// conventional Ctrl, Alt, Shift order as <see cref="Format"/>,
    /// without the main key — the waiting modal renders one keycap
    /// per name plus a pending main-key slot. Empty for mask
    /// <see cref="None"/>.</summary>
    public static IReadOnlyList<string> Names(byte mask)
    {
        var parts = new List<string>(3);
        if ((mask & Ctrl) != 0) parts.Add("Ctrl");
        if ((mask & Alt) != 0) parts.Add("Alt");
        if ((mask & Shift) != 0) parts.Add("Shift");
        return parts;
    }
}
