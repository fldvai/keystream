using Keystream.Core.Config;

namespace Keystream.App.Views;

/// <summary>§124 butir 3 - pure decision logic for select mode +
/// bulk delete. Deliberately WPF-FREE (no WPF namespace
/// reference of any kind), so this file can be linked directly into
/// the plain net8.0 wb-harness - same discipline as
/// TextBoxRevertLogic.cs / MouseRecordPlanner.cs. The WPF halves
/// (MainWindow.xaml overlay + MainViewModel commands) call into
/// here; do not add WPF types, dependency-object parameters, or
/// resource lookups to this file.
/// §132: the right-click floating action bar is gone (replaced by a
/// native WPF ContextMenu built in MainWindow.xaml.cs), so
/// ClampBarPosition / ActionBarFallbackWidth are no longer called by
/// the UI - they are KEPT as tested helpers (TileActionBarTests)
/// for any future floating chrome, not deleted. Everything else in
/// this file is still live: BulkDeleteBlocked, UniquifyDuplicateLabel,
/// DuplicateBinding, TileSelection.</summary>
public static class TileActionBarLogic
{
    /// <summary>Fallback width estimate for the action bar clamp, used
    /// only before the bar has rendered (ActualWidth == 0). One row
    /// of buttons at ~72 DIP each (ActionBarButton MinWidth 62 plus
    /// label overflow - "Duplicate" exceeds the minimum) plus the
    /// border Padding (4 + 4). §125: 4 buttons after OPEN was
    /// removed. An estimate only - the real size wins once rendered.</summary>
    public static double ActionBarFallbackWidth(int buttonCount)
        => buttonCount * 72 + 8;

    /// <summary>Clamp the action bar's top-left corner inside its area:
    /// the bar opens at the cursor (anchor) and is pushed back from any
    /// edge it would cross, so it is never clipped by the window edge.
    /// No flip-above behaviour - clamp only, per §124.</summary>
    public static (double Left, double Top) ClampBarPosition(
        double anchorX, double anchorY,
        double barWidth, double barHeight,
        double areaWidth, double areaHeight,
        double margin = 8)
    {
        double left = Math.Clamp(anchorX, margin, Math.Max(margin, areaWidth - barWidth - margin));
        double top = Math.Clamp(anchorY, margin, Math.Max(margin, areaHeight - barHeight - margin));
        return (left, top);
    }

    /// <summary>Bulk delete is refused while mapping (capture armed) or
    /// any recording/playback/countdown session is active. The two
    /// inputs are the guards that already exist - MainViewModel
    /// IsCaptureArmed and FloatPanelSession.SessionActive - no new
    /// guard is created here; this only ORs them for one decision.</summary>
    public static bool BulkDeleteBlocked(bool isCaptureArmed, bool isSessionActive)
        => isCaptureArmed || isSessionActive;

    /// <summary>Label for a duplicated card: the base label unchanged
    /// when free, otherwise "Base (2)", "Base (3)", … - the same
    /// numbering the template loader uses for duplicate page names.
    /// Comparison is ordinal; the page's existing labels decide.</summary>
    public static string UniquifyDuplicateLabel(string baseLabel, IEnumerable<string> existingLabels)
    {
        var taken = new HashSet<string>(existingLabels, StringComparer.Ordinal);
        if (!taken.Contains(baseLabel)) return baseLabel;
        int n = 2;
        while (taken.Contains($"{baseLabel} ({n})")) n++;
        return $"{baseLabel} ({n})";
    }

    /// <summary>Deep copy of one binding for DUPLIKAT: every field and
    /// step copied (JSON round-trip, the same mechanism as
    /// DuplicateProfile), combination Ids regenerated so the copy's
    /// combinations never share an identity with the original's. The
    /// physical key (ScanCode/KeyId) is deliberately KEPT - the copy
    /// is the same key until the user reassigns it with Ganti Tombol,
    /// and the §123 same-page duplicate warning marks it meanwhile.
    /// The caller supplies the already-uniquified label.</summary>
    public static BindingConfig DuplicateBinding(BindingConfig source, string newLabel)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var copy = System.Text.Json.JsonSerializer.Deserialize<BindingConfig>(json)
                   ?? throw new InvalidOperationException("DuplicateBinding: round-trip produced null.");
        copy.Label = newLabel;
        foreach (var combo in copy.Combinations)
            combo.Id = Guid.NewGuid().ToString("N");
        return copy;
    }
}

/// <summary>§124 - the select-mode list, keyed by binding object
/// identity (BindingConfig has no Equals override, so the default
/// List.Contains/IndexOf are reference-based). The ViewModel owns
/// one instance; RefreshBindings prunes it so a deleted card never
/// stays selected. Kept separate from the ViewModel so the
/// toggle/prune semantics are harness-testable without WPF.
/// §132: Replace/ReplaceAll for the unified click-select model
/// (plain click replaces, Select-All replaces with the visible set)
/// and MajorityActive for the §153 Part A bulk toggle rule
/// (AnyActive retained for compatibility / old harness checks).
/// §133 M3: ORDERED - a List in insertion order, NOT a HashSet:
/// the selection numbers (SelectionIndex) are the 1-based positions
/// in this list. Toggle appends at the end; re-selecting a card
/// that was deselected appends it again at the end (no "original"
/// position is remembered). Prune keeps the order of the survivors;
/// ReplaceAll de-duplicates keeping the first occurrence.</summary>
public sealed class TileSelection
{
    private readonly List<BindingConfig> _selected = new();

    public int Count => _selected.Count;

    /// <summary>Toggle one binding. Returns true when it ends up
    /// selected, false when it ends up deselected. Selecting appends
    /// at the END of the order; deselecting removes without shifting
    /// the relative order of the rest (callers renumber from this).</summary>
    public bool Toggle(BindingConfig binding)
    {
        if (_selected.Contains(binding))
        {
            _selected.Remove(binding);
            return false;
        }
        _selected.Add(binding);
        return true;
    }

    /// <summary>§132: replace the whole selection with one binding
    /// (plain click). Returns true when the set changed.</summary>
    public bool Replace(BindingConfig binding)
    {
        bool changed = _selected.Count != 1 || !_selected.Contains(binding);
        _selected.Clear();
        _selected.Add(binding);
        return changed;
    }

    /// <summary>§132: replace the whole selection with the given
    /// bindings (Select-All over the visible set), in the given
    /// order. An empty input clears the selection. Duplicates in the
    /// input are kept once, at their first occurrence.</summary>
    public void ReplaceAll(IEnumerable<BindingConfig> bindings)
    {
        _selected.Clear();
        foreach (var b in bindings)
            if (!_selected.Contains(b)) _selected.Add(b);
    }

    /// <summary>§132 U4 (dipertahankan untuk kompatibilitas + harness
    /// lama; TIDAK LAGI menggerakkan bulk toggle sejak §153 Part A):
    /// true bila sekurang-kurangnya satu binding terseleksi aktif.</summary>
    public bool AnyActive() => _selected.Any(b => b.IsActive);

    /// <summary>§153 Part A - aturan MAYORITAS (verdict user 21:14 WIB
    /// 28 Okt 2026; menggantikan aturan ANY §132 U4 dan M6 §133):
    /// true bila kartu AKTIF merupakan mayoritas himpunan, yaitu
    /// activeCount &gt;= inactiveCount. SERI (jumlah sama) dihitung
    /// mayoritas-aktif, sehingga aksi yang jalan adalah NONAKTIFKAN -
    /// arah aman, karena mengaktifkan kartu bisa memicu makro yang
    /// tak terduga. SELEKSI KOSONG (0 &gt;= 0) mengembalikan true
    /// sebagai nilai no-op terdokumentasi: nilai ini TIDAK PERNAH
    /// dipakai sebagai aksi - ToggleSelectedActive mengembalikan
    /// kontrol lebih dulu bila tidak ada target, dan MenuToggleText
    /// memakai keadaan kartu yang diklik bila seleksi kosong.
    /// Overload statis menghitung mayoritas atas himpunan target
    /// yang sudah difilter handler (bukan atas _selection mentah),
    /// supaya label dan aksi selalu dihitung dari himpunan yang sama
    /// dengan yang dimutasi SetActive.</summary>
    public static bool MajorityActive(IEnumerable<BindingConfig> bindings)
    {
        int active = 0, inactive = 0;
        foreach (var b in bindings)
        {
            if (b.IsActive) active++;
            else inactive++;
        }
        return active >= inactive;
    }

    /// <summary>§153 Part A: mayoritas atas seleksi saat ini
    /// (lihat MajorityActive statis untuk aturan seri/kosong).</summary>
    public bool MajorityActive() => MajorityActive(_selected);

    public bool Contains(BindingConfig binding) => _selected.Contains(binding);

    /// <summary>§133 M3: 0-based position in insertion order, or -1
    /// when not selected. Callers add 1 for the displayed
    /// SelectionIndex (0 = not selected).</summary>
    public int IndexOf(BindingConfig binding) => _selected.IndexOf(binding);

    public void Clear() => _selected.Clear();

    /// <summary>Drop every selection whose binding is no longer on the
    /// live page (deleted / page switched without a Clear). The
    /// order of the survivors is preserved.</summary>
    public void Prune(IEnumerable<BindingConfig> liveBindings)
    {
        var live = new HashSet<BindingConfig>(liveBindings);
        _selected.RemoveAll(b => !live.Contains(b));
    }

    /// <summary>Snapshot in INSERTION ORDER (a copy - mutating it
    /// never affects the selection).</summary>
    public List<BindingConfig> Snapshot() => new(_selected);
}

/// <summary>§134 A3: outcome of one left-click on a card, decided
/// from booleans the WPF handler extracts at event time. The handler
/// (MainWindow.xaml.cs) executes the outcome with the existing
/// selection primitives; this enum + decision live here, WPF-free,
/// so the whole truth table is harness-testable.</summary>
public enum TileLeftClickAction
{
    Ignore,
    ToggleInMode,
    CtrlSelect,
    OpenEditor,
}

/// <summary>§134 A1/A2/A3: WPF-free decision helpers for selection
/// behaviour. Deliberately no WPF types - same discipline as
/// TileSelection above - so the wb-harness links this file
/// directly. The ViewModel / code-behind own the state and the
/// side effects; these helpers own only the decisions.</summary>
public static class Selection134Logic
{
    /// <summary>§134 A3: guard order is the contract, in this exact
    /// sequence - (1) inner button first: a Ctrl+click on the
    /// card's × / keycap button keeps that button's own behaviour,
    /// never toggles or opens the editor; (2) in Mode Pilih only
    /// ClickCount==1 toggles (a fast double-click must not toggle
    /// twice), unguarded by BulkDeleteBlocked exactly as before
    /// §134; (3) outside the mode BulkDeleteBlocked silences the
    /// click entirely (no editor, no Ctrl-select) - the F02 guard
    /// keeps its existing position between the mode branch and the
    /// editor branch, so a Ctrl+click during a capture/recording
    /// session behaves like the existing left-click guard;
    /// (4) Ctrl outside the mode (ClickCount==1 only, mirroring the
    /// in-mode rule) selects; (5) plain click opens the editor on
    /// every MouseLeftButtonDown, ClickCount unchecked, as before
    /// §134.</summary>
    public static TileLeftClickAction DecideTileLeftClick(
        bool isSelectMode, bool ctrlHeld, bool insideInnerButton,
        bool bulkDeleteBlocked, int clickCount)
    {
        if (insideInnerButton) return TileLeftClickAction.Ignore;
        if (isSelectMode)
            return clickCount == 1 ? TileLeftClickAction.ToggleInMode : TileLeftClickAction.Ignore;
        if (bulkDeleteBlocked) return TileLeftClickAction.Ignore;
        if (ctrlHeld)
            return clickCount == 1 ? TileLeftClickAction.CtrlSelect : TileLeftClickAction.Ignore;
        return TileLeftClickAction.OpenEditor;
    }

    /// <summary>§136: apakah klik kiri pada kartu harus ditelan
    /// karena menu kartu sedang terbuka. True HANYA bila menu
    /// terbuka DAN di luar Mode Pilih - di dalam mode klik kiri
    /// tetap dispatch §133/§134 seperti biasa. Predikat ini sengaja
    /// hanya menerima dua argumen: Ctrl dan tombol-dalam tidak
    /// perlu dibedakan, karena penjaga di code-behind
    /// (Tile_MouseLeftButtonDown) berjalan SEBELUM dispatch
    /// DecideTileLeftClick, jadi satu klik saat menu terbuka -
    /// polos, Ctrl, maupun dari tombol-dalam - ditelan sebagai
    /// gestur menutup menu. Kepemilikan marker-nya sendiri
    /// (_openCardMenuId: ditugaskan di OpenCardContextMenu,
    /// dihapus di handler Closed) tetap di code-behind dan hanya
    /// tercakup review kode + CARA, bukan oleh predikat ini.</summary>
    public static bool ShouldSwallowTileLeftClick(bool menuOpen, bool isSelectMode)
        => menuOpen && !isSelectMode;

    /// <summary>§134 A1: the "Batalkan semua" (cancel-all) label
    /// state - true only when a select-all snapshot is active AND
    /// the current selection still covers every visible card.
    /// Order-insensitive set equality; an empty visible set is
    /// never the cancel-all state (SelectAllVisible no-ops there
    /// and takes no snapshot).</summary>
    public static bool SelectionCoversVisible(
        IReadOnlyList<BindingConfig> selection, IEnumerable<BindingConfig> visible)
    {
        var visibleList = visible.ToList();
        if (visibleList.Count == 0) return false;
        if (selection.Count != visibleList.Count) return false;
        var set = new HashSet<BindingConfig>(selection);
        return visibleList.All(set.Contains);
    }

    /// <summary>§134 A2: whether a card-menu Closed event may clear
    /// the temporary right-click selection. ALL three conditions
    /// must hold: outside Mode Pilih (entering the mode via
    /// "Pilih" keeps the selection); this menu is still the
    /// latest card menu opened (a newer right-click opened another
    /// menu after this one closed); and the current selection
    /// still equals the selection this menu's prelude set (a newer
    /// prelude already replaced it). The token + equality pair
    /// makes the handler safe under EVERY event order for a rapid
    /// re-right-click: if Closed fires before the new prelude,
    /// the clear runs first and the prelude then replaces the
    /// selection with the new card; if it fires after the prelude
    /// (with or without the new menu opened yet), one of the two
    /// conditions fails and the new selection survives. Submenu
    /// clicks never reach this decision early: ContextMenu.Closed
    /// fires once, when the whole menu closes, not when a submenu
    /// opens or an item is clicked.</summary>
    public static bool ShouldClearTempSelectionOnMenuClose(
        bool isSelectMode, int menuId, int latestMenuId, bool selectionUnchanged)
        => !isSelectMode && menuId == latestMenuId && selectionUnchanged;
}
