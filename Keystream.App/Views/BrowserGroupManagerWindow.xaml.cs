using System.ComponentModel;
using System.Windows;
using Keystream.Core.Localization;
using System.Windows.Input;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

public partial class BrowserGroupManagerWindow : Window
{
    public BrowserGroupManagerWindow(BrowserGroupManagerViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        DataContext = viewModel;
        viewModel.ConfirmPrompt = message =>
            AppDialog.Show(this, message, "Keystream",
                AppDialogButtons.YesNo, AppDialogIcon.Warning)
            == AppDialogResult.Yes;
        viewModel.RequestShowMessage += msg =>
            AppDialog.Show(this, msg, "Keystream",
                AppDialogButtons.Ok, AppDialogIcon.Warning);
        // §140: tanpa grup terpilih, buka langsung di tab Jendela
        // Terlacak (tab global); bila seleksi hilang belakangan
        // (hapus grup terakhir), handler PropertyChanged di bawah
        // memindahkan seleksi tab ke sana.
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        if (viewModel.HasNoSelectedGroup)
            GroupTabControl.SelectedIndex = 2;
        RefreshTrackedList();
        viewModel.RequestClose += () => Close();
        Closing += Window_Closing;
        viewModel.RequestShowDryRunReport += report =>
        {
            var win = new DryRunReportWindow(report) { Owner = this };
            win.ShowDialog();
        };
    }

    /// <summary>Discard-diam fix: dulu Batal/× membuang salinan dalam
    /// (grup + entri + daftar browser kustom) tanpa konfirmasi apa pun -
    /// VM sendiri mendokumentasikan "Cancel discards everything". Kini:
    /// ada perubahan belum disimpan → konfirmasi AppDialog S0906 (string
    /// yang sama dengan ConfirmDiscard macro, §97); tidak ada perubahan
    /// → tutup diam adalah benar, tanpa konfirmasi. Sengaja tidak memakai
    /// ulang ConfirmPrompt VM (itu untuk konfirmasi hapus grup) dan tidak
    /// mengonfirmasi di CancelCommand - Close() memicu Closing sekali,
    /// konfirmasi ganda dihindari seperti CancelButton macro. Validasi
    /// Save + rollback S0937 (§104) tidak disentuh.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not BrowserGroupManagerViewModel vm) return;
        if (vm.SuppressDiscardConfirm || !vm.HasUnsavedChanges) return;
        if (AppDialog.Show(this, Localizer.Get("S0906"), "Keystream",
                AppDialogButtons.YesNo, AppDialogIcon.Question) != AppDialogResult.Yes)
            e.Cancel = true;
    }

    /// <summary>§140: daftar terlacak bersifat global - refresh saat
    /// jendela dibuka (ctor) dan saat tab Jendela Terlacak dipilih;
    /// refresh sesudah tiap aksi menjadi urusan command VM sendiri.
    /// Memanggil command kontrak RefreshTrackedWindowsCommand (bukan
    /// method VM) agar permukaan yang dipakai XAML dan code-behind
    /// sama; tanpa timer/polling latar belakang.</summary>
    private void RefreshTrackedList()
    {
        if (DataContext is BrowserGroupManagerViewModel vm)
            vm.RefreshTrackedWindowsCommand.Execute(null);
    }

    private void GroupTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TrackedWindowsTab is not { IsSelected: true }) return;
        RefreshTrackedList();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserGroupManagerViewModel.HasNoSelectedGroup)) return;
        if (DataContext is not BrowserGroupManagerViewModel vm || !vm.HasNoSelectedGroup) return;
        GroupTabControl.SelectedIndex = 2;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Buka jendela Kelola Browser (kustom + pindai) sebagai modal
    /// di atas jendela grup. Jendela itu bekerja pada daftar yang sama
    /// dengan VM ini; apa pun hasilnya, dropdown browser di sini
    /// di-refresh agar browser yang baru ditambah/dihapus langsung
    /// terlihat di form tambah entri.</summary>
    private void ManageBrowsers_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserGroupManagerViewModel vm) return;
        var win = new BrowserManagerWindow(new BrowserManagerViewModel(vm.SharedCustoms)) { Owner = this };
        win.ShowDialog();
        vm.RefreshBrowsersAfterCustomChange();
    }

    private void BrowseManagerManualExe_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserGroupManagerViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get("S0873"),
            Filter = Localizer.Get("S0872"),
        };
        if (dialog.ShowDialog() == true) vm.ManagerManualExe = dialog.FileName;
    }

    private void BrowseManagerManualFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BrowserGroupManagerViewModel vm) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Localizer.Get("S0874"),
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) vm.ManagerManualProfilePath = dialog.SelectedPath;
    }
}
