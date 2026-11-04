using System;
using Keystream.Core.Localization;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Keystream.App.Host;
using Keystream.Core.Config;
using Keystream.Core.Devices;
using Keystream.Core.Input;
using Keystream.Windows.Interception;

namespace Keystream.App.Views;

/// <summary>Modal pilih keyboard deck (dibuka dari klik nama keyboard di
/// Settings). Daftar diisi dari enumerasi Interception, BUKAN dari
/// wizard tekan-tombol - jadi bisa dipakai saat wizard gagal membedakan
/// atau saat user sekadar ingin mengganti deck tanpa identifikasi ulang.
/// Identitas yang disimpan tetap ID perangkat (exact-match, seperti
/// wizard); nomor perangkat hanya konteks tampilan dan tidak disimpan.
/// Error handling: semua kegagalan tampil sebagai teks di modal, tidak
/// pernah crash, tidak pernah menutup modal di tengah jalan - kecuali
/// Hubungkan yang sukses (modal tutup, Settings refresh lewat caller).
/// Wizard tetap tersedia dari tombolnya untuk kasus ID identik.</summary>
public partial class DeckDevicePickerWindow : Window
{
    private readonly AppHost _host;

    /// <summary>True bila Hubungkan sukses - caller me-refresh deck.</summary>
    public bool Applied { get; private set; }

    /// <summary>True bila user memilih jalur wizard - caller membukanya.</summary>
    public bool OpenWizardRequested { get; private set; }

    public sealed class Row
    {
        public int DeviceNumber { get; set; }
        public string HardwareId { get; set; } = "";
        /// <summary>§161 - nama tampilan polos (hasil
        /// <see cref="DeckDisplayName.For"/>), tanpa penanda S0887/S0888.
        /// Dipakai daftar kembar di dialog konfirmasi; <see cref="Display"/>
        /// tetap nama + penanda seperti hari ini.</summary>
        public string BaseDisplay { get; set; } = "";
        public string Display { get; set; } = "";
        public string Detail { get; set; } = "";
        public bool IsCurrent { get; set; }
        public bool IsDuplicate { get; set; }
    }

    private readonly ObservableCollection<Row> _rows = new();

    /// <summary>Hardware ID → nama asli dari refresh terakhir (§61).
    /// Kosong = semua baris memakai tampilan VID/PID lama.</summary>
    private System.Collections.Generic.Dictionary<string, string> _names = new(System.StringComparer.OrdinalIgnoreCase);

    public DeckDevicePickerWindow(AppHost host)
    {
        InitializeComponent();
        _host = host;
        DeviceList.ItemsSource = _rows;
        RefreshList();
    }

    // §161 B1: nama baris memakai helper bersama DeckDisplayName.For
    // (satu rantai fallback dengan jalur identifikasi); method
    // DisplayName privat dihapus - perilaku byte-identik untuk
    // input non-null (helper adalah ekstraksi verbatim-nya).

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = "";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void RefreshList()
    {
        ClearError();
        InfoText.Visibility = Visibility.Collapsed;
        WarningBanner.Visibility = Visibility.Collapsed;
        _rows.Clear();

        var (devices, error) = InterceptionDeviceEnumerator.Enumerate();
        if (error is not null)
        {
            ShowError(error + " " + Localizer.Get("S0884"));
            DeviceList.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Collapsed;
            UpdateApplyState();
            return;
        }
        DeviceList.Visibility = Visibility.Visible;
        EmptyText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Nama asli dibaca sekali per refresh (§61); gagal = peta kosong,
        // daftar tetap tampil dengan VID/PID - tidak pernah menghalangi.
        _names = HidDeviceNameResolver.Resolve(devices);

        var current = _host.Config.DeckDevice;
        bool currentIsInterception = current.Kind == nameof(DeviceIdKind.InterceptionHardwareId)
            && current.Value.Length > 0;
        var duplicateIds = DeviceTwins.DuplicateHardwareIds(
            devices.Select(d => d.HardwareId));
        if (duplicateIds.Count > 0)
        {
            WarningText.Text = Localizer.Get("S0885") + " " +
                Localizer.Get("S0886");
            WarningBanner.Visibility = Visibility.Visible;
        }

        foreach (var device in devices)
        {
            bool isCurrent = currentIsInterception &&
                string.Equals(device.HardwareId, current.Value, StringComparison.OrdinalIgnoreCase);
            bool isDuplicate = duplicateIds.Contains(device.HardwareId);
            // §161 B1: BaseDisplay = nama polos dari helper bersama;
            // Display = nama polos + penanda, persis seperti hari ini.
            var baseDisplay = DeckDisplayName.For(device.HardwareId, device.DeviceNumber, _names);
            _rows.Add(new Row
            {
                DeviceNumber = device.DeviceNumber,
                HardwareId = device.HardwareId,
                BaseDisplay = baseDisplay,
                Display = baseDisplay
                    + (isCurrent ? " " + Localizer.Get("S0887") : "")
                    + (isDuplicate ? " " + Localizer.Get("S0888") : ""),
                Detail = Localizer.Format("S0889", device.DeviceNumber, device.HardwareId),
                IsCurrent = isCurrent,
                IsDuplicate = isDuplicate,
            });
        }

        if (currentIsInterception && !_rows.Any(r => r.IsCurrent))
        {
            InfoText.Text = Localizer.Get("S0890");
            InfoText.Visibility = Visibility.Visible;
        }

        var currentRow = _rows.FirstOrDefault(r => r.IsCurrent);
        if (currentRow is not null)
            DeviceList.SelectedItem = currentRow;
        UpdateApplyState();
    }

    private void UpdateApplyState()
    {
        var row = DeviceList.SelectedItem as Row;
        ConnectButton.IsEnabled = row is not null && !row.IsCurrent;
        HintText.Text = row is not null && row.IsCurrent
            ? Localizer.Get("S0891")
            : "";
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateApplyState();

    private void DeviceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ConnectButton.IsEnabled)
            ConnectButton_Click(sender, new RoutedEventArgs());
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void WizardButton_Click(object sender, RoutedEventArgs e)
    {
        OpenWizardRequested = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>§161 B2 - konfirmasi eksplisit untuk baris duplikat.
    /// Menyebut perangkat-perangkat yang berbenturan (nama tampilan +
    /// nomor perangkat, format S0889) dan ID perangkat bersama, serta
    /// menyatakan plainly risiko keyboard utama ikut termakan (S1774).
    /// True hanya bila user menjawab Ya; false = batalkan tanpa efek
    /// samping apa pun (pemanggil return sebelum re-validasi/simpan).
    /// Judul memakai literal "Keystream" seperti pola konfirmasi
    /// Yes/No existing (SettingsWindow ConfirmPrompt): tidak ada
    /// string judul konfirmasi generik di tabel (grep: tidak ada
    /// kunci bertitel "Konfirmasi"), jadi tidak ada +1 kunci judul.</summary>
    private bool ConfirmDuplicate(Row row)
    {
        var twins = _rows
            .Where(r => string.Equals(r.HardwareId, row.HardwareId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        string list = string.Join("\n", twins.Select(r =>
            Localizer.Format("S0889", r.DeviceNumber, r.BaseDisplay)));
        return AppDialog.Show(this,
            Localizer.Format("S1774", list, row.HardwareId),
            "Keystream", AppDialogButtons.YesNo, AppDialogIcon.Warning)
            == AppDialogResult.Yes;
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var row = DeviceList.SelectedItem as Row;
        if (row is null) return;
        if (row.IsCurrent) return; // Tombol sudah nonaktif; pertahanan ganda.

        if (_host.Health.State == InterceptionState.Starting)
        {
            ShowError(Localizer.Get("S0892"));
            return;
        }

        // §161 B2 - gerbang konfirmasi kembar: SEBELUM re-validasi,
        // penghentian service, dan penyimpanan apa pun. Peringatan +
        // konfirmasi eksplisit, BUKAN pemblokiran: pemilik kembar yang
        // sah tetap bisa memilih secara sadar. Jawaban selain Ya
        // (Tidak / tutup dialog) = batal total: tidak ada simpan,
        // tidak ada restart service, picker tetap terbuka dengan
        // pilihan tidak berubah. Daftar kembar diambil dari baris
        // yang sedang tampil (satu enumerasi dengan banner S0885/
        // S0886); S0885+S0886 tidak dipakai verbatim di sini karena
        // kalimat keduanya mengarahkan ke wizard - salah konteks
        // saat user sedang mengonfirmasi pilihan ini.
        if (row.IsDuplicate && !ConfirmDuplicate(row))
            return;

        // 1. Re-validasi: perangkat masih ada saat tombol ditekan?
        // (Dicabut di antara buka modal dan Hubungkan = skenario nyata.)
        var (devices, enumError) = InterceptionDeviceEnumerator.Enumerate();
        if (enumError is not null)
        {
            ShowError(Localizer.Get("S0893") + " " + enumError);
            return;
        }
        var fresh = devices.FirstOrDefault(d =>
            string.Equals(d.HardwareId, row.HardwareId, StringComparison.OrdinalIgnoreCase));
        if (fresh is null)
        {
            ShowError(Localizer.Get("S0894"));
            RefreshList();
            return;
        }

        // 2. Simpan deck lama untuk rollback. Service.Start melempar bila
        // worker sudah jalan, jadi service yang Running wajib berhenti dulu.
        var oldDeck = _host.Config.DeckDevice;
        bool wasRunning = _host.Health.State == InterceptionState.Running;
        try
        {
            if (wasRunning)
                _host.StopService();
            _host.Config.DeckDevice = new DeviceBinding
            {
                Kind = nameof(DeviceIdKind.InterceptionHardwareId),
                Value = row.HardwareId,
                FriendlyName = DeckDisplayName.For(fresh.HardwareId, fresh.DeviceNumber, HidDeviceNameResolver.Resolve(devices)),
                RequiresReidentification = false,
            };
            _host.Save();
        }
        catch (Exception ex)
        {
            _host.Config.DeckDevice = oldDeck;
            try { _host.Save(); } catch (Exception) { }
            if (wasRunning)
                _host.TryStartService(oldDeck.ToDeviceId());
            ShowError(Localizer.Get("S0895") + " " + ex.Message + " " + Localizer.Get("S0896"));
            return;
        }

        // 3. Jalankan service dengan deck baru (jalur yang sama seperti
        // wizard). Gagal = rollback penuh + pesan alasan dari Health.
        var newDeck = _host.Config.DeckDevice.ToDeviceId();
        if (!_host.TryStartService(newDeck))
        {
            string reason = _host.Health.LastError ?? Localizer.Get("S0897");
            _host.Config.DeckDevice = oldDeck;
            try { _host.Save(); } catch (Exception) { }
            if (wasRunning)
                _host.TryStartService(oldDeck.ToDeviceId());
            ShowError(Localizer.Get("S0898") + " " + reason + " " + Localizer.Get("S0896"));
            return;
        }

        Applied = true;
        Close();
    }
}
