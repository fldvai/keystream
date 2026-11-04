using System.Windows;

namespace Keystream.App.Views;

public partial class ResetConfirmWindow : Window
{
    /// <summary>True only when OK was pressed with the exact
    /// confirmation text. Read by the caller after ShowDialog
    /// returns true, the same Accepted + DialogResult pattern as
    /// RenameBindingWindow.</summary>
    public bool Confirmed { get; private set; }

    public ResetConfirmWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        Loaded += (_, _) => ConfirmBox.Focus();
    }

    /// <summary>Live gate: OK is enabled ONLY while the input is
    /// exactly "DELETE" - ordinal comparison, no trim, no
    /// case/spacing tolerance. Any edit (including paste or the
    /// template's clear button) re-evaluates immediately.</summary>
    private void ConfirmBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => OkButton.IsEnabled = ResetConfirmLogic.IsConfirmed(ConfirmBox.Text);

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        // Belt-and-braces: the button is disabled unless the text
        // matches, but never trust the enabled state alone.
        if (!ResetConfirmLogic.IsConfirmed(ConfirmBox.Text)) return;
        Confirmed = true;
        DialogResult = true;
    }

    /// <summary>Esc closes without action. A PreviewKeyDown handler
    /// (like WaitingKeyWindow) wins over the global TextBox Esc-revert,
    /// so the first Esc already closes instead of merely reverting
    /// the typed text.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
