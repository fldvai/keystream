using System;
using System.Collections.Generic;
using Keystream.Core.Localization;

namespace Keystream.Core.Config;

/// <summary>§170 Task 4: template anti-bentrok. When a template page is
/// loaded, its cards are fitted to EMPTY keys: a card keeps its own key
/// when free, otherwise it takes the first free key from the preference
/// order (letters A-Z, digits 0-9, Enter, numpad). No existing card is
/// ever re-keyed, deactivated, or touched, and no loaded card is ever
/// dropped: when no key is free the card keeps its original key and the
/// existing duplicate warning fires as before. Key identity is the
/// §168 identity (ScanCode, IsE0, ChordMask): a chord-mapped key does
/// not block a plain key and vice versa. Only the fitted card's key
/// (ScanCode + matching KeyName + recomputed KeyId) changes; function,
/// label, and steps are untouched.</summary>
public static class TemplateKeyFit
{
    /// <summary>One remap applied by <see cref="FitKeys"/>: the card's
    /// label plus the key it moved from and to, for the load notice.</summary>
    public sealed record Remap(string Label, string FromKey, string ToKey);

    private static (ushort Scan, bool IsE0, string KeyName) Letter(char letter, ushort scan)
        => (scan, false, letter.ToString());

    /// <summary>Deterministic preference order: letters A-Z, digits 0-9,
    /// Enter, then the numpad keys. Numpad display names come from the
    /// same string keys the template builders use ("Num 7", ...).</summary>
    public static IReadOnlyList<(ushort Scan, bool IsE0, string KeyName)> PreferenceOrder()
    {
        var order = new List<(ushort, bool, string)>
        {
            Letter('A', 0x1E), Letter('B', 0x30), Letter('C', 0x2E), Letter('D', 0x20),
            Letter('E', 0x12), Letter('F', 0x21), Letter('G', 0x22), Letter('H', 0x23),
            Letter('I', 0x17), Letter('J', 0x24), Letter('K', 0x25), Letter('L', 0x26),
            Letter('M', 0x32), Letter('N', 0x31), Letter('O', 0x18), Letter('P', 0x19),
            Letter('Q', 0x10), Letter('R', 0x13), Letter('S', 0x1F), Letter('T', 0x14),
            Letter('U', 0x16), Letter('V', 0x2F), Letter('W', 0x11), Letter('X', 0x2D),
            Letter('Y', 0x15), Letter('Z', 0x2C),
            (0x0B, false, "0"), (0x02, false, "1"), (0x03, false, "2"), (0x04, false, "3"),
            (0x05, false, "4"), (0x06, false, "5"), (0x07, false, "6"), (0x08, false, "7"),
            (0x09, false, "8"), (0x0A, false, "9"),
            (0x1C, false, "Enter"),
            (0x47, false, Localizer.Get("S0421")), (0x48, false, Localizer.Get("S0413")),
            (0x49, false, Localizer.Get("S0426")), (0x4B, false, Localizer.Get("S0411")),
            (0x4C, false, Localizer.Get("S0407")), (0x4D, false, Localizer.Get("S0409")),
            (0x4F, false, Localizer.Get("S0417")), (0x50, false, Localizer.Get("S0415")),
            (0x51, false, Localizer.Get("S0419")), (0x52, false, "Num 0"),
            (0x53, false, Localizer.Get("S0433")),
        };
        return order;
    }

    /// <summary>Fit <paramref name="page"/>'s cards against
    /// <paramref name="blocked"/> (Global + nav keys, §168 identity).
    /// Cards are processed in template order; a remapped key counts as
    /// taken for the cards after it. Returns the remaps applied, in
    /// template order; empty when every card kept its own key.</summary>
    public static IReadOnlyList<Remap> FitKeys(PageConfig page, IEnumerable<(ushort ScanCode, bool IsE0, byte ChordMask)> blocked)
    {
        var taken = new HashSet<(ushort, bool, byte)>(blocked);
        var remaps = new List<Remap>();
        var preference = PreferenceOrder();
        foreach (var card in page.Bindings)
        {
            // §175-sweep: a keyless card (ScanCode 0) has no key to
            // fit and must never be assigned one: without this guard
            // a second keyless card would look "blocked" by the first
            // and silently gain key A. Keyless cards stay keyless and
            // do not occupy a key for the cards after them.
            if (card.ScanCode == 0) continue;
            var identity = (card.ScanCode, card.IsE0, card.ChordMask);
            if (!taken.Contains(identity))
            {
                taken.Add(identity);
                continue;
            }
            foreach (var (scan, e0, name) in preference)
            {
                var candidate = (scan, e0, card.ChordMask);
                if (taken.Contains(candidate)) continue;
                string from = string.IsNullOrWhiteSpace(card.KeyName) ? card.KeyId : card.KeyName;
                remaps.Add(new Remap(card.Label, from, name));
                card.ScanCode = scan;
                card.IsE0 = e0;
                card.KeyName = name;
                card.KeyId = $"K-{scan:X2}";
                taken.Add(candidate);
                break;
            }
            // No free key: the card keeps its original key (the identity
            // is already in "taken") and the existing warning fires.
        }
        return remaps;
    }
}
