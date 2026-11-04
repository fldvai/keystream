using System.ComponentModel;
using System.Windows;
using Keystream.App.ViewModels;
using Keystream.Core.Localization;

namespace Keystream.App.Views;

public partial class ProfileEditWindow : Window
{
    private readonly ProfileEditViewModel _viewModel;
    // Snapshot staged-vs-tersimpan (pola MouseSettingsWindow, lihat
    // PageEditWindow): VM mengisi field dari Profile di ctor-nya,
    // sebelum ctor window ini - snapshot di sini melihat prefill persis.
    private readonly string _originalName;
    private readonly string[] _originalAutoSwitchExe;
    private readonly bool _originalSwitchBack;
    private bool _suppressConfirm;

    public ProfileEditWindow(ProfileEditViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        _originalName = viewModel.Name;
        _originalAutoSwitchExe = ParseAutoSwitchExe(viewModel.AutoSwitchText);
        _originalSwitchBack = viewModel.SwitchBackOnAutoSwitch;
        // Simpan (CloseOk) sudah menerapkan staged ke Profile, dan
        // Duplikat (CloseOk yang sama) memang menutup tanpa menyimpan
        // atas perintah tombolnya sendiri - keduanya tanpa S0906.
        // Batal (CloseCancel) lewat Close() agar Closing yang
        // membatalkan benar-benar menahan jendela tetap terbuka.
        viewModel.CloseOk += () => { _suppressConfirm = true; DialogResult = true; };
        viewModel.CloseCancel += () => Close();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Choke point tutup tunggal (pola ObsConfigWindow):
    /// Batal, X header, dan Alt+F4 semuanya lewat Closing ini -
    /// Batal/X tidak mengonfirmasi sendiri. Ada perubahan staged yang
    /// belum disimpan -> konfirmasi S0906; menjawab Tidak = tetap
    /// terbuka. Tanpa perubahan -> tutup diam. Simpan/Duplikat lewat
    /// CloseOk menandai _suppressConfirm sehingga tidak ada konfirmasi
    /// palsu; perilaku Simpan tidak diubah.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressConfirm) return;
        if (HasUnsavedChanges() && !ConfirmDiscard())
            e.Cancel = true;
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), "Keystream",
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;

    private bool HasUnsavedChanges()
    {
        if (!string.Equals((_viewModel.Name ?? "").Trim(), (_originalName ?? "").Trim(), StringComparison.Ordinal)) return true;
        if (!ParseAutoSwitchExe(_viewModel.AutoSwitchText).SequenceEqual(_originalAutoSwitchExe, StringComparer.OrdinalIgnoreCase)) return true;
        return _viewModel.SwitchBackOnAutoSwitch != _originalSwitchBack;
    }

    /// <summary>Parse yang sama dengan ProfileEditViewModel.Save:
    /// bandingkan daftar hasil parse, bukan teks mentah, agar spasi/koma
    /// ekstra yang tidak mengubah hasil tidak dihitung perubahan.</summary>
    private static string[] ParseAutoSwitchExe(string text) =>
        (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
