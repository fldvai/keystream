using System.Text.Json;
using Keystream.Core.Config;

namespace Keystream.App.Services;

/// <summary>§132 D1/D2/D3 - card transfer, page duplication, and the
/// session undo store for bulk delete. Deliberately WPF-FREE (only
/// Core config types + plain collections + System.Text.Json), so this
/// file can be linked directly into the plain net8.0 wb-harness - the
/// same discipline as TileActionBarLogic.cs / TextBoxRevertLogic.cs.
/// No Save, no reload, no UI lives here: every mutating operation
/// prepares in-memory list changes and returns exactly what the
/// caller (MainViewModel) needs for its ONE Save + rollback + reload
/// + footer notice. See the wiring spec in each method's remarks.
/// The caller owns: Save, ReloadActivePage, RefreshBindings, the
/// AppDialog confirms, and all footer/section notices (string keys
/// are named in the wiring spec, never hard-coded here).</summary>
public static class CardTransferService
{
    /// <summary>Label basis shared by §124 Duplikat: a blank label
    /// displays as the KeyId, so the duplicate-name rule compares
    /// display labels. Mirrors BindingItemViewModel.DisplayLabel
    /// (MainViewModel.cs) and the basis DuplicateBarTarget passes to
    /// TileActionBarLogic.UniquifyDuplicateLabel.</summary>
    public static string DisplayLabelOf(BindingConfig binding)
        => string.IsNullOrWhiteSpace(binding.Label) ? binding.KeyId : binding.Label;

    /// <summary>KeyId derivation, identical to
    /// BindingEditorViewModel.KeyIdFor. KeyId is a display/derivation
    /// key, never an identity (selection is reference-keyed, the
    /// resolver keys on ScanCode/IsE0): "fresh" for a copy therefore
    /// means recomputed from the copied physical key, which yields
    /// the same string for the same key - correct, not a bug.</summary>
    private static string KeyIdFor(ushort scan, bool e0)
        => $"K-{scan:X2}{(e0 ? "E" : "")}";

    /// <summary>Duplicate-name rule, identical to
    /// TileActionBarLogic.UniquifyDuplicateLabel (Ordinal comparison,
    /// "Base", "Base (2)", "Base (3)", …). Kept as a local copy so
    /// this file stays linkable without an App.Views dependency; the
    /// harness asserts parity between the two implementations over
    /// the same cases (CardTransferTests).</summary>
    public static string UniquifyDuplicateLabel(string baseLabel, IEnumerable<string> existingLabels)
    {
        var taken = new HashSet<string>(existingLabels, StringComparer.Ordinal);
        if (!taken.Contains(baseLabel)) return baseLabel;
        int n = 2;
        while (taken.Contains($"{baseLabel} ({n})")) n++;
        return $"{baseLabel} ({n})";
    }

    /// <summary>Deep clone of one card for copy/move/duplicate-page.
    /// JSON round-trip - the same mechanism as DuplicateProfile and
    /// TileActionBarLogic.DuplicateBinding - so EVERY serialized
    /// field travels: ScanCode/IsE0/VirtualKey/KeyName, Trigger,
    /// Label, Icon + AutoIcon verbatim (no re-fetch, no IconStore
    /// touch), BackgroundColor, Steps with Params JsonObjects
    /// deep-cloned (a macroId reference inside Params is kept
    /// verbatim - the macro itself is NEVER duplicated), and
    /// Combinations. Combination Ids are regenerated so the clone's
    /// combinations never share an identity with the source's. The
    /// round-trip also carries any future serialized field (e.g. the
    /// §132 IsActive flag) with no code change here. The caller
    /// supplies the final label (already uniquified for copy/move,
    /// verbatim for page duplication); KeyId is recomputed from the
    /// copied physical key.</summary>
    public static BindingConfig CloneCard(BindingConfig source, string newLabel)
    {
        ArgumentNullException.ThrowIfNull(source);
        var json = JsonSerializer.Serialize(source);
        var copy = JsonSerializer.Deserialize<BindingConfig>(json)
                   ?? throw new InvalidOperationException("CloneCard: round-trip produced null.");
        // A-02: round-trip dari binding ber-Steps/Combinations: null
        // (explicit-null di config) harus jadi daftar kosong, bukan NRE
        // di loop kombinasi di bawah.
        copy.Steps ??= new();
        copy.Combinations ??= new();
        copy.Combinations.RemoveAll(c => c is null);
        copy.Label = newLabel;
        copy.KeyId = KeyIdFor(copy.ScanCode, copy.IsE0);
        foreach (var combo in copy.Combinations)
            combo.Id = Guid.NewGuid().ToString("N");
        return copy;
    }

    /// <summary>§132 D1 - COPY. Appends a deep clone of each card to
    /// the target page, in input order, at the END of the target list
    /// (append order IS the §123 conflict rule: the resolver's
    /// last-wins assignment makes an appended copy win over an
    /// earlier same-key+trigger binding; the existing S1056/S1634
    /// machinery surfaces the conflict - no silent retarget, no
    /// dialog, no new warning code here).
    /// Labels: "(2)/(3)" ONLY when the target page already has the
    /// same display label (§96/§124 rule), counting clones appended
    /// earlier in this same batch. Source == target behaves like
    /// Duplikat (append + uniquified label). Every card must already
    /// be a member of <paramref name="source"/> (reference
    /// membership, like DeleteSelected's filter); a foreign card
    /// throws ArgumentException instead of copying silently.
    /// WIRING (worker-C, MainViewModel): filter the selection to
    /// page members, call CopyCards, then _host.ReloadActivePage()
    /// + ONE _host.Save() with D9 rollback (remove the returned
    /// clones from the target list on failure) + RefreshBindings()
    /// + section notice Localizer.Format("S1698", displayLabel,
    /// destinationName) - destinationName is the target profile/page
    /// name, or Localizer.Get("S1697") for Halaman Global; when the
    /// target IS Global and IsGlobalEnabled is false, append
    /// Localizer.Get("S1700") (footer wording only, U6 - no dialog).
    /// Do NOT switch SelectedProfile/SelectedPage and do NOT call
    /// RefreshPages (it resets SelectedPage). Does NOT Save.</summary>
    public static IReadOnlyList<BindingConfig> CopyCards(
        PageConfig source, PageConfig target, IEnumerable<BindingConfig> cards)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(cards);
        var list = cards.ToList();
        source.Bindings ??= new();
        target.Bindings ??= new();
        foreach (var card in list)
            if (!source.Bindings.Contains(card))
                throw new ArgumentException("CopyCards: every card must belong to the source page.", nameof(cards));

        var taken = new HashSet<string>(
            target.Bindings.Select(DisplayLabelOf), StringComparer.Ordinal);
        var clones = new List<BindingConfig>(list.Count);
        foreach (var card in list)
        {
            string newLabel = UniquifyDuplicateLabel(DisplayLabelOf(card), taken);
            taken.Add(newLabel);
            var clone = CloneCard(card, newLabel);
            target.Bindings.Add(clone);
            clones.Add(clone);
        }
        return clones;
    }

    /// <summary>§132 D1 - MOVE = copy + remove from source, prepared
    /// for the caller's SINGLE Save. Cross-page: clones are appended
    /// to the target (CopyCards semantics) and the originals are
    /// removed from the source; <see cref="TransferResult.Removed"/>
    /// records each original's pre-removal index for the caller's
    /// Save-failure rollback. Same page (source == target): behaves
    /// like Duplikat - clones only, nothing removed (Removed empty),
    /// because removing the originals would just rename the cards.
    /// WIRING (worker-C, MainViewModel): same as CopyCards, except
    /// the notice is Localizer.Format("S1699", displayLabel,
    /// destinationName) (+ S1700 suffix for Global-OFF, as above),
    /// and the D9 rollback on Save failure is: remove every
    /// TransferResult.Copies entry from the target list, then
    /// re-insert TransferResult.Removed entries ascending by Index
    /// (clamped), then ReloadActivePage + RefreshBindings + S0937
    /// notice. Moving TO Global removes the card from profile export
    /// by design (Global is not part of profile exports) - no extra
    /// warning beyond the footer notice. Does NOT Save.</summary>
    public static TransferResult MoveCards(
        PageConfig source, PageConfig target, IEnumerable<BindingConfig> cards)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(cards);
        var list = cards.ToList();
        source.Bindings ??= new();
        target.Bindings ??= new();
        // CopyCards validates membership; record the originals'
        // indices BEFORE any removal (list order is not index order).
        var removed = list
            .Select(model => (Index: source.Bindings.IndexOf(model), Model: model))
            .OrderBy(pair => pair.Index)
            .ToList();
        var clones = CopyCards(source, target, list);
        if (ReferenceEquals(source, target))
            return new TransferResult(clones, Array.Empty<(int, BindingConfig)>());
        foreach (var model in list)
            source.Bindings.Remove(model);
        return new TransferResult(clones, removed);
    }

    /// <summary>§132 D2 - duplicate a whole page. Returns a NEW
    /// PageConfig (not inserted): fresh Id, name following the "(2)"
    /// rule over <paramref name="existingPageNames"/> (the same loop
    /// LoadTemplatePageAsync uses for template page names; the caller
    /// passes the owning profile's page names, including the source's
    /// own name), TemplateId KEPT verbatim (U8), and ALL cards cloned
    /// via CloneCard with labels verbatim (a page copy preserves
    /// labels exactly - duplicates inside a page are legal and the
    /// §123 warnings describe them), fresh KeyIds (recomputed),
    /// regenerated combination Ids, macroId references verbatim.
    /// WIRING (worker-C, MainViewModel, sidebar duplicate button):
    /// call DuplicatePage(sourcePage, SelectedProfile.Pages names),
    /// insert the result directly AFTER the source in
    /// SelectedProfile.Pages, _host.ReloadActivePage() + ONE
    /// _host.Save() with D9 rollback (remove the copy from Pages on
    /// failure) + RefreshPages() + SelectedPage = copy (RefreshPages
    /// resets SelectedPage, so the assignment MUST come after it -
    /// the AddPage pattern) + section notice
    /// Localizer.Format("S1701", copy.Name). Never touches
    /// GlobalPage (there is no owning list / sidebar row for it).
    /// Does NOT Save and does NOT insert.</summary>
    public static PageConfig DuplicatePage(PageConfig source, IEnumerable<string> existingPageNames)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingPageNames);
        var copy = new PageConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = UniquifyDuplicateLabel(source.Name, existingPageNames),
            TemplateId = source.TemplateId,
            // Full Edit Mode modal: a duplicated mode keeps its
            // auto-mode triggers verbatim (copied list, not shared).
            AutoSwitchExe = (source.AutoSwitchExe ?? Enumerable.Empty<string>()).ToList(),
            // Switch-back flag copies verbatim with the triggers.
            SwitchBackOnAutoSwitch = source.SwitchBackOnAutoSwitch,
        };
        foreach (var card in source.Bindings ?? Enumerable.Empty<BindingConfig>())
            copy.Bindings.Add(CloneCard(card, card.Label));
        return copy;
    }

    /// <summary>Result of a prepared MoveCards: the clones appended
    /// to the target, and the originals removed from the source with
    /// their pre-removal indices (for Save-failure rollback).</summary>
    public sealed record TransferResult(
        IReadOnlyList<BindingConfig> Copies,
        IReadOnlyList<(int Index, BindingConfig Model)> Removed);
}

/// <summary>§132 D3 - session undo store for bulk delete. In-memory
/// ONLY: never persisted, so a restart never resurrects deleted
/// cards. Single level: recording a second bulk delete replaces the
/// snapshot. Restore reinserts the SAME binding objects (verbatim:
/// original KeyIds, original order, original steps/flags) at their
/// original indices. The store knows nothing about profiles; the
/// caller supplies the liveness check and owns Save/rollback/reload.
/// WIRING (worker-C, MainViewModel): after a successful bulk delete,
/// call Record(page, removed index+model pairs) - the same pairs
/// DeleteSelected already computes. Call Clear() at exactly the
/// F13 sites: OnIsEditingGlobalChanged, OnSelectedPageChanged,
/// RefreshPages (covers profile switch), and ResetProfile; restore
/// consumes (clears) the snapshot itself. "Urungkan" (S0428) is
/// shown only while CanRestore; after restore the selection stays
/// EMPTY (do not re-select). Restore Save-failure rollback: remove
/// every model in the returned <c>restored</c> list from the page,
/// ReloadActivePage + RefreshBindings + S0937 notice; the snapshot
/// is already consumed and is NOT revived. Gone-page guard: if the
/// snapshot's page no longer exists (profile/page deleted), restore
/// is a no-op and the snapshot clears.</summary>
public sealed class BulkDeleteUndoStore
{
    private (PageConfig Page, List<(int Index, BindingConfig Model)> Items)? _snapshot;

    public bool CanRestore => _snapshot.HasValue;

    /// <summary>Records a completed bulk delete, replacing any
    /// previous snapshot. Pairs are copied and sorted ascending by
    /// index; an empty record clears the store.</summary>
    public void Record(PageConfig page, IEnumerable<(int Index, BindingConfig Model)> removed)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(removed);
        var items = removed
            .OrderBy(pair => pair.Index)
            .ToList();
        _snapshot = items.Count == 0
            ? null
            : (page, items);
    }

    /// <summary>Reinserts the recorded bindings into their page at
    /// their original indices (clamped to the live list, ascending
    /// order preserves relative order) and consumes the snapshot.
    /// Returns false (no-op, snapshot cleared) when there is nothing
    /// to restore or <paramref name="pageStillExists"/> rejects the
    /// snapshot's page. The caller then Saves once (see class
    /// remarks for the failure rollback).</summary>
    public bool TryRestore(Func<PageConfig, bool> pageStillExists, out IReadOnlyList<BindingConfig> restored)
    {
        ArgumentNullException.ThrowIfNull(pageStillExists);
        restored = Array.Empty<BindingConfig>();
        if (_snapshot is not { } snapshot)
            return false;
        _snapshot = null;
        if (!pageStillExists(snapshot.Page))
            return false;
        var result = new List<BindingConfig>(snapshot.Items.Count);
        foreach (var (index, model) in snapshot.Items)
        {
            int at = Math.Clamp(index, 0, snapshot.Page.Bindings.Count);
            snapshot.Page.Bindings.Insert(at, model);
            result.Add(model);
        }
        restored = result;
        return true;
    }

    public void Clear() => _snapshot = null;
}
