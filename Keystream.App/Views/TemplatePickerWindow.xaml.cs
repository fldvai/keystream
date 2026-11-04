using System.Collections.Generic;
using Keystream.Core.Localization;
using System.Windows;
using System.Windows.Controls;

namespace Keystream.App.Views;

public partial class TemplatePickerWindow : Window
{
    public sealed record TemplateItem(string Id, string NameKey, string DescriptionKey, int CardCount)
    {
        // §169: Nama + deskripsi DIRESOLVE LANGSUNG dari kunci string
        // (seperti CardCountText) - BUKAN Localizer.Get sekali di
        // inisialisasi statis. Daftar statis itu membeku pada bahasa
        // saat modal pertama dibuka: ganti bahasa sesudahnya membuat
        // chrome modal (binding LocSource) + jumlah kartu berbahasa
        // baru sementara nama/deskripsi tertinggal di bahasa lama
        // (Gambar 2: chrome Inggris + deskripsi Indonesia). Properti
        // terhitung dibaca binding saat jendela dirender, jadi selalu
        // bahasa aktif. Tidak ada string hardcode di jendela ini.
        public string Name => Localizer.Get(NameKey);
        public string Description => Localizer.Get(DescriptionKey);
        public string CardCountText => Localizer.Format("S0996", CardCount);
    }

    // §75: urutan tampil = Gelombang 1 dulu, Stream terakhir.
    // §79: + Gelombang 2 (Browser, Aplikasi, YouTube) sebelum Stream;
    // Stream kini REVISI 12 kartu (dulu 15).
    // §169: BuildBrowserPage 15 kartu (dulu 12).
    // §170: BuildAplikasiPage 15 kartu Windows Tools (dulu 11);
    // kunci huruf + warna baru di semua template kecuali Browser.
    // Jumlah kartu HARUS sama dengan builder di MainViewModel
    // (BuildMediaPage 8, BuildProductivityPage 11, BuildNavigationPage
    // 12, BuildBrowserPage 15, BuildAplikasiPage 15, BuildYouTubePage
    // 11, BuildStreamPage 12, BuildMixPage 12) - picker hanya
    // menampilkan, bukan sumber kebenaran; builder yang menentukan
    // isi sebenarnya.
    private static readonly IReadOnlyList<TemplateItem> Items = new[]
    {
        new TemplateItem("media", "S0406",
            "S0997", 8),
        new TemplateItem("produktivitas", "S0423",
            "S0998", 11),
        new TemplateItem("navigasi", "S0435",
            "S0999", 12),
        new TemplateItem("browser", "S0448",
            "S1000", 15),
        new TemplateItem("aplikasi", "S0467",
            "S1001", 15),
        new TemplateItem("youtube", "S0477",
            "S1002", 11),
        new TemplateItem("stream", "S0489",
            "S1003", 12),
        new TemplateItem("mix", "S0496",
            "S1004", 12),
    };

    public string? SelectedTemplateId { get; private set; }

    public TemplatePickerWindow()
    {
        InitializeComponent();
        TemplateList.ItemsSource = Items;
        TemplateList.SelectedIndex = 0;
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Load_Click(object sender, RoutedEventArgs e) => LoadSelected();

    private void TemplateList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        // Silent-save fix: dulu tanpa pilihan, klik Muat (dan klik-ganda)
        // diam-diam tidak melakukan apa-apa. Normalnya tidak tercapai
        // (ctor memilih indeks 0), tapi bila seleksi kosong, syaratnya
        // kini disebut lewat AppDialog dan modal tetap terbuka.
        if (TemplateList.SelectedItem is not TemplateItem item)
        {
            AppDialog.Show(this, Localizer.Get("S1568"), "Keystream",
                AppDialogButtons.Ok, AppDialogIcon.Warning);
            return;
        }
        SelectedTemplateId = item.Id;
        DialogResult = true; // menutup jendela (WPF)
    }
}
