using System.ComponentModel;
using System.Windows;
using Keystream.Core.Localization;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

public partial class PageEditWindow : Window
{
    private readonly PageEditViewModel _viewModel;
    // Snapshot staged-vs-tersimpan (pola MouseSettingsWindow: _original
    // + HasUnsavedChanges di code-behind; VM tidak punya snapshot karena
    // MainViewModel mengisi field SESUDAH ctor VM, sebelum ctor window
    // ini - snapshot di sini melihat nilai prefill persis).
    private readonly string _originalName;
    private readonly string[] _originalAutoSwitchExe;
    private readonly bool _originalSwitchBack;
    private readonly string? _originalInstantColor;
    private bool _suppressConfirm;

    public PageEditWindow(PageEditViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        _originalName = viewModel.Name;
        _originalAutoSwitchExe = ParseAutoSwitchExe(viewModel.AutoSwitchText);
        _originalSwitchBack = viewModel.SwitchBackOnAutoSwitch;
        _originalInstantColor = viewModel.SelectedInstantColor;
        // Duplicate path: the VM sets WantsDuplicate, then closes
        // WITHOUT saving — DialogResult=false (like Batal), NOT true:
        // DialogResult=true stays reserved for Simpan (Accepted).
        // MainViewModel reads WantsDuplicate after Invoke returns,
        // before its !Accepted check, so the flag survives the
        // false result. Mirrors ProfileEditWindow's CloseOk/CloseCancel
        // event wiring, with its own event (no shared contract).
        // Duplikat menutup TANPA menyimpan (Accepted tetap false) dan
        // tombol itu sendiri konfirmasinya (MainViewModel: tanpa
        // prompt) - tandai bersih agar Closing tidak menanyakan S0906
        // atas edit staged yang memang dibuang di jalur ini.
        viewModel.CloseDuplicate += () => { _suppressConfirm = true; DialogResult = false; };
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Silent-save fix: dulu Simpan selalu menutup modal tanpa syarat,
        // lalu pemanggil (MainViewModel AddPage/EditPage) membuang nama
        // kosong secara diam-diam - klik Simpan seolah tidak terjadi
        // apa-apa. Kini nama kosong ditolak DI SINI, modal tetap terbuka
        // dan syaratnya disebut lewat AppDialog (pola popup aplikasi);
        // aturan nama yang valid tidak diubah (hanya kosong yang ditolak,
        // duplikat memang tidak pernah ditolak di jalur ini).
        if (string.IsNullOrWhiteSpace(_viewModel.Name))
        {
            AppDialog.Show(this, Localizer.Get("S1566"), "Keystream",
                AppDialogButtons.Ok, AppDialogIcon.Warning);
            return;
        }
        // Simpan diterapkan oleh MainViewModel dari Accepted - tandai
        // bersih agar Closing tidak menanyakan S0906 atas nilai yang
        // baru saja disimpan.
        _suppressConfirm = true;
        _viewModel.Accepted = true;
        DialogResult = true;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Choke point tutup tunggal (pola ObsConfigWindow):
    /// Batal (IsCancel), X header, dan Alt+F4 semuanya lewat Closing
    /// ini - Batal/X tidak mengonfirmasi sendiri. Ada perubahan staged
    /// yang belum disimpan -> konfirmasi S0906; menjawab Tidak = tetap
    /// terbuka. Tanpa perubahan -> tutup diam. Simpan/Duplikat menandai
    /// _suppressConfirm sehingga tidak ada konfirmasi palsu; perilaku
    /// Simpan tidak diubah.</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_suppressConfirm) return;
        if (HasUnsavedChanges() && !ConfirmDiscard())
            e.Cancel = true;
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), "Keystream",
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;

    private bool HasUnsavedChanges()
    {
        if (!string.Equals((_viewModel.Name ?? "").Trim(), (_originalName ?? "").Trim(), StringComparison.Ordinal)) return true;
        if (!ParseAutoSwitchExe(_viewModel.AutoSwitchText).SequenceEqual(_originalAutoSwitchExe, StringComparer.OrdinalIgnoreCase)) return true;
        if (_viewModel.SwitchBackOnAutoSwitch != _originalSwitchBack) return true;
        return !string.Equals(_viewModel.SelectedInstantColor, _originalInstantColor, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parse yang sama dengan MainViewModel.ParseAutoSwitchExe
    /// (dipakai Simpan): bandingkan daftar hasil parse, bukan teks mentah,
    /// agar spasi/koma ekstra yang tidak mengubah hasil tidak dihitung
    /// perubahan.</summary>
    private static string[] ParseAutoSwitchExe(string text) =>
        (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
