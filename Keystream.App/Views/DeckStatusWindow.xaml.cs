using System.Windows;
using System.Windows.Input;
using Keystream.App.Host;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

/// <summary>§149 C2/C3: modal status "Keyboard Deck" - dibuka dari pill
/// Deck di header MainWindow. DataContext-nya adalah SATU instance
/// <see cref="SettingsViewModel"/> yang dibuat MainWindow dari
/// <see cref="AppHost"/> yang sama dengan SettingsWindow (bukan ViewModel
/// kedua dengan state salinan): semua nilai (DeckText, status service,
/// driver) dibaca dari AppHost yang sama dan diperbarui lewat
/// HealthChanged yang sama, jadi nilainya live. Tombol-tombol mengikat
/// instance command yang sama dengan Settings (IdentifyDeckCommand,
/// ToggleServiceCommand, ToggleDeckDetailCommand) - tidak ada logika
/// service/identifikasi yang diduplikasi di sini. Satu-satunya wiring
/// code-behind adalah RequestOpenWizard (pola SettingsWindow verbatim:
/// wizard + OnIdentificationCompleted), karena tanpa itu
/// IdentifyDeckCommand tidak akan membuka apa pun dari modal ini.
/// Modal tidak men-stage/menyimpan apa pun: tidak ada SaveAll, tidak
/// ada ConfirmPrompt/RequestShowMessage/RequestClose, dan tidak ada
/// konfirmasi tutup. Closed me-Dispose ViewModel (pola SettingsWindow
/// §45) agar langganan HealthChanged tidak bocor.</summary>
public partial class DeckStatusWindow : Window
{
    private readonly AppHost _host;
    private readonly SettingsViewModel _viewModel;

    public DeckStatusWindow(AppHost host, SettingsViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        DataContext = viewModel;
        _host = host;
        _viewModel = viewModel;
        viewModel.RequestOpenWizard += () =>
        {
            var wizard = new WizardWindow(host) { Owner = this };
            wizard.ShowDialog();
            viewModel.OnIdentificationCompleted();
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
