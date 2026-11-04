using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Keystream.Setup.Localization;

namespace Keystream.Setup;

/// <summary>Setup-owned message dialog: the Setup equivalent of the
/// app's AppDialog pattern. Raw MessageBox is forbidden in Setup the
/// same as in the app (it renders in the OS theme); this window uses
/// the Setup dark palette instead. Setup is a standalone assembly
/// with no reference to Keystream.App, so it cannot reuse AppDialog.
/// </summary>
public partial class MessageDialog : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MessageDialog(string title, string message)
    {
        InitializeComponent();
        btnOk.Content = SetupLocalizer.Get("W0034");
        Title = title;
        txtTitle.Text = title;
        txtMessage.Text = message;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        }
        catch { }
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public static void Show(Window? owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message);
        if (owner != null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        dialog.ShowDialog();
    }
}
