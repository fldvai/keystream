using System.Collections;
using Keystream.Core.Localization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Keystream.App.Views;

/// <summary>Pemilih profil PANEL, bukan dropdown (25 Okt 2026).
/// Menggantikan SearchableComboBox: tidak ada Popup sama sekali, jadi
/// kelas bug capture popup (klik harus sambil menahan) hilang total.
/// Tombol header menampilkan pilihan; klik membuka panel DI TEMPAT
/// (kotak cari + daftar) yang mendorong konten di bawahnya - kedua
/// jendela pemakai sudah berada dalam ScrollViewer sehingga panel
/// tidak pernah meluber keluar modal, melainkan discroll.
/// Penutupan: pilih item, Esc, toggle header, atau klik di luar
/// kontrol (handler PreviewMouseDown jendela induk - andal di sini
/// karena panel berada dalam visual tree yang sama, tidak seperti
/// popup yang visual tree-nya terpisah). Fokus kotak cari sinkron
/// dan andal karena elemennya sudah dimuat dalam tree yang sama.</summary>
public partial class SearchablePanelPicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchablePanelPicker),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedKeyProperty =
        DependencyProperty.Register(nameof(SelectedKey), typeof(string), typeof(SearchablePanelPicker),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedKeyChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchablePanelPicker),
            new PropertyMetadata("", OnPlaceholderChanged));

    /// <summary>Mode multi-centang (25 Okt 2026, hanya dipakai editor
    /// tile browser). False = perilaku tunggal lama; True = daftar
    /// berubah menjadi CheckBox, panel tidak menutup saat mencentang,
    /// dan header menampilkan jumlah centang. Manager grup tidak
    /// mengikat properti ini sehingga tetap tunggal.</summary>
    public static readonly DependencyProperty IsMultiSelectProperty =
        DependencyProperty.Register(nameof(IsMultiSelect), typeof(bool), typeof(SearchablePanelPicker),
            new PropertyMetadata(false, OnMultiModeChanged));

    /// <summary>Daftar kunci yang dicentang (koleksi milik VM, panel
    /// hanya menambah/menghapus di tempat - referensi tidak pernah
    /// diganti panel). Null = tidak ada multi (manager).</summary>
    public static readonly DependencyProperty CheckedKeysProperty =
        DependencyProperty.Register(nameof(CheckedKeys), typeof(IList), typeof(SearchablePanelPicker),
            new PropertyMetadata(null, OnCheckedKeysChanged));

    /// <summary>§126: Esc dua tahap HANYA untuk pemilih Windows
    /// Commands di editor (EscapeClearsFilterFirst="True" di sana).
    /// Esc pertama membersihkan teks filter (pola hapus-teks
    /// DashboardSearchBox_KeyDown §124) dan panel tetap terbuka; Esc
    /// kedua - teks sudah kosong - menutup panel seperti dulu.
    /// Default false: perilaku pemilih profil Browser dan manager
    /// grup (Esc langsung menutup panel) TIDAK berubah.</summary>
    public static readonly DependencyProperty EscapeClearsFilterFirstProperty =
        DependencyProperty.Register(nameof(EscapeClearsFilterFirst), typeof(bool), typeof(SearchablePanelPicker),
            new PropertyMetadata(false));

    /// <summary>§126 pekerja C (§A): semantik filter AND-per-kata +
    /// keyword HANYA untuk pemilih Windows Commands di editor
    /// (UseWordAndFilter="True" di sana). Default false = perilaku
    /// lama pemilih profil Browser / manager grup: frasa literal
    /// Contains atas tampilan + kunci, tanpa pecah kata, tanpa
    /// keyword - kueri multi-kata berperilaku persis seperti sebelum
    /// §126. Pola flag yang sama dengan EscapeClearsFilterFirst.</summary>
    public static readonly DependencyProperty UseWordAndFilterProperty =
        DependencyProperty.Register(nameof(UseWordAndFilter), typeof(bool), typeof(SearchablePanelPicker),
            new PropertyMetadata(false));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string SelectedKey
    {
        get => (string)GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool IsMultiSelect
    {
        get => (bool)GetValue(IsMultiSelectProperty);
        set => SetValue(IsMultiSelectProperty, value);
    }

    public IList? CheckedKeys
    {
        get => (IList?)GetValue(CheckedKeysProperty);
        set => SetValue(CheckedKeysProperty, value);
    }

    public bool EscapeClearsFilterFirst
    {
        get => (bool)GetValue(EscapeClearsFilterFirstProperty);
        set => SetValue(EscapeClearsFilterFirstProperty, value);
    }

    public bool UseWordAndFilter
    {
        get => (bool)GetValue(UseWordAndFilterProperty);
        set => SetValue(UseWordAndFilterProperty, value);
    }

    private sealed class ItemRow : INotifyPropertyChanged
    {
        public required string Key { get; init; }
        public required string Display { get; init; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private List<ItemRow> _rows = new();
    private INotifyCollectionChanged? _tracked;
    private NotifyCollectionChangedEventHandler? _collectionHandler;
    private INotifyCollectionChanged? _trackedChecked;
    private NotifyCollectionChangedEventHandler? _checkedHandler;
    private Window? _owner;

    public SearchablePanelPicker()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _owner = Window.GetWindow(this);
            if (_owner is not null)
            {
                _owner.PreviewMouseDown += Owner_PreviewMouseDown;
                _owner.Deactivated += Owner_Deactivated;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_owner is not null)
            {
                _owner.PreviewMouseDown -= Owner_PreviewMouseDown;
                _owner.Deactivated -= Owner_Deactivated;
                _owner = null;
            }
            DetachCollection();
            DetachChecked();
        };
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SearchablePanelPicker)d).AttachCollection();

    private static void OnSelectedKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SearchablePanelPicker)d).RefreshHeader();

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SearchablePanelPicker)d).RefreshHeader();

    private static void OnMultiModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (SearchablePanelPicker)d;
        // WPF MELARANG DisplayMemberPath dan ItemTemplate aktif
        // bersamaan ("Cannot set both...") - asumsi lama bahwa
        // ItemTemplate mengalahkan DisplayMemberPath SALAH, dan
        // itulah crash saat mengaktifkan multi (25 Okt 2026).
        // Urutan wajib: kosongkan dulu yang satu, baru isi yang lain.
        if (picker.IsMultiSelect)
        {
            picker.PickList.DisplayMemberPath = null;
            picker.PickList.ItemTemplate = (DataTemplate)picker.Resources["CheckRowTemplate"];
        }
        else
        {
            picker.PickList.ItemTemplate = null;
            picker.PickList.DisplayMemberPath = "Display";
        }
        picker.RefreshItems();
    }

    private static void OnCheckedKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SearchablePanelPicker)d).AttachChecked();

    private void AttachCollection()
    {
        DetachCollection();
        if (ItemsSource is INotifyCollectionChanged notify)
        {
            _tracked = notify;
            _collectionHandler = (_, _) => RefreshItems();
            notify.CollectionChanged += _collectionHandler;
        }
        RefreshItems();
    }

    private void DetachCollection()
    {
        if (_tracked is not null && _collectionHandler is not null)
            _tracked.CollectionChanged -= _collectionHandler;
        _tracked = null;
        _collectionHandler = null;
    }

    private void AttachChecked()
    {
        DetachChecked();
        if (CheckedKeys is INotifyCollectionChanged notify)
        {
            _trackedChecked = notify;
            _checkedHandler = (_, _) => { SyncRowsFromChecked(); RefreshHeader(); };
            notify.CollectionChanged += _checkedHandler;
        }
        SyncRowsFromChecked();
        RefreshHeader();
    }

    private void DetachChecked()
    {
        if (_trackedChecked is not null && _checkedHandler is not null)
            _trackedChecked.CollectionChanged -= _checkedHandler;
        _trackedChecked = null;
        _checkedHandler = null;
    }

    /// <summary>Baris mengikuti koleksi centang VM (idempotent: setter
    /// IsChecked mengabaikan nilai yang sama, jadi tidak ada loop
    /// dengan handler PropertyChanged di bawah).</summary>
    private void SyncRowsFromChecked()
    {
        var keys = CheckedKeys;
        foreach (var row in _rows)
            row.IsChecked = keys is not null && keys.Cast<string>().Contains(row.Key);
    }

    private void Row_IsCheckedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ItemRow row || e.PropertyName != nameof(ItemRow.IsChecked)) return;
        var keys = CheckedKeys;
        if (keys is null) return;
        if (row.IsChecked)
        {
            if (!keys.Cast<string>().Contains(row.Key)) keys.Add(row.Key);
        }
        else
        {
            if (keys.Cast<string>().Contains(row.Key)) keys.Remove(row.Key);
        }
        RefreshHeader();
    }

    private void RefreshItems()
    {
        foreach (var old in _rows)
            old.PropertyChanged -= Row_IsCheckedChanged;
        var rows = new List<ItemRow>();
        if (ItemsSource is not null)
        {
            foreach (object item in ItemsSource)
            {
                var type = item.GetType();
                // §126: kunci dibaca dari "Key" (BrowserProfileChoice),
                // jatuh ke "Value" (ActionCatalog.ValueOption - entri
                // Windows Commands tidak punya properti Key; tanpa
                // fallback ini semua baris berkunci "" dan header +
                // pilihan pecah). Display tetap dari "Display".
                string key = type.GetProperty("Key")?.GetValue(item)?.ToString() ?? "";
                if (string.IsNullOrEmpty(key))
                    key = type.GetProperty("Value")?.GetValue(item)?.ToString() ?? "";
                string display = type.GetProperty("Display")?.GetValue(item)?.ToString() ?? item.ToString() ?? "";
                var row = new ItemRow { Key = key, Display = display };
                row.PropertyChanged += Row_IsCheckedChanged;
                rows.Add(row);
            }
        }
        _rows = rows;
        SyncRowsFromChecked();
        ApplyFilter();
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        if (IsMultiSelect)
        {
            int count = CheckedKeys?.Cast<string>().Count() ?? 0;
            HeaderText.Text = count > 0 ? Localizer.Format("S0986", count) : Placeholder;
            return;
        }
        var row = _rows.FirstOrDefault(r => r.Key == SelectedKey);
        HeaderText.Text = row is not null ? row.Display : Placeholder;
    }

    private void ApplyFilter()
    {
        // §126 pekerja C (§A): dua semantik di belakang flag
        // UseWordAndFilter (default false = perilaku lama). True
        // (hanya pemilih Windows Commands): substring AND-per-kata +
        // keyword atas nama tampilan + kunci entri - logika WPF-free
        // di WindowsCommandFilter. False (pemilih profil Browser /
        // manager grup): frasa literal Contains seperti sebelum §126,
        // tanpa pecah kata dan tanpa keyword. Urutan katalog utuh di
        // kedua jalur; tak-cocok = daftar kosong, tanpa pesan/panel
        // tambahan. Hanya ItemsSource daftar yang diganti: _rows dan
        // SelectedKey tidak disentuh filter.
        PickList.ItemsSource = UseWordAndFilter
            ? WindowsCommandFilter.Filter(FilterBox.Text, _rows, r => r.Display, r => r.Key)
            : WindowsCommandFilter.FilterContainsPhrase(FilterBox.Text, _rows, r => r.Display, r => r.Key);
    }

    private bool IsOpen => PanelBorder.Visibility == Visibility.Visible;

    private void OpenPanel()
    {
        RefreshItems();
        FilterBox.Text = "";
        ApplyFilter();
        PanelBorder.Visibility = Visibility.Visible;
        FilterBox.Focus();
    }

    private void ClosePanel()
    {
        PanelBorder.Visibility = Visibility.Collapsed;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOpen) ClosePanel();
        else OpenPanel();
    }

    private void Owner_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsOpen) return;
        // Klik di dalam kontrol ini ditangani toggle/panel sendiri.
        DependencyObject? hit = e.OriginalSource as DependencyObject;
        while (hit is not null && hit != this)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit == this) return;
        ClosePanel();
    }

    private void Owner_Deactivated(object? sender, EventArgs e) => ClosePanel();

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // §126 dua tahap, hanya bila EscapeClearsFilterFirst
            // (pemilih Windows Commands): Esc pertama membersihkan
            // teks filter, panel tetap terbuka; Esc kedua menutup.
            // Tanpa flag itu, Esc langsung menutup seperti dulu
            // (pemilih profil Browser / manager grup tak berubah).
            // Opt-out Esc-restore §110 (EnableEscapeRestore="False"
            // di XAML) tidak disentuh: handler kelas TextBoxRevert
            // tetap tidak menelan Esc kotak ini.
            if (EscapeClearsFilterFirst && !string.IsNullOrEmpty(FilterBox.Text))
                FilterBox.Text = "";
            else
                ClosePanel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (PickList.ItemsSource is IList list && list.Count > 0 && list[0] is ItemRow row)
            {
                // Multi: Enter mencentang hasil pertama, panel tetap
                // terbuka; tunggal: pilih + tutup seperti biasa.
                if (IsMultiSelect) row.IsChecked = !row.IsChecked;
                else SelectRow(row);
            }
            e.Handled = true;
        }
    }

    private void PickList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsMultiSelect)
        {
            // Status centang dipegang CheckBox; seleksi baris hanya
            // dibersihkan agar klik berikutnya pada baris yang sama
            // tetap terasa (tanpa toggle ganda).
            if (PickList.SelectedItem is not null)
                PickList.SelectedItem = null;
            return;
        }
        if (PickList.SelectedItem is ItemRow row && IsOpen)
            SelectRow(row);
    }

    private void SelectRow(ItemRow row)
    {
        SelectedKey = row.Key;
        RefreshHeader();
        ClosePanel();
    }
}
