using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Keystream.App.ViewModels;
using Keystream.Core.Localization;

namespace Keystream.App.Views;

/// <summary>Modal "Pengaturan Mouse" (§107): pengaturan perekaman mouse
/// untuk sesi berjalan, dibuka dari tombol gir di modal Macro.
///
/// Buka dengan <see cref="Window.ShowDialog"/> dan baca
/// <see cref="Settings"/> hanya bila <see cref="Window.DialogResult"/> == true.
/// Ctor tidak pernah throw untuk pengaturan default yang valid; bekerja
/// pada salinan (record), pengaturan asal tidak diubah sampai Simpan
/// berhasil. Tidak ada persistensi di sini (session-scoped).</summary>
public partial class MouseSettingsWindow : Window
{
    private readonly MouseRecordSettings _original;
    private MouseRecordSettings? _savedSettings;
    private bool _suppressConfirm;

    /// <summary>Pengaturan hasil edit; bermakna hanya bila
    /// <see cref="Window.DialogResult"/> == true. Bila dialog dibatalkan,
    /// mengembalikan pengaturan asal yang tidak diubah.</summary>
    public MouseRecordSettings Settings => _savedSettings ?? _original;

    public MouseSettingsWindow(MouseRecordSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _original = settings;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        ApplySettings(settings);
        Loaded += (_, _) => ScopeBox.Focus();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Batal: TIDAK konfirmasi di sini - Close() memicu Window_Closing,
    // satu-satunya choke point, jadi konfirmasi hanya ditanya sekali
    // (pola MacroManagerWindow.CancelButton_Click, §97/§105).
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadThreshold(out int threshold))
        {
            ShowError(Localizer.Get("S1621"));
            ThresholdBox.Focus();
            ThresholdBox.SelectAll();
            return;
        }
        if (!TryReadDelay(out int delay))
        {
            ShowError(Localizer.Get("S1622"));
            DelayBox.Focus();
            DelayBox.SelectAll();
            return;
        }

        // Valid: bangun salinan hasil edit dari record asal (with),
        // tandai bersih, lalu tutup sebagai OK. DialogResult=true
        // memicu Window_Closing; _suppressConfirm mencegah konfirmasi
        // palsu atas nilai yang baru saja disimpan.
        _savedSettings = _original with
        {
            Verbatim = VerbatimBox.IsChecked == true,
            Scope = SelectedScope(),
            MoveThresholdPx = threshold,
            MinMoveDelayMs = delay,
            CoordinateMode = RelativeRadio.IsChecked == true
                ? MouseCoordinateMode.RelativeWindow
                : MouseCoordinateMode.Absolute,
            IgnoreSelfClicks = IgnoreSelfClicksBox.IsChecked == true,
        };
        ClearError();
        _suppressConfirm = true;
        DialogResult = true;
    }

    private bool TryReadThreshold(out int value) =>
        int.TryParse(ThresholdBox.Text.Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out value)
        && value >= MouseRecordSettings.ThresholdMin
        && value <= MouseRecordSettings.ThresholdMax;

    private bool TryReadDelay(out int value) =>
        int.TryParse(DelayBox.Text.Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out value)
        && value >= MouseRecordSettings.DelayMin
        && value <= MouseRecordSettings.DelayMax;

    private MouseScope SelectedScope()
    {
        if (ScopeBox.SelectedItem is ComboBoxItem item
            && Enum.TryParse<MouseScope>(item.Tag as string, out var scope))
            return scope;
        return _original.Scope;
    }

    private void ApplySettings(MouseRecordSettings settings)
    {
        foreach (var item in ScopeBox.Items)
        {
            if (item is ComboBoxItem combo
                && string.Equals(combo.Tag as string, settings.Scope.ToString(), StringComparison.Ordinal))
            {
                ScopeBox.SelectedItem = combo;
                break;
            }
        }
        if (ScopeBox.SelectedItem is null)
            ScopeBox.SelectedIndex = 0;
        ThresholdBox.Text = settings.MoveThresholdPx.ToString(CultureInfo.InvariantCulture);
        DelayBox.Text = settings.MinMoveDelayMs.ToString(CultureInfo.InvariantCulture);
        AbsoluteRadio.IsChecked = settings.CoordinateMode == MouseCoordinateMode.Absolute;
        RelativeRadio.IsChecked = settings.CoordinateMode == MouseCoordinateMode.RelativeWindow;
        IgnoreSelfClicksBox.IsChecked = settings.IgnoreSelfClicks;
        VerbatimBox.IsChecked = settings.Verbatim;
        RefreshVerbatimState();
        ClearError();
    }

    /// <summary>Mode Persis (§108): saat dicentang, cakupan/ambang/jeda
    /// dinonaktifkan (greyed) karena tidak berlaku - nilainya TETAP
    /// dipertahankan di kontrol dan tetap dibaca saat Simpan, jadi
    /// mematikan Persis mengembalikan nilai lama. Radio koordinat dan
    /// IgnoreSelfClicksBox selalu aktif. Label satuan px/ms ikut meredup
    /// (Opacity 0.5, sama seperti ComboBox nonaktif) karena TextBlock
    /// tidak punya visual nonaktif sendiri dan bukan anak TextBox -
    /// menonaktifkan TextBox saja tidak meredupkannya. Opacity TextBox
    /// sendiri TIDAK disentuh di sini: trigger IsEnabled=False di style
    /// global TextBox (App.xaml) yang mengaturnya, dan nilai lokal akan
    /// mengalahkan trigger itu sehingga kotak tetap redup saat Persis
    /// dimatikan. Dipanggil dari load dan dari
    /// handler Checked/Unchecked VerbatimBox.</summary>
    private void RefreshVerbatimState()
    {
        bool verbatim = VerbatimBox.IsChecked == true;
        ScopeBox.IsEnabled = !verbatim;
        ThresholdBox.IsEnabled = !verbatim;
        DelayBox.IsEnabled = !verbatim;
        ThresholdUnitLabel.Opacity = verbatim ? 0.5 : 1.0;
        DelayUnitLabel.Opacity = verbatim ? 0.5 : 1.0;
    }

    private void VerbatimBox_Checked(object sender, RoutedEventArgs e) => RefreshVerbatimState();

    private void VerbatimBox_Unchecked(object sender, RoutedEventArgs e) => RefreshVerbatimState();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Choke point tutup tunggal: Batal, X header, dan Alt+F4
    /// semuanya lewat sini. Ada perubahan (nilai kontrol != record asal,
    /// dibandingkan per-field agar teks angka yang belum valid pun tetap
    /// terhitung sebagai perubahan) → konfirmasi S0906 dulu; menjawab
    /// Tidak = tetap terbuka. Tanpa perubahan → tutup diam. Pola yang
    /// dicerminkan: ObsConfigWindow.Window_Closing + ConfirmDiscard
    /// (AppDialog YesNo, judul "Keystream").</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressConfirm) return;
        if (HasUnsavedChanges() && !ConfirmDiscard())
            e.Cancel = true;
    }

    private bool HasUnsavedChanges()
    {
        if (SelectedScope() != _original.Scope) return true;
        if (!string.Equals(ThresholdBox.Text.Trim(),
                _original.MoveThresholdPx.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)) return true;
        if (!string.Equals(DelayBox.Text.Trim(),
                _original.MinMoveDelayMs.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal)) return true;
        var coordinate = RelativeRadio.IsChecked == true
            ? MouseCoordinateMode.RelativeWindow
            : MouseCoordinateMode.Absolute;
        if (coordinate != _original.CoordinateMode) return true;
        if (IgnoreSelfClicksBox.IsChecked != _original.IgnoreSelfClicks) return true;
        return VerbatimBox.IsChecked != _original.Verbatim;
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), "Keystream",
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;
}
