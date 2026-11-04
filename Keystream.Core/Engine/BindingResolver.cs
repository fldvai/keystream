using Keystream.Core.Config;
using Keystream.Core.Localization;
using Keystream.Core.Input;

namespace Keystream.Core.Engine;

/// <summary>Turns the persisted config into the lookup maps the engine
/// needs for one page. Multiple BindingConfig entries may share a
/// KeyIdentity (one per trigger - e.g. Tap does X, LongPress does Y);
/// the resolver groups them instead of letting one silently overwrite the
/// other.</summary>
public static class BindingResolver
{
    public sealed record ResolvedPage(
        IReadOnlyDictionary<KeyIdentity, IReadOnlySet<TriggerKind>> TriggerMap,
        IReadOnlyDictionary<(KeyIdentity Key, TriggerKind Trigger), BindingConfig> Bindings,
        IReadOnlyDictionary<(KeyIdentity FirstKey, KeyIdentity SecondKey), (BindingConfig Primary, KeyCombinationConfig Combo)> Combinations,
        IReadOnlyDictionary<(byte Mask, KeyIdentity Key), BindingConfig> ChordBindings,
        IReadOnlyList<string> Warnings);

    public static ResolvedPage Resolve(DeckConfig config, string? pageId = null)
    {
        var triggerMap = new Dictionary<KeyIdentity, HashSet<TriggerKind>>();
        var bindings = new Dictionary<(KeyIdentity, TriggerKind), BindingConfig>();
        var combinations = new Dictionary<(KeyIdentity, KeyIdentity), (BindingConfig, KeyCombinationConfig)>();
        var chordBindings = new Dictionary<(byte Mask, KeyIdentity Key), BindingConfig>();

        // Peringatan resolver (§45): dulu semua pembuangan di bawah ini
        // diam-diam - binding hilang tanpa jejak. Kini dihitung dan
        // dikembalikan lewat ResolvedPage.Warnings untuk footer.
        int droppedNoKey = 0, droppedNonTapCombo = 0, droppedPhantomCombo = 0;
        int overriddenBinding = 0, overriddenCombo = 0, droppedChordNonTap = 0;
        var profile = config.ActiveProfile;
        // A-02: an explicit-null list reads as empty, null elements are
        // skipped (Normalize in ConfigStore owns the canonical fix).
        var pages = (profile?.Pages ?? new List<PageConfig>()).Where(p => p is not null).ToList();
        bool pageFallback = pageId is not null && profile is not null && pages.Count > 0
            && pages.All(p => p.Id != pageId);
        var page = pages.FirstOrDefault(p => pageId is null || p.Id == pageId)
                   ?? pages.FirstOrDefault();
        if (page is not null)
        {
            foreach (var binding in page.Bindings ?? new List<BindingConfig>())
            {
                if (binding is null) continue; // A-02
                if (!binding.IsActive) continue; // §132: inactive = invisible to all three maps
                if (binding.ScanCode == 0) { droppedNoKey++; continue; } // unplaced legacy/blank binding
                var identity = new KeyIdentity(binding.ScanCode, binding.IsE0, binding.IsE1);
                // §168: a chord-mapped binding's key is (mask, key) -
                // it owns NO plain-key entry: the plain maps below
                // would let the main key alone fire its steps. Only
                // plain (mask 0) bindings enter triggerMap/bindings.
                if (binding.ChordMask == ChordMask.None)
                {
                    if (!triggerMap.TryGetValue(identity, out var set))
                    {
                        set = new HashSet<TriggerKind>();
                        triggerMap[identity] = set;
                    }
                    set.Add(binding.Trigger);
                    if (bindings.ContainsKey((identity, binding.Trigger))) overriddenBinding++;
                    bindings[(identity, binding.Trigger)] = binding;
                }

                // Batch 1 fix: combinations are only safe on Tap parents.
                // Repeat fires on DOWN, LongPress fires via Tick, Momentary
                // fires on DOWN + needs its release - all of them would
                // already have run (or stuck a layer) before the second key
                // is pressed, so a non-Tap parent must never own a combo.
                // §156: ScrollDial falls under the same gate (fires on
                // DOWN + needs its release to end the wheel capture), so
                // a ScrollDial binding's combinations are dropped and
                // counted in droppedNonTapCombo (warning S1054) exactly
                // like the other non-Tap triggers - no new combo rule.
                // Release kinds (MomentaryRelease/ScrollDialRelease) are
                // never directly bindable: the editor's TriggerOptions
                // picker (App layer) does not offer them, and the
                // dispatcher pairs them with the remembered hold binding,
                // so a stray hand-edited release binding in this map can
                // never execute a step directly. No filter is added here,
                // mirroring MomentaryRelease exactly.
                // §168: the SAME Tap-only gate drops a chord-mapped
                // binding itself (counted separately, warning S1784) -
                // a chord fires only as Tap in v1, like combinations.
                if (binding.Trigger != TriggerKind.Tap)
                {
                    droppedNonTapCombo += binding.Combinations?.Count ?? 0;
                    if (binding.ChordMask != ChordMask.None) droppedChordNonTap++;
                    continue;
                }
                foreach (var combo in binding.Combinations ?? new List<KeyCombinationConfig>())
                {
                    if (combo is null) continue; // A-02
                    if (combo.SecondScanCode == 0) { droppedPhantomCombo++; continue; }
                    var secondIdentity = new KeyIdentity(combo.SecondScanCode, combo.SecondIsE0, false);
                    if (combinations.ContainsKey((identity, secondIdentity))) overriddenCombo++;
                    combinations[(identity, secondIdentity)] = (binding, combo);
                }
                // §168: chord-mapped Tap binding enters the chord map
                // keyed by (mask, key). A duplicate (mask, key) pair
                // last-wins, counted with the plain override counter
                // (warning S1056) - one binding overriding another.
                if (binding.ChordMask != ChordMask.None)
                {
                    if (chordBindings.ContainsKey((binding.ChordMask, identity))) overriddenBinding++;
                    chordBindings[(binding.ChordMask, identity)] = binding;
                }
            }
        }

        // Global navigation overlay (v2.7 slots; retired into the Global
        // page in v2.8 but kept here so an un-migrated config still
        // resolves identically): nav shadows the WHOLE key.
        if (profile is not null)
        {
            foreach (var nav in profile.NavBindings ?? new List<BindingConfig>())
            {
                if (nav is null) continue; // A-02
                if (!nav.IsActive) continue; // §132: inactive nav must not shadow the whole key
                if (nav.ScanCode == 0) { droppedNoKey++; continue; }
                var identity = new KeyIdentity(nav.ScanCode, nav.IsE0, nav.IsE1);
                foreach (var stale in bindings.Keys.Where(k => k.Item1 == identity).ToList())
                    bindings.Remove(stale);
                // Batch 1 fix: nav shadows the WHOLE key, so page combos
                // starting from this key must go too, or the old combo
                // would still fire under the nav binding.
                foreach (var staleCombo in combinations.Keys.Where(k => k.Item1 == identity).ToList())
                    combinations.Remove(staleCombo);
                // §168: same whole-key rule for chord-mapped bindings
                // on this key, for every mask.
                foreach (var staleChord in chordBindings.Keys.Where(k => k.Key == identity).ToList())
                    chordBindings.Remove(staleChord);
                triggerMap[identity] = new HashSet<TriggerKind> { nav.Trigger };
                bindings[(identity, nav.Trigger)] = nav;
            }
        }

        // GLOBAL page overlay (v2.8): highest scope, key-level like nav -
        // if the Global page uses a key at all, every lower-scope binding
        // for that key is dropped; then ALL global bindings for the key
        // (it may itself use several triggers) are installed.
        if (config.Settings is not null && config.Settings.IsGlobalEnabled)
        {
            var globalBindings = config.GlobalPage?.Bindings ?? new List<BindingConfig>();
            var globalKeys = new HashSet<KeyIdentity>(globalBindings
                .Where(g => g is not null && g.ScanCode != 0 && g.IsActive) // §132: inactive global must not shadow page bindings
                .Select(g => new KeyIdentity(g.ScanCode, g.IsE0, g.IsE1)));
            foreach (var identity in globalKeys)
            {
                foreach (var stale in bindings.Keys.Where(k => k.Item1 == identity).ToList())
                    bindings.Remove(stale);
                triggerMap.Remove(identity);
                // Batch 1 fix: same as nav above - a key fully owned by
                // Global must not keep page combos. Global's own combos are
                // added afterwards, so they survive this cleanup.
                foreach (var staleCombo in combinations.Keys.Where(k => k.Item1 == identity).ToList())
                    combinations.Remove(staleCombo);
                // §168: same whole-key rule for chord-mapped bindings.
                foreach (var staleChord in chordBindings.Keys.Where(k => k.Key == identity).ToList())
                    chordBindings.Remove(staleChord);
            }
            foreach (var global in globalBindings)
            {
                if (global is null) continue; // A-02
                if (!global.IsActive) continue; // §132: inactive = invisible to all three maps
                if (global.ScanCode == 0) { droppedNoKey++; continue; }
                var identity = new KeyIdentity(global.ScanCode, global.IsE0, global.IsE1);
                // §168: same plain/chord split as the page loop above -
                // a chord-mapped global binding owns no plain-key entry.
                if (global.ChordMask == ChordMask.None)
                {
                    if (!triggerMap.TryGetValue(identity, out var gset))
                    {
                        gset = new HashSet<TriggerKind>();
                        triggerMap[identity] = gset;
                    }
                    gset.Add(global.Trigger);
                    bindings[(identity, global.Trigger)] = global;
                }

                // Batch 1 fix: same Tap-only gate as page bindings
                // (§156: ScrollDial combos are dropped with S1054 here
                // too - see the page loop's comment).
                if (global.Trigger != TriggerKind.Tap)
                {
                    droppedNonTapCombo += global.Combinations?.Count ?? 0;
                    if (global.ChordMask != ChordMask.None) droppedChordNonTap++;
                    continue;
                }
                foreach (var combo in global.Combinations ?? new List<KeyCombinationConfig>())
                {
                    if (combo is null) continue; // A-02
                    if (combo.SecondScanCode == 0) { droppedPhantomCombo++; continue; }
                    var secondIdentity = new KeyIdentity(combo.SecondScanCode, combo.SecondIsE0, false);
                    if (combinations.ContainsKey((identity, secondIdentity))) overriddenCombo++;
                    combinations[(identity, secondIdentity)] = (global, combo);
                }
                // §168: chord-mapped global Tap binding, same rule as
                // the page loop (duplicate last-wins, S1056 count).
                if (global.ChordMask != ChordMask.None)
                {
                    if (chordBindings.ContainsKey((global.ChordMask, identity))) overriddenBinding++;
                    chordBindings[(global.ChordMask, identity)] = global;
                }
            }
        }

        var warnings = new List<string>();
        if (droppedNoKey > 0) warnings.Add(Localizer.Format("S1053", droppedNoKey));
        if (droppedNonTapCombo > 0) warnings.Add(Localizer.Format("S1054", droppedNonTapCombo));
        if (droppedPhantomCombo > 0) warnings.Add(Localizer.Format("S1055", droppedPhantomCombo));
        if (overriddenBinding > 0) warnings.Add(Localizer.Format("S1056", overriddenBinding));
        if (overriddenCombo > 0) warnings.Add(Localizer.Format("S1057", overriddenCombo));
        if (droppedChordNonTap > 0) warnings.Add(Localizer.Format("S1784", droppedChordNonTap));
        if (pageFallback) warnings.Add(Localizer.Format("S1058", pageId));
        return new ResolvedPage(
            triggerMap.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<TriggerKind>)kv.Value),
            bindings,
            combinations,
            chordBindings,
            warnings);
    }

    /// <summary>§132: the single implementation of "same-scope duplicate
    /// keys" - keys used by at least two ACTIVE bindings in the supplied
    /// list (keyless bindings, ScanCode == 0, never count). Used by the
    /// dashboard warning grouping and the editor warning; both call
    /// sites must use this helper (or the identical active-only rule)
    /// rather than inventing a second rule. §168: the key INCLUDES the
    /// chord mask - (S, mask 0) and (S, mask Ctrl) are DIFFERENT keys,
    /// so a plain card and a chord-mapped card on the same physical
    /// key never count as duplicates of each other. A-07: the key also
    /// includes IsE1, so an E1 card and a non-E1 card on the same scan
    /// code never count as duplicates of each other either.</summary>
    public static IReadOnlySet<(ushort ScanCode, bool IsE0, byte ChordMask)> DuplicateKeys(IEnumerable<BindingConfig> bindings)
    {
        // A-07: grouping includes IsE1 (an E1 binding and a non-E1
        // binding on the same scan code are different keys); the
        // returned tuple shape is unchanged, so callers are unaffected.
        var counts = new Dictionary<(ushort, bool, bool, byte), int>();
        if (bindings is not null)
            foreach (var b in bindings)
            {
                if (b is null || !b.IsActive || b.ScanCode == 0) continue; // A-02: null element
                var k = (b.ScanCode, b.IsE0, b.IsE1, b.ChordMask);
                counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
            }
        var result = new HashSet<(ushort, bool, byte)>();
        foreach (var (k, n) in counts)
            if (n >= 2) result.Add((k.Item1, k.Item2, k.Item4));
        return result;
    }
}
