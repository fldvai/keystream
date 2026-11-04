using System.ComponentModel;
using System.Windows;
using Keystream.Core.Localization;
using System.Windows.Input;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

public partial class BrowserManagerWindow : Window
{
    public BrowserManagerWindow(BrowserManagerViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
        Closing += Window_Closing;
    }

    /// <summary>Discard-diam fix: dulu Batal/× membuang daftar kerja
    /// (tambah/hapus/pindai yang belum di-Save) tanpa konfirmasi apa
    /// pun, padahal Save di sini hanya menyalin ke daftar bersama -
    /// persistensi sebenarnya ada di Simpan grup. Kini: ada perubahan
    /// belum disimpan → konfirmasi AppDialog S0906 (pola yang sama
    /// dengan ConfirmDiscard macro, §97); tidak ada perubahan → tutup
    /// diam adalah benar, tanpa konfirmasi. Save menandai
    /// SuppressDiscardConfirm sehingga tidak ada konfirmasi palsu.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not BrowserManagerViewModel vm) return;
        if (vm.SuppressDiscardConfirm || !vm.HasUnsavedChanges) return;
        if (AppDialog.Show(this, Localizer.Get("S0906"), "Keystream",
                AppDialogButtons.YesNo, AppDialogIcon.Question) != AppDialogResult.Yes)
            e.Cancel = true;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void BrowseCustomExe_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserManagerViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get("S0873"),
            Filter = Localizer.Get("S0872"),
        };
        if (dialog.ShowDialog() == true) vm.CustomExe = dialog.FileName;
    }

    private void BrowseCustomUserData_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserManagerViewModel vm) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Localizer.Get("S0880"),
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) vm.CustomUserDataDir = dialog.SelectedPath;
    }

    private void ScanUserData_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserManagerViewModel vm) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Localizer.Get("S0881"),
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) vm.StartUserDataScan(dialog.SelectedPath);
    }

    private void BrowseScanExe_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserManagerViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get("S0873"),
            Filter = Localizer.Get("S0872"),
        };
        if (dialog.ShowDialog() == true) vm.ScanExe = dialog.FileName;
    }
}
