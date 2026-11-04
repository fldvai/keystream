using System.Windows;
using Keystream.Core.Localization;

namespace Keystream.App.Views;

public enum AppDialogButtons { Ok, YesNo, YesNoCancel, SaveDiscardCancel }
public enum AppDialogIcon { Info, Warning, Error, Question }
public enum AppDialogResult { None, Ok, Yes, No, Cancel, Save, Discard }

/// <summary>
/// THE app's dialog (v2.9.35) - the mandatory popup pattern from
/// WORKFLOW.md: every notification/confirmation in the app goes through
/// <see cref="AppDialog"/> (or a styled app window on the same pattern,
/// like WaitingKeyWindow). Raw MessageBox is forbidden: it renders in
/// the OS theme and breaks the app's visual system.
/// </summary>
public partial class DialogWindow : Window
{
    private AppDialogResult _result = AppDialogResult.None;

    /// <summary>§106: true bila dialog ini varian Simpan/Buang/Batal -
    /// Yes = Simpan, No = Buang, Cancel = tetap. Varian lain tidak
    /// berubah (Yes/No/Cancel seperti sebelumnya).</summary>
    private bool _saveDiscard;

    /// <summary>§171 Task 6: three-choice variant with
    /// caller-supplied labels (e.g. Ganti/Gabung/Lewati). Chains the
    /// YesNoCancel ctor, then overrides all three button labels; the
    /// result mapping stays Yes/No/Cancel (X/close = Cancel), and the
    /// caller translates it. No other dialog behavior changes.</summary>
    public DialogWindow(string message, string title,
        string yesLabel, string noLabel, string cancelLabel, AppDialogIcon icon)
        : this(message, title, AppDialogButtons.YesNoCancel, icon)
    {
        YesButton.Content = yesLabel;
        NoButton.Content = noLabel;
        CancelButton.Content = cancelLabel;
    }

    public DialogWindow(string message, string title, AppDialogButtons buttons, AppDialogIcon icon)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        Title = title;
        MessageText.Text = message;
        (KindIcon.Icon, KindIcon.Foreground) = icon switch
        {
            AppDialogIcon.Warning => ("alert", new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0xC6, 0x5C))),
            AppDialogIcon.Error => ("alert", new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71))),
            AppDialogIcon.Question => ("info", new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x86, 0xE0))),
            _ => ("info", new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x86, 0xE0))),
        };
        switch (buttons)
        {
            case AppDialogButtons.Ok:
                YesButton.Content = Localizer.Get("S0335");
                NoButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                break;
            case AppDialogButtons.YesNo:
                CancelButton.Visibility = Visibility.Collapsed;
                break;
            case AppDialogButtons.SaveDiscardCancel:
                // §106: urutan baris XAML sudah tepat (Batal | Buang |
                // Simpan); hanya label No/Yes yang diganti. CancelButton
                // tetap S0010, YesButton tetap gaya DialogPrimary.
                _saveDiscard = true;
                YesButton.Content = Localizer.Get("S0123");
                NoButton.Content = Localizer.Get("S1570");
                break;
        }
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
        => Finish(_saveDiscard ? AppDialogResult.Save
            : YesButton.Content as string == Localizer.Get("S0335") ? AppDialogResult.Ok : AppDialogResult.Yes);

    private void No_Click(object sender, RoutedEventArgs e)
        => Finish(_saveDiscard ? AppDialogResult.Discard : AppDialogResult.No);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(AppDialogResult.Cancel);

    private void Finish(AppDialogResult result)
    {
        _result = result;
        Close();
    }

    public AppDialogResult ShowAndWait()
    {
        ShowDialog();
        return _result;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Cancel_Click(sender, e);
}

/// <summary>Entry point mirroring the old MessageBox call shape.</summary>
public static class AppDialog
{
    public static AppDialogResult Show(Window? owner, string message, string title = "Keystream",
        AppDialogButtons buttons = AppDialogButtons.Ok, AppDialogIcon icon = AppDialogIcon.Info)
    {
        var window = new DialogWindow(message, title, buttons, icon);
        if (owner is not null) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowAndWait();
    }

    /// <summary>§171 Task 6: the same styled dialog with three
    /// caller-supplied labels. Returns Yes for the first label, No
    /// for the second, Cancel for the third (and for X/close).</summary>
    public static AppDialogResult ShowThreeChoices(Window? owner, string message, string title,
        string yesLabel, string noLabel, string cancelLabel,
        AppDialogIcon icon = AppDialogIcon.Question)
    {
        var window = new DialogWindow(message, title, yesLabel, noLabel, cancelLabel, icon);
        if (owner is not null) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowAndWait();
    }

    /// <summary>Two-choice styled dialog with caller-supplied labels
    /// (e.g. "Perbarui Sekarang" / "Nanti"). Returns Yes for the primary
    /// choice, No for the secondary choice.</summary>
    public static AppDialogResult ShowTwoChoices(Window? owner, string message, string title,
        string yesLabel, string noLabel,
        AppDialogIcon icon = AppDialogIcon.Question)
    {
        var window = new DialogWindow(message, title, AppDialogButtons.YesNo, icon);
        window.YesButton.Content = yesLabel;
        window.NoButton.Content = noLabel;
        if (owner is not null) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowAndWait();
    }

}
