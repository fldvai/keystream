using System.Windows;
using Keystream.Core.Localization;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

public partial class RenameBindingWindow : Window
{
    private readonly BindingRenameViewModel _viewModel;

    public RenameBindingWindow(BindingRenameViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Pola §104/PageEditWindow: nama kosong ditolak DI SINI lewat
        // AppDialog, modal tetap terbuka dan syaratnya disebut -
        // klik Simpan tidak boleh berakhir diam-diam.
        if (string.IsNullOrWhiteSpace(_viewModel.Name))
        {
            AppDialog.Show(this, Localizer.Get("S1644"), "Keystream",
                AppDialogButtons.Ok, AppDialogIcon.Warning);
            return;
        }
        _viewModel.Accepted = true;
        DialogResult = true;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
