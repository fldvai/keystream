using System.Collections.Generic;
using System.Windows;

namespace Keystream.App.Views;

/// <summary>Modal About (§98, layout ulang §100): identitas + hak
/// cipta + deskripsi + kredit komponen + tombol tautan, SEMUA rata
/// tengah; header memakai judul gradasi + tagline dashboard TANPA
/// tombol X (tutup hanya via tombol Close di tengah). Baris "proyek
/// open source" (S1556) DIBUANG atas perintah user 2026-10-27 - kunci
/// tetap di tabel, hanya tak terpakai di XAML. Tautan (GitHub, donasi;
/// Telegram/YouTube dibuang pada §99) aktif: URL sudah diisi di kamus
/// Urls di bawah. Nama lisensi repo sudah diputuskan: MIT.
/// §171: pemeriksaan pembaruan PINDAH ke Pengaturan (bagian paling
/// bawah) lewat UpdateChecker bersama - tidak ada lagi tombol atau
/// kode HTTP di modal ini.</summary>
public partial class AboutWindow : Window
{
    /// <summary>§98: URL tautan About, sudah diisi di bawah. URL donasi
    /// belum dikonfirmasi user; jangan ubah nilainya tanpa konfirmasi.</summary>
    private static readonly Dictionary<string, string?> Urls = new()
    {
        ["github"] = "https://github.com/fldvai/keystream",
        ["donate"] = "https://teer.id/fld",
    };

    public AboutWindow(string versionText)
    {
        InitializeComponent();
        VersionText.Text = versionText;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        if (!Urls.TryGetValue(tag, out string? url) || string.IsNullOrWhiteSpace(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true,
        });
    }
}
