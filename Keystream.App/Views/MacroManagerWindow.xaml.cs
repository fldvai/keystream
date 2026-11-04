using System;
using Keystream.Core.Localization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using WKey = System.Windows.Input.Key;
using Keystream.Actions.Common;
using Keystream.Actions.Hotkey;
using Keystream.Actions.Macro;
using Keystream.Actions.Mouse;
using Keystream.App.Host;
using Keystream.App.ViewModels;
using Keystream.Core.Config;
using Keystream.Windows.Input;

namespace Keystream.App.Views;

/// <summary>One row of the macro list. Display carries the "(belum
/// disimpan)" suffix for drafts, so a draft never looks like a saved
/// macro - drafts live only in this window until Simpan commits them
/// to the library.</summary>
public sealed class MacroListRow : INotifyPropertyChanged
{
    public string Id { get; }
    private string _name = "";
    private bool _isUnsaved;

    public MacroListRow(string id, string name, bool isUnsaved)
    {
        Id = id;
        _name = name;
        _isUnsaved = isUnsaved;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Display));
        }
    }

    public bool IsUnsaved
    {
        get => _isUnsaved;
        set
        {
            if (_isUnsaved == value) return;
            _isUnsaved = value;
            OnPropertyChanged(nameof(IsUnsaved));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Display => IsUnsaved ? Name + " " + Localizer.Get("S0899") : Name;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One row of the step table. Chord is the display string
/// ("Ctrl+C"); DelayText stays a string so a half-typed or invalid
/// delay is a Simpan-time error, never a binding crash. Original is
/// the working copy's step: for non-hotkey steps (hand-edited config,
/// future Fase 3) it preserves ActionId + Params verbatim - only the
/// delay is rewritten at Simpan.</summary>
public sealed class MacroStepRow : INotifyPropertyChanged
{
    public ActionStep Original { get; }
    public bool IsHotkey { get; }
    /// <summary>Kolom Aksi (gambar acuan): "Tombol" untuk langkah tombol,
    /// ActionId untuk langkah aksi umum (Fase 3 - belum bisa dibuat
    /// dari UI, tapi barisnya sudah siap menampilkannya).</summary>
    public string Action { get; }

    private int _number;
    private string _chord = "";
    private string _delayText = "0";

    public MacroStepRow(ActionStep original, bool isHotkey, string chord, string delayText, string action)
    {
        Original = original;
        IsHotkey = isHotkey;
        Action = action;
        _chord = chord;
        _delayText = delayText;
    }

    public int Number
    {
        get => _number;
        set { if (_number != value) { _number = value; OnPropertyChanged(nameof(Number)); } }
    }

    public string Chord
    {
        get => _chord;
        set { if (_chord != value) { _chord = value; OnPropertyChanged(nameof(Chord)); } }
    }

    public string DelayText
    {
        get => _delayText;
        set { if (_delayText != value) { _delayText = value; OnPropertyChanged(nameof(DelayText)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Macro library manager (§62 modal, kembali dari tab §62-tab
/// atas perintah user 23:28 WIB: konsisten dengan pola modal aplikasi).
/// Code-behind, like the deck picker: the window owns a working copy of
/// ONE macro; the library copy only changes on Simpan, so Batal/close
/// never needs a restore. Macros are created and saved here ONLY -
/// they never run from this window. Execution happens exclusively
/// through a card whose action is "macro" (see MacroAction). Record
/// (Fase 2) is built in: the Rekam button appends real key chords via
/// a WH_KEYBOARD_LL hook (KeyboardRecorder), with an optional Record
/// Delay checkbox for inter-key gaps.
/// Error handling: every failure shows as ErrorText or a dialog, never
/// a crash, never a silent no-op - including a failed Save, which
/// restores the library snapshot taken before the commit.</summary>
public partial class MacroManagerWindow : Window
{
    private readonly AppHost _host;
    private readonly ObservableCollection<MacroListRow> _macroRows = new();
    private readonly ObservableCollection<MacroStepRow> _stepRows = new();

    private MacroListRow? _editingRow;
    private MacroConfig? _working;
    private bool _dirty;

    /// <summary>§96: nama macro yang berhasil disimpan lewat Save
    /// (null bila modal ditutup tanpa save). Dibaca MainWindow setelah
    /// ShowDialog untuk info footer halaman utama.</summary>
    public string? SavedMacroName { get; private set; }
    private bool _loading;
    private bool _suppressSelection;
    private bool _capturing;
    private bool _captureReplace;
    // §67: UBAH pada baris pisah mengganti SEPASANG (↓ + ↑), bukan
    // satu baris - pasangan dijepret saat capture dimulai (seleksi
    // bisa berubah selama capture; baris juga bisa dihapus/diurutkan
    // ulang - completion memakai jepretan ini, bukan seleksi akhir).
    // _capturePairRows dalam urutan tabel (keydown dulu, keyup
    // kemudian, apa pun baris yang dipilih); _capturePairIndex =
    // indeks tabel baris pasangan pertama saat dijepret (fallback
    // bila kedua baris pasangan sudah dihapus selama capture).
    private bool _captureReplacePair;
    private List<MacroStepRow>? _capturePairRows;
    private int _capturePairIndex;

    // --- recording (Fase 2) --------------------------------------------
    private KeyboardRecorder? _recorder;
    // §137 E1: snapshot of the master-hotkey chords for this
    // recording session (null outside a session). Consulted by
    // OnRecordedKeyEvent before RecordStepBuilder.Build so a master
    // chord - above all the Toggle Recording chord used to stop -
    // is never recorded as a step. Dropped at Stop with the session.
    private MasterChordExclusion? _chordExclusion;
    private bool _recording;
    // §111: jeda rekaman - hook berhenti, tetapi _recording tetap
    // true, editor tetap terkunci, _mousePlanner dipertahankan
    // (thinning state tidak boleh hilang), dan _lastRecordAt TIDAK
    // direset di sini; reset jam jeda terjadi di ResumeRecording,
    // supaya jeda selama pause tidak menjadi DelayAfterMs raksasa
    // pada langkah berikutnya. Event yang antre selama pause
    // dibuang oleh guard handler, seperti event antre setelah Stop.
    private bool _paused;
    private DateTimeOffset? _lastRecordAt;
    // §111: delegasi dari panel mengambang (Worker C): true bila
    // titik (x, y) berada di permukaan panel, sehingga klik pada
    // panel tidak terekam sebagai langkah mouse.
    private Func<int, int, bool>? _isFloatSurfacePoint;

    // --- mouse recording (§107) ---------------------------------------
    // Session-scoped only: _mouseSettings is never persisted and
    // never marks the macro dirty; already-recorded mouse steps stay
    // even if MouseBox is unchecked later.
    private MouseRecordSettings _mouseSettings = new();
    private MouseRecorder? _mouseRecorder;
    private bool _mouseRecorderSubscribed;
    // Virtual screen at recording start (null when the capture
    // failed - steps then carry no fingerprint, 1:1 replay).
    private VirtualScreenSnapshot? _recordFingerprint;
    // §109 Order B: the pure mouse-recording decisions (ignore-self,
    // scope, kind/button resolution, thinning, params, gap)
    // live in MouseRecordPlanner (Keystream.Actions/Mouse), which
    // owns the thinning state (last EMITTED position/time) - the
    // former _lastEmittedMouse* fields. Created fresh at each Rekam
    // start from the current _mouseSettings; the shared _lastRecordAt
    // gap clock stays here (keyboard path shares it) and is threaded
    // through the planner per event.
    private MouseRecordPlanner? _mousePlanner;

    // §69: pilihan pengali kecepatan (string "0.5×" - tampilannya
    // langsung bermakna; parsing hanya strip "×" + invariant).
    // ObservableCollection agar nilai di luar daftar (config
    // edit-tangan) bisa ditambahkan saat dimuat tanpa merusak
    // ItemsSource.
    private readonly ObservableCollection<string> _speedItems = new(new[]
    {
        "0.5×", "0.75×", "1×", "1.5×", "2×", "3×",
    });

    public MacroManagerWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        CyclesBox.ItemsSource = Enumerable.Range(1, MacroAction.MaxCycles).ToList();
        SpeedBox.ItemsSource = _speedItems;
        MacroList.ItemsSource = _macroRows;
        StepList.ItemsSource = _stepRows;
        PreviewKeyDown += OnPreviewKeyDown;
        // Sabuk pengaman kedua: hook tidak boleh hidup lebih lama dari
        // jendela - Stop() idempotent, jadi Closing + Closed boleh
        // sama-sama memanggilnya.
        Closed += (_, _) => StopRecording();
        // §113: sesi bersama panel. Checkbox + _mouseSettings dimulai
        // dari sesi (jalankan pertama = default sesi = default XAML,
        // jadi tidak ada perubahan tampak; buka berikutnya memulihkan
        // nilai terakhir). Handler Checked/Unchecked di XAML menulis
        // balik ke sesi pada perubahan apa pun, termasuk perubahan
        // programatik di bawah ini (nilainya sama, jadi NotifyChanged
        // yang timbul tidak mengubah apa pun sebelum langganan di
        // bawah dipasang). AttachModal di sini; DetachModal hanya di
        // Closed (OnModalClosed) - Window_Closing yang dibatalkan
        // TIDAK boleh melepas attach.
        Delay3sBox.IsChecked = _host.FloatPanel.Delay3s;
        UpDownBox.IsChecked = _host.FloatPanel.RecordUpDown;
        RecordDelayBox.IsChecked = _host.FloatPanel.RecordDelay;
        MouseBox.IsChecked = _host.FloatPanel.MouseEnabled;
        _mouseSettings = _host.FloatPanel.MouseSettings.Copy();
        _host.FloatPanel.Changed += OnFloatSessionChanged;
        // §120 Batch 1: opening the modal ENDS a standalone session
        // orderly (countdown cancelled, recording stopped + saved,
        // playback cancelled) BEFORE the modal attaches and takes
        // panel routing - never the other way round. A standalone
        // session left alive past AttachModal would be hijacked by
        // modal routing (B1a/B1b) and its clock reset by modal
        // refreshes (B1c). Runs before RefreshMacroList so a
        // saved standalone recording is already in the library when
        // the list is built.
        _host.PanelHost.EndStandaloneSessionForModalTakeover();
        _host.PanelHost.AttachModal(this);
        _host.PanelHost.PanelShown += RefreshPanelToggleButton;
        Closed += OnModalClosed;
        RefreshMacroList();
        if (_macroRows.Count > 0)
        {
            _suppressSelection = true;
            MacroList.SelectedItem = _macroRows[0];
            _suppressSelection = false;
            LoadSelectedRow(_macroRows[0]);
        }
        else
        {
            EditorPanel.IsEnabled = false;
            EmptyText.Visibility = Visibility.Visible;
        }
        RefreshMacroButtons();
        RefreshSessionGating();
    }

    // --- header / close -------------------------------------------------

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CancelCapture();
        // Hook dilepas dulu, apa pun jawaban konfirmasi: rekaman tidak
        // berlanjut melewati niat menutup (langkah yang sudah terekam
        // tetap ada di tabel; pengguna bisa menekan Rekam lagi bila
        // menjawab Tidak).
        StopRecording();
        // §97: "ada perubahan" = edit yang belum disimpan (_dirty) ATAU
        // draft yang belum disimpan (baris IsUnsaved). Sebelumnya hanya
        // _dirty yang dicek - celahnya: tombol + membuat draft tanpa
        // _dirty (tidak ada ketikan/edit), sehingga Cancel/× menutup
        // modal langsung tanpa konfirmasi dan draft hilang diam-diam.
        // Edit nama/siklus/kecepatan/langkah sudah menaikkan _dirty,
        // hapus tersimpan tidak menyentuh _dirty, dan Save (§96)
        // membersihkan keduanya - jadi tidak ada konfirmasi palsu.
        // Predikat yang sama dipakai jalur buang-dalam-modal (ganti
        // pilihan, Baru, Duplikat): draf segar juga harus
        // dikonfirmasi di sana, bukan hanya saat menutup modal.
        // §106: dialog 3 pilihan - Simpan (gagal = tetap terbuka),
        // Buang (tutup), Batal/X (tetap terbuka).
        if (HasUnsavedChanges())
        {
            switch (AskUnsavedChanges())
            {
                case AppDialogResult.Save:
                    if (!TrySaveMacro()) e.Cancel = true;
                    break;
                case AppDialogResult.Discard:
                    break;
                default:
                    e.Cancel = true;
                    break;
            }
        }
        // §113 (menggantikan §112): modal benar-benar menutup
        // (tidak dibatalkan) → DETACH dari host, panel TIDAK ditutup
        // dan TIDAK disembunyikan (P1: panel tetap hidup). Countdown
        // dibatalkan supaya tidak ada playback setelah modal hilang,
        // dan hasil playback yang sudah terlanjur jalan tidak
        // menyentuh UI yang menutup. Sesi milik modal diakhiri di
        // sini (dan lagi, idempotent, di OnModalClosed).
        if (!e.Cancel)
        {
            _playtestUiSuppressed = true;
            CancelPlaytest();
            // §122 recheck (F-C1): the modal-owned session MUST end
            // BEFORE CloseFloatPanel detaches - DetachModal refreshes
            // the panel to the SAVED state only while no modal
            // session is active, so ending it after the detach
            // skipped the refresh and left the panel showing the
            // unsaved working draft after the modal closed (in
            // particular after a playback, whose CancelPlaytest
            // does not end the session).
            EndModalSession();
            CloseFloatPanel();
        }
    }

    /// <summary>Predikat tunggal "ada yang belum disimpan", dipakai
    /// Window_Closing, DiscardIfDirty, dan ganti pilihan.</summary>
    private bool HasUnsavedChanges() => _dirty || _macroRows.Any(r => r.IsUnsaved);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // No confirm here: Close() raises Window_Closing, which asks
        // once. Confirming here too would ask twice.
        CancelCapture();
        Close();
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), Localizer.Get("S0093"),
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;

    /// <summary>§106: dialog 3 pilihan untuk perubahan belum disimpan
    /// (varian SaveDiscardCancel AppDialog): Simpan / Buang perubahan /
    /// Batal. Pesan netral S1569; judul Macro (S0093); ikon Question.
    /// X dialog = Batal (AppDialog Close_Click → Cancel).</summary>
    private AppDialogResult AskUnsavedChanges() => AppDialog.Show(this,
        Localizer.Get("S1569"), Localizer.Get("S0093"),
        AppDialogButtons.SaveDiscardCancel, AppDialogIcon.Question);

    /// <summary>§106: selesaikan perubahan belum disimpan sebelum lanjut.
    /// true = boleh lanjut (sudah disimpan, atau dibuang); false = batal,
    /// pemanggil tetap di tempat. Simpan yang gagal validasi/simpan
    /// mengembalikan false (error sudah ditampilkan, modal tetap).</summary>
    private bool ResolveUnsavedChanges()
    {
        if (!HasUnsavedChanges()) return true;
        switch (AskUnsavedChanges())
        {
            case AppDialogResult.Save: return TrySaveMacro();
            case AppDialogResult.Discard: return true;
            default: return false;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        // §113: error selalu menyembunyikan notice sesi (§104/§105:
        // satu pesan pada satu waktu); HideError mengevaluasi ulang.
        NoticeText.Visibility = Visibility.Collapsed;
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        // §113: notice sesi dievaluasi ulang - tampil lagi bila sesi
        // standalone masih aktif.
        RefreshSessionGating();
    }

    // --- macro list -----------------------------------------------------

    private void RefreshMacroList()
    {
        var unsaved = _macroRows.Where(r => r.IsUnsaved).ToList();
        var keepId = (_editingRow is { } er) ? er.Id : null;
        _suppressSelection = true;
        _macroRows.Clear();
        foreach (var m in _host.Config.Macros)
            _macroRows.Add(new MacroListRow(m.Id, m.Name, false));
        foreach (var row in unsaved)
            _macroRows.Add(row);
        MacroList.SelectedItem = keepId is null ? null : _macroRows.FirstOrDefault(r => r.Id == keepId);
        _suppressSelection = false;
        // Re-sync: the rebuild created NEW row objects, so the old
        // _editingRow reference would point at a row no longer in the
        // list (identity comparisons in selection handling would fail).
        _editingRow = MacroList.SelectedItem as MacroListRow;
        EmptyText.Visibility = _macroRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshMacroButtons()
    {
        bool hasSelection = MacroList.SelectedItem is MacroListRow;
        DeleteButton.IsEnabled = hasSelection;
        DuplicateButton.IsEnabled = hasSelection;
        // §81: Ekspor butuh pustaka tak kosong; Impor selalu aktif.
        ExportButton.IsEnabled = _host.Config.Macros.Count > 0;
    }

    private void MacroList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        var row = MacroList.SelectedItem as MacroListRow;
        if (row == _editingRow)
        {
            RefreshMacroButtons();
            return;
        }
        if (row is null)
        {
            ClearEditor();
            RefreshMacroButtons();
            return;
        }
        // Celah §97 di dalam modal: draf segar (IsUnsaved, _dirty
        // masih false karena LoadWorkingIntoEditor membersihkannya)
        // hilang diam-diam saat pilihan diganti - baris draf lama
        // dihapus di bawah tanpa pernah ditanya. HasUnsavedChanges
        // menutupnya; "Batal" mengembalikan pilihan seperti dulu,
        // "Simpan" menyimpan dulu (gagal = tetap di macro ini).
        if (!ResolveUnsavedChanges())
        {
            _suppressSelection = true;
            MacroList.SelectedItem = _editingRow;
            _suppressSelection = false;
            return;
        }
        if (_editingRow is { IsUnsaved: true } oldDraft)
            _macroRows.Remove(oldDraft);
        LoadSelectedRow(row);
        RefreshMacroButtons();
    }

    private void LoadSelectedRow(MacroListRow row)
    {
        if (row.IsUnsaved)
        {
            // Draft: the working copy already exists (created by Baru /
            // Duplikat) - just show its fields again.
            if (_working is null) return;
            LoadWorkingIntoEditor();
            _editingRow = row;
            return;
        }
        var macro = _host.Config.Macros.FirstOrDefault(m => m.Id == row.Id);
        if (macro is null)
        {
            ShowError(Localizer.Get("S0907"));
            ClearEditor();
            return;
        }
        _working = macro.Copy();
        LoadWorkingIntoEditor();
        _editingRow = row;
    }

    private void LoadWorkingIntoEditor()
    {
        if (_working is null) return;
        _loading = true;
        NameBox.Text = _working.Name;
        CyclesBox.SelectedItem = Math.Clamp(_working.Cycles, 1, MacroAction.MaxCycles);
        // §69: kecepatan - nilai tak valid tampil sebagai 1× (bukan
        // ditolak di sini; Simpan berikutnya yang menormalkannya),
        // nilai di luar daftar ditambahkan agar tidak hilang diam-diam.
        double speed = MacroAction.IsValidSpeed(_working.SpeedMultiplier) ? _working.SpeedMultiplier : 1.0;
        string speedText = FormatSpeed(speed);
        if (!_speedItems.Contains(speedText)) _speedItems.Add(speedText);
        SpeedBox.SelectedItem = speedText;
        _stepRows.Clear();
        foreach (var step in _working.Steps)
            AddStepRow(BuildRow(step));
        RenumberSteps();
        _loading = false;
        _dirty = false;
        EditorPanel.IsEnabled = true;
        HideError();
        RefreshStepButtons();
        // §113: satu titik tulis pilihan - LoadSelectedRow, Baru dan
        // Duplikat semuanya berakhir di sini. Panel tidak pernah
        // memilih macro sendiri; ia membaca id ini dari sesi.
        WriteSelectedMacro(_working.Id);
    }

    private MacroStepRow BuildRow(ActionStep step)
    {
        bool isHotkey = string.Equals(step.ActionId, "hotkey", StringComparison.OrdinalIgnoreCase);
        if (isHotkey)
            return new MacroStepRow(step, true,
                ParamReader.GetString(step.Params, "keys", "Key") ?? "",
                step.DelayAfterMs.ToString(), Localizer.Get("S0193"));
        // §65 hold/release steps: one key + its direction, not a chord.
        bool isKeyDown = string.Equals(step.ActionId, "keydown", StringComparison.OrdinalIgnoreCase);
        bool isKeyUp = string.Equals(step.ActionId, "keyup", StringComparison.OrdinalIgnoreCase);
        if (isKeyDown || isKeyUp)
            return new MacroStepRow(step, false,
                ParamReader.GetString(step.Params, "key") ?? "",
                step.DelayAfterMs.ToString(), isKeyDown ? Localizer.Get("S0908") : Localizer.Get("S0909"));
        // §107: mouse steps (recorded, never hand-built in the
        // editor): display text from params, via the same helper
        // recording uses, so a reloaded macro reads the same as when
        // it was recorded. IsHotkey=false, like keydown/keyup rows.
        bool isMouse = string.Equals(step.ActionId, "mouse", StringComparison.OrdinalIgnoreCase);
        if (isMouse)
            return new MacroStepRow(step, false, MouseDisplayText(step),
                step.DelayAfterMs.ToString(), Localizer.Get("S1571"));
        return new MacroStepRow(step, false, "—", step.DelayAfterMs.ToString(), step.ActionId);
    }

    private void AddStepRow(MacroStepRow row)
    {
        row.PropertyChanged += (_, _) => { if (!_loading) _dirty = true; };
        _stepRows.Add(row);
    }

    // §67: sisipkan baris di indeks tabel (untuk ganti pasangan) -
    // langganan PropertyChanged sama seperti AddStepRow.
    private void InsertStepRow(int index, MacroStepRow row)
    {
        row.PropertyChanged += (_, _) => { if (!_loading) _dirty = true; };
        _stepRows.Insert(Math.Clamp(index, 0, _stepRows.Count), row);
    }

    // §67: cari pasangan baris pisah, dalam urutan tabel. keydown(K)
    // → keyup(K) pertama setelahnya; keyup(K) → keydown(K) terakhir
    // sebelumnya. Pencocokan memakai Chord (token tunggal) sehingga
    // pasangan bersarang terurai benar (Ctrl↓, C↓, C↑, Ctrl↑: pasangan
    // Ctrl↓ adalah Ctrl↑ terakhir, bukan C↑). Bila pasangan tidak
    // ditemukan (satu sisi sudah dihapus), kembalikan baris itu saja
    // - ganti tetap berjalan, hanya satu baris yang diganti.
    private List<MacroStepRow> FindPair(MacroStepRow row)
    {
        int idx = _stepRows.IndexOf(row);
        if (idx < 0) return new List<MacroStepRow> { row };
        string action = row.Original.ActionId;
        if (string.Equals(action, "keydown", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = idx + 1; i < _stepRows.Count; i++)
                if (string.Equals(_stepRows[i].Original.ActionId, "keyup", StringComparison.OrdinalIgnoreCase)
                    && _stepRows[i].Chord == row.Chord)
                    return new List<MacroStepRow> { row, _stepRows[i] };
        }
        else if (string.Equals(action, "keyup", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = idx - 1; i >= 0; i--)
                if (string.Equals(_stepRows[i].Original.ActionId, "keydown", StringComparison.OrdinalIgnoreCase)
                    && _stepRows[i].Chord == row.Chord)
                    return new List<MacroStepRow> { _stepRows[i], row };
        }
        return new List<MacroStepRow> { row };
    }

    private void ClearEditor()
    {
        _editingRow = null;
        _working = null;
        _dirty = false;
        _loading = true;
        NameBox.Text = "";
        _stepRows.Clear();
        _loading = false;
        EditorPanel.IsEnabled = false;
        HideError();
        RefreshStepButtons();
        // §113: tidak ada macro terpilih - panel membaca null.
        WriteSelectedMacro(null);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (!DiscardIfDirty()) return;
        DropUnsavedDraft();
        // §96: nama macro baru tidak boleh sama dengan yang sudah ada -
        // pola yang sama seperti Duplikat (cek library + daftar, abaikan
        // besar-kecil huruf): "Macro Baru", lalu "(2)", "(3)", ...
        // Sebelumnya selalu "Macro Baru" mentah sehingga Save selalu
        // gagal validasi duplikat bila nama itu sudah dipakai.
        string newBase = Localizer.Get("S0911");
        string newName = newBase;
        int newN = 2;
        while (_host.Config.Macros.Any(m => string.Equals(m.Name, newName, StringComparison.OrdinalIgnoreCase))
               || _macroRows.Any(r => string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase)))
            newName = $"{newBase} ({newN++})";
        _working = new MacroConfig { Name = newName };
        var row = new MacroListRow(_working.Id, _working.Name, true);
        _macroRows.Add(row);
        _suppressSelection = true;
        MacroList.SelectedItem = row;
        _suppressSelection = false;
        _editingRow = row;
        LoadWorkingIntoEditor();
        EmptyText.Visibility = Visibility.Collapsed;
        RefreshMacroButtons();
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (!DiscardIfDirty()) return;
        MacroConfig? source = _working
            ?? (MacroList.SelectedItem as MacroListRow is { } sel
                ? _host.Config.Macros.FirstOrDefault(m => m.Id == sel.Id)
                : null);
        if (source is null) return;
        DropUnsavedDraft();
        string baseName = source.Name + " " + Localizer.Get("S0912");
        string name = baseName;
        int n = 2;
        while (_host.Config.Macros.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
               || _macroRows.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {n++}";
        _working = source.Copy();
        _working.Id = Guid.NewGuid().ToString("N");
        _working.Name = name;
        var row = new MacroListRow(_working.Id, name, true);
        _macroRows.Add(row);
        _suppressSelection = true;
        MacroList.SelectedItem = row;
        _suppressSelection = false;
        _editingRow = row;
        LoadWorkingIntoEditor();
        _dirty = true;
        EmptyText.Visibility = Visibility.Collapsed;
        RefreshMacroButtons();
    }

    /// <summary>True when it is safe to abandon the current editor
    /// content (not dirty, or the user confirmed the discard).</summary>
    // §106: nama lama dipertahankan; semantik kini "selesaikan perubahan
    // belum disimpan" (Simpan/Buang/Batal), bukan hanya konfirmasi buang.
    private bool DiscardIfDirty() => ResolveUnsavedChanges();

    private void DropUnsavedDraft()
    {
        if (_editingRow is { IsUnsaved: true } draft)
            _macroRows.Remove(draft);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        var row = MacroList.SelectedItem as MacroListRow;
        if (row is null) return;
        if (row.IsUnsaved)
        {
            if (_dirty && !ConfirmDiscard()) return;
            _macroRows.Remove(row);
            // §113: bila yang dihapus adalah macro yang sedang
            // diedit, pilih pertama yang tersisa (atau kosong).
            if (row == _editingRow) SelectFirstRemainingOrClear();
            EmptyText.Visibility = _macroRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshMacroButtons();
            return;
        }
        var usages = MacroUsage.FindUsages(_host.Config, row.Id);
        if (usages.Count > 0)
        {
            var shown = usages.Take(8);
            string rest = usages.Count > 8 ? Localizer.Format("S0913", usages.Count - 8) : "";
            AppDialog.Show(this,
                Localizer.Format("S0914", row.Name, usages.Count, string.Join("\n", shown), rest),
                Localizer.Get("S0093"), AppDialogButtons.Ok, AppDialogIcon.Warning);
            return;
        }
        if (AppDialog.Show(this, Localizer.Format("S0915", row.Name), Localizer.Get("S0093"),
                AppDialogButtons.YesNo, AppDialogIcon.Question) != AppDialogResult.Yes)
            return;
        var backup = _host.Config.Macros.Select(m => m.Copy()).ToList();
        _host.Config.Macros.RemoveAll(m => m.Id == row.Id);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // §46 discipline: a failed save restores the snapshot -
            // the library must never claim a delete that did not persist.
            _host.Config.Macros.Clear();
            _host.Config.Macros.AddRange(backup);
            ShowError(Localizer.Get("S0916") + " " + ex.Message);
            return;
        }
        bool wasEditing = row == _editingRow;
        RefreshMacroList();
        // §113: bila yang dihapus adalah macro yang sedang diedit,
        // pilih pertama yang tersisa (atau kosong) - bukan dibiarkan
        // tanpa pilihan bila pustaka masih berisi macro.
        if (wasEditing) SelectFirstRemainingOrClear();
        RefreshMacroButtons();
    }

    /// <summary>§113: setelah Hapus, bila macro yang diedit ikut
    /// terhapus, pilih baris pertama yang tersisa lewat jalur
    /// pilihan normal (LoadSelectedRow → tulis sesi); bila pustaka
    /// kosong, editor dibersihkan (sesi = null). Satu-satunya draf
    /// yang mungkin ada selalu baris yang sedang diedit (lihat
    /// DropUnsavedDraft / MacroList_SelectionChanged), jadi memilih
    /// baris draf pertama di sini aman: _working-nya adalah salinan
    /// draf itu.</summary>
    private void SelectFirstRemainingOrClear()
    {
        if (_macroRows.Count == 0)
        {
            ClearEditor();
            return;
        }
        _suppressSelection = true;
        MacroList.SelectedItem = _macroRows[0];
        _suppressSelection = false;
        LoadSelectedRow(_macroRows[0]);
        RefreshMacroButtons();
    }

    // --- export / import (§81) ------------------------------------------

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        // Fully qualified: Keystream.App juga mereferensikan
        // System.Windows.Forms (dialog Forms vs Win32 ambigu).
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localizer.Get("S0917"),
            Filter = Localizer.Get("S0918"),
            DefaultExt = "json",
            FileName = Localizer.Get("S0919"),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, MacroLibraryFile.Export(_host.Config.Macros));
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, Localizer.Get("S0920") + " " + ex.Message, Localizer.Get("S0093"),
                AppDialogButtons.Ok, AppDialogIcon.Error);
            return;
        }
        // Draf yang belum disimpan bukan bagian pustaka - katakan terus
        // terang supaya tidak ada yang mengira filenya tidak lengkap.
        bool hasDrafts = _dirty || _macroRows.Any(r => r.IsUnsaved);
        AppDialog.Show(this,
            Localizer.Format("S0921", _host.Config.Macros.Count, dialog.FileName)
            + (hasDrafts ? Localizer.Get("S0922") : ""),
            Localizer.Get("S0093"), AppDialogButtons.Ok, AppDialogIcon.Info);
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get("S0923"),
            Filter = Localizer.Get("S0918"),
            DefaultExt = "json",
        };
        if (dialog.ShowDialog(this) != true) return;
        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, Localizer.Get("S0924") + " " + ex.Message, Localizer.Get("S0093"),
                AppDialogButtons.Ok, AppDialogIcon.Error);
            return;
        }
        MacroLibraryFile.ImportResult result;
        try
        {
            result = MacroLibraryFile.Import(text, _host.Config.Macros);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, Localizer.Get("S0925") + " " + ex.Message, Localizer.Get("S0093"),
                AppDialogButtons.Ok, AppDialogIcon.Error);
            return;
        }
        // §46 discipline, seperti Hapus/Simpan: snapshot dulu, gagal
        // simpan = pustaka kembali seperti semula. Impor yang berhasil
        // langsung tersimpan (bukan draf) - Batal tidak membatalkannya,
        // sama seperti Hapus yang juga langsung tersimpan.
        var backup = _host.Config.Macros.Select(m => m.Copy()).ToList();
        _host.Config.Macros.AddRange(result.Imported);
        try
        {
            _host.Save();
        }
        catch (Exception ex)
        {
            _host.Config.Macros.Clear();
            _host.Config.Macros.AddRange(backup);
            AppDialog.Show(this, Localizer.Get("S0926") + " " + ex.Message, Localizer.Get("S0093"),
                AppDialogButtons.Ok, AppDialogIcon.Error);
            return;
        }
        RefreshMacroList();
        RefreshMacroButtons();
        string rejectedText = result.Rejected.Count == 0 ? ""
            : Localizer.Format("S0927", result.Rejected.Count) + string.Join("\n", result.Rejected.Take(8))
            + (result.Rejected.Count > 8 ? Localizer.Format("S0928", result.Rejected.Count - 8) : "");
        if (result.Imported.Count == 0)
        {
            AppDialog.Show(this, Localizer.Get("S0929") + rejectedText,
                Localizer.Get("S0093"), AppDialogButtons.Ok, AppDialogIcon.Warning);
            return;
        }
        AppDialog.Show(this, Localizer.Format("S0930", result.Imported.Count) + rejectedText,
            Localizer.Get("S0093"), AppDialogButtons.Ok, AppDialogIcon.Info);
    }

    // --- save -----------------------------------------------------------

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (TrySaveMacro()) Close();
    }

    /// <summary>§106: inti Simpan yang dipakai tombol Simpan footer DAN
    /// pilihan Simpan di dialog 3 pilihan (§106). Mengembalikan true
    /// hanya bila semua validasi + _host.Save() lolos; semua jalur
    /// gagal menampilkan ShowError dan mengembalikan false (modal tetap
    /// terbuka). TIDAK menutup modal - penutupnya (Close / lanjut
    /// pindah / lanjut Baru-Duplikat) urusan pemanggil.</summary>
    private bool TrySaveMacro()
    {
        CancelCapture();
        // Tombol Simpan TIDAK berada di dalam EditorPanel (footer XAML:457,
        // SaveButton XAML:458, di luar panel editor), sehingga tetap bisa diklik saat
        // tidak ada macro yang sedang dikerjakan: pustaka kosong saat
        // window dibuka (ctor:199) atau editor dibersihkan
        // (ClearEditor:433). Dulu return diam di sini - kini ShowError
        // menyebut syaratnya (pilih macro yang ada, atau tekan Baru).
        // S1563: kunci baru (tabel 1561 kunci, sudah diregenerasi).
        if (_working is null) { ShowError(Localizer.Get("S1563")); return false; }
        string name = NameBox.Text.Trim();
        if (name.Length == 0) { ShowError(Localizer.Get("S0931")); return false; }
        if (_host.Config.Macros.Any(m => m.Id != _working.Id
                && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError(Localizer.Format("S0932", name));
            return false;
        }
        if (_stepRows.Count == 0) { ShowError(Localizer.Get("S0933")); return false; }
        var steps = new List<ActionStep>();
        for (int i = 0; i < _stepRows.Count; i++)
        {
            var row = _stepRows[i];
            if (!int.TryParse(row.DelayText, out int delay) || delay < 0 || delay > MacroAction.MaxDelayMs)
            {
                ShowError(Localizer.Format("S0935", i + 1, MacroAction.MaxDelayMs));
                return false;
            }
            if (row.IsHotkey)
            {
                if (!KeyChord.TryParse(row.Chord, out _, out _))
                {
                    ShowError(Localizer.Format("S0936", i + 1, row.Chord));
                    return false;
                }
                steps.Add(new ActionStep
                {
                    ActionId = "hotkey",
                    Params = new JsonObject { ["keys"] = row.Chord },
                    DelayAfterMs = delay,
                });
            }
            else
            {
                row.Original.DelayAfterMs = delay;
                steps.Add(row.Original);
            }
        }
        int cycles = CyclesBox.SelectedItem is int c ? c : 1;
        _working.Name = name;
        _working.Cycles = cycles;
        _working.SpeedMultiplier = ParseSpeed(SpeedBox.SelectedItem as string);
        _working.Steps = steps;

        var backup = _host.Config.Macros.Select(m => m.Copy()).ToList();
        int idx = _host.Config.Macros.FindIndex(m => m.Id == _working.Id);
        if (idx >= 0) _host.Config.Macros[idx] = _working;
        else _host.Config.Macros.Add(_working);
        try { _host.Save(); }
        catch (Exception ex)
        {
            _host.Config.Macros.Clear();
            _host.Config.Macros.AddRange(backup);
            RefreshMacroList();
            ShowError(Localizer.Get("S0937") + " " + ex.Message);
            return false;
        }
        if (_editingRow is not null)
        {
            _editingRow.Name = name;
            _editingRow.IsUnsaved = false;
        }
        _dirty = false;
        HideError();
        // §96: Save = simpan + tutup modal (atas perintah user).
        // Hanya tercapai bila semua validasi + _host.Save() lolos -
        // semua jalur gagal mengembalikan false, jadi modal tidak
        // tertutup saat error. Window_Closing tidak meminta konfirmasi
        // karena _dirty sudah false. Dialog §106 memakai TrySaveMacro
        // yang sama (Simpan dari dialog, tanpa Close di sini).
        // Nama macro yang tersimpan dilaporkan ke MainWindow lewat
        // SavedMacroName agar footer halaman utama bisa memberi info.
        SavedMacroName = name;
        return true;
    }

    // --- steps ----------------------------------------------------------

    private void StepList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => RefreshStepButtons();

    private void RefreshStepButtons()
    {
        var row = StepList.SelectedItem as MacroStepRow;
        int idx = row is null ? -1 : _stepRows.IndexOf(row);
        // §67: UBAH aktif untuk baris ketukan DAN baris pisah (keydown/
        // keyup) - bukan untuk baris aksi umum (Fase 3), yang tidak
        // punya chord untuk ditangkap. Tooltip menjelaskan apa yang
        // akan diganti: satu chord, atau satu pasangan ↓+↑.
        // (OrdinalIgnoreCase: BuildRow/Save sudah begitu; config
        // edit-tangan bisa menulis "KeyDown".)
        bool isPairRow = row is not null && !row.IsHotkey
            && (string.Equals(row.Original.ActionId, "keydown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Original.ActionId, "keyup", StringComparison.OrdinalIgnoreCase));
        ChangeChordButton.IsEnabled = (row is { IsHotkey: true } || isPairRow) && !_capturing;
        ChangeChordButton.ToolTip = isPairRow
            ? Localizer.Get("S0938")
            : Localizer.Get("S0197");
        DeleteStepButton.IsEnabled = row is not null;
        TopButton.IsEnabled = idx > 0;
        UpButton.IsEnabled = idx > 0;
        DownButton.IsEnabled = row is not null && idx < _stepRows.Count - 1;
        BottomButton.IsEnabled = row is not null && idx < _stepRows.Count - 1;
        ClearStepsButton.IsEnabled = _stepRows.Count > 0;
        AddStepButton.IsEnabled = !_capturing;
        // §113: tombol ▶ modal mengikuti jumlah langkah + status
        // sesi (corong yang sama dipakai semua jalur perubahan
        // langkah/pilihan; gating standalone tidak tertimpa).
        RefreshSessionGating();
    }

    private void RenumberSteps()
    {
        for (int i = 0; i < _stepRows.Count; i++)
            _stepRows[i].Number = i + 1;
    }

    private void DeleteStepButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (StepList.SelectedItem is MacroStepRow row)
        {
            _stepRows.Remove(row);
            RenumberSteps();
            _dirty = true;
            RefreshStepButtons();
        }
    }

    private void ClearStepsButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (_stepRows.Count == 0) return;
        _stepRows.Clear();
        _dirty = true;
        RefreshStepButtons();
    }

    private void MoveStep(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _stepRows.Count || to >= _stepRows.Count || from == to) return;
        var row = _stepRows[from];
        _stepRows.RemoveAt(from);
        _stepRows.Insert(to, row);
        RenumberSteps();
        _dirty = true;
        _suppressSelection = true;
        StepList.SelectedItem = row;
        _suppressSelection = false;
        RefreshStepButtons();
    }

    private void TopButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (StepList.SelectedItem is MacroStepRow row) MoveStep(_stepRows.IndexOf(row), 0);
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (StepList.SelectedItem is MacroStepRow row) MoveStep(_stepRows.IndexOf(row), _stepRows.IndexOf(row) - 1);
    }

    private void DownButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (StepList.SelectedItem is MacroStepRow row) MoveStep(_stepRows.IndexOf(row), _stepRows.IndexOf(row) + 1);
    }

    private void BottomButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        if (StepList.SelectedItem is MacroStepRow row) MoveStep(_stepRows.IndexOf(row), _stepRows.Count - 1);
    }

    // --- recording (Fase 2) ----------------------------------------------
    // Beda dengan chord capture (satu kombinasi → satu langkah): Rekam
    // menambahkan TIAP tombol yang ditekan sebagai langkah baru, selama
    // tombol Rekam tidak ditekan lagi. Aturan: tombol yang sama yang
    // ditahan tidak mengulang langkah (hook sudah melipat repeat);
    // tombol injeksi (playback macro, deck yang diteruskan service)
    // diabaikan hook; tombol yang direkam TETAP sampai ke aplikasi
    // yang fokus (hook tidak pernah menelan tombol) - jadi merekam
    // sambil mengetik di Notepad juga mengetik di Notepad.

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        CancelCapture();
        // §112: Rekam dari modal membatalkan countdown playtest yang
        // sedang berjalan - ● Rekam dari panel masuk lewat jalur ini
        // juga, jadi satu titik batal ini menutup kedua jalur.
        CancelPlaytest();
        // §120 F-B2: recording while a playback is running is
        // refused with a VISIBLE error (S1633), in BOTH delay
        // cells - Delay3s OFF (instant StartRecordingCore below)
        // and Delay3s ON (which would otherwise die silently in
        // StartActionCountdown's guard). Cancelling a playtest
        // COUNTDOWN above is preserved: _playtestRunning is only
        // true once playback has actually fired, so a countdown
        // is still cancelled-then-recorded as before.
        if (_playtestRunning)
        {
            ShowError(Localizer.Get("S1633"));
            return;
        }
        if (_recording)
        {
            StopRecording();
            return;
        }
        if (_working is null) return;
        // §113: Rekam dari modal mengikuti checkbox jeda - aktif =
        // hitung mundur 3 detik dulu (editor tetap aktif selama
        // hitung mundur; tembak = StartRecordingCore), mati = instan.
        // ● Rekam dari panel SELALU 3 detik: jalur itu memaksa lewat
        // StartActionCountdown(forceCountdown: true) di
        // OnPanelRecordRequested, bukan lewat sini.
        if (_host.FloatPanel.Delay3s)
            StartActionCountdown(CountdownKind.Record, StartRecordingCore);
        else
            StartRecordingCore();
    }

    /// <summary>§113: inti mulai-Rekam (dulu badan
    /// RecordButton_Click). Dipanggil langsung (jeda mati) atau
    /// sebagai tembakan hitung mundur (jeda aktif / panel ●).
    /// Sesi modal dimulai di sini; gagal mulai hook mengakhiri sesi
    /// hitung mundur yang mendahului (tidak ada sesi menggantung).</summary>
    private void StartRecordingCore()
    {
        // §120 B1b: single-owner enforcement at the hook entry.
        // Refuses to start the hooks while a standalone session
        // owns the panel (the countdown path is already guarded in
        // StartActionCountdown; this covers the instant Delay3s-OFF
        // entry from RecordButton_Click). Silent by design: the
        // modal's Record button is disabled by session gating while
        // a standalone session is active, so this is unreachable in
        // normal flow - it is the last guard, not the user message.
        if (!_host.FloatPanel.CanModalTakeOwnership()) return;
        // Fresh planner per recording: clean thinning state and the
        // current settings snapshot, before either hook starts (an
        // event queued by the hook is only converted on the
        // dispatcher after this handler returns, so the planner is
        // always in place by then).
        _mousePlanner = new MouseRecordPlanner(MapMouseRecordOptions(_mouseSettings));
        // §137 E1: snapshot the master chords ONCE, before the hook
        // starts - a chord assigned mid-recording cannot exist
        // (Settings cannot be opened while recording), and the set
        // must be fixed for the session the filter tracks.
        _chordExclusion = MasterChordExclusion.FromSettings(_host.Config.Settings);
        _recorder ??= new KeyboardRecorder();
        // Langganan sekali seumur hidup recorder: handler mengabaikan
        // event bila _recording sudah false atau _paused (event yang
        // sudah antre di dispatcher saat Stop/pause tidak boleh masuk).
        if (!_recorderSubscribed)
        {
            _recorder.KeyEvent += OnRecordedKeyEvent;
            _recorderSubscribed = true;
        }
        if (!_recorder.Start())
        {
            ShowError(Localizer.Get("S0939"));
            EndModalSession();
            return;
        }
        // Mouse (§107): only when the checkbox opts in. The keyboard
        // hook is already up at this point; if the mouse hook refuses,
        // the keyboard hook is stopped again and Rekam never starts -
        // recording must never run with a dead mouse hook while the
        // checkbox promises capture (mirror of the S0939 path).
        _recordFingerprint = null;
        if (MouseBox.IsChecked == true)
        {
            _mouseRecorder ??= new MouseRecorder();
            // Subscribe once per recorder lifetime, like
            // _recorderSubscribed: the handler ignores events once
            // _recording is false or _paused (events queued at Stop/pause).
            if (!_mouseRecorderSubscribed)
            {
                _mouseRecorder.MouseEvent += OnRecordedMouseEvent;
                _mouseRecorderSubscribed = true;
            }
            // Capture failure (null) still records: steps just carry
            // no fingerprint, and playback falls back to 1:1 scaling.
            if (MouseCoordinates.TryCaptureCurrent(out var fingerprint))
                _recordFingerprint = fingerprint;
            if (!_mouseRecorder.Start())
            {
                _recorder.Stop();
                ShowError(Localizer.Get("S1603"));
                EndModalSession();
                return;
            }
        }
        HideError();
        _recording = true;
        CrashLog.WriteDebug($"Rekaman dimulai (modal): macro '{_working?.Name ?? "-"}'.");
        BeginModalSession();
        _lastRecordAt = null;
        // Tombol kini ikon saja: tidak ada lagi teks "■ Berhenti".
        // Sinyal merekam = face HIJAU SuccessButton (ikon putih di
        // atasnya via Foreground style); saat diam tombol Rekam
        // berwarna MERAH (DangerButton di XAML). Tooltip yang sudah
        // berbunyi "Tekan lagi untuk berhenti" tidak diubah.
        RecordButton.Style = (Style)FindResource("SuccessButton");
        SetEditorEnabled(false);
        // §118: Rekam modal TIDAK memunculkan panel - panel hanya
        // muncul bila user menekan tombol ⤢.
    }

    private bool _recorderSubscribed;

    private void SetEditorEnabled(bool enabled)
    {
        // Selama merekam, hanya tombol Berhenti yang hidup: ganti macro,
        // ubah nama/pengulangan, dan tombol langkah lain bisa merusak
        // urutan rekaman atau membuangnya tanpa konfirmasi.
        NameBox.IsEnabled = enabled;
        CyclesBox.IsEnabled = enabled;
        SpeedBox.IsEnabled = enabled;
        MacroList.IsEnabled = enabled;
        NewButton.IsEnabled = enabled;
        DuplicateButton.IsEnabled = enabled;
        DeleteButton.IsEnabled = enabled;
        AddStepButton.IsEnabled = enabled;
        ChangeChordButton.IsEnabled = enabled;
        DeleteStepButton.IsEnabled = enabled;
        ClearStepsButton.IsEnabled = enabled;
        TopButton.IsEnabled = enabled;
        UpButton.IsEnabled = enabled;
        DownButton.IsEnabled = enabled;
        BottomButton.IsEnabled = enabled;
        CancelButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        // §107: the mouse checkbox and its gear are dead while
        // recording too (a mid-session opt-in/out would split one
        // recording across two settings sets). RefreshMouseControls
        // re-derives the gear state from the checkbox after this.
        MouseBox.IsEnabled = enabled;
        MouseSettingsButton.IsEnabled = enabled;
        RefreshMouseControls();
        // §112: tombol panel mengambang SELALU aktif - pengecualian
        // total dari penguncian editor (RefreshPanelToggleButton
        // memaksa IsEnabled + Opacity 1.0); baris ini tidak lagi
        // menyentuhnya sama sekali.
        RefreshPanelToggleButton();
        // §113: penguncian editor tidak boleh menimpa gating sesi -
        // bila sesi standalone aktif, Rekam/▶ modal tetap nonaktif
        // setelah SetEditorEnabled(true) sekalipun.
        RefreshSessionGating();
    }

    private void StopRecording()
    {
        if (_recording)
            CrashLog.WriteDebug("Rekaman berhenti (modal).");
        // §119 flush-on-Stop: a pending held Move must be saved, so
        // flush BEFORE _recording is cleared and before anything
        // reads the step rows. Only while a recording is actually
        // running, a planner exists, and there is a working macro
        // to receive the row (the same guards Convert uses). A
        // recording is ending, there is no step to protect - and a flush
        // failure must never break stop: the hold is already
        // consumed inside FlushPending, so the recording simply
        // ends without that one Move. The panel/clock refreshes
        // below run as before; no second StopRecording call can
        // re-flush, because _recording is false by then.
        if (_recording && _mousePlanner is not null && _working is not null)
        {
            try
            {
                var flush = _mousePlanner.FlushPending(
                    lastRecordAt: _lastRecordAt,
                    existingStepCount: _stepRows.Count,
                    fingerprint: _recordFingerprint,
                    getForeground: () => MouseWindowHelper.TryGetForeground(out var foreground)
                        ? new ForegroundWindowSnapshot(foreground.Title, foreground.ClassName, foreground.ProcessName,
                            foreground.Left, foreground.Top, foreground.Width, foreground.Height)
                        : null,
                    recordDelayEnabled: RecordDelayBox.IsChecked == true);
                if (flush.Outcome == MouseRecordOutcomeKind.Emitted && flush.Step is not null)
                {
                    var step = flush.Step;
                    if (step.GapToPreviousMs is { } gap && _stepRows.Count > 0)
                    {
                        var prev = _stepRows[^1];
                        prev.DelayText = gap.ToString();
                        prev.Original.DelayAfterMs = gap;
                    }
                    _lastRecordAt = step.NewLastRecordAt;
                    AddStepRow(new MacroStepRow(new ActionStep
                    {
                        ActionId = "mouse",
                        Params = step.Params,
                        DelayAfterMs = 0,
                    }, false,
                        MouseDisplayText(step.Kind, step.Button ?? "", step.X, step.Y, step.Delta), "0", Localizer.Get("S1571")));
                    RenumberSteps();
                    _dirty = true;
                }
            }
            catch { /* §119: flush must never break Stop - see above. */ }
        }
        _paused = false;
        _recording = false;
        _recorder?.Stop();
        _mouseRecorder?.Stop();
        // §137 E1: the session filter goes with the session. Any
        // modifier down still buffered in it is discarded, not
        // flushed: its key-up can never be recorded now that the
        // hook stopped, so flushing it would leave exactly the
        // unmatched keydown the filter exists to prevent.
        _chordExclusion = null;
        // Kembali ke MERAH (DangerButton): ClearValue tidak bisa dipakai
        // karena XAML sudah menetapkan DangerButton sebagai nilai lokal
        // - ClearValue akan jatuh ke GhostButton gelap, bukan merah.
        RecordButton.Style = (Style)FindResource("DangerButton");
        SetEditorEnabled(true);
        // §113: sesi modal berakhir di sini (penjaga pemilik di
        // EndModalSession mencegahnya menghapus sesi standalone).
        EndModalSession();
        // §112/§118: Berhenti TIDAK menutup panel - panel yang tampil
        // kembali ke SIAP; panel yang disembunyikan × tetap
        // tersembunyi, hanya ⤢ yang memunculkannya lagi.
        RefreshFloatPanel(restoreHidden: false);
        RefreshPanelToggleButton();
        RefreshSessionGating();
    }

    // §111: jeda rekaman. Hook KEDUAnya dihentikan persis seperti
    // StopRecording, tetapi _recording tetap true dan editor tetap
    // terkunci (SetEditorEnabled(true) TIDAK dipanggil), _mousePlanner
    // dipertahankan (thinning state selamat dari jeda), dan
    // _lastRecordAt tidak direset di sini - resetnya di
    // ResumeRecording. Idempotent lewat guard _recording/_paused;
    // dipanggil hanya dari panel mengambang (Worker C).
    private void PauseRecording()
    {
        if (!_recording || _paused) return;
        _recorder?.Stop();
        _mouseRecorder?.Stop();
        _paused = true;
        RefreshFloatPanel(restoreHidden: true);
        RefreshPanelToggleButton();
    }

    // §111: lanjut rekaman setelah jeda. Hook dimulai ulang persis
    // seperti jalur mulai Rekam, tetapi _mousePlanner TIDAK dibuat
    // baru (thinning state dari sebelum jeda dipertahankan - kritis),
    // dan _lastRecordAt direset ke null supaya jeda selama pause
    // tidak pernah menjadi DelayAfterMs raksasa pada langkah
    // berikutnya. Gagal mulai hook = StopRecording + pesan yang sama
    // seperti jalur mulai (S0939 keyboard, S1603 mouse).
    private void ResumeRecording()
    {
        if (!_recording || !_paused) return;
        if (_recorder?.Start() != true)
        {
            StopRecording();
            ShowError(Localizer.Get("S0939"));
            return;
        }
        if (MouseBox.IsChecked == true)
        {
            if (_mouseRecorder?.Start() != true)
            {
                _recorder?.Stop();
                StopRecording();
                ShowError(Localizer.Get("S1603"));
                return;
            }
        }
        // §137 E1: resume REUSES the session filter (held/suppressed
        // state survives the pause, like the mouse planner's
        // thinning state); recreate only if it is somehow missing,
        // so a resumed session is never left unfiltered.
        _chordExclusion ??= MasterChordExclusion.FromSettings(_host.Config.Settings);
        _lastRecordAt = null;
        _paused = false;
        RefreshFloatPanel(restoreHidden: true);
        RefreshPanelToggleButton();
    }

    private void OnRecordedKeyEvent(KeyboardRecorder.KeyRecord p)
    {
        // Hook berjalan di thread UI, tapi marshal eksplisit menjaga
        // keamanan bila Windows mendispatch dari thread lain; urutan
        // antrean dispatcher FIFO sehingga urutan langkah terjaga, dan
        // jeda dihitung dari stempel proc-time (p.At), bukan waktu
        // marshal.
        Dispatcher.BeginInvoke(() =>
        {
            if (!_recording || _paused || _working is null) return;
            // §137 E1: the filter runs here, inside the dispatched
            // callback, before conversion - in FIFO hook order, so
            // its held/suppressed tracking sees the true press
            // sequence. It returns zero or more events (a buffered
            // modifier down released ahead of the event that
            // resolves its press); each is converted exactly as
            // before. Events queued after Stop never reach this
            // point: the guard above drops them.
            foreach (var ev in FeedRecordedKey(p))
            {
            // §113: conversion factored into RecordStepBuilder -
            // same tokens, same tap/up-down modes, same gap-on-
            // previous-step rule, same skip semantics (a skipped
            // event returns a null step and never touches the
            // clock). Row display is derived from the built step
            // alone, so the table below is unchanged.
            var (step, gap, newLastRecordAt) = RecordStepBuilder.Build(
                ev,
                UpDownBox.IsChecked == true,
                RecordDelayBox.IsChecked == true,
                _lastRecordAt,
                _stepRows.Count);
            if (step is null) continue;
            bool isHotkeyRow = string.Equals(step.ActionId, "hotkey", StringComparison.OrdinalIgnoreCase);
            string chordDisplay = isHotkeyRow
                ? ParamReader.GetString(step.Params, "keys", "Key") ?? ""
                : ParamReader.GetString(step.Params, "key") ?? "";
            string actionDisplay = isHotkeyRow
                ? Localizer.Get("S0193")
                : string.Equals(step.ActionId, "keyup", StringComparison.OrdinalIgnoreCase)
                    ? Localizer.Get("S0909")
                    : Localizer.Get("S0908");
            // §65 gap-on-previous-step: the builder computed the
            // gap; only the table write stays here.
            if (gap is { } gapMs && _stepRows.Count > 0)
            {
                var prev = _stepRows[^1];
                prev.DelayText = gapMs.ToString();
                prev.Original.DelayAfterMs = gapMs;
            }
            _lastRecordAt = newLastRecordAt;
            AddStepRow(new MacroStepRow(step, isHotkeyRow, chordDisplay, "0", actionDisplay));
            RenumberSteps();
            _dirty = true;
            StepList.SelectedItem = _stepRows[^1];
            RefreshStepButtons();
            RefreshFloatPanel();
            }
        });
    }

    /// <summary>§137 E1: master-chord exclusion for one hook event.
    /// No session filter (outside a recording) means pass-through,
    /// preserving the old behaviour exactly.</summary>
    private IReadOnlyList<KeyboardRecorder.KeyRecord> FeedRecordedKey(KeyboardRecorder.KeyRecord record)
        => _chordExclusion?.Feed(record) ?? new[] { record };

    // --- mouse recording (§107) ------------------------------------------

    private void MouseBox_CheckChanged(object sender, RoutedEventArgs e)
    {
        RefreshMouseControls();
        // §113: tulis ke sesi bersama agar panel (saat modal tertutup)
        // merekam dengan pilihan terakhir dari modal.
        _host.FloatPanel.MouseEnabled = MouseBox.IsChecked == true;
        _host.FloatPanel.NotifyChanged();
    }

    /// <summary>§113: checkbox jeda 3 detik → sesi bersama (dibaca
    /// panel untuk ▶ Main; Rekam panel selalu 3 detik apa pun
    /// nilainya). Menulis balik nilai yang sama saat init tidak
    /// mengubah apa pun.</summary>
    private void Delay3sBox_CheckChanged(object sender, RoutedEventArgs e)
    {
        _host.FloatPanel.Delay3s = Delay3sBox.IsChecked == true;
        _host.FloatPanel.NotifyChanged();
    }

    /// <summary>§113: checkbox Rekam Up/Down → sesi bersama.</summary>
    private void UpDownBox_CheckChanged(object sender, RoutedEventArgs e)
    {
        _host.FloatPanel.RecordUpDown = UpDownBox.IsChecked == true;
        _host.FloatPanel.NotifyChanged();
    }

    /// <summary>§113: checkbox Record Delay → sesi bersama.</summary>
    private void RecordDelayBox_CheckChanged(object sender, RoutedEventArgs e)
    {
        _host.FloatPanel.RecordDelay = RecordDelayBox.IsChecked == true;
        _host.FloatPanel.NotifyChanged();
    }

    /// <summary>Gear follows the checkbox: usable only when mouse
    /// recording is opted in, and dead while recording (MouseBox is
    /// disabled by SetEditorEnabled then, so this stays false).
    /// Settings changes never mark the macro dirty - they are
    /// session-scoped recording options, not macro content - so no
    /// path here touches _dirty.</summary>
    private void RefreshMouseControls()
    {
        MouseSettingsButton.IsEnabled = MouseBox.IsChecked == true && MouseBox.IsEnabled;
    }

    private void MouseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new MouseSettingsWindow(_mouseSettings.Copy()) { Owner = this };
            if (window.ShowDialog() == true)
            {
                _mouseSettings = window.Settings;
                // §113: commit juga ke sesi bersama (panel membaca
                // pengaturan mouse terakhir modal saat modal tertutup).
                _host.FloatPanel.MouseSettings = _mouseSettings;
                _host.FloatPanel.NotifyChanged();
                // Push the committed settings into a planner from a
                // previous session; the next Rekam start rebuilds
                // the planner from _mouseSettings regardless.
                if (_mousePlanner is not null)
                    _mousePlanner.Options = MapMouseRecordOptions(_mouseSettings);
            }
        }
        catch (Exception ex)
        {
            // Gear open never throws to the modal (§104/§105): an
            // honest message, and the previous settings stay in force.
            ShowError(Localizer.Format("S1605", ex.Message));
        }
        // No _dirty here by design (see RefreshMouseControls).
    }

    private void OnRecordedMouseEvent(MouseRecorder.MouseRecord p)
    {
        // Same marshal as OnRecordedKeyEvent: explicit dispatcher hop
        // keeps FIFO order, and gaps come from proc-time p.At, not
        // marshal time. The lambda only enqueues - the hook proc
        // never sees handler exceptions.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ConvertRecordedMouseEvent(p);
            }
            catch (Exception ex)
            {
                // A conversion failure stops BOTH recorders with a
                // message - never drops an event silently, never
                // throws into the hook chain.
                StopRecording();
                ShowError(Localizer.Format("S1604", ex.GetType().Name, ex.Message));
                CrashLog.Write("MacroManagerWindow.OnRecordedMouseEvent", ex);
            }
        });
    }

    private void ConvertRecordedMouseEvent(MouseRecorder.MouseRecord p)
    {
        if (_isFloatSurfacePoint?.Invoke(p.X, p.Y) == true) return;
        if (!_recording || _paused || _working is null) return; // queued after Stop/pause: ignore, like the keyboard handler
        var planner = _mousePlanner;
        if (planner is null) return; // created at Rekam start; unreachable guard, same class as the guard above
        // §109 Order B: every decision below used to be inline here
        // (ignore-self, scope, kind/button resolution, thinning,
        // params, gap). The planner decides; this method only gathers
        // the Windows-only facts as plain data and applies the decided
        // gap/step to the WPF table.
        // Short-circuit preserved: IsOwnProcessPoint is only called
        // when IgnoreSelfClicks is on (as before); the planner
        // re-applies the setting, so simulation semantics stay
        // complete. The foreground provider is invoked by the
        // planner at most once, and only on the emit path in
        // relative mode - the old inline call site.
        //
        // §119: the foreground provider is one local function so
        // both the flush call and the Plan call below pass the
        // identical provider. The jitter flush runs BEFORE Plan
        // for a non-move event: Plan can return only one decision,
        // so a pending held Move must land first (move, then
        // Down/Up/wheel). A Move event never flushes here - it
        // extends or exits the held run inside Plan(). The flush
        // decision is applied through the same local Apply path as
        // the Plan decision (gap to previous row, shared clock,
        // AddStepRow).
        ForegroundWindowSnapshot? GetForeground() => MouseWindowHelper.TryGetForeground(out var foreground)
            ? new ForegroundWindowSnapshot(foreground.Title, foreground.ClassName, foreground.ProcessName,
                foreground.Left, foreground.Top, foreground.Width, foreground.Height)
            : null;
        bool ApplyDecision(MouseRecordDecision decision)
        {
            if (decision.Outcome != MouseRecordOutcomeKind.Emitted || decision.Step is null) return true; // dropped: neither clock touched
            var step = decision.Step;
            // Gap decided by the planner goes on the PREVIOUS step, as
            // before; the shared clock then advances to this event.
            if (step.GapToPreviousMs is { } gap && _stepRows.Count > 0)
            {
                var prev = _stepRows[^1];
                prev.DelayText = gap.ToString();
                prev.Original.DelayAfterMs = gap;
            }
            _lastRecordAt = step.NewLastRecordAt;
            // Same row-creation path as keyboard recording: a non-hotkey
            // MacroStepRow, dirty via the existing mechanism (AddStepRow
            // subscription + the explicit _dirty below). Unchecking
            // MouseBox later keeps _mouseSettings AND these steps:
            // nothing here deletes or resets either.
            var actionStep = new ActionStep
            {
                ActionId = "mouse",
                Params = step.Params,
                DelayAfterMs = 0,
            };
            AddStepRow(new MacroStepRow(actionStep, false,
                MouseDisplayText(step.Kind, step.Button ?? "", step.X, step.Y, step.Delta), "0", Localizer.Get("S1571")));
            RenumberSteps();
            _dirty = true;
            RefreshFloatPanel();
            StepList.SelectedItem = _stepRows[^1];
            RefreshStepButtons();
            return true;
        }
        if (p.Kind != MouseRecorder.MouseEventKind.Move)
        {
            if (!ApplyDecision(planner.FlushPending(
                lastRecordAt: _lastRecordAt,
                existingStepCount: _stepRows.Count,
                fingerprint: _recordFingerprint,
                getForeground: GetForeground,
                recordDelayEnabled: RecordDelayBox.IsChecked == true)))
                return;
        }
        var decision = planner.Plan(
            MapRecordedMouseEvent(p),
            isOwnProcess: _mouseSettings.IgnoreSelfClicks && MouseWindowHelper.IsOwnProcessPoint(p.X, p.Y),
            upDownChecked: UpDownBox.IsChecked == true,
            recordDelayEnabled: RecordDelayBox.IsChecked == true,
            lastRecordAt: _lastRecordAt,
            existingStepCount: _stepRows.Count,
            fingerprint: _recordFingerprint,
            getForeground: GetForeground);
        if (!ApplyDecision(decision)) return;
    }

    // §113: bodies moved verbatim to MouseRecordMapping; these
    // one-line delegations keep every call site identical.
    private static RecordedMouseEvent MapRecordedMouseEvent(MouseRecorder.MouseRecord p)
        => MouseRecordMapping.MapRecordedMouseEvent(p);

    private static MouseRecordOptions MapMouseRecordOptions(MouseRecordSettings settings)
        => MouseRecordMapping.MapMouseRecordOptions(settings);

    private static string MouseButtonWord(string button) => button switch
    {
        "right" => Localizer.Get("S1612"),
        "middle" => Localizer.Get("S1613"),
        _ => Localizer.Get("S1611"),
    };

    /// <summary>Display text for a mouse step, shared by recording
    /// and by BuildRow (a reloaded macro shows the same text it had
    /// when recorded). Mouse rows are NOT hotkey rows (IsHotkey=false),
    /// so they can never enter chord-pair logic: RefreshStepButtons
    /// disables UBAH for them, and ChangeChordButton_Click returns
    /// early for non-pair rows.</summary>
    private static string MouseDisplayText(string kind, string button, int x, int y, int delta) => kind switch
    {
        "move" => Localizer.Format("S1607", x, y),
        "down" => Localizer.Format("S1609", MouseButtonWord(button), x, y),
        "up" => Localizer.Format("S1610", MouseButtonWord(button), x, y),
        "click" => Localizer.Format("S1606", MouseButtonWord(button), x, y),
        "scroll" => Localizer.Format("S1608", x, y, delta),
        _ => kind + " (" + x + ", " + y + ")",
    };

    private static string MouseDisplayText(ActionStep step)
    {
        string kind = ParamReader.GetString(step.Params, "kind")?.Trim().ToLowerInvariant() ?? "";
        string button = ParamReader.GetString(step.Params, "button")?.Trim().ToLowerInvariant() ?? "";
        return MouseDisplayText(kind, button,
            GetParamInt(step.Params, "x"), GetParamInt(step.Params, "y"), GetParamInt(step.Params, "delta"));
    }

    /// <summary>Tolerant int read for display only (mirrors
    /// MouseAction's reader): int/long/integral double/string that
    /// parses. Unreadable = 0; Validate at save/playback is
    /// authoritative - this never invents a coordinate for execution.</summary>
    private static int GetParamInt(JsonObject parameters, string name)
    {
        foreach (var kv in parameters)
        {
            if (!kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Value is not JsonValue json) return 0;
            try
            {
                if (json.TryGetValue<int>(out int i)) return i;
                if (json.TryGetValue<long>(out long l) && l >= int.MinValue && l <= int.MaxValue) return (int)l;
                if (json.TryGetValue<double>(out double d) && d == Math.Truncate(d)
                    && d >= int.MinValue && d <= int.MaxValue) return (int)d;
                if (json.TryGetValue<string>(out string? s) && int.TryParse(s, out int parsed)) return parsed;
            }
            catch { return 0; }
            return 0;
        }
        return 0;
    }

    // --- chord capture --------------------------------------------------

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working is null) return;
        if (_capturing) { CancelCapture(); return; }
        StartCapture(replace: false);
    }

    private void ChangeChordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working is null) return;
        if (StepList.SelectedItem is not MacroStepRow row) return;
        if (_capturing) { CancelCapture(); return; }
        // §67: baris pisah → ganti pasangan (jepret sekarang, bukan
        // saat selesai - lihat _capturePairRows); baris ketukan →
        // ganti chord seperti dulu; baris aksi umum → tidak ada
        // chord, tombol sudah nonaktif (pengaman ganda).
        bool isPair = !row.IsHotkey
            && (string.Equals(row.Original.ActionId, "keydown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Original.ActionId, "keyup", StringComparison.OrdinalIgnoreCase));
        if (!row.IsHotkey && !isPair) return;
        if (isPair)
        {
            _capturePairRows = FindPair(row);
            _capturePairIndex = _stepRows.IndexOf(_capturePairRows[0]);
        }
        StartCapture(replace: true, replacePair: isPair);
    }

    private void StartCapture(bool replace, bool replacePair = false)
    {
        _capturing = true;
        // Saling lepas: ganti pasangan tidak pernah jatuh ke cabang
        // ganti chord (dan sebaliknya).
        _captureReplace = replace && !replacePair;
        _captureReplacePair = replace && replacePair;
        if (!replacePair) _capturePairRows = null;
        // Tombol kini ikon saja: tidak ada lagi teks status di tombol.
        // Sinyal capture = ikon berubah amber (Foreground lokal; template
        // tombol tidak punya trigger disabled sehingga warna tampil
        // penuh) + tooltip instruksi (ShowOnDisabled di XAML agar
        // tooltip tetap tampil walau tombol nonaktif selama capture).
        AddStepButton.ToolTip = Localizer.Get("S0941");
        AddStepButton.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24));
        HideError();
        RefreshStepButtons();
    }

    private void CancelCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _captureReplacePair = false;
        _capturePairRows = null;
        AddStepButton.ToolTip = Localizer.Get("S0196");
        AddStepButton.ClearValue(ForegroundProperty);
        RefreshStepButtons();
    }

    private void EndCapture()
    {
        _capturing = false;
        _captureReplacePair = false;
        _capturePairRows = null;
        AddStepButton.ToolTip = Localizer.Get("S0196");
        AddStepButton.ClearValue(ForegroundProperty);
        RefreshStepButtons();
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturing) return;
        // Swallow everything while capturing: Space/Enter must not
        // re-click the capture button, and typing must not reach a
        // TextBox mid-capture.
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key == System.Windows.Input.Key.Escape)
        {
            CancelCapture();
            return;
        }
        if (IsModifierKey(key)) return; // keep waiting for the main key
        string? token = KeyToToken(key);
        if (token is null)
        {
            ShowError(Localizer.Get("S0942"));
            return;
        }
        var parts = new List<string>();
        var mods = System.Windows.Input.Keyboard.Modifiers;
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(System.Windows.Input.ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(token);
        string chord = string.Join("+", parts);
        if (!KeyChord.TryParse(chord, out _, out _))
        {
            ShowError(Localizer.Format("S0943", chord));
            return;
        }
        HideError();
        if (_captureReplacePair && _capturePairRows is { Count: > 0 })
        {
            // §67: UBAH pada baris pisah mengganti SEPASANG sekaligus.
            // Chord baru diurai seperti Tambah pisah (modifier turun
            // berurutan, utama turun, utama lepas, modifier lepas
            // terbalik). Aturan: UBAH mengubah isi, bukan jenis
            // langkah - pasangan pisah tetap pasangan pisah, apa pun
            // posisi checkbox Up/Down. Pasangan lama diganti UTUH
            // (tidak ada setengah ganti). Jeda baris terakhir pasangan lama
            // diwariskan ke baris terakhir hasil uraian, agar timing
            // langkah berikutnya tidak hilang. Bila pasangan tidak
            // bersebelahan (ada baris lain bersarang di dalamnya),
            // hasil uraian mengambil posisi awal pasangan dan baris
            // sarang bergeser setelahnya - macro tetap seimbang,
            // tapi sarang tidak lagi berada di dalam tahan.
            var tokens = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var still = _capturePairRows.Where(r => _stepRows.Contains(r)).ToList();
            int at = still.Count > 0 ? _stepRows.IndexOf(still[0]) : Math.Clamp(_capturePairIndex, 0, _stepRows.Count);
            string lastDelay = still.Count > 0 ? still[^1].DelayText : "0";
            foreach (var r in _capturePairRows) _stepRows.Remove(r);
            var inserted = new List<MacroStepRow>();
            foreach (var t in tokens)
                inserted.Add(new MacroStepRow(
                    new ActionStep { ActionId = "keydown", Params = new JsonObject { ["key"] = t }, DelayAfterMs = 0 },
                    false, t, "0", Localizer.Get("S0908")));
            for (int i = tokens.Length - 1; i >= 0; i--)
                inserted.Add(new MacroStepRow(
                    new ActionStep { ActionId = "keyup", Params = new JsonObject { ["key"] = tokens[i] }, DelayAfterMs = 0 },
                    false, tokens[i], "0", Localizer.Get("S0909")));
            // Warisan jeda (string mentah - bila setengah ketik /
            // invalid, tetap jadi error Simpan seperti dulu).
            inserted[^1].DelayText = lastDelay;
            for (int i = 0; i < inserted.Count; i++)
                InsertStepRow(at + i, inserted[i]);
            RenumberSteps();
            _dirty = true;
            StepList.SelectedItem = inserted[0];
        }
        else if (_captureReplace && StepList.SelectedItem is MacroStepRow row)
        {
            // UBAH selalu mengubah ketukan (tidak mengubah jenis
            // langkah): baris ketukan tetap satu ketukan, apa pun
            // posisi checkbox Up/Down.
            row.Chord = chord;
            _dirty = true;
        }
        else
        {
            // §66: Tambah mengikuti checkbox yang sama dengan Rekam.
            // Up/down aktif: chord diurai menjadi langkah tekan+lepas
            // (modifier turun berurutan, tombol utama turun, utama
            // lepas, modifier lepas terbalik - urutan yang sama dengan
            // batch HotkeyAction). Tidak aktif: satu ketukan seperti
            // dulu; seluruh langkah uraian ditambahkan (↓ tanpa ↑
            // akan menyangkutkan tombol saat macro dijalankan).
            if (UpDownBox.IsChecked == true)
            {
                var tokens = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                foreach (var t in tokens)
                    AddStepRow(new MacroStepRow(
                        new ActionStep { ActionId = "keydown", Params = new JsonObject { ["key"] = t }, DelayAfterMs = 0 },
                        false, t, "0", Localizer.Get("S0908")));
                for (int i = tokens.Length - 1; i >= 0; i--)
                    AddStepRow(new MacroStepRow(
                        new ActionStep { ActionId = "keyup", Params = new JsonObject { ["key"] = tokens[i] }, DelayAfterMs = 0 },
                        false, tokens[i], "0", Localizer.Get("S0909")));
            }
            else
            {
                var step = new ActionStep
                {
                    ActionId = "hotkey",
                    Params = new JsonObject { ["keys"] = chord },
                };
                AddStepRow(new MacroStepRow(step, true, chord, "0", Localizer.Get("S0193")));
            }
            RenumberSteps();
            _dirty = true;
            StepList.SelectedItem = _stepRows[^1];
        }
        EndCapture();
    }

    private static bool IsModifierKey(WKey key) => key is
        WKey.LeftCtrl or WKey.RightCtrl
        or WKey.LeftShift or WKey.RightShift
        or WKey.LeftAlt or WKey.RightAlt
        or WKey.LWin or WKey.RWin;

    /// <summary>WPF Key → KeyChord token. Returns null for keys the
    /// chord parser cannot represent (IME keys, OEM specials, and any
    /// key not listed here) - never a guessed token. Note: Win alone
    /// cannot complete a capture (it is a modifier), and Alt+key
    /// arrives as Key.System with the real key in SystemKey.</summary>
    private static string? KeyToToken(WKey key)
    {
        if (key >= WKey.A && key <= WKey.Z) return key.ToString();
        if (key >= WKey.D0 && key <= WKey.D9) return key.ToString()[1..];
        if (key >= WKey.NumPad0 && key <= WKey.NumPad9) return Localizer.Get("S0947") + key.ToString()[6..];
        if (key >= WKey.F1 && key <= WKey.F24) return key.ToString();
        return key switch
        {
            WKey.Space => "Space",
            WKey.Tab => "Tab",
            WKey.Enter => "Enter",
            WKey.Back => "Backspace",
            WKey.Delete => "Delete",
            WKey.Insert => "Insert",
            WKey.Home => "Home",
            WKey.End => "End",
            WKey.PageUp => Localizer.Get("S0951"),
            WKey.PageDown => Localizer.Get("S0952"),
            WKey.Up => Localizer.Get("S0482"),
            WKey.Down => Localizer.Get("S0483"),
            WKey.Left => Localizer.Get("S0953"),
            WKey.Right => Localizer.Get("S0954"),
            WKey.Capital => Localizer.Get("S0955"),
            WKey.Scroll => Localizer.Get("S0956"),
            WKey.NumLock => Localizer.Get("S0957"),
            WKey.Pause => "Pause",
            WKey.Apps => Localizer.Get("S0053"),
            WKey.OemMinus => "-",
            WKey.OemPlus => "=",
            WKey.OemOpenBrackets => "[",
            WKey.OemCloseBrackets => "]",
            WKey.OemPipe => "\\",
            WKey.OemSemicolon => ";",
            WKey.OemQuotes => "'",
            WKey.OemComma => ",",
            WKey.OemPeriod => ".",
            WKey.OemQuestion => "/",
            WKey.OemTilde => "`",
            _ => null,
        };
    }

    // --- name / cycles --------------------------------------------------

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        if (_editingRow is { IsUnsaved: true })
            _editingRow.Name = NameBox.Text;
    }

    private void CyclesBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
    }

    // §69: "2×" → 2.0 (invariant - titik desimal, bukan koma, karena
    // string daftar ditulis dengan titik). Gagal parse / null → 1.0
    // (tidak pernah terjadi dari daftar, tapi Simpan tidak boleh
    // menulis kecepatan sampah).
    private static string FormatSpeed(double speed)
        => speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";

    private static double ParseSpeed(string? text)
    {
        if (text is null) return 1.0;
        string t = text.Trim().TrimEnd('×').Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 1.0;
    }

    private void SpeedBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
    }
}
