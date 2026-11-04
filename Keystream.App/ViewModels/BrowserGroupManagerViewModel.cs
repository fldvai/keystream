using System.Collections.ObjectModel;
using Keystream.Core.Localization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.Actions.Browser;
using Keystream.App.Host;
using Keystream.Core.Config;

namespace Keystream.App.ViewModels;

/// <summary>Edits browser profile groups (playlists) only - two-column
/// redesign, 25 Okt 2026. Custom browsers and User Data scanning live in
/// BrowserManagerWindow; this VM still owns their working list (for the
/// entry dropdown and for Save) and refreshes it after that window
/// closes. Works on deep copies; nothing touches host.Config until Save,
/// so Cancel discards everything. The editor's group dropdown refreshes
/// after this window closes (see BindingEditorWindow wiring).</summary>
public partial class BrowserGroupManagerViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly List<CustomBrowserConfig> _customWorking;
    private bool _ready;

    public event Action? RequestClose;
    public event Action? Saved;

    /// <summary>§140: pesan info pelacakan (S0852/S0854/S0857, hasil
    /// prune, kegagalan simpan) - dipasang code-behind ke AppDialog Ok
    /// seperti SettingsWindow.xaml.cs:67-69 (pemasangan milik Worker B;
    /// VM ini hanya mendeklarasikan + memanggilnya, meniru
    /// SettingsViewModel.RequestShowMessage).</summary>
    public event Action<string>? RequestShowMessage;
    public Func<string, bool>? ConfirmPrompt { get; set; }

    /// <summary>True sesaat sebelum Save yang berhasil menutup jendela:
    /// salinan kerja sudah dipersistenkan, jadi penutupan ini bukan
    /// discard dan Window_Closing tidak boleh meminta konfirmasi.
    /// (Tanpa ini, normalisasi saat Save - Trim nama, Clamp DelayMs,
    /// Toggle→Focus - bisa membuat perbandingan mengira masih ada
    /// perubahan dan memunculkan konfirmasi palsu persis setelah Simpan.)</summary>
    public bool SuppressDiscardConfirm { get; private set; }

    /// <summary>True bila ada yang akan hilang bila jendela ditutup tanpa
    /// Simpan: grup/entri berbeda dari Config (dibandingkan dengan
    /// normalisasi yang sama seperti Save), daftar browser kustom
    /// berbeda dari Config (termasuk hasil Save jendela Kelola Browser
    /// yang hanya menyalin ke daftar bersama - persistensinya tetap di
    /// sini), atau draf form tambah-entri yang belum ditambahkan.
    /// Seleksi dan status turunan (ManagerManualProfileStatus) bukan
    /// perubahan - tidak dihitung.</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            // Draf form tambah-entri yang belum ditambahkan ikut hilang.
            if (!string.IsNullOrWhiteSpace(ManagerEntryUrl)
                || !string.IsNullOrWhiteSpace(ManagerManualExe)
                || !string.IsNullOrWhiteSpace(ManagerManualProfilePath))
                return true;
            if (!CustomsEqual(_customWorking, _host.Config.CustomBrowsers))
                return true;
            var saved = _host.Config.BrowserProfileGroups ?? new List<BrowserProfileGroup>();
            if (Groups.Count != saved.Count) return true;
            for (int i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                var s = saved[i];
                if (s is null) return true;
                if (g.Id != s.Id) return true;
                if ((g.Name ?? "").Trim() != (s.Name ?? "").Trim()) return true;
                if (g.Mode != s.Mode) return true;
                if (Math.Clamp(g.DelayMs, 0, 10000) != s.DelayMs) return true;
                if (SavedOpenMode(g.OpenMode) != s.OpenMode) return true;
                if (!EntriesEqual(g.Entries, s.Entries)) return true;
            }
            return false;
        }
    }

    private static bool CustomsEqual(IReadOnlyList<CustomBrowserConfig> working,
        IReadOnlyList<CustomBrowserConfig> saved)
    {
        if (working is null || saved is null) return working is null && saved is null;
        if (working.Count != saved.Count) return false;
        for (int i = 0; i < working.Count; i++)
        {
            var w = working[i];
            var s = saved[i];
            if (w is null || s is null)
            {
                if (w is not null || s is not null) return false;
                continue;
            }
            if (w.Id != s.Id || w.Name != s.Name || w.ExePath != s.ExePath
                || w.Engine != s.Engine || w.UserDataDir != s.UserDataDir)
                return false;
        }
        return true;
    }

    private static string SavedOpenMode(string openMode)
    {
        string m = BrowserAction.NormalizeOpenMode(openMode);
        return m == "Toggle" ? "Focus" : m;
    }

    private static bool EntriesEqual(ObservableCollection<EntryEdit> edits,
        IReadOnlyList<BrowserGroupEntry> saved)
    {
        if (saved is null) return edits.Count == 0;
        if (edits.Count != saved.Count) return false;
        for (int i = 0; i < edits.Count; i++)
        {
            var e = edits[i];
            var s = saved[i];
            if (s is null) return false;
            if (e.BrowserExe != s.BrowserExe || e.Engine != s.Engine
                || e.ProfileKey != s.ProfileKey || e.ProfileDisplay != s.ProfileDisplay)
                return false;
            // Normalisasi yang sama seperti Save: entri manual menyimpan
            // path kanonis + UserDataDir kosong; entri dropdown menyimpan
            // path kosong + UserDataDir ter-Trim; URL selalu ter-Trim.
            string savedPath = e.IsManual ? CanonicalProfilePath(e.ProfilePath) : "";
            if (savedPath != s.ProfilePath) return false;
            string savedUserData = e.IsManual ? "" : (e.UserDataDir ?? "").Trim();
            if (savedUserData != s.UserDataDir) return false;
            if ((e.Url ?? "").Trim() != s.Url) return false;
        }
        return true;
    }

    public sealed partial class GroupEdit : ObservableObject
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _mode = "Simultaneous";
        [ObservableProperty] private int _delayMs = 800;
        [ObservableProperty] private string _openMode = "New";
        public ObservableCollection<EntryEdit> Entries { get; } = new();
    }

    public sealed partial class EntryEdit : ObservableObject
    {
        [ObservableProperty] private string _browserExe = "";
        [ObservableProperty] private string _engine = "Chromium";
        [ObservableProperty] private string _profileKey = "";
        [ObservableProperty] private string _profileDisplay = "";
        [ObservableProperty] private string _browserName = "";
        [ObservableProperty] private string _profilePath = "";
        [ObservableProperty] private string _userDataDir = "";
        [ObservableProperty] private string _url = "";
        public bool IsManual => ProfilePath.Length > 0;
        public string Display => $"{BrowserName} — {ProfileDisplay}" + (IsManual ? Localizer.Get("S0789") : "");
        partial void OnBrowserNameChanged(string value) => OnPropertyChanged(nameof(Display));
        partial void OnProfileDisplayChanged(string value) => OnPropertyChanged(nameof(Display));
        partial void OnProfilePathChanged(string value)
        {
            OnPropertyChanged(nameof(IsManual));
            OnPropertyChanged(nameof(Display));
        }
    }

    public ObservableCollection<GroupEdit> Groups { get; } = new();
    [ObservableProperty] private GroupEdit? _selectedGroup;
    [ObservableProperty] private EntryEdit? _selectedEntry;

    public ObservableCollection<DetectedBrowser> ManagerBrowsers { get; } = new();
    [ObservableProperty] private string _managerBrowserExe = "";
    // Daftar PENUH profil browser terpilih - penyaringan dilakukan oleh
    // ComboBoxFilterBehavior pada ComboBox editable (25 Okt 2026),
    // bukan kolom terpisah.
    public ObservableCollection<BrowserProfile> ManagerAllProfiles { get; } = new();
    [ObservableProperty] private string _managerProfileKey = "";
    [ObservableProperty] private string _managerEntryUrl = "";
    // Manual (CUSTOM) entry fields - mirror of the editor's manual section.
    [ObservableProperty] private string _managerManualExe = "";
    [ObservableProperty] private string _managerManualProfilePath = "";
    [ObservableProperty] private string _managerManualProfileStatus = "";
    public const string ManagerCustomSentinel = "CUSTOM";
    public bool IsManagerCustom => ManagerBrowserExe == ManagerCustomSentinel;
    public bool IsManagerDropdown => !IsManagerCustom;
    private string _managerManualEngine = "Chromium";
    private string _managerManualProfileDisplay = "";

    [ObservableProperty] private string _errorText = "";
    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasErrorText));

    public IReadOnlyList<ActionCatalog.ValueOption> ModeOptions => new[]
    {
        new ActionCatalog.ValueOption("Simultaneous", Localizer.Get("S0790")),
        new ActionCatalog.ValueOption("Sequential", Localizer.Get("S0791")),
    };
    public IReadOnlyList<ActionCatalog.ValueOption> GroupOpenModeOptions => new[]
    {
        new ActionCatalog.ValueOption("New", Localizer.Get("S0792")),
        new ActionCatalog.ValueOption("Focus", Localizer.Get("S0793")),
    };

    public bool HasSelectedGroup => SelectedGroup is not null;
    public bool HasNoSelectedGroup => SelectedGroup is null;

    /// <summary>The custom-browser working list, shared with
    /// BrowserManagerWindow (its Save copies back into this list).</summary>
    public List<CustomBrowserConfig> SharedCustoms => _customWorking;

    /// <summary>After the browser window closes: re-detect browsers for
    /// the entry dropdown (customs may have been added/removed there)
    /// and refresh the profile list.</summary>
    public void RefreshBrowsersAfterCustomChange()
    {
        RefreshManagerBrowsers();
        RefreshManagerProfiles();
    }

    public BrowserGroupManagerViewModel(AppHost host)
    {
        _host = host;
        // A-02: tahan explicit-null dari config (jalur Load sudah
        // dinormalisasi; guard ini menutup konstruksi dari config lain).
        _customWorking = (host.Config.CustomBrowsers ?? Enumerable.Empty<CustomBrowserConfig>())
            .Where(c => c is not null).Select(c => new CustomBrowserConfig
            {
                Id = c.Id, Name = c.Name, ExePath = c.ExePath, Engine = c.Engine, UserDataDir = c.UserDataDir,
            }).ToList();
        foreach (var g in host.Config.BrowserProfileGroups ?? Enumerable.Empty<BrowserProfileGroup>())
        {
            if (g is null) continue;
            var edit = new GroupEdit { Id = g.Id, Name = g.Name ?? "", Mode = g.Mode, DelayMs = g.DelayMs, OpenMode = BrowserAction.NormalizeOpenMode(g.OpenMode) };
            foreach (var e in g.Entries ?? Enumerable.Empty<BrowserGroupEntry>())
            {
                if (e is null) continue;
                edit.Entries.Add(new EntryEdit
                {
                    BrowserExe = e.BrowserExe ?? "", Engine = e.Engine ?? "", ProfileKey = e.ProfileKey ?? "",
                    ProfileDisplay = e.ProfileDisplay ?? "", ProfilePath = e.ProfilePath ?? "", UserDataDir = e.UserDataDir ?? "", Url = e.Url ?? "",
                    BrowserName = BrowserDisplayName(e.BrowserExe ?? ""),
                });
            }
            Groups.Add(edit);
        }
        SelectedGroup = Groups.FirstOrDefault();
        RefreshManagerBrowsers();
        ManagerBrowserExe = ManagerBrowsers.FirstOrDefault()?.ExePath ?? "";
        RefreshManagerProfiles();
        _ready = true;
        // §140: daftar terlacak terisi sejak jendela dibuka (pola ctor
        // Settings lama); refresh ulang saat tab terlacak dipilih
        // adalah tugas code-behind (Worker B).
        RefreshTrackedWindows();
    }

    private string BrowserDisplayName(string exePath)
    {
        var found = BrowserDiscovery.DetectBrowsers(_customWorking)
            .FirstOrDefault(b => b.ExePath == exePath);
        return found?.Name ?? Path.GetFileNameWithoutExtension(exePath);
    }

    public void RefreshManagerBrowsers()
    {
        ManagerBrowsers.Clear();
        foreach (var b in BrowserDiscovery.DetectBrowsers(_customWorking)) ManagerBrowsers.Add(b);
        // CUSTOM selalu tersedia, bahkan bila tidak ada browser terdeteksi -
        // jalan keluar untuk entri manual di grup (cermin editor).
        ManagerBrowsers.Add(new DetectedBrowser("custom", Localizer.Get("S0542"), ManagerCustomSentinel, "Chromium", "", false));
    }

    public void RefreshManagerProfiles()
    {
        if (IsManagerCustom)
        {
            // Sentinel tidak punya UserDataDir; profil datang dari field
            // manual, bukan dari daftar ini.
            ManagerAllProfiles.Clear();
            return;
        }
        var browser = ManagerBrowsers.FirstOrDefault(b => b.ExePath == ManagerBrowserExe)
            ?? ManagerBrowsers.FirstOrDefault();
        if (browser is null)
        {
            ManagerAllProfiles.Clear();
            return;
        }
        ManagerAllProfiles.Clear();
        foreach (var pr in BrowserDiscovery.GetProfiles(browser)) ManagerAllProfiles.Add(pr);
        if (string.IsNullOrEmpty(ManagerProfileKey) || ManagerAllProfiles.All(p => p.Key != ManagerProfileKey))
            ManagerProfileKey = ManagerAllProfiles.FirstOrDefault()?.Key ?? "";
    }

    partial void OnManagerBrowserExeChanged(string value)
    {
        OnPropertyChanged(nameof(IsManagerCustom));
        OnPropertyChanged(nameof(IsManagerDropdown));
        if (!_ready) return;
        RefreshManagerProfiles();
    }

    partial void OnManagerManualProfilePathChanged(string value) => RefreshManagerManualStatus();

    private void RefreshManagerManualStatus()
    {
        string path = (ManagerManualProfilePath ?? "").Trim();
        if (path.Length == 0)
        {
            ManagerManualProfileStatus = "";
            _managerManualProfileDisplay = "";
            return;
        }
        if (!Directory.Exists(path))
        {
            ManagerManualProfileStatus = Localizer.Get("S0546");
            if (_managerManualProfileDisplay.Length == 0)
                _managerManualProfileDisplay = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return;
        }
        if (BrowserDiscovery.TryDetectProfileFolder(path, out string engine, out string display))
        {
            _managerManualEngine = engine;
            _managerManualProfileDisplay = display;
            ManagerManualProfileStatus = Localizer.Format("S0547", engine, display);
        }
        else
        {
            _managerManualProfileDisplay = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            ManagerManualProfileStatus = Localizer.Get("S0548");
        }
    }

    private static string CanonicalProfilePath(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    partial void OnSelectedGroupChanged(GroupEdit? value)
    {
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(HasNoSelectedGroup));
    }

    [RelayCommand]
    private void AddGroup()
    {
        string baseName = Localizer.Get("S0794");
        string name = baseName;
        int n = 2;
        while (Groups.Any(g => g.Name == name)) name = $"{baseName} {n++}";
        var group = new GroupEdit { Name = name };
        Groups.Add(group);
        SelectedGroup = group;
        ErrorText = "";
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup is null) return;
        if (ConfirmPrompt?.Invoke(Localizer.Format("S0796", SelectedGroup.Name, SelectedGroup.Entries.Count)) != true)
            return;
        Groups.Remove(SelectedGroup);
        SelectedGroup = Groups.FirstOrDefault();
    }

    [RelayCommand]
    private void AddEntry()
    {
        if (SelectedGroup is null) { ErrorText = Localizer.Get("S0797"); return; }
        (_, string? entryUrlError) = BrowserAction.NormalizeUrl(ManagerEntryUrl);
        if (entryUrlError is not null) { ErrorText = entryUrlError; return; }
        string entryUrl = (ManagerEntryUrl ?? "").Trim();
        if (IsManagerCustom)
        {
            string exe = (ManagerManualExe ?? "").Trim();
            string path = CanonicalProfilePath(ManagerManualProfilePath ?? "");
            if (exe.Length == 0 || path.Length == 0)
            {
                ErrorText = Localizer.Get("S0798");
                return;
            }
            if (!Directory.Exists(path))
            {
                ErrorText = Localizer.Get("S0799");
                return;
            }
            if (!BrowserDiscovery.TryDetectProfileFolder(path, out _, out _))
            {
                ErrorText = Localizer.Get("S0800");
                return;
            }
            if (SelectedGroup.Entries.Any(e => e.IsManual
                && string.Equals(e.BrowserExe, exe, StringComparison.OrdinalIgnoreCase)
                && string.Equals(CanonicalProfilePath(e.ProfilePath), path, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorText = Localizer.Get("S0801");
                return;
            }
            string folder = Path.GetFileName(path);
            SelectedGroup.Entries.Add(new EntryEdit
            {
                BrowserExe = exe,
                Engine = _managerManualEngine,
                ProfileKey = folder,
                ProfileDisplay = _managerManualProfileDisplay.Length > 0 ? _managerManualProfileDisplay : folder,
                ProfilePath = path,
                Url = entryUrl,
                BrowserName = Path.GetFileNameWithoutExtension(exe),
            });
            ErrorText = "";
            return;
        }
        if (string.IsNullOrWhiteSpace(ManagerBrowserExe) || string.IsNullOrWhiteSpace(ManagerProfileKey))
        {
            ErrorText = Localizer.Get("S0802");
            return;
        }
        var browser = ManagerBrowsers.FirstOrDefault(b => b.ExePath == ManagerBrowserExe);
        var profile = ManagerAllProfiles.FirstOrDefault(p => p.Key == ManagerProfileKey);
        SelectedGroup.Entries.Add(new EntryEdit
        {
            BrowserExe = ManagerBrowserExe,
            Engine = browser?.Engine ?? "Chromium",
            ProfileKey = ManagerProfileKey,
            ProfileDisplay = profile?.Display ?? ManagerProfileKey,
            // Hanya browser kustom membawa UserDataDir (cakupan aman
            // temuan riset 4); browser terdeteksi memakai lokasi default.
            UserDataDir = browser?.IsCustom == true ? browser.UserDataDir : "",
            Url = entryUrl,
            BrowserName = browser?.Name ?? Path.GetFileNameWithoutExtension(ManagerBrowserExe),
        });
        ErrorText = "";
    }

    [RelayCommand]
    private void DeleteEntry()
    {
        if (SelectedGroup is null || SelectedEntry is null) return;
        SelectedGroup.Entries.Remove(SelectedEntry);
        SelectedEntry = null;
    }

    /// <summary>Laporan Periksa dibuka sebagai modal tersendiri (25 Okt
    /// 2026): teks inline di samping tombol tidak sanggup menampilkan
    /// grup dengan banyak profil tanpa terpotong, dan timer 5 detik
    /// membuat laporan panjang tidak sempat dibaca. Modal bertahan
    /// sampai tombol SELESAI ditekan, dengan scroll untuk laporan panjang.</summary>
    public event Action<string>? RequestShowDryRunReport;

    /// <summary>Periksa grup terpilih TANPA membuka apa pun (tambahan 3).
    /// Tidak seperti Test grup yang benar-benar meluncurkan profilnya,
    /// dry-run hanya membaca disk: exe, folder manual, URL, dan daftar
    /// profil live browsernya.</summary>
    [RelayCommand]
    private void CheckGroupDryRun()
    {
        if (SelectedGroup is null) { RequestShowDryRunReport?.Invoke(Localizer.Get("S0797")); return; }
        if (SelectedGroup.Entries.Count == 0) { RequestShowDryRunReport?.Invoke(Localizer.Get("S0803")); return; }
        var group = new BrowserProfileGroup
        {
            Id = SelectedGroup.Id,
            Name = SelectedGroup.Name,
            Mode = SelectedGroup.Mode,
            DelayMs = SelectedGroup.DelayMs,
            OpenMode = SelectedGroup.OpenMode,
            Entries = SelectedGroup.Entries.Select(e => new BrowserGroupEntry
            {
                BrowserExe = e.BrowserExe, Engine = e.Engine, ProfileKey = e.ProfileKey,
                ProfileDisplay = e.ProfileDisplay, ProfilePath = e.ProfilePath,
                UserDataDir = e.UserDataDir, Url = e.Url,
            }).ToList(),
        };
        var results = BrowserGroupDryRun.CheckGroup(group, _customWorking);
        var lines = new List<string> { BrowserGroupDryRun.Summarize(results) };
        foreach (var r in results)
        {
            string mark = r.Ok ? "✓" : "✗";
            lines.Add(r.Note.Length > 0 ? $"{mark} {r.Display} — {r.Note}" : Localizer.Format("S0806", mark, r.Display));
        }
        RequestShowDryRunReport?.Invoke(string.Join("\n", lines));
    }

    [RelayCommand]
    private void MoveEntryUp()
    {
        if (SelectedGroup is null || SelectedEntry is null) return;
        int i = SelectedGroup.Entries.IndexOf(SelectedEntry);
        if (i > 0) SelectedGroup.Entries.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveEntryDown()
    {
        if (SelectedGroup is null || SelectedEntry is null) return;
        int i = SelectedGroup.Entries.IndexOf(SelectedEntry);
        if (i >= 0 && i < SelectedGroup.Entries.Count - 1) SelectedGroup.Entries.Move(i, i + 1);
    }

    // §140: anggota pelacakan jendela browser, dipindahkan dari
    // SettingsViewModel dengan semantik yang sama. Aksi pelacakan
    // menulis config live + _host.Save() segera (bukan draf grup) dan
    // TIDAK memengaruhi HasUnsavedChanges - Batal tidak membatalkan
    // Tutup/Fokus/prune. Pesan info (S0852/S0854/S0857, hasil prune)
    // lewat RequestShowMessage (AppDialog di code-behind, pola
    // Settings); kegagalan simpan lewat ErrorText via ShowSaveError
    // di bawah (pola ShowSaveError Settings: ErrorText + dialog).

    /// <summary>Satu baris daftar "profil sedang terbuka": rekaman
    /// pelacakan + tampilannya + grup yang cocok (dihitung ulang di
    /// tiap RefreshTrackedWindows dari salinan kerja Groups). Kunci
    /// manual adalah path penuh, jadi tampilannya memakai nama folder;
    /// kunci terdaftar tampil apa adanya (normalisasi Settings).</summary>
    public sealed class TrackedWindowRow
    {
        public required BrowserWindowTracking Record { get; init; }
        public required string Display { get; init; }
        public required bool IsValid { get; init; }
        public required string GroupName { get; init; }
        public required string GroupDisplay { get; init; }
    }

    public ObservableCollection<TrackedWindowRow> TrackedWindows { get; } = new();

    [ObservableProperty] private string _trackingStatusText = "";

    // §140: pesan prune dari pipeline - S1739 (nol mati) dan S1740
    // (jumlah terhapus); tidak ada string lama yang menyatakan hasil
    // prune dengan jujur (S0852/S0854 menyebut penghapusan SEMUA
    // pelacakan).

    /// <summary>Pola ShowSaveError SettingsViewModel: kegagalan
    /// _host.Save() tidak boleh diam - ErrorText (permukaan pesan
    /// jendela ini) DAN dialog RequestShowMessage, S0937 + alasan.</summary>
    private void ShowSaveError(Exception ex)
    {
        string msg = Localizer.Get("S0937") + " " + ex.Message;
        ErrorText = msg;
        RequestShowMessage?.Invoke(msg);
    }

    [RelayCommand]
    public void RefreshTrackedWindows()
    {
        TrackedWindows.Clear();
        foreach (var record in _host.Config.BrowserWindowTracking ?? Enumerable.Empty<BrowserWindowTracking>())
        {
            if (record is null) continue;
            string key = record.ProfileKey ?? "";
            string keyDisplay = key.Contains('\\') || key.Contains('/')
                ? Path.GetFileName(key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : key;
            // Nama grup: salinan kerja Groups (urutannya = urutan
            // config), cocok pertama yang memenuhi BrowserExe
            // OrdinalIgnoreCase + ProfileKey Ordinal terhadap entri
            // grup (EntryEdit.BrowserExe / EntryEdit.ProfileKey); tak
            // cocok → GroupName "" dan GroupDisplay "-". Dihitung
            // ulang tiap refresh - grup bisa diedit di sesi modal ini.
            // Konsekuensi keputusan tetap: entri manual melacak dengan
            // kunci path penuh sementara EntryEdit.ProfileKey-nya nama
            // folder, jadi rekaman manual tak-cocok dan menampilkan "-".
            string groupName = "";
            foreach (var g in Groups)
            {
                if (g.Entries.Any(e =>
                    string.Equals(e.BrowserExe, record.BrowserExe, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.ProfileKey, record.ProfileKey, StringComparison.Ordinal)))
                {
                    groupName = g.Name.Trim();
                    break;
                }
            }
            TrackedWindows.Add(new TrackedWindowRow
            {
                Record = record,
                Display = $"{Path.GetFileNameWithoutExtension(record.BrowserExe ?? "")} — {keyDisplay}",
                IsValid = BrowserWindowTracker.IsValid(record),
                GroupName = groupName,
                GroupDisplay = string.IsNullOrWhiteSpace(groupName) ? "—" : groupName,
            });
        }
        RefreshTrackingStatus();
    }

    /// <summary>Status pelacakan (temuan riset 5): IsValid per rekaman
    /// adalah panggilan user32/proses - murah untuk satu hitungan saat
    /// jendela dibuka atau setelah tiap aksi. Teks S0850/S0851, sama
    /// seperti RefreshBrowserTrackingStatus Settings lama.</summary>
    private void RefreshTrackingStatus()
    {
        var list = _host.Config.BrowserWindowTracking ?? new List<BrowserWindowTracking>();
        int valid = list.Count(r => BrowserWindowTracker.IsValid(r));
        TrackingStatusText = list.Count == 0
            ? Localizer.Get("S0850")
            : Localizer.Format("S0851", list.Count, valid);
    }

    [RelayCommand]
    private async Task CloseTrackedWindow(TrackedWindowRow? row)
    {
        if (row is null) return;
        if (BrowserWindowTracker.IsValid(row.Record))
            BrowserWindowTracker.Close(row.Record);
        await Task.Delay(500);
        // Jendela yang menolak close (mis. prompt leave-page) tetap valid
        // dan tetap di daftar - hanya rekaman yang kini tidak valid yang
        // dibuang, agar Focus tidak menunjuk jendela yang sudah tiada.
        if (!BrowserWindowTracker.IsValid(row.Record))
        {
            BrowserWindowTracker.Remove(
                _host.Config.BrowserWindowTracking, row.Record.BrowserExe, row.Record.ProfileKey);
            try { _host.Save(); }
            catch (Exception ex)
            {
                // Rollback lokal (pola ImportProfile): rekaman kembali
                // ke daftar memori agar sesuai file; baris di UI tidak
                // di-refresh sehingga tetap tampil dan bisa diulang.
                _host.Config.BrowserWindowTracking.Add(row.Record);
                ShowSaveError(ex);
                return;
            }
        }
        RefreshTrackedWindows();
    }

    [RelayCommand]
    private void FocusTrackedWindow(TrackedWindowRow? row)
    {
        if (row is null) return;
        // BrowserWindowTracker.Focus tidak memeriksa IsValid di dalam
        // (kontrak BrowserWindowTracker.cs:76-92) - pemanggil wajib
        // memeriksa dulu; tombol Fokus nonaktif bila tak-valid
        // (binding IsValid, Worker B), penjaga ini lapis keduanya.
        if (!row.IsValid) return;
        if (BrowserWindowTracker.Focus(row.Record)) return;
        // Focus() false bukan sukses: refresh dulu agar baris yang
        // ternyata mati terbarui, lalu pesan jujur. S1112 sudah ada
        // ("Jendela '{0}' gagal difokuskan.", dipakai BrowserAction
        // untuk kegagalan fokus yang sama) - dipakai ulang dengan
        // Display baris ini, tanpa kunci baru. ErrorText, bukan klaim
        // sukses; tidak ada penyimpanan yang terlibat.
        RefreshTrackedWindows();
        ErrorText = Localizer.Format("S1112", row.Display);
    }

    [RelayCommand]
    private void ClearBrowserTracking()
    {
        if (_host.Config.BrowserWindowTracking.Count == 0)
        {
            RequestShowMessage?.Invoke(Localizer.Get("S0852"));
            return;
        }
        if (ConfirmPrompt?.Invoke(
            Localizer.Get("S0853")) != true)
            return;
        var trackingBackup = _host.Config.BrowserWindowTracking.ToList();
        _host.Config.BrowserWindowTracking.Clear();
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Rollback lokal: kembalikan daftar memori agar sesuai file;
            // tanpa ini daftar memori kosong padahal file masih berisi
            // rekaman, sehingga Clear berikutnya keliru mengira kosong.
            _host.Config.BrowserWindowTracking.Clear();
            _host.Config.BrowserWindowTracking.AddRange(trackingBackup);
            ShowSaveError(ex);
            return;
        }
        RefreshTrackedWindows();
        RequestShowMessage?.Invoke(Localizer.Get("S0854"));
    }

    [RelayCommand]
    private async Task CloseAllTrackedWindows()
    {
        var list = _host.Config.BrowserWindowTracking;
        if (list.Count == 0)
        {
            RequestShowMessage?.Invoke(Localizer.Get("S0855"));
            return;
        }
        if (ConfirmPrompt?.Invoke(
            Localizer.Get("S0856")) != true)
            return;
        // Snapshot dulu: Close() hanya menyentuh jendela dalam daftar ini,
        // tidak pernah jendela lain. Jeda 1 detik memberi waktu jendela
        // yang mematuhi close untuk hilang sebelum pass re-validasi.
        var snapshot = list.ToList();
        // Hitung yang valid SEBELUM menutup - menghitung sesudah jeda
        // akan selalu nol, karena jendela yang tertutup sudah tidak valid.
        var validBefore = snapshot.Where(r => BrowserWindowTracker.IsValid(r)).ToList();
        foreach (var record in validBefore)
            BrowserWindowTracker.Close(record);
        await Task.Delay(1000);
        var survivors = list.Where(r => BrowserWindowTracker.IsValid(r)).ToList();
        int closed = validBefore.Count - survivors.Count;
        // Rekaman yang kini tidak valid dibuang (termasuk yatim lama);
        // yang selamat dipertahankan agar Focus tetap bekerja untuknya.
        list.Clear();
        list.AddRange(survivors);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Rollback dari snapshot yang sudah ada di atas: kembalikan
            // daftar memori ke keadaan sebelum penutupan.
            list.Clear();
            list.AddRange(snapshot);
            ShowSaveError(ex);
            return;
        }
        RefreshTrackedWindows();
        RequestShowMessage?.Invoke(Localizer.Format("S0857", closed, survivors.Count));
    }

    [RelayCommand]
    private void PruneDeadTracking()
    {
        var list = _host.Config.BrowserWindowTracking;
        var dead = list.Where(r => !BrowserWindowTracker.IsValid(r)).ToList();
        if (dead.Count == 0)
        {
            // Nol rekaman tak-valid = tidak ada yang dihapus: laporkan
            // jujur lewat RequestShowMessage (pesan S1739), jangan klaim bersih-bersih. Refresh tetap
            // jalan agar validitas baris segar.
            RefreshTrackedWindows();
            RequestShowMessage?.Invoke(Localizer.Get("S1739"));
            return;
        }
        var snapshot = list.ToList();
        // Hanya rekaman mati yang dihapus - rekaman valid tidak
        // disentuh, dan tidak ada jendela yang ditutup. Remove memakai
        // referensi yang sama dari daftar (BrowserWindowTracking tanpa
        // Equals kustom), jadi hanya rekaman snapshot mati yang hilang.
        foreach (var record in dead)
            list.Remove(record);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Rollback dari snapshot: kembalikan daftar memori agar
            // sesuai file; baris tidak di-refresh agar bisa diulang.
            list.Clear();
            list.AddRange(snapshot);
            ShowSaveError(ex);
            return;
        }
        RefreshTrackedWindows();
        RequestShowMessage?.Invoke(Localizer.Format("S1740", dead.Count));
    }

    [RelayCommand]
    private void Save()
    {
        foreach (var g in Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name))
            {
                ErrorText = Localizer.Get("S0808");
                return;
            }
            foreach (var e in g.Entries)
            {
                if (string.IsNullOrWhiteSpace(e.BrowserExe) || string.IsNullOrWhiteSpace(e.ProfileKey))
                {
                    ErrorText = Localizer.Format("S0809", g.Name);
                    return;
                }
                (_, string? saveUrlError) = BrowserAction.NormalizeUrl(e.Url);
                if (saveUrlError is not null)
                {
                    ErrorText = Localizer.Format("S0810", g.Name, saveUrlError);
                    return;
                }
                if (e.IsManual)
                {
                    string path = CanonicalProfilePath(e.ProfilePath);
                    if (path.Length == 0)
                    {
                        ErrorText = Localizer.Format("S0811", g.Name);
                        return;
                    }
                    if (Directory.Exists(path) && !BrowserDiscovery.TryDetectProfileFolder(path, out _, out _))
                    {
                        ErrorText = Localizer.Format("S0812", g.Name);
                        return;
                    }
                }
            }
        }
        // Silent-save fix (§45/§46): dulu _host.Save() tanpa try/catch -
        // gagal simpan melempar keluar command tanpa pesan, sementara
        // Config memori sudah diganti (nama/grup baru bertahan di sesi
        // lalu kembali sendiri saat restart). Kini: snapshot dulu,
        // gagal = rollback + ErrorText S0937, modal tetap terbuka.
        var prevGroups = _host.Config.BrowserProfileGroups;
        var prevCustoms = _host.Config.CustomBrowsers;
        _host.Config.BrowserProfileGroups = Groups.Select(g => new BrowserProfileGroup
        {
            Id = g.Id,
            Name = g.Name.Trim(),
            Mode = g.Mode,
            DelayMs = Math.Clamp(g.DelayMs, 0, 10000),
            OpenMode = BrowserAction.NormalizeOpenMode(g.OpenMode) == "Toggle" ? "Focus" : BrowserAction.NormalizeOpenMode(g.OpenMode),
            Entries = g.Entries.Select(e => new BrowserGroupEntry
            {
                BrowserExe = e.BrowserExe, Engine = e.Engine, ProfileKey = e.ProfileKey, ProfileDisplay = e.ProfileDisplay,
                ProfilePath = e.IsManual ? CanonicalProfilePath(e.ProfilePath) : "",
                UserDataDir = e.IsManual ? "" : (e.UserDataDir ?? "").Trim(),
                Url = (e.Url ?? "").Trim(),
            }).ToList(),
        }).ToList();
        _host.Config.CustomBrowsers = _customWorking.Select(c => new CustomBrowserConfig
        {
            Id = c.Id, Name = c.Name, ExePath = c.ExePath, Engine = c.Engine, UserDataDir = c.UserDataDir,
        }).ToList();
        try
        {
            _host.Save();
        }
        catch (Exception ex)
        {
            _host.Config.BrowserProfileGroups = prevGroups;
            _host.Config.CustomBrowsers = prevCustoms;
            ErrorText = Localizer.Get("S0937") + " " + ex.Message;
            return;
        }
        Saved?.Invoke();
        SuppressDiscardConfirm = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
