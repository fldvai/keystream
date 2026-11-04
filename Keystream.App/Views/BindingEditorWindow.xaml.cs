using System.ComponentModel;
using System.Windows;
using Keystream.Core.Localization;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

public partial class BindingEditorWindow : Window
{
    public bool Changed { get; private set; }

    public BindingEditorWindow(BindingEditorViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        DataContext = viewModel;
        viewModel.ConflictPrompt = message =>
        {
            var popup = new ConflictWarningWindow(message) { Owner = this };
            bool proceed = popup.ShowDialog() == true;
            return (proceed, popup.DontShowAgain);
        };
        viewModel.SwapPrompt = message =>
            AppDialog.Show(this, message, Localizer.Get("S0007"),
                AppDialogButtons.YesNo, AppDialogIcon.Question)
            == AppDialogResult.Yes;
        viewModel.DiscardPrompt = message =>
            AppDialog.Show(this, message, "Keystream",
                AppDialogButtons.YesNo, AppDialogIcon.Question)
            == AppDialogResult.Yes;
        viewModel.RequestClose += changed =>
        {
            Changed = changed;
            Close();
        };
        viewModel.RequestManageBrowserGroups += () =>
        {
            var manager = new BrowserGroupManagerWindow(new BrowserGroupManagerViewModel(viewModel.Host)) { Owner = this };
            manager.ShowDialog();
            viewModel.RefreshBrowserGroups();
        };
        viewModel.RequestManageMacros += () =>
        {
            var manager = new MacroManagerWindow(viewModel.Host) { Owner = this };
            manager.ShowDialog();
            viewModel.RefreshMacros();
        };
        // §159: a combination step's browse/Pindai request arrives
        // here with the shared dialog spec + an apply callback that
        // writes into THAT step only. The window owns all OS dialogs.
        viewModel.RequestStepFileDialog += (spec, apply) => ShowStepFileDialog(spec, apply);
        Closed += (_, _) => viewModel.OnWindowClosed();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && viewModel.IsListeningForKey)
            {
                viewModel.CancelReassignCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private BindingEditorViewModel Vm => (BindingEditorViewModel)DataContext;

    // Satu-satunya choke point tutup: Batal (footer + X header) lewat
    // CancelCommand yang sudah konfirmasi sendiri dan menandai
    // DiscardConfirmed, jadi di sini tidak ditanya lagi. Jalur lain
    // (Alt+F4, tutup dari taskbar) sampai ke sini tanpa konfirmasi:
    // ada perubahan -> tanya sekali (S0906, string yang sama dengan
    // macro); "Tidak" -> tetap terbuka, VM utuh; "Ya"/tanpa
    // perubahan -> pulihkan snapshot defensif (§45) lalu tutup.
    // Tanpa handler ini, Alt+F4 membuang edit diam-diam dan melewati
    // RestorePageSnapshot. WindowStyle=None: tidak ada X OS, tapi
    // Alt+F4/taskbar tetap memicu Closing.
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (Vm.DiscardConfirmed) return;
        if (Vm.HasUnsavedChanges() && !ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        Vm.RestoreDiscardedChanges();
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), "Keystream",
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;

    private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
    {
        Vm.IsAppDrawerOpen = false;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get(EditorDialogSpecs.LauncherExe.TitleKey),
            Filter = Localizer.Get(EditorDialogSpecs.LauncherExe.FilterKey),
        };
        if (dialog.ShowDialog() == true) Vm.LauncherTarget = dialog.FileName;
    }

    private void BrowseManualExe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get(EditorDialogSpecs.ManualBrowserExe.TitleKey),
            Filter = Localizer.Get(EditorDialogSpecs.ManualBrowserExe.FilterKey),
        };
        if (dialog.ShowDialog() == true) Vm.ManualBrowserExe = dialog.FileName;
    }

    private void BrowseManualFolder_Click(object sender, RoutedEventArgs e) => BrowseManualFolder();

    private void BrowseManualFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Localizer.Get(EditorDialogSpecs.ManualProfileFolder.DescriptionKey),
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) Vm.ManualProfilePath = dialog.SelectedPath;
    }

    /// <summary>§159: opens the shared-spec dialog for a combination
    /// step and applies the chosen path through the owner's callback
    /// (which captures the requesting step). Same dialog kinds,
    /// titles, and filters as the main-editor handlers above - the
    /// spec is the single home for both.</summary>
    private void ShowStepFileDialog(EditorDialogSpec spec, Action<string> apply)
    {
        switch (spec.Kind)
        {
            case EditorDialogKind.File:
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Localizer.Get(spec.TitleKey),
                    Filter = Localizer.Get(spec.FilterKey),
                };
                if (dialog.ShowDialog() == true) apply(dialog.FileName);
                break;
            }
            case EditorDialogKind.Folder:
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = Localizer.Get(spec.DescriptionKey),
                };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) apply(dialog.SelectedPath);
                break;
            }
        }
    }

    private void PindaiBrowser_Click(object sender, RoutedEventArgs e)
    {
        // Satu tombol, dua fungsi menurut mode: CUSTOM tidak memakai daftar
        // terdeteksi, jadi tombol ini beralih menjadi pemilih folder profil.
        if (Vm.IsBrowserCustom) BrowseManualFolder();
        else Vm.RefreshBrowsers();
    }

    private void AppDrawerItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is Services.AppInfoItem app)
        {
            Vm.SelectAppDrawerItemCommand.Execute(app);
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get(EditorDialogSpecs.IconImage.TitleKey),
            Filter = Localizer.Get(EditorDialogSpecs.IconImage.FilterKey),
        };
        if (dialog.ShowDialog() == true) ImportIconFile(dialog.FileName);
    }

    private void UploadDropZone_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void UploadDropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
            ImportIconFile(files[0]);
        e.Handled = true;
    }

    private void ImportIconFile(string path)
    {
        var (ok, message) = Vm.ImportIconFile(path);
        if (!ok)
            AppDialog.Show(this, message, Localizer.Get("S0877"),
                AppDialogButtons.Ok, AppDialogIcon.Error);
    }

    private void BrowseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get(EditorDialogSpecs.SoundFile.TitleKey),
            Filter = Localizer.Get(EditorDialogSpecs.SoundFile.FilterKey),
        };
        if (dialog.ShowDialog() == true) Vm.SoundFile = dialog.FileName;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Vm.CancelCommand.Execute(null);
}
