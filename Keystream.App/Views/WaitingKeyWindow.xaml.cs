using System.Windows;

namespace Keystream.App.Views;

/// <summary>
/// The press-to-map waiting popup (v2.9.33): shown while the app waits
/// for one deck key press. Purely visual - the capture itself lives in
/// MainViewModel; this window closing without a press means "cancelled".
/// </summary>
public partial class WaitingKeyWindow : Window
{
    public WaitingKeyWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
    }

    public void SetContent(string title, string promptTitle, string promptDesc)
    {
        Title = title;
        TitleBlock.Text = title;
        PromptTitleBlock.Text = promptTitle;
        PromptDescBlock.Text = promptDesc;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) Close();
        base.OnPreviewKeyDown(e);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
