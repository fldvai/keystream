using Keystream.Core.Localization;
namespace Keystream.Actions.Hotkey;

/// <summary>
/// Parses a hotkey chord string ("F13", "Ctrl+Shift+F13", "Alt+F4") into
/// virtual-key codes. Pure logic - the parser is fully unit-tested; only
/// the final SendInput call is Windows-bound. The LAST token is the main
/// key; every preceding token must be a modifier. Unknown tokens are
/// errors, never silently dropped (the legacy HotkeyAction silently
/// returned when its single "Key" param was missing or unparsable).
/// </summary>
public sealed record KeyChord(IReadOnlyList<ushort> Modifiers, ushort MainKey, string Canonical, bool MainExtended = false)
{
    private static readonly Dictionary<string, ushort> ModifiersByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11,
        ["shift"] = 0x10,
        ["alt"] = 0x12,
        ["win"] = 0x5B, ["windows"] = 0x5B, ["meta"] = 0x5B,
    };

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["space"] = 0x20, ["tab"] = 0x09, ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["ins"] = 0x2D,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["capslock"] = 0x14, ["printscreen"] = 0x2C, ["pause"] = 0x13,
        ["scrolllock"] = 0x91, ["numlock"] = 0x90,
        ["divide"] = 0x6F, ["multiply"] = 0x6A, ["subtract"] = 0x6D,
        ["add"] = 0x6B, ["decimal"] = 0x6E,
        ["numpaddivide"] = 0x6F, ["numpadmultiply"] = 0x6A,
        ["numpadsubtract"] = 0x6D, ["numpadadd"] = 0x6B, ["numpaddecimal"] = 0x6E,
        ["menu"] = 0x5D, ["apps"] = 0x5D,
        ["backtick"] = 0xC0, ["minus"] = 0xBD, ["equals"] = 0xBB,
        ["leftbracket"] = 0xDB, ["rightbracket"] = 0xDD, ["backslash"] = 0xDC,
        ["semicolon"] = 0xBA, ["apostrophe"] = 0xDE, ["quote"] = 0xDE,
        ["comma"] = 0xBC, ["period"] = 0xBE, ["slash"] = 0xBF,
    };

    /// <summary>Literal punctuation that may be typed directly as a
    /// one-character token (everything except '+', the separator).</summary>
    private static readonly Dictionary<char, ushort> PunctuationKeys = new()
    {
        ['-'] = 0xBD, ['='] = 0xBB, ['['] = 0xDB, [']'] = 0xDD, ['\\'] = 0xDC,
        [';'] = 0xBA, ['\''] = 0xDE, [','] = 0xBC, ['.'] = 0xBE, ['/'] = 0xBF,
        ['`'] = 0xC0,
    };

    /// <summary>VK codes that need KEYEVENTF_EXTENDEDKEY in SendInput.</summary>
    public static bool IsExtendedKey(ushort vk)
        => vk is 0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22
            or 0x25 or 0x26 or 0x27 or 0x28 or 0x6F or 0x90;

    public static bool TryParse(string? text, out KeyChord? chord, out string error)
    {
        chord = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = Localizer.Get("S1144");
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = Localizer.Get("S1144");
            return false;
        }

        var modifiers = new List<ushort>();
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (ModifiersByName.TryGetValue(tokens[i], out var mod))
            {
                if (!modifiers.Contains(mod)) modifiers.Add(mod);
            }
            else
            {
                error = Localizer.Format("S1145", tokens[i]);
                return false;
            }
        }

        if (!TryParseKeyEx(tokens[^1], out ushort main, out bool mainExtended))
        {
            error = Localizer.Format("S1146", tokens[^1]);
            return false;
        }

        chord = new KeyChord(modifiers, main, string.Join("+", tokens.Select(t => t.ToUpperInvariant())), mainExtended);
        return true;
    }

    /// <summary>Resolves a SINGLE key token (§65, keydown/keyup steps) to
    /// its VK code: modifiers (Ctrl/Shift/Alt/Win) first, then the normal
    /// key table. Unlike TryParse, this accepts a lone modifier - a
    /// chord always needs a non-modifier main key, but a hold/release
    /// step IS one key, modifier or not. Multi-token strings ("Ctrl+C")
    /// are rejected: one step, one key.</summary>
    public static bool TryParseSingleKey(string? token, out ushort vk, out bool extended)
    {
        vk = 0;
        extended = false;
        if (string.IsNullOrWhiteSpace(token)) return false;
        string t = token.Trim();
        if (ModifiersByName.TryGetValue(t, out vk)) return true;
        return TryParseKeyEx(t, out vk, out extended);
    }

    public static bool TryParseKeyEx(string token, out ushort vk, out bool extended)
    {
        vk = 0;
        extended = false;
        // Numpad Enter shares VK_RETURN with the main Enter; only the
        // extended flag tells them apart.
        if (token.Equals("numpadenter", StringComparison.OrdinalIgnoreCase))
        {
            vk = 0x0D;
            extended = true;
            return true;
        }
        if (NamedKeys.TryGetValue(token, out vk)) return true;

        if (token.Length >= 2 && (token[0] == 'F' || token[0] == 'f')
            && int.TryParse(token.AsSpan(1), out int fNum) && fNum >= 1 && fNum <= 24)
        {
            vk = (ushort)(0x70 + fNum - 1); // VK_F1..VK_F24 = 0x70..0x87
            return true;
        }

        if (token.Length == 1)
        {
            if (PunctuationKeys.TryGetValue(token[0], out vk)) return true;
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' || c is >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        if (token.StartsWith("numpad", StringComparison.OrdinalIgnoreCase)
            && token.Length == 7 && char.IsDigit(token[6]))
        {
            vk = (ushort)(0x60 + (token[6] - '0')); // VK_NUMPAD0..
            return true;
        }
        return false;
    }
}
