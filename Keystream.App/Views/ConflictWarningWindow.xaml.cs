using System.Windows;

namespace Keystream.App.Views;

/// <summary>The editor's save-time conflict popup (UI v2.8): shown when a
/// binding is saved for a key the other scope already uses. The user can
/// proceed anyway, cancel, and/or silence future popups (the ⚠ tile
/// badge always remains).</summary>
public partial class ConflictWarningWindow : Window
{
    public bool DontShowAgain => DontShowAgainBox.IsChecked == true;

    public ConflictWarningWindow(string message)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        MessageTextBlock.Text = message;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);
}
