using System.Collections.ObjectModel;
using Keystream.Core.Localization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.Actions.Browser;
using Keystream.Core.Config;

namespace Keystream.App.ViewModels;

/// <summary>Edits custom browsers and scans User Data folders - the
/// setup-only half of the old single-modal manager (two-column redesign,
/// 25 Okt 2026). Works on a deep copy of the list the group window owns;
/// Save copies the result back into that shared list, so the group
/// window's own Simpan is still what persists everything to disk and
/// Batal there still discards browser changes too.</summary>
public partial class BrowserManagerViewModel : ObservableObject
{
    private readonly List<CustomBrowserConfig> _shared;
    private readonly List<CustomBrowserConfig> _working;

    public event Action? RequestClose;

    /// <summary>True sesaat sebelum Save menutup jendela: salinan kerja
    /// sudah disalin ke daftar bersama, jadi penutupan ini bukan discard
    /// dan Window_Closing tidak boleh meminta konfirmasi.</summary>
    public bool SuppressDiscardConfirm { get; private set; }

    /// <summary>True bila ada yang akan hilang bila jendela ditutup tanpa
    /// Save: daftar kerja berbeda dari daftar bersama, atau draf form
    /// tambah/pindai yang belum dimasukkan ke daftar. Seleksi saja
    /// (SelectedCustomBrowser) bukan perubahan - tidak dihitung.</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            if (_working.Count != _shared.Count) return true;
            for (int i = 0; i < _working.Count; i++)
            {
                var w = _working[i];
                var s = _shared[i];
                if (w.Id != s.Id || w.Name != s.Name || w.ExePath != s.ExePath
                    || w.Engine != s.Engine || w.UserDataDir != s.UserDataDir)
                    return true;
            }
            // Draf yang belum ditambah/dikonfirmasi ikut hilang saat tutup.
            if (!string.IsNullOrWhiteSpace(CustomName)
                || !string.IsNullOrWhiteSpace(CustomExe)
                || !string.IsNullOrWhiteSpace(CustomUserDataDir))
                return true;
            if (HasScanPreview || !string.IsNullOrWhiteSpace(ScanExe)
                || !string.IsNullOrWhiteSpace(ScanName))
                return true;
            return false;
        }
    }

    public ObservableCollection<CustomBrowserConfig> CustomBrowsersWorking { get; } = new();
    [ObservableProperty] private CustomBrowserConfig? _selectedCustomBrowser;
    [ObservableProperty] private string _customName = "";
    [ObservableProperty] private string _customExe = "";
    [ObservableProperty] private string _customEngine = "Chromium";
    [ObservableProperty] private string _customUserDataDir = "";

    [ObservableProperty] private string _errorText = "";
    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasErrorText));

    /// Engine picker: Value is the literal engine persisted as
    /// CustomBrowserConfig.Engine; Display resolves S0813/S0814 lazily,
    /// so a translated display label can never leak into storage.
    public IReadOnlyList<ActionCatalog.ValueOption> EngineOptions => new[]
    {
        new ActionCatalog.ValueOption("Chromium", "S0813"),
        new ActionCatalog.ValueOption("Firefox", "S0814"),
    };

    public BrowserManagerViewModel(List<CustomBrowserConfig> shared)
    {
        _shared = shared;
        _working = shared.Select(c => new CustomBrowserConfig
        {
            Id = c.Id, Name = c.Name, ExePath = c.ExePath, Engine = c.Engine, UserDataDir = c.UserDataDir,
        }).ToList();
        foreach (var c in _working) CustomBrowsersWorking.Add(c);
    }

    [RelayCommand]
    private void AddCustomBrowser()
    {
        if (string.IsNullOrWhiteSpace(CustomExe))
        {
            ErrorText = Localizer.Get("S0815");
            return;
        }
        var custom = new CustomBrowserConfig
        {
            Name = string.IsNullOrWhiteSpace(CustomName) ? Path.GetFileNameWithoutExtension(CustomExe) : CustomName.Trim(),
            ExePath = CustomExe.Trim(),
            Engine = CustomEngine,
            UserDataDir = CustomUserDataDir.Trim(),
        };
        _working.Add(custom);
        CustomBrowsersWorking.Add(custom);
        CustomName = "";
        CustomExe = "";
        CustomEngine = "Chromium";
        CustomUserDataDir = "";
        ErrorText = "";
    }

    [RelayCommand]
    private void DeleteCustomBrowser()
    {
        if (SelectedCustomBrowser is null) return;
        _working.RemoveAll(c => c.Id == SelectedCustomBrowser.Id);
        CustomBrowsersWorking.Remove(SelectedCustomBrowser);
        SelectedCustomBrowser = null;
    }

    // ---- Pindai folder User Data (temuan riset 4, Chromium saja) ----
    [ObservableProperty] private string _scanStatusText = "";
    [ObservableProperty] private string _scanExe = "";
    [ObservableProperty] private string _scanName = "";
    public ObservableCollection<BrowserProfile> ScanPreview { get; } = new();
    [ObservableProperty] private bool _hasScanPreview;
    private string _scanFolder = "";

    /// <summary>Dipanggil code-behind setelah folder dipilih. Mengklasifikasi
    /// dulu: folder profil tunggal diarahkan ke alur CUSTOM editor (bukan
    /// didaftarkan sebagai browser - itu akan menampilkan nol profil);
    /// folder User Data dipindai dengan GetChromiumProfiles yang ada
    /// (Default + Profile * saja; System/Guest Profile terkecuali).</summary>
    public void StartUserDataScan(string folder)
    {
        _scanFolder = "";
        ScanPreview.Clear();
        HasScanPreview = false;
        ScanExe = "";
        ScanName = "";
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ScanStatusText = Localizer.Get("S0816");
            return;
        }
        if (BrowserDiscovery.TryDetectProfileFolder(folder, out _, out _))
        {
            ScanStatusText = Localizer.Get("S0817");
            return;
        }
        var scan = BrowserDiscovery.GetProfilesDetailed(
            new DetectedBrowser("scan", Localizer.Get("S0818"), "", "Chromium", folder, false));
        var profiles = scan.Profiles.ToList();
        if (profiles.Count == 0)
        {
            // §45: folder tak-terbaca dulu dilaporkan sebagai "bukan
            // folder User Data" - dua keadaan yang sangat berbeda.
            ScanStatusText = scan.Unreadable
                ? Localizer.Get("S0819")
                : Localizer.Get("S0820");
            return;
        }
        foreach (var pr in profiles) ScanPreview.Add(pr);
        _scanFolder = folder;
        HasScanPreview = true;
        ScanStatusText = Localizer.Format("S0821", profiles.Count)
            + (scan.Unreadable ? Localizer.Get("S0822") : "");
    }

    [RelayCommand]
    private void ConfirmScan()
    {
        if (!HasScanPreview || _scanFolder.Length == 0)
        {
            ScanStatusText = Localizer.Get("S0823");
            return;
        }
        string exe = (ScanExe ?? "").Trim();
        if (exe.Length == 0)
        {
            ScanStatusText = Localizer.Get("S0815");
            return;
        }
        // Peringatan eksplisit, bukan drop diam-diam seperti dedup seenExe
        // di DetectBrowsers: pindai yang tampak tidak melakukan apa-apa
        // adalah hasil yang membingungkan.
        if (_working.Any(c => string.Equals(c.ExePath, exe, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.UserDataDir, _scanFolder, StringComparison.OrdinalIgnoreCase)))
        {
            ScanStatusText = Localizer.Get("S0824");
            return;
        }
        var custom = new CustomBrowserConfig
        {
            Name = string.IsNullOrWhiteSpace(ScanName) ? Path.GetFileNameWithoutExtension(exe) : ScanName.Trim(),
            ExePath = exe,
            Engine = "Chromium",
            UserDataDir = _scanFolder,
        };
        _working.Add(custom);
        CustomBrowsersWorking.Add(custom);
        ScanPreview.Clear();
        HasScanPreview = false;
        _scanFolder = "";
        ScanExe = "";
        ScanName = "";
        ScanStatusText = Localizer.Get("S0825");
    }

    [RelayCommand]
    private void Save()
    {
        _shared.Clear();
        _shared.AddRange(_working.Select(c => new CustomBrowserConfig
        {
            Id = c.Id, Name = c.Name, ExePath = c.ExePath, Engine = c.Engine, UserDataDir = c.UserDataDir,
        }));
        // Save menyalin working → shared; tidak ada lagi yang belum
        // disimpan, jadi Window_Closing tidak boleh meminta konfirmasi
        // (pola yang sama: Save macro membersihkan _dirty, §96/§97).
        SuppressDiscardConfirm = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
