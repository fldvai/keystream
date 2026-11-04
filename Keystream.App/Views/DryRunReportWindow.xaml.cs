using System.Windows;
using System.Windows.Input;

namespace Keystream.App.Views;

public partial class DryRunReportWindow : Window
{
    public DryRunReportWindow(string report)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        ReportText.Text = report;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
