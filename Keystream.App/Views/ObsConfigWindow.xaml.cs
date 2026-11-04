using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Keystream.App.ViewModels;
using Keystream.Core.Localization;

namespace Keystream.App.Views;

public partial class ObsConfigWindow : Window
{
    private readonly ObsConfigViewModel _viewModel;

    public ObsConfigWindow(ObsConfigViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        DataContext = viewModel;
        _viewModel = viewModel;
        viewModel.RequestClose += () => Close();
        // §173c: lepas langganan HealthChanged saat modal ditutup
        // (pola SettingsWindow.xaml.cs) - VM tidak menerima
        // RefreshStatus lagi sesudah jendela hilang.
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Audit Cancel: ×, Batal, dan Alt+F4 semuanya lewat
    /// Closing ini (Batal tidak mengonfirmasi sendiri - konfirmasi
    /// di sini sekali, pola MacroManager §97). Tanpa handler,
    /// host/password yang diubah tapi belum disimpan dibuang
    /// diam-diam. Kini: bila ada perubahan yang belum disimpan,
    /// konfirmasi S0906; menjawab Tidak = tetap terbuka. Tanpa
    /// perubahan - termasuk sesudah Save/Connect yang berhasil -
    /// tutup diam, benar.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.HasUnsavedChanges() && !ConfirmDiscard())
            e.Cancel = true;
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), "Keystream",
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;
}
