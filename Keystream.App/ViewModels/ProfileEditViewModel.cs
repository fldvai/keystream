using System.Collections.ObjectModel;
using Keystream.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.App.Host;
using Keystream.Core.Config;

namespace Keystream.App.ViewModels;

/// <summary>
/// Profile edit modal (UI v2.9): everything about ONE profile lives here
/// - rename, auto-profile trigger apps (moved out of Settings, where it
/// was buried), stats, and duplicate. The auto-profile matcher only
/// looks at the ACTIVE profile, so its settings belong to the profile,
/// not to a global settings page.
/// </summary>
public partial class ProfileEditViewModel : ObservableObject
{
    private readonly AppHost _host;

    public event Action? CloseOk;
    public event Action? CloseCancel;

    public ProfileConfig Profile { get; }
    public bool WantsDuplicate { get; private set; }

    /// <summary>§175 (dikoreksi 30 Okt 2026): true only on the
    /// sidebar-pencil path for an EXISTING profile — the modal title
    /// is "Edit Profil" (S0249). The new-profile auto-open path
    /// leaves it false → "Tambah Profil" (S1837). Never "Edit Mode":
    /// Mode is a page, a different modal (PageEditViewModel, S0836).
    /// Precedent: PageEditViewModel.Title.</summary>
    public bool IsExistingProfile { get; init; }
    public string Title => IsExistingProfile ? Localizer.Get("S0249") : Localizer.Get("S1837");

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _autoSwitchText;
    /// <summary>Switch-back checkbox, staged like every other modal
    /// field: check = stage, Simpan = apply to the profile,
    /// Batal = discard. Prefilled from the profile; default OFF.</summary>
    [ObservableProperty] private bool _switchBackOnAutoSwitch;
    // Pesan inline modal (pola BrowserManager/BrowserGroupManager):
    // ProfileEditWindow.xaml menampilkan ErrorText ini di atas footer.
    [ObservableProperty] private string _errorText = "";
    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasErrorText));
    [ObservableProperty] private RunningAppOption? _selectedRunningApp;
    public ObservableCollection<RunningAppOption> RunningApps { get; } = new();

    public string InfoText { get; }

    public ProfileEditViewModel(AppHost host, ProfileConfig profile)
    {
        _host = host;
        Profile = profile;
        _name = profile.Name;
        _autoSwitchText = string.Join(", ", profile.AutoSwitchExe ?? Enumerable.Empty<string>());
        _switchBackOnAutoSwitch = profile.SwitchBackOnAutoSwitch;
        int bindings = (profile.Pages ?? Enumerable.Empty<PageConfig>()).Where(p => p is not null).Sum(p => p.Bindings?.Count ?? 0);
        InfoText = Localizer.Format("S0834", profile.Pages?.Count ?? 0, bindings) +
                   (ReferenceEquals(host.Config.ActiveProfile, profile) ? Localizer.Get("S0835") : "");
        foreach (var app in ViewModels.RunningApps.List())
            RunningApps.Add(app);
    }

    [RelayCommand]
    private void AppendRunningApp()
    {
        if (SelectedRunningApp is null) return;
        var entries = AutoSwitchText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!entries.Contains(SelectedRunningApp.Exe, StringComparer.OrdinalIgnoreCase))
            entries.Add(SelectedRunningApp.Exe);
        AutoSwitchText = string.Join(", ", entries);
    }

    [RelayCommand]
    private void Save()
    {
        // Silent-save fix: dulu nama kosong diam-diam memakai nama lama
        // (tanpa info) sementara perubahan lain tetap tersimpan dan
        // modal tertutup - klik Simpan seolah tidak mengganti nama.
        // Kini nama kosong ditolak dengan pesan yang menyebut syaratnya
        // dan modal tetap terbuka; aturan tidak diubah (duplikat nama
        // profil memang tidak pernah ditolak di jalur ini).
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorText = Localizer.Get("S1567");
            return;
        }
        ErrorText = "";
        string oldName = Profile.Name;
        var oldAutoSwitch = (Profile.AutoSwitchExe ?? Enumerable.Empty<string>()).ToList();
        bool oldSwitchBack = Profile.SwitchBackOnAutoSwitch;
        Profile.Name = Name.Trim();
        Profile.SwitchBackOnAutoSwitch = SwitchBackOnAutoSwitch;
        Profile.AutoSwitchExe = AutoSwitchText
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        try
        {
            _host.Save();
        }
        catch (Exception ex)
        {
            // Pola §45/§46: gagal simpan = rollback memori (bukan nama
            // baru bertahan di sesi lalu kembali sendiri saat restart),
            // pesan tampil, modal tetap terbuka untuk coba lagi.
            Profile.Name = oldName;
            Profile.AutoSwitchExe = oldAutoSwitch;
            Profile.SwitchBackOnAutoSwitch = oldSwitchBack;
            ErrorText = Localizer.Get("S0937") + " " + ex.Message;
            return;
        }
        CloseOk?.Invoke();
    }

    [RelayCommand]
    private void Duplicate()
    {
        WantsDuplicate = true;
        CloseOk?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseCancel?.Invoke();
}

/// <summary>Page add/rename modal (UI v2.9): name, auto-mode trigger
/// apps, stats, and duplicate — the full Edit Mode modal, mirroring
/// ProfileEditWindow for a MODE (not a profile). Accepted is set by
/// the window code on Simpan; WantsDuplicate is set by the
/// DuplicateCommand and read by MainViewModel AFTER Invoke returns,
/// before the !Accepted check — the duplicate button closes the
/// modal WITHOUT saving, so Accepted stays false on that path.
/// No ErrorText row: under the Accepted/DialogResult contract the
/// modal closes before MainViewModel saves, so a VM error row would
/// have no writer (dead UI); the error surface stays the S1566
/// AppDialog (empty name, modal stays open) + the S0937 footer
/// (save failure), exactly as before.</summary>
public partial class PageEditViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _autoSwitchText = "";
    /// <summary>Switch-back checkbox, staged like every other modal
    /// field: check = stage, Simpan = apply (MainViewModel), Batal =
    /// discard. MainViewModel prefills it on edit; default OFF on
    /// add. No Save lives in this VM (Accepted/DialogResult
    /// contract), so no rollback lives here either.</summary>
    [ObservableProperty] private bool _switchBackOnAutoSwitch;
    [ObservableProperty] private RunningAppOption? _selectedRunningApp;
    public ObservableCollection<RunningAppOption> RunningApps { get; } = new();
    public bool IsNew { get; init; }
    public bool Accepted { get; set; }
    public bool WantsDuplicate { get; private set; }
    /// <summary>Duplicate button visible only on the edit path:
    /// computed !IsNew (IsNew never changes after construction, so
    /// a get-only property bound after the initializer is exact).</summary>
    public bool ShowDuplicate => !IsNew;
    /// <summary>Binding count of THIS mode only (set by MainViewModel
    /// from page.Bindings.Count on edit; 0 on add). S0996 is the
    /// existing "{0} cards" format — no new string needed; S0834 is
    /// profile-level (modes • bindings) and does not fit.</summary>
    public int BindingCount { get; init; }
    public string InfoText => Localizer.Format("S0996", BindingCount);
    public string Title => IsNew ? Localizer.Get("S0229") : Localizer.Get("S0836");

    /// <summary>Instant tile color, one swatch per ActionCatalog preset
    /// (same 8 colors as the card editor's picker — no second palette).
    /// Mirrors the editor's SwatchOption IsSelected pattern.</summary>
    public sealed partial class InstantColorOption : ObservableObject
    {
        public string Value { get; init; } = "";
        [ObservableProperty] private bool _isSelected;
    }

    public ObservableCollection<InstantColorOption> InstantColorOptions { get; } = new();
    /// <summary>Null = no selection: Simpan without touching the row
    /// repaints nothing. MainViewModel sets this to the uniform preset
    /// color on the edit path (opt-in preselect); the setter only
    /// moves the IsSelected flags, it never paints a card.</summary>
    [ObservableProperty] private string? _selectedInstantColor;
    /// <summary>Row enabled only for an existing mode that has cards:
    /// on the add path (IsNew, BindingCount 0) and on empty modes the
    /// row is hidden.</summary>
    public bool InstantColorEnabled => !IsNew && BindingCount > 0;

    partial void OnSelectedInstantColorChanged(string? value)
    {
        foreach (var o in InstantColorOptions)
            o.IsSelected = string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void PickInstantColor(string hex) => SelectedInstantColor = hex;

    public event Action? CloseDuplicate;

    public PageEditViewModel()
    {
        // Qualified exactly like ProfileEditViewModel: the bare name
        // would resolve to the RunningApps property, not the type.
        foreach (var app in ViewModels.RunningApps.List())
            RunningApps.Add(app);
        foreach (var hex in ActionCatalog.ColorPresets)
            InstantColorOptions.Add(new InstantColorOption { Value = hex });
    }

    [RelayCommand]
    private void AppendRunningApp()
    {
        if (SelectedRunningApp is null) return;
        var entries = AutoSwitchText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!entries.Contains(SelectedRunningApp.Exe, StringComparer.OrdinalIgnoreCase))
            entries.Add(SelectedRunningApp.Exe);
        AutoSwitchText = string.Join(", ", entries);
    }

    [RelayCommand]
    private void Duplicate()
    {
        WantsDuplicate = true;
        CloseDuplicate?.Invoke();
    }
}
