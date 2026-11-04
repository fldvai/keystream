using System.ComponentModel;
using Keystream.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Keystream.Actions.Hotkey;
using Keystream.App.Services;
using Keystream.App.Host;
using Keystream.App.ViewModels;

namespace Keystream.App.Views;

public partial class MainWindow : Window
{
    private WaitingKeyWindow? _waitingWin;

    // §134 A2: monotonically increasing id of the card ContextMenu
    // most recently opened. Each menu captures its own id + the
    // selection its prelude set; its Closed handler clears the
    // temporary selection only if it is still the latest menu AND
    // the selection is unchanged (see OpenCardContextMenu). The
    // transfer dropdown (OpenTransferDropdown) has no such id and
    // no Closed hook - it lives in Mode Pilih, where a close must
    // never clear anyway.
    private int _cardMenuSequence;

    // §136: id menu kartu yang SEDANG TERBUKA, atau 0 bila tidak ada.
    // Memakai ulang token §134 di atas: tiap menu menangkap menuId
    // sendiri, dan field ini hanya menunjuk menu terbaru. Ditugaskan
    // SEBELUM menu.IsOpen = true dan dihapus HANYA di handler Closed
    // menu yang id-nya cocok - tanpa syarat mode/seleksi, karena
    // marker bukan seleksi: menu yang menutup dalam Mode Pilih pun
    // harus menghapus markernya, kalau tidak klik kiri sesudah mode
    // keluar akan tertelan selamanya. Dropdown transfer
    // (OpenTransferDropdown) tidak pernah menyentuh field ini.
    // Urutan re-right-click (Closed lama vs penugasan baru) aman di
    // KEDUA urutan: (1) Closed lama tiba SEBELUM penugasan -
    // marker masih id lama, syarat cocok terpenuhi, marker dibersihkan
    // ke 0, lalu penugasan menetapkan id baru; (2) Closed lama tiba
    // SELAMA IsOpen = true menu baru, SESUDAH penugasan - marker
    // sudah id baru, syarat id gagal, marker baru selamat. Closed
    // menu itu sendiri selalu cocok dan membersihkan ke 0. Submenu
    // adalah bagian ContextMenu yang sama: Closed tiba sekali.
    private int _openCardMenuId;

    // --- mode rename via modal (§89, seperti pensil di daftar mode
    // sidebar): klik pensil atau klik-dua-kali judul membuka
    // PageEditWindow yang sama dengan EditPageCommand; kolom input
    // inline TitleEdit dihapus.
    private void PageTitle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_viewModel.CanRenamePage) return;
        if (e.ClickCount == 2 || sender is IconView)
            _viewModel.EditPageCommand.Execute(null);
    }

    // §124: Esc di kotak cari dashboard menghapus teks (bukan
    // memulihkan seperti TextBox lain - kotak ini memakai
    // EnableEscapeRestore="False" seperti FilterBox di
    // SearchablePanelPicker, jadi handler kelas TextBoxRevert tidak
    // menelan Esc; tombol × bawaan template global melakukan hal
    // yang sama lewat Clear()).
    // §133 M7: teks KOSONG = Esc keluar mode (ExitSelectMode:
    // flag off + hapus seleksi; di luar mode sama dengan hapus
    // seleksi lama). Esc saat teks tidak kosong hanya menghapus
    // teks (seleksi dipertahankan). Kasus
    // kotak-kosong tidak pernah sampai ke Window_PreviewKeyDown
    // (tunneling return lebih awal untuk TextBox yang fokus), jadi
    // fall-through ke hapus-seleksi hidup di sini, bukan di handler
    // jendela - dan harus tetap begitu.
    private void DashboardSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (sender is not System.Windows.Controls.TextBox tb) return;
        if (!string.IsNullOrEmpty(tb.Text))
        {
            tb.Text = string.Empty;
            e.Handled = true;
            return;
        }
        _viewModel.ExitSelectMode();
        e.Handled = true;
    }

    // --- §133 M4/M7 + F02: klik kiri -------------------------------------
    // DI DALAM Mode Pilih: klik kiri = toggle pilih/batal pilih
    // (ToggleSelection), HANYA ClickCount==1 - ClickCount==2
    // diabaikan total (klik ganda cepat tidak boleh toggle 2x).
    // DI LUAR mode: klik kiri membuka editor + menghapus seleksi
    // klik-kanan yang ada (ClearSelection, lewat ExitSelectMode
    // yang flag-nya sudah off). Guard tombol-dalam tetap di KEDUA
    // cabang: tombol itu perilaku sendiri, tidak toggle, tidak
    // keluar mode.
    // F02 (KEPUTUSAN KOORDINATOR, bukan verdict user): jalur editor
    // klik kiri DI LUAR mode dijaga BulkDeleteBlocked - bila
    // terblokir, klik diam total (tanpa editor, tanpa popup/footer).
    // Toggle DALAM mode TIDAK dijaga: toggle hanya mengubah state
    // seleksi UI, tidak membuka modal, tidak mendispatch deck.
    // Tidak ada benturan dengan takeover sesi panel §120 di jalur
    // ini: guard mencegah editor terbuka selama sesi aktif, jadi
    // takeover (buka modal mengakhiri sesi mandiri) tidak pernah
    // terpicu dari klik kiri; jalur menu sudah dijaga guard yang
    // sama di Tile_MouseRightButtonDown.
    // §134 A3: Ctrl+klik kiri DI LUAR mode = toggle kartu ke dalam
    // seleksi + masuk Mode Pilih bila seleksi hasil toggle tidak
    // kosong (toggle-off kartu terakhir = tetap di luar mode,
    // seleksi kosong, tanpa flag mode). Keputusan diambil SATU
    // tempat (Selection134Logic.DecideTileLeftClick, WPF-free,
    // harness-testable) dengan URUTAN PENJAGA YANG SAMA seperti
    // sebelum §134: tombol-dalam dulu (Ctrl+klik pada ×/keycap
    // tetap perilaku tombol itu), lalu cabang mode, lalu
    // BulkDeleteBlocked - jadi Ctrl+klik selama sesi
    // capture/perekaman diam total seperti klik kiri biasa
    // (F02), dan Ctrl+klik DALAM mode tetap toggle lama tanpa
    // special-casing. Klik kiri polos di luar mode tidak berubah
    // (editor + ExitSelectMode). Ctrl dibaca di sini, saat event,
    // pola yang sama dengan SelectCard.
    // §136: penjaga menu-terbuka BERDIRI SEBELUM keputusan di bawah
    // (Tile_MouseLeftButtonDown, lewat
    // Selection134Logic.ShouldSwallowTileLeftClick): menu kartu
    // terbuka + di luar Mode Pilih = klik kiri ditelan total,
    // termasuk Ctrl+klik dan klik tombol-dalam, karena penjaga
    // berjalan sebelum dispatch - klik itu gestur menutup menu,
    // klik kedua barulah beraksi. Di DALAM mode penjaga tidak
    // berlaku sama sekali. Seleksi TIDAK disentuh penjaga: clear
    // seleksi sementara tetap satu-satunya milik hook Closed §134.
    // Tanpa e.Handled: cabang Ignore handler ini (tombol-dalam /
    // terblokir) juga tidak menandai Handled, dan menu menutup
    // sendiri oleh framework.
    private void Tile_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        if (el.DataContext is not BindingItemViewModel item) return;
        if (Selection134Logic.ShouldSwallowTileLeftClick(
            _openCardMenuId != 0, _viewModel.IsSelectMode)) return;
        bool ctrl = (System.Windows.Input.Keyboard.Modifiers
                     & System.Windows.Input.ModifierKeys.Control) != 0;
        switch (Selection134Logic.DecideTileLeftClick(
            _viewModel.IsSelectMode, ctrl,
            IsInsideInnerButton(e.OriginalSource as DependencyObject),
            _viewModel.BulkDeleteBlocked, e.ClickCount))
        {
            case TileLeftClickAction.ToggleInMode:
                _viewModel.ToggleSelection(item);
                break;
            case TileLeftClickAction.CtrlSelect:
                // Primitif yang sudah ada: ToggleSelection (toggle
                // append di akhir urutan) + EnterSelectMode bila
                // hasilnya tidak kosong. Tidak ada jalur seleksi
                // paralel; snapshot select-all §134 A1 ikut
                // terhapus lewat ToggleSelection.
                _viewModel.ToggleSelection(item);
                if (_viewModel.HasSelection) _viewModel.EnterSelectMode();
                break;
            case TileLeftClickAction.OpenEditor:
                _viewModel.OpenEditorCommand.Execute(item);
                _viewModel.ExitSelectMode();
                break;
            default:
                break;
        }
    }

    // Klik area kosong grid dashboard keluar mode (§133 M7:
    // ExitSelectMode = flag off + hapus seleksi; di luar mode sama
    // dengan hapus seleksi lama). Klik kartu menggelembung lewat grid ini juga, tetapi dibedakan lewat
    // DataContext yang diwarisi template (kartu =
    // BindingItemViewModel) dan lewat tombol-dalam apa pun; klik
    // scrollbar tidak sampai ke sini (scrollbar di luar grid isi).
    private void DashboardEmptyArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement fe && fe.DataContext is BindingItemViewModel) return;
            if (current is System.Windows.Controls.Button) return;
            current = VisualTreeHelper.GetParent(current);
        }
        _viewModel.ExitSelectMode();
    }

    // Menu klik-kanan kartu (§132 C3, diperbarui §133): WPF ContextMenu
    // yang dibangun segar tiap pembukaan - header selalu bahasa aktif,
    // submenu tujuan selalu live, placement dijepit ke rect jendela
    // (§133), Esc, dan navigasi keyboard. SEBELUM menu dibuka, klik
    // kanan mengatur seleksi (SelectForContextMenu): Ctrl = toggle
    // kartu diklik; tanpa Ctrl + kartu belum terseleksi = ganti
    // seleksi dengan kartu itu; kartu sudah terseleksi = seleksi
    // dipertahankan. Salin/Pindah/Nonaktifkan/Hapus di menu bekerja
    // pada SELEKSI; Buka/Ganti Nama/Duplikat hanya pada kartu yang
    // diklik.
    private void Tile_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        if (el.DataContext is not BindingItemViewModel item) return;
        // §125: klik kanan yang berasal dari tombol-dalam kartu
        // (tombol × hapus / tombol badge keycap) TIDAK membuka menu -
        // tombol itu mempertahankan perilakunya sendiri. Return lebih
        // awal TANPA set Handled dan tanpa mengubah apa pun.
        if (IsInsideInnerButton(e.OriginalSource as DependencyObject)) return;
        // Menu tidak dibuka selama pemetaan (capture armed) atau sesi
        // perekaman/playback panel aktif - penjaga BulkDeleteBlocked
        // yang sama dengan hapus massal.
        if (_viewModel.BulkDeleteBlocked) return;
        _viewModel.SelectForContextMenu(item);
        OpenCardContextMenu(item, el);
        e.Handled = true;
    }

    /// <summary>§125: true bila sumber event berada di dalam sebuah
    /// Button di pohon visual - dipakai Tile_MouseRightButtonDown
    /// DAN Tile_MouseLeftButtonDown untuk membedakan permukaan kartu
    /// dari tombol-dalamnya. Batasnya Grid kartu (sender), jadi Button di
    /// luar kartu tidak mungkin terjangkau dari event yang
    /// menggelembung lewat Grid.</summary>
    private static bool IsInsideInnerButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Button) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private Style? CardMenuItemStyle => TryFindResource("CardMenuItemStyle") as Style;

    private Style? CardMenuSeparatorStyle => TryFindResource("CardMenuSeparatorStyle") as Style;

    // §133: menu kartu tanpa ikon (Gambar 1) - NewMenuItem tidak lagi
    // menerima kunci ikon; jangan kembalikan parameter ikon ke sini.
    private static System.Windows.Controls.MenuItem NewMenuItem(
        string header, Style? style, System.Windows.RoutedEventHandler onClick)
    {
        var mi = new System.Windows.Controls.MenuItem
        {
            Header = header,
        };
        if (style is not null) mi.Style = style;
        mi.Click += onClick;
        return mi;
    }

    private void OpenCardContextMenu(BindingItemViewModel item, FrameworkElement placementTarget)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = placementTarget,
            // §133 C2: Placement=Custom + callback (ApplyWindowClampedMenuPlacement,
            // dipasang sebelum IsOpen=true di bawah). CustomPopupPlacementCallback
            // ADA di ContextMenu pada net8.0-windows - dibuktikan build (bukan
            // asumsi): callback mengembalikan SATU placement relatif terhadap
            // PlacementTarget (kartu), dan framework TIDAK boleh flip default
            // - flip default framework inilah kegagalan §132. Offset 0 agar
            // titik callback persis (tidak ada penambahan offset tersembunyi).
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            HorizontalOffset = 0,
            VerticalOffset = 0,
        };
        if (TryFindResource("CardContextMenuStyle") is Style menuStyle) menu.Style = menuStyle;
        var style = CardMenuItemStyle;
        var clicked = item;

        // §134 A2: token + snapshot seleksi menu ini. Prelude klik
        // kanan (SelectForContextMenu) sudah berjalan di
        // Tile_MouseRightButtonDown SEBELUM method ini dipanggil,
        // jadi snapshot di sini adalah seleksi yang ditetapkan
        // menu ini. Keduanya ditangkap per menu (closure), BUKAN
        // di field bersama - field bersama hanya id terbaru
        // (_cardMenuSequence), yang dimajukan tiap pembukaan.
        int menuId = ++_cardMenuSequence;
        var menuSelection = _viewModel.SelectionSnapshot();

        // §133 B3: "Buka" tebal + suffix abu-abu via Localizer (S1704 +
        // S1707), bukan literal. Header TextBlock + Run aman untuk navigasi
        // keyboard: fokus/navigasi milik MenuItem, TextBlock tidak
        // focusable; template sudah RecognizesAccessKey="False".
        var openHeader = new System.Windows.Controls.TextBlock();
        openHeader.Inlines.Add(new System.Windows.Documents.Run(Localizer.Get("S1704"))
        {
            FontWeight = System.Windows.FontWeights.Bold,
        });
        openHeader.Inlines.Add(new System.Windows.Documents.Run(" " + Localizer.Get("S1707"))
        {
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x8A, 0x93, 0xA5)),
        });
        var openItem = new System.Windows.Controls.MenuItem { Header = openHeader };
        if (style is not null) openItem.Style = style;
        openItem.Click += (_, _) => _viewModel.OpenEditorCommand.Execute(clicked);
        menu.Items.Add(openItem);

        var copySub = new System.Windows.Controls.MenuItem { Header = Localizer.Get("S1694") };
        var moveSub = new System.Windows.Controls.MenuItem { Header = Localizer.Get("S1695") };
        if (style is not null) { copySub.Style = style; moveSub.Style = style; }
        foreach (var dest in _viewModel.TransferDestinations())
        {
            var target = dest;
            var copyItem = new System.Windows.Controls.MenuItem { Header = target.DisplayName };
            var moveItem = new System.Windows.Controls.MenuItem { Header = target.DisplayName };
            if (style is not null) { copyItem.Style = style; moveItem.Style = style; }
            copyItem.Click += (_, _) =>
            {
                // §133: salin bekerja pada SELEKSI (sudah diatur klik
                // kanan sebelum menu dibuka); fallback kartu diklik
                // bila seleksi kosong.
                if (_viewModel.HasSelection) _viewModel.CopySelectedTo(target);
                else _viewModel.CopyCardsTo(new[] { clicked.Model }, target);
            };
            moveItem.Click += (_, _) =>
            {
                // §133: pindah bekerja pada SELEKSI; fallback kartu
                // diklik bila seleksi kosong (seperti salin).
                if (_viewModel.HasSelection) _viewModel.MoveSelectedTo(target);
                else _viewModel.MoveCardsTo(new[] { clicked.Model }, target);
            };
            copySub.Items.Add(copyItem);
            moveSub.Items.Add(moveItem);
        }
        menu.Items.Add(copySub);
        menu.Items.Add(moveSub);

        // §133 B2: pemisah mockup revisi (1px #2A3348, margin 4,6).
        menu.Items.Add(new System.Windows.Controls.Separator { Style = CardMenuSeparatorStyle });

        menu.Items.Add(NewMenuItem(Localizer.Get("S1636"), style,
            (_, _) => _viewModel.RenameCard(clicked)));
        menu.Items.Add(NewMenuItem(Localizer.Get("S0183"), style,
            (_, _) => _viewModel.DuplicateCard(clicked)));
        menu.Items.Add(NewMenuItem(
            // §133 F01: label dari HIMPUNAN EFEKTIF saat menu dibangun
            // (MenuToggleText): seleksi ada = aturan mayoritas §153
            // Part A pada seleksi; seleksi kosong = keadaan kartu diklik.
            _viewModel.MenuToggleText(clicked), style,
            (_, _) =>
            {
                // §133: toggle bekerja pada SELEKSI; fallback kartu
                // diklik bila seleksi kosong.
                if (_viewModel.HasSelection) _viewModel.ToggleSelectedActive();
                else _viewModel.ToggleCardActive(clicked);
            }));
        menu.Items.Add(NewMenuItem(Localizer.Get("S0184"), style,
            (_, _) =>
            {
                // §133: hapus memakai jalur bulk-delete satu AppDialog
                // (DeleteSelected) bila seleksi ada; fallback hapus
                // satu kartu bila seleksi kosong.
                if (_viewModel.HasSelection) _viewModel.DeleteSelected();
                else _viewModel.DeleteBindingCommand.Execute(clicked);
            }));

        // §133 M1b: item "Pilih" (S1637) kembali sebagai item
        // TERAKHIR menu, sesudah Hapus, tanpa separator tambahan.
        // Dieksekusi pada kartu yang SUDAH terseleksi: prelude klik
        // kanan (SelectForContextMenu) sudah berjalan sebelum menu
        // dibuka. Jalur abnormal (seleksi tetap kosong) = pilih kartu
        // diklik dulu, lalu masuk mode. Sudah dalam mode = no-op
        // (EnterSelectMode). Item menu lain, style, dan placement
        // glue tidak diubah di sini (milik B/C).
        menu.Items.Add(NewMenuItem(Localizer.Get("S1637"), style,
            (_, _) =>
            {
                if (!_viewModel.HasSelection) _viewModel.SelectForContextMenu(clicked);
                _viewModel.EnterSelectMode();
            }));

        // §133 C2/C3: placement dijepit ke rect jendela (live, saat
        // dibuka) + clamp submenu; gagal apa pun = fallback diam ke
        // placement lama di dalam method ini, tanpa popup/pesan.
        // S1: geometri diambil live di sini (kursor + rect jendela
        // saat open via PointToScreen), BUKAN dari indeks kartu atau
        // posisi scroll - kartu baris bawah yang terlihat sesudah
        // scroll flip ke atas, kartu yang sama di tengah jendela
        // membuka ke bawah normal.
        ApplyWindowClampedMenuPlacement(menu, placementTarget, copySub, moveSub);

        // §134 A2: menu menutup = seleksi sementara klik-kanan
        // dibatalkan, HANYA di luar Mode Pilih. ClearSelection
        // polos (bukan ClearSelectionAndUndo - snapshot undo
        // hapus massal tidak disentuh). Masuk mode via "Pilih"
        // menjaga seleksi: handler Click "Pilih" memanggil
        // EnterSelectMode SEBELUM menu menutup, jadi syarat
        // IsSelectMode di keputusan sudah true saat Closed tiba;
        // aksi menu lain juga berakhir lewat jalur tutup yang
        // sama (sukses Salin/Pindah/Hapus sudah mengosongkan
        // seleksi sendiri lewat ExitSelectMode, jadi syarat
        // kesamaan-seleksi menggagalkan clear ganda yang tak
        // berbahaya itu). Esc dan klik-away sama-sama lewat
        // Closed, keduanya membersihkan. Klik submenu TIDAK
        // memicu Closed prematur: Closed milik ContextMenu
        // utuh dan hanya tiba sekali saat menu benar-benar
        // menutup, bukan saat submenu dibuka/diklik. Re-right-
        // click cepat aman di SEMUA urutan event berkat pasangan
        // token + kesamaan-seleksi (bukti kasus di
        // Selection134Logic.ShouldClearTempSelectionOnMenuClose):
        // Closed sebelum prelude baru -> clear dulu, prelude
        // lalu mengganti seleksi dengan kartu baru; Closed
        // sesudah prelude baru -> token atau kesamaan gagal,
        // seleksi baru selamat. Hook HANYA di menu kartu ini -
        // OpenTransferDropdown tidak disentuh. §136: baris pertama
        // handler menghapus marker menu-terbuka, HANYA bila id menu
        // yang menutup cocok dengan marker (lihat komentar field
        // _openCardMenuId untuk urutan re-right-click); penghapusan
        // ini tanpa syarat mode/seleksi, terpisah dari keputusan
        // clear seleksi di bawah.
        menu.Closed += (_, _) =>
        {
            if (menuId == _openCardMenuId) _openCardMenuId = 0;
            if (Selection134Logic.ShouldClearTempSelectionOnMenuClose(
                _viewModel.IsSelectMode, menuId, _cardMenuSequence,
                _viewModel.SelectionMatches(menuSelection)))
                _viewModel.ClearSelection();
        };

        // §136: tandai menu ini terbuka SEBELUM IsOpen = true (lihat
        // komentar field _openCardMenuId untuk alasan urutannya).
        _openCardMenuId = menuId;
        menu.IsOpen = true;
    }

    // --- §133 M5: dropdown Salin ke / Pindah ke di bar Mode Pilih -----
    // Handler Click XAML (BulkCopyButton_Click/BulkMoveButton_Click)
    // + OpenTransferDropdown - kontrak Pekerja E untuk bar F (XAML
    // mengikat Click ke nama ini; tanpa handler, build gagal).
    // Dropdown = ContextMenu datar berisi TransferDestinations()
    // live, memakai style menu yang sama (Gambar 1, milik B -
    // style-nya tidak diubah). Klik tujuan memanggil
    // CopySelectedTo/MoveSelectedTo: sukses = keluar mode (M7b),
    // gagal simpan = mode+seleksi dipertahankan + footer error.
    // Guard: tanpa seleksi atau BulkDeleteBlocked = diam (bar hanya
    // terlihat dalam mode dengan seleksi; menu klik-kanan dijaga
    // guard yang sama). Placement: PlacementMode.Top berjangkar
    // tombol - bar duduk di bawah dashboard, jadi satu-satunya arah
    // yang muat adalah ke atas; TIDAK memakai glue clamp jendela C
    // (API-nya spesifik menu kartu: butuh copySub/moveSub untuk
    // submenu) - perilaku dropdown saat jendela sangat pendek adalah
    // residu PC/CARA, dicatat di laporan E.
    private void BulkCopyButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => OpenTransferDropdown(sender, isMove: false);

    private void BulkMoveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => OpenTransferDropdown(sender, isMove: true);

    private void OpenTransferDropdown(object sender, bool isMove)
    {
        if (sender is not FrameworkElement button) return;
        if (!_viewModel.HasSelection) return;
        if (_viewModel.BulkDeleteBlocked) return;
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            HorizontalOffset = 0,
            VerticalOffset = 0,
        };
        if (TryFindResource("CardContextMenuStyle") is Style menuStyle) menu.Style = menuStyle;
        var style = CardMenuItemStyle;
        foreach (var dest in _viewModel.TransferDestinations())
        {
            var target = dest;
            menu.Items.Add(NewMenuItem(target.DisplayName, style,
                (_, _) =>
                {
                    if (isMove) _viewModel.MoveSelectedTo(target);
                    else _viewModel.CopySelectedTo(target);
                }));
        }
        menu.IsOpen = true;
    }
    // Mekanisme final: ContextMenu.Placement=Custom +
    // CustomPopupPlacementCallback (properti ini ADA di ContextMenu
    // pada net8.0-windows - dibuktikan build App, lihat laporan
    // §133; bukan asumsi), dan PART_Popup submenu dengan mekanisme
    // yang sama, dipasang dari code-behind (Loaded + retry
    // SubmenuOpened). Callback mengembalikan SATU placement relatif
    // terhadap PlacementTarget, dihitung dari geometri DIP layar:
    //   titik relatif = titik layar hasil helper − origin layar target
    //   (targetOrigin = PlacementTarget.PointToScreen(0,0)).
    // HorizontalOffset/VerticalOffset DIZOLKAN (0) pada menu utama
    // maupun PART_Popup saat callback dipasang, agar titik callback
    // persis dan tidak ada penambahan offset tersembunyi; overlap
    // submenu -4/-6 yang lama direproduksi DI DALAM helper
    // (MenuPlacementLogic.SubmenuHorizontalOverlap/VerticalOverlap).
    // PrimaryAxis=None: tidak ada geser sumbu oleh framework - titik
    // sudah dijamin muat di jendela ∩ work area oleh helper, dan
    // geser apa pun akan mengalahkan clamp jendela. Flip default
    // framework TIDAK dipakai (itu kegagalan §132).
    // ASUMSI yang hanya bisa dibuktikan PC: (1) callback dipanggil
    // framework sebelum popup tampil sehingga titiknya dipakai pada
    // pembukaan pertama; (2) PART_Popup merespons perubahan
    // Placement→Custom saat SubmenuOpened bila Loaded terlewat;
    // (3) Measure() pada ContextMenu sebelum IsOpen memberi
    // DesiredSize yang berguna untuk keputusan MaxHeight (callback
    // sendiri memakai popupSize framework yang otoritatif, jadi
    // kegagalan ukur hanya memengaruhi cap awal, bukan posisi).
    // Esc + navigasi keyboard TIDAK disentuh: tidak ada StaysOpen,
    // handler tombol, atau perubahan fokus di sini - framework
    // yang memilikinya, seperti sebelum §133.
    private void ApplyWindowClampedMenuPlacement(
        System.Windows.Controls.ContextMenu menu,
        FrameworkElement placementTarget,
        System.Windows.Controls.MenuItem copySub,
        System.Windows.Controls.MenuItem moveSub)
    {
        try
        {
            // S1: geometri LIVE saat menu dibuka. Titik kursor via
            // Mouse.GetPosition(this) + PointToScreen (posisi di
            // dalam jendela saat klik kanan, setelah scroll apa pun);
            // rect jendela via PointToScreen(0,0) + ActualWidth/Height.
            // DIP layar untuk keduanya (lihat MenuPlacementLogic).
            var cursorScreen = PointToScreen(System.Windows.Input.Mouse.GetPosition(this));
            var windowOrigin = PointToScreen(new System.Windows.Point(0, 0));
            var windowRect = new MenuPlacementLogic.DipRect(
                windowOrigin.X, windowOrigin.Y, ActualWidth, ActualHeight);
            MenuPlacementLogic.DipRect workArea;
            if (!TryGetDipWorkAreaForPoint(cursorScreen, out workArea))
            {
                // Degradasi: work area monitor utama (DIP). Bila
                // irisannya dengan jendela degenerat, helper memakai
                // rect jendela saja - tidak pernah MousePoint di sini.
                var area = SystemParameters.WorkArea;
                workArea = new MenuPlacementLogic.DipRect(area.Left, area.Top, area.Width, area.Height);
            }

            // Ukur SEBELUM IsOpen=true, dengan ukuran jendela sebagai
            // available size, untuk keputusan MaxHeight (C4).
            menu.Measure(new System.Windows.Size(windowRect.Width, windowRect.Height));
            var desired = menu.DesiredSize;
            if (IsFiniteValue(desired.Width) && IsFiniteValue(desired.Height)
                && desired.Width > 0 && desired.Height > 0)
            {
                var pre = MenuPlacementLogic.PlaceMainMenu(
                    cursorScreen.X, cursorScreen.Y,
                    desired.Width, desired.Height, windowRect, workArea);
                if (!pre.IsFallback && pre.HeightCapped)
                    menu.MaxHeight = pre.Height;
            }
            else
            {
                // Ukur degenerat: cap aman tetap dipasang; posisi
                // tetap dihitung callback dari popupSize framework.
                menu.MaxHeight = Math.Max(0, windowRect.Height - 2 * MenuPlacementLogic.EdgeMargin);
            }

            var targetOrigin = placementTarget.PointToScreen(new System.Windows.Point(0, 0));
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                try
                {
                    if (IsFiniteValue(popupSize.Width) && IsFiniteValue(popupSize.Height)
                        && popupSize.Width > 0 && popupSize.Height > 0)
                    {
                        var placed = MenuPlacementLogic.PlaceMainMenu(
                            cursorScreen.X, cursorScreen.Y,
                            popupSize.Width, popupSize.Height, windowRect, workArea);
                        if (!placed.IsFallback)
                        {
                            return new[]
                            {
                                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                                    new System.Windows.Point(placed.Left - targetOrigin.X, placed.Top - targetOrigin.Y),
                                    System.Windows.Controls.Primitives.PopupPrimaryAxis.None),
                            };
                        }
                    }
                }
                catch
                {
                    // Jatuh ke fallback kursor-relatif di bawah.
                }
                return new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(cursorScreen.X - targetOrigin.X, cursorScreen.Y - targetOrigin.Y),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.None),
                };
            };

            AttachSubmenuPlacement(copySub);
            AttachSubmenuPlacement(moveSub);
        }
        catch
        {
            // Fallback DIAM ke placement lama (§132), tanpa popup
            // atau pesan apa pun: menu tetap terbuka, hanya tanpa
            // clamp jendela.
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.CustomPopupPlacementCallback = null;
            menu.MaxHeight = double.NaN;
        }
    }

    /// <summary>§133 C3: memasang clamp jendela pada PART_Popup milik
    /// satu MenuItem induk submenu. Attachment dibuktikan di kode:
    /// template diterapkan paksa (ApplyTemplate), Popup dicari lewat
    /// Template.FindName("PART_Popup", parentItem), lalu Placement,
    /// PlacementTarget, offset, dan callback diset LANGSUNG pada
    /// Popup itu - bukan asumsi XAML saja. PART_Popup di XAML
    /// sengaja DIBIARKAN Placement="Right" (MainWindow.xaml): itu
    /// fallback bila attachment gagal (FindName null / exception),
    /// dan warna/border/padding border popup milik Pekerja B tidak
    /// disentuh. Loaded adalah jalur utama (item harus loaded
    /// sebelum submenu mana pun bisa dibuka); SubmenuOpened adalah
    /// retry bila Loaded terlewat karena template belum teraplikasi.
    /// Bila keduanya gagal, submenu tampil dengan Right lama.</summary>
    private void AttachSubmenuPlacement(System.Windows.Controls.MenuItem parentItem)
    {
        parentItem.Loaded += (_, _) => ApplySubmenuPlacement(parentItem);
        parentItem.SubmenuOpened += (_, _) => ApplySubmenuPlacement(parentItem);
    }

    private void ApplySubmenuPlacement(System.Windows.Controls.MenuItem parentItem)
    {
        try
        {
            parentItem.ApplyTemplate();
            if (parentItem.Template.FindName("PART_Popup", parentItem)
                is not System.Windows.Controls.Primitives.Popup popup)
                return;
            popup.PlacementTarget = parentItem;
            popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 0;
            CapSubmenuScrollViewer(popup);
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                try
                {
                    var parentOrigin = parentItem.PointToScreen(new System.Windows.Point(0, 0));
                    var parentRect = new MenuPlacementLogic.DipRect(
                        parentOrigin.X, parentOrigin.Y, parentItem.ActualWidth, parentItem.ActualHeight);
                    var windowOrigin = PointToScreen(new System.Windows.Point(0, 0));
                    var windowRect = new MenuPlacementLogic.DipRect(
                        windowOrigin.X, windowOrigin.Y, ActualWidth, ActualHeight);
                    // Geometri submenu dihitung LIVE saat callback
                    // dipanggil (submenu dibuka belakangan, jendela
                    // mungkin sudah berpindah sejak menu utama dibuka).
                    var centre = new System.Windows.Point(
                        parentOrigin.X + parentItem.ActualWidth / 2,
                        parentOrigin.Y + parentItem.ActualHeight / 2);
                    MenuPlacementLogic.DipRect workArea;
                    if (IsFiniteValue(popupSize.Width) && IsFiniteValue(popupSize.Height)
                        && popupSize.Width > 0 && popupSize.Height > 0
                        && TryGetDipWorkAreaForPoint(centre, out workArea))
                    {
                        var placed = MenuPlacementLogic.PlaceSubmenu(
                            parentRect, popupSize.Width, popupSize.Height, windowRect, workArea);
                        if (!placed.IsFallback)
                        {
                            return new[]
                            {
                                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                                    new System.Windows.Point(placed.Left - parentOrigin.X, placed.Top - parentOrigin.Y),
                                    System.Windows.Controls.Primitives.PopupPrimaryAxis.None),
                            };
                        }
                    }
                    else if (IsFiniteValue(popupSize.Width) && IsFiniteValue(popupSize.Height)
                        && popupSize.Width > 0 && popupSize.Height > 0)
                    {
                        var area = SystemParameters.WorkArea;
                        workArea = new MenuPlacementLogic.DipRect(area.Left, area.Top, area.Width, area.Height);
                        var placed = MenuPlacementLogic.PlaceSubmenu(
                            parentRect, popupSize.Width, popupSize.Height, windowRect, workArea);
                        if (!placed.IsFallback)
                        {
                            return new[]
                            {
                                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                                    new System.Windows.Point(placed.Left - parentOrigin.X, placed.Top - parentOrigin.Y),
                                    System.Windows.Controls.Primitives.PopupPrimaryAxis.None),
                            };
                        }
                    }
                }
                catch
                {
                    // Jatuh ke fallback Right di bawah.
                }
                // Fallback: reproduksi Placement="Right" lama
                // (tepi kanan induk − 4, atas induk − 6), relatif
                // terhadap induk.
                return new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(targetSize.Width - MenuPlacementLogic.SubmenuHorizontalOverlap,
                            -MenuPlacementLogic.SubmenuVerticalOverlap),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.None),
                };
            };
        }
        catch
        {
            // Diam: PART_Popup tetap Placement="Right" dari XAML.
        }
    }

    /// <summary>§133 C4 (submenu): ScrollViewer di dalam PART_Popup
    /// sudah MaxHeight="400" di XAML (milik template Pekerja B -
    /// TIDAK diubah di sini). Bila jendela lebih pendek dari itu,
    /// 400 akan meluber melewati bawah jendela meski placement
    /// sudah dijepit (helper mengasumsikan tinggi ≤ jendela − 16).
    /// Di sini MaxHeight ScrollViewer diturunkan di CODE menjadi
    /// min(400, tinggi jendela − 16) saat submenu dipasang, agar
    /// popupSize yang diukur framework sudah konsisten dengan cap
    /// helper dan submenu bisa di-scroll di dalam batas itu.
    /// Warna/border/padding Border tidak disentuh.</summary>
    private void CapSubmenuScrollViewer(System.Windows.Controls.Primitives.Popup popup)
    {
        try
        {
            if (popup.Child is System.Windows.Controls.Border border
                && border.Child is System.Windows.Controls.ScrollViewer scroller)
            {
                double cap = ActualHeight - 2 * MenuPlacementLogic.EdgeMargin;
                if (cap > 0 && cap < scroller.MaxHeight)
                    scroller.MaxHeight = cap;
            }
        }
        catch
        {
            // Diam: MaxHeight 400 XAML tetap berlaku.
        }
    }

    /// <summary>Work area DIP untuk satu titik layar, pola yang sama
    /// dengan FloatPanelHost.TryClampToScreenWorkArea: Screen.AllScreens
    /// (piksel) → tiap layar dikonversi dengan DPI-nya SENDIRI via
    /// MonitorDpi (pusat layar), fallback skala transform jendela
    /// ini bila query DPI gagal → ComputeDipWorkAreas (tiling DIP)
    /// → area yang memuat titik (atau terdekat). False bila tidak
    /// ada layar usable / error apa pun; caller memakai
    /// SystemParameters.WorkArea sebagai degradasi. Tidak melempar.</summary>
    private bool TryGetDipWorkAreaForPoint(System.Windows.Point point, out MenuPlacementLogic.DipRect workArea)
    {
        workArea = default;
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens is null || screens.Length == 0) return false;
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice;
            double fallbackScaleX = 0, fallbackScaleY = 0;
            if (transform is not null
                && IsFiniteValue(transform.Value.M11) && IsFiniteValue(transform.Value.M22)
                && transform.Value.M11 > 0 && transform.Value.M22 > 0)
            {
                fallbackScaleX = transform.Value.M11;
                fallbackScaleY = transform.Value.M22;
            }
            var pixelAreas = new List<FloatingRecordPanelLogic.PixelWorkArea>();
            foreach (var screen in screens)
            {
                if (screen is null) continue;
                var pixels = screen.WorkingArea;
                if (pixels.Width <= 0 || pixels.Height <= 0) continue;
                double dpiX, dpiY;
                if (MonitorDpi.TryGetDpiForPixelPoint(
                        pixels.Left + pixels.Width / 2, pixels.Top + pixels.Height / 2,
                        out uint qx, out uint qy))
                {
                    dpiX = qx;
                    dpiY = qy;
                }
                else if (fallbackScaleX > 0 && fallbackScaleY > 0)
                {
                    dpiX = fallbackScaleX * 96.0;
                    dpiY = fallbackScaleY * 96.0;
                }
                else
                {
                    continue;
                }
                pixelAreas.Add(new FloatingRecordPanelLogic.PixelWorkArea(
                    pixels.Left, pixels.Top, pixels.Width, pixels.Height, dpiX, dpiY));
            }
            if (pixelAreas.Count == 0) return false;
            var dipAreas = FloatingRecordPanelLogic.ComputeDipWorkAreas(pixelAreas);
            var rects = new List<MenuPlacementLogic.DipRect>();
            foreach (var area in dipAreas)
                rects.Add(new MenuPlacementLogic.DipRect(area.Left, area.Top, area.Width, area.Height));
            return MenuPlacementLogic.TrySelectWorkArea(point.X, point.Y, rects, out workArea);
        }
        catch
        {
            workArea = default;
            return false;
        }
    }

    private static bool IsFiniteValue(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    // §132 preseden Esc (F42), hanya jendela utama:
    //   1. Menu konteks yang terbuka menelan Esc sendiri (framework) -
    //      menu adalah popup window terpisah, jadi handler tunneling
    //      ini tidak pernah melihat keypress itu; jangan dilawan.
    //   2. TextBox/ComboBox yang fokus memiliki Esc-nya sendiri
    //      (§110 revert, KeyDown kotak cari, dropdown ComboBox yang
    //      menutup sendiri) - return tanpa handling. Kasus kotak cari
    //      KOSONG secara struktural tidak mungkin sampai ke sini:
    //      tunneling jalan SEBELUM handler bubbling kotak cari, jadi
    //      fall-through ke hapus-seleksi hidup di
    //      DashboardSearchBox_KeyDown, bukan di sini - dan harus
    //      tetap begitu.
    //   3. Selain itu, bila ada seleksi ATAU mode aktif, satu Esc
    //      keluar mode (ExitSelectMode). Mode dengan seleksi kosong
    //      (jalur abnormal) ikut keluar lewat syarat IsSelectMode.
    // Jendela modal (editor, pemilih, popup tunggu) memiliki Esc-nya
    // sendiri; handler ini tidak jalan selama modal terbuka.
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        // Esc milik kontrol yang sedang fokus dulu: kotak cari
        // menghapus teksnya sendiri (handler KeyDown-nya), dropdown
        // ComboBox menutup sendiri - preview di sini tidak boleh
        // merebutnya untuk menghapus seleksi.
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox) return;
        if (_viewModel.HasSelection || _viewModel.IsSelectMode)
        {
            _viewModel.ExitSelectMode();
            e.Handled = true;
        }
    }
    private const int PanicHotkeyId = 0x4D01;
    private const int ToggleGlobalHotkeyId = 0x4D02;
    private const int NextProfileHotkeyId = 0x4D03;
    private const int PrevProfileHotkeyId = 0x4D04;
    private const int ToggleRecordingHotkeyId = 0x4D05;
    private const int TogglePlaybackHotkeyId = 0x4D06;
    private const int PauseResumePlaybackHotkeyId = 0x4D07;
    private readonly List<int> _registeredHotkeys = new();

    private readonly AppHost _host;
    private readonly MainViewModel _viewModel;

    public bool ExitRequested { get; set; }

    public MainWindow(AppHost host, MainViewModel viewModel)
    {
        InitializeComponent();
        _host = host;
        _viewModel = viewModel;
        DataContext = viewModel;
        // §113: footer notices from the floating panel (standalone
        // controller via AppHost.NotifyFooter) reach the main footer.
        // MainWindow lives for the app run; no unsubscribe needed.
        _host.FooterNotice += (m, c, s) => _viewModel.Notify(m, c, s);
        _viewModel.RequestOpenWizard += () =>
        {
            var wizard = new WizardWindow(_host) { Owner = this };
            wizard.ShowDialog();
        };
        _viewModel.RequestOpenMacro += () =>
        {
            var macro = new MacroManagerWindow(_host) { Owner = this };
            macro.ShowDialog();
            // §96: Save berhasil di modal macro → info di footer halaman
            // utama (DOT + READY), seperti pola log toggle global.
            if (macro.SavedMacroName is string saved)
                _viewModel.Notify(Localizer.Format("S1553", saved), "#10B981", 3.5);
        };
        _viewModel.RequestOpenAbout += () =>
        {
            var about = new AboutWindow(_viewModel.AppVersionText) { Owner = this };
            about.ShowDialog();
        };
        _viewModel.RequestOpenTemplatePicker += async () =>
        {
            var picker = new TemplatePickerWindow { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedTemplateId is string id)
                await _viewModel.LoadTemplatePageAsync(id);
        };
        _viewModel.RequestOpenSettings += () =>
        {
            var settingsVm = new SettingsViewModel(_host);
            settingsVm.ProfilesChanged += () => _viewModel.RefreshProfilesAndPages();
            settingsVm.Saved += () =>
            {
                _viewModel.Notify(Localizer.Get("S0958"), "#38BDF8", 3.5);
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero) RegisterMasterHotkeys(handle);
            };
            var settings = new SettingsWindow(_host, settingsVm) { Owner = this };
            settings.ShowDialog();
        };
        _viewModel.RequestOpenObsConfig += () =>
        {
            var obsVm = new ObsConfigViewModel(_host);
            obsVm.Saved += () =>
            {
                _viewModel.Notify(Localizer.Get("S0959"), "#38BDF8", 3.5);
            };
            var window = new ObsConfigWindow(obsVm) { Owner = this };
            window.ShowDialog();
        };
        _viewModel.RequestOpenDeckStatus += () =>
        {
            var deckVm = new SettingsViewModel(_host);
            var window = new DeckStatusWindow(_host, deckVm) { Owner = this };
            window.ShowDialog();
        };
        _viewModel.RequestWaitingCapture += () =>
        {
            _waitingWin = new WaitingKeyWindow { Owner = this, DataContext = _viewModel };
            _waitingWin.Closed += (_, _) =>
            {
                _waitingWin = null;
                if (_viewModel.IsCaptureArmed) _viewModel.CancelCapture();
            };
            _waitingWin.ShowDialog();
        };
        _viewModel.RequestWaitingCustomCapture += (title, promptTitle, promptDesc) =>
        {
            _waitingWin = new WaitingKeyWindow { Owner = this, DataContext = _viewModel };
            _waitingWin.SetContent(title, promptTitle, promptDesc);
            _waitingWin.Closed += (_, _) =>
            {
                _waitingWin = null;
                if (_viewModel.IsCaptureArmed) _viewModel.CancelCapture();
            };
            _waitingWin.ShowDialog();
        };
        _viewModel.CaptureCompleted += () => _waitingWin?.Close();
        _viewModel.RequestEditBinding += editorVm =>
        {
            var editor = new BindingEditorWindow(editorVm) { Owner = this };
            editor.ShowDialog();
            if (editor.Changed)
            {
                _viewModel.RefreshBindings();
                string desc = string.IsNullOrWhiteSpace(editorVm.Label) ? editorVm.PhysicalKeyText : editorVm.Label;
                _viewModel.Notify(Localizer.Format("S0960", desc), "#10B981", 3.5);
            }

        };
        // §124 butir 3: dialog kecil ganti-nama kartu - Owner yang
        // sama dengan semua modal lain (this), bukan Owner baru;
        // Accepted dibaca pemanggil (RenameCard) sesudah
        // ShowDialog kembali, seperti pola PageEdit.
        _viewModel.RequestRenameBinding += renameVm =>
        {
            var rename = new RenameBindingWindow(renameVm) { Owner = this };
            rename.ShowDialog();
        };
        // §132: kehilangan fokus jendela tidak lagi menutup apa pun -
        // menu konteks menutup sendiri (framework) dan seleksi
        // dipertahankan melewati Deactivated.
        _viewModel.RequestEditProfile += profileVm =>
        {
            var window = new ProfileEditWindow(profileVm) { Owner = this };
            bool? result = window.ShowDialog();
            // §104 (D5): dulu Notify "Profil diperbarui" selalu tampil,
            // termasuk saat Batal - info sukses palsu. Kini hanya bila
            // modal ditutup lewat Simpan (DialogResult true), dan bukan
            // jalur Duplikat (yang sudah punya Notify S0373 sendiri).
            if (result == true && !profileVm.WantsDuplicate)
                _viewModel.Notify(Localizer.Format("S0961", profileVm.Name), "#38BDF8", 3.0);
        };
        _viewModel.RequestEditPage += pageVm =>
        {
            var window = new PageEditWindow(pageVm) { Owner = this };
            window.ShowDialog();
            // §104 (D5): Notify "Mode diperbarui" di sini DIHAPUS - dulu
            // selalu tampil termasuk saat Batal. Sukses sudah
            // diberitahu MainViewModel sendiri (S0397 dibuat / S0398
            // diperbarui, hanya saat Save lolos); Batal = diam, benar.
        };
        _viewModel.ConfirmPrompt = message =>
            AppDialog.Show(this, message, "Keystream",
                AppDialogButtons.YesNo, AppDialogIcon.Warning)
            == AppDialogResult.Yes;
        // §171 Task 6: prompt impor Global (Ganti/Gabung/Lewati) -
        // hook yang sama polanya dengan ConfirmPrompt di atas.
        _viewModel.RequestGlobalImportChoice = count => GlobalImport.Ask(this, count);
        // Every click on a page row activates that page - including a
        // click on the page that is already selected, which produces no
        // selection change and once left the user stuck in global editing.
        // §153 B: penjaga sidebar selama Mode Pilih - klik area seleksi
        // baris (bukan tombol-dalam) DITAHAN: SelectedPage tidak
        // berubah, seleksi + mode + snapshot Urungkan tidak disentuh,
        // tanpa Save, footer menampilkan pemberitahuan S1765 sekali
        // per klik yang ditahan. Penjaga HANYA di level UI ini
        // (handler PreviewMouseLeftButtonDown PageList): jalur
        // programatik (aksi macro pindah halaman, auto-switch,
        // OnSelectedPageChanged/RefreshPages) TIDAK disentuh dan
        // tetap jalan selama Mode Pilih. Klik tombol-dalam baris
        // (pensil/duplikat/sampah) dilewatkan apa adanya - perilaku
        // lama (ActivatePage + command tombol) dipertahankan, jadi
        // command tombol itu sendiri masih bisa mengganti halaman
        // lewat RefreshPages/SelectedPage (lihat laporan §153 B).
        PageList.AddHandler(PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                // §154: AddHandler ini didaftarkan dengan
                // handledEventsToo=true, jadi handler ini TETAP jalan
                // sesudah handler wadah SidebarRoot menandai event
                // Handled - tanpa guard di bawah, klik baris di Mode
                // Pilih akan memanggil notice KEDUA dan ActivatePage.
                // Guard ini + urutan tunneling (wadah dulu, anak
                // kemudian) membuktikan tepat SATU notice per klik
                // kiri yang ditahan, dan ActivatePage tidak jalan.
                if (e.Handled) return;
                if (e.OriginalSource is System.Windows.DependencyObject src)
                {
                    var item = ItemsControl.ContainerFromElement(PageList, src) as ListBoxItem;
                    if (item?.DataContext is Keystream.Core.Config.PageConfig page)
                    {
                        if (_viewModel.IsSelectMode && !IsInsideInnerButton(src))
                        {
                            ShowSidebarSelectModeGuardNotice();
                            e.Handled = true;
                            return;
                        }
                        _viewModel.ActivatePage(page);
                    }
                }
            }), true);
        // §153 B: choke point keyboard + teks untuk KEDUA kontrol
        // seleksi sidebar. PageList adalah Selector: panah/Home/End/
        // PageUp/PageDown/Space dan ketikan TextSearch (PageConfig.
        // ToString() = Name) mengubah SelectedItem TANPA melewati
        // handler mouse di atas, jadi harus ditahan di PreviewKeyDown/
        // PreviewTextInput (tunneling, sebelum Selector menanganinya).
        // ComboBox profil: klik mouse ditahan di
        // PreviewMouseLeftButtonDown (dropdown tidak sempat terbuka,
        // jadi klik item dropdown tidak pernah terjadi); panah/F4/
        // Space/roda-mouse ditahan di PreviewKeyDown/PreviewMouseWheel.
        // Esc TIDAK ditahan di sini - keluar mode via Esc tetap milik
        // Window_PreviewKeyDown. Di luar Mode Pilih semua handler
        // return lebih awal: perilaku normal tidak berubah.
        PageList.PreviewKeyDown += SidebarSelectionGuard_PreviewKeyDown;
        PageList.PreviewTextInput += SidebarSelectionGuard_PreviewTextInput;
        ProfileComboBox.PreviewMouseLeftButtonDown += SidebarSelectionGuard_PreviewMouseLeftButtonDown;
        ProfileComboBox.PreviewKeyDown += SidebarSelectionGuard_PreviewKeyDown;
        ProfileComboBox.PreviewTextInput += SidebarSelectionGuard_PreviewTextInput;
        ProfileComboBox.PreviewMouseWheel += SidebarSelectionGuard_PreviewMouseWheel;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
    }

    // --- §153 B: penjaga sidebar selama Mode Pilih ----------------------
    // Satu-satunya pemberitahuan penjaga: footer amber #F59E0B, 3.5 dtk
    // (preseden aksi-terblokir S1642). Dipanggil SEKALI per gestur yang
    // ditahan - tidak ada dialog (WORKFLOW.md §7).
    private void ShowSidebarSelectModeGuardNotice()
        => _viewModel.Notify(Localizer.Get("S1765"), "#F59E0B", 3.5);

    // Tombol navigasi/seleksi Selector yang harus ditahan selama Mode
    // Pilih. Alt+panah melaporkan Key.System: kunci efektif dibaca dari
    // SystemKey dalam kasus itu (Alt+Bawah membuka dropdown ComboBox).
    private void SidebarSelectionGuard_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        var key = e.Key == System.Windows.Input.Key.System
            ? e.SystemKey
            : e.Key;
        switch (key)
        {
            case System.Windows.Input.Key.Up:
            case System.Windows.Input.Key.Down:
            case System.Windows.Input.Key.Home:
            case System.Windows.Input.Key.End:
            case System.Windows.Input.Key.PageUp:
            case System.Windows.Input.Key.PageDown:
            case System.Windows.Input.Key.Space:
            case System.Windows.Input.Key.F4:
                ShowSidebarSelectModeGuardNotice();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    // Ketikan huruf (TextSearch) melompatkan seleksi ke item bernama
    // itu - PageConfig.ToString() = Name, ProfileConfig.ToString() =
    // Name - jadi ditahan juga selama Mode Pilih. Tidak ada kontrol
    // input teks di dalam kedua kontrol ini, jadi tidak ada ketikan
    // sah yang hilang.
    private void SidebarSelectionGuard_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        ShowSidebarSelectModeGuardNotice();
        e.Handled = true;
    }

    // Klik pada ComboBox profil yang tertutup: menahan klik berarti
    // dropdown tidak terbuka, jadi pemilihan item lewat mouse tidak
    // pernah terjadi. (Dropdown yang SUDAH terbuka saat mode masuk
    // adalah residu: popup hidup di pohon visual terpisah dan klik
    // itemnya tidak melewati handler ini - lihat laporan §153 B.)
    private void SidebarSelectionGuard_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        ShowSidebarSelectModeGuardNotice();
        e.Handled = true;
    }

    // Roda mouse di atas ComboBox yang fokus menggilir pilihan profil;
    // ditahan selama Mode Pilih. Roda di atas PageList TIDAK ditahan:
    // roda di sana hanya menggulir daftar, tidak mengubah seleksi.
    private void SidebarSelectionGuard_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        ShowSidebarSelectModeGuardNotice();
        e.Handled = true;
    }

    // --- §154: pembekuan region Mode Pilih (mode fokus) ---------------
    // SATU set handler preview yang sama untuk ketiga wadah beku
    // (SidebarRoot, HeaderRoot, TitleRowRoot - dipasang di atribut
    // XAML tiap wadah). Tunneling: handler wadah jalan SEBELUM
    // handler anak mana pun, jadi satu choke point per wadah; bar
    // Mode Pilih, kartu, dan menu kartu BUKAN keturunan wadah
    // mana pun, jadi tidak pernah tersentuh handler ini.
    // Flag IsSelectMode dibaca LANGSUNG dari _viewModel di tiap
    // event - TIDAK ada state beku/latch milik handler ini, jadi
    // tidak ada flag yang bisa macet dan mengunci UI ("senjata
    // makan tuan"). Di luar Mode Pilih: return lebih awal tanpa
    // menyentuh event - perilaku + tampilan identik §153.
    // Esc TIDAK ditahan (keluar mode milik Window_PreviewKeyDown;
    // untuk TextBox/ComboBox yang fokus, Esc milik kontrol itu -
    // lihat Window_PreviewKeyDown + DashboardSearchBox_KeyDown).
    // Tab/Shift+Tab TIDAK ditahan: fokus harus bisa KELUAR dari
    // region beku; menjebak fokus adalah bug baru.
    // TIDAK ADA IsEnabled=false di region mana pun: elemen disabled
    // dilewati hit-testing WPF, klik bisa jatuh ke latar dashboard
    // dan memicu keluar-mode lewat klik-area-kosong - persis
    // keluar-mode diam-diam yang diblokir pekerjaan ini.
    // Roda mouse SENGAJA tidak ditahan di sini (hanya scroll);
    // guard roda §153 di atas tidak diubah.
    private void SidebarFreeze_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        ShowSidebarSelectModeGuardNotice();
        e.Handled = true;
    }

    private void SidebarFreeze_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        e.Handled = true;
    }

    private void SidebarFreeze_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_viewModel.IsSelectMode) return;
        var key = e.Key == System.Windows.Input.Key.System
            ? e.SystemKey
            : e.Key;
        if (key == System.Windows.Input.Key.Escape || key == System.Windows.Input.Key.Tab) return;
        e.Handled = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowTheming.ApplyDarkTitleBar(this);
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
        RegisterMasterHotkeys(handle);
    }

    private void RegisterMasterHotkeys(IntPtr handle)
    {
        UnregisterMasterHotkeys(handle);
        _host.MasterHotkeyFailures.Clear();

        RegisterOne(PanicHotkeyId, Localizer.Get("S0963"), _host.Config.Settings.PauseHotkey);
        RegisterOne(ToggleGlobalHotkeyId, Localizer.Get("S0964"), _host.Config.Settings.ToggleGlobalHotkey);
        RegisterOne(NextProfileHotkeyId, Localizer.Get("S0965"), _host.Config.Settings.NextProfileHotkey);
        RegisterOne(PrevProfileHotkeyId, Localizer.Get("S0966"), _host.Config.Settings.PrevProfileHotkey);
        RegisterOne(ToggleRecordingHotkeyId, Localizer.Get("S1719"), _host.Config.Settings.ToggleRecordingHotkey);
        RegisterOne(TogglePlaybackHotkeyId, Localizer.Get("S1720"), _host.Config.Settings.TogglePlaybackHotkey);
        RegisterOne(PauseResumePlaybackHotkeyId, Localizer.Get("S1721"), _host.Config.Settings.PauseResumePlaybackHotkey);

        void RegisterOne(int id, string name, string hotkeyText)
        {
            if (string.IsNullOrWhiteSpace(hotkeyText)) return; // tidak dikonfigurasi - bukan kegagalan
            if (!KeyChord.TryParse(hotkeyText, out var chord, out _) || chord is null)
            {
                _host.MasterHotkeyFailures.Add(Localizer.Format("S0967", name, hotkeyText));
                CrashLog.WriteDebug($"Master shortcut gagal didaftarkan (tidak ter-parse): {name} ({hotkeyText}).");
                return;
            }
            uint fsModifiers = 0;
            foreach (var mod in chord.Modifiers)
            {
                if (mod == 0x11) fsModifiers |= 0x0002; // MOD_CONTROL
                else if (mod == 0x12) fsModifiers |= 0x0001; // MOD_ALT
                else if (mod == 0x10) fsModifiers |= 0x0004; // MOD_SHIFT
                else if (mod == 0x5B || mod == 0x5C) fsModifiers |= 0x0008; // MOD_WIN
            }
            if (NativeMethods.RegisterHotKey(handle, id, fsModifiers, chord.MainKey))
                _registeredHotkeys.Add(id);
            else
            {
                // Dulu false di sini ditelan diam-diam (§45): Panic
                // "tidak bekerja" tanpa alasan apa pun yang terlihat.
                _host.MasterHotkeyFailures.Add($"{name} ({hotkeyText})");
                CrashLog.WriteDebug($"Master shortcut gagal didaftarkan (RegisterHotKey): {name} ({hotkeyText}).");
            }
        }
    }

    private void UnregisterMasterHotkeys(IntPtr handle)
    {
        foreach (var id in _registeredHotkeys)
            NativeMethods.UnregisterHotKey(handle, id);
        _registeredHotkeys.Clear();
    }

    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            // F-C7: a monitor was added/removed or a resolution
            // changed - re-clamp the floating panel if it is
            // visible. The host no-ops when the panel is hidden or
            // absent, so a user-hidden panel is never resurrected.
            // Deliberately not handled: no default action to
            // suppress, and the message must keep flowing. This
            // window lives for the whole run (Hide to tray keeps
            // its HWND), so the broadcast reaches it even while
            // the main window is hidden. Never throws out of a
            // window procedure.
            try { _host.PanelHost?.ReclampVisiblePanel(); }
            catch { /* a clamp failure must never break the pump */ }
        }
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            // §137 A6 - penjaga capture: selama capture apa pun sedang
            // berjalan, WM_HOTKEY ditelan tanpa memicu aksi apa pun.
            // Dua flag yang diperiksa (keduanya flag yang sudah ada):
            // (1) _host.SuppressEngineEvents - naik saat capture chord
            //     di SettingsWindow (StartRecording, disiplin
            //     simpan/pulihkan), saat capture tombol deck armed
            //     (MainViewModel.ArmCaptureSuppression), dan saat
            //     listening editor (BindingEditorViewModel);
            // (2) _viewModel.IsCaptureArmed - capture tombol deck itu
            //     sendiri (sabuk-pengaman kedua; flag (1) sudah naik
            //     saat armed, kecuali jendela sempit antara _captureArmed
            //     = false dan pemulihan flag di RunOnUi).
            // SENGAJA TIDAK diperiksa: status sesi panel mengambang -
            // ketiga hotkey §137 adalah kendali SESI itu (mulai/
            // berhenti rekaman & pemutaran); memblokir saat sesi aktif
            // akan membuat Toggle Recording tak pernah bisa berhenti.
            // Rekaman macro juga tidak menelan tombol (KeyboardRecorder
            // selalu CallNextHookEx), jadi chord yang ditekan saat
            // merekam memang sampai ke sini; apakah chord stop ikut
            // terekam sebagai langkah adalah urusan Worker B / hook,
            // dibuktikan hanya di PC (CARA §137, lihat laporan Worker A).
            // Penjaga ini berlaku untuk ketujuh cabang, termasuk 4 lama:
            // chord Panic/Global/Next/Prev yang ditekan selagi merekam
            // chord sebelumnya memicu aksi diam-diam tanpa penjaga ini.
            if (_host.SuppressEngineEvents || _viewModel.IsCaptureArmed)
            {
                handled = true;
            }
            else if (id == PanicHotkeyId)
            {
                _viewModel.TogglePassthroughFromHotkey();
                handled = true;
            }
            else if (id == ToggleGlobalHotkeyId)
            {
                _viewModel.ToggleGlobalEnabledCommand.Execute(null);
                handled = true;
            }
            else if (id == NextProfileHotkeyId)
            {
                _viewModel.NextProfile();
                handled = true;
            }
            else if (id == PrevProfileHotkeyId)
            {
                _viewModel.PreviousProfile();
                handled = true;
            }
            else if (id == ToggleRecordingHotkeyId)
            {
                // §137 (Worker B owns ToggleRecordingFromHotkey):
                // dipanggil persis seperti cabang-cabang di atas.
                CrashLog.WriteDebug($"WM_HOTKEY: ToggleRecording (id 0x{ToggleRecordingHotkeyId:X4}) ditekan.");
                _viewModel.ToggleRecordingFromHotkey();
                handled = true;
            }
            else if (id == TogglePlaybackHotkeyId)
            {
                // §137 (Worker B owns TogglePlaybackFromHotkey).
                CrashLog.WriteDebug($"WM_HOTKEY: TogglePlayback (id 0x{TogglePlaybackHotkeyId:X4}) ditekan.");
                _viewModel.TogglePlaybackFromHotkey();
                handled = true;
            }
            else if (id == PauseResumePlaybackHotkeyId)
            {
                // §137 (Worker B owns PauseResumePlaybackFromHotkey).
                CrashLog.WriteDebug($"WM_HOTKEY: PauseResumePlayback (id 0x{PauseResumePlaybackHotkeyId:X4}) ditekan.");
                _viewModel.PauseResumePlaybackFromHotkey();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        UnregisterMasterHotkeys(new WindowInteropHelper(this).Handle);
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Minimize_Click(object sender, RoutedEventArgs e) => ShellChrome.Minimize(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

}
