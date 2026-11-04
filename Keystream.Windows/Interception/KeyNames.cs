using System.Runtime.InteropServices;
using Keystream.Core.Localization;
using System.Text;

namespace Keystream.Windows.Interception;

/// <summary>Human-readable key names from the OS itself (GetKeyNameText),
/// used to label captured deck keys in the press-to-map grid.</summary>
public static class KeyNames
{
    public static string GetDisplayName(ushort scanCode, bool isE0)
    {
        int lParam = (scanCode << 16) | (isE0 ? (1 << 24) : 0);
        var sb = new StringBuilder(64);
        int len = GetKeyNameText(lParam, sb, sb.Capacity);
        return len > 0 ? Normalize(sb.ToString()) : Localizer.Format("S1224", scanCode, (isE0 ? "+E0" : ""));
    }

    /// <summary>
    /// Badge style rule (v2.9.34): the OS names a letter/digit key with a
    /// bare character ("P", "5") while the template page spells "Key A" -
    /// normalize single alphanumeric characters to "Key X" so real
    /// captures and the template read identically. Longer names
    /// ("Num 7", "Space", "F13") pass through untouched.
    /// </summary>
    public static string Normalize(string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length == 1 && char.IsLetterOrDigit(trimmed[0])
            ? $"Key {char.ToUpperInvariant(trimmed[0])}" // Literal — never localized: "Key " is an identifier prefix stripped by KeyCapBadge/ChordMask.
            : name;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int nSize);
}
