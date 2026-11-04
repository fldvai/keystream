using System;

namespace Keystream.App.Host;

/// <summary>§152 - aritmetika skala DPI murni untuk menu tray.
/// WPF-FREE dan WinForms-FREE (hanya tipe System), tanpa P/Invoke:
/// berkas ini hanya mengubah angka dasar §151 menjadi angka skala.
/// Sumber faktor (MonitorDpi / Graphics.DpiX) TIDAK tinggal di sini;
/// yang tinggal di sini hanyalah rumus + konstanta dasar, agar
/// wb-harness dapat menautkan berkas ini langsung di Linux dan
/// membuktikan identitas faktor 1.0 terhadap literal §151.
///
/// Aturan pembulatan (SATU aturan, dipakai Scale): MidpointRounding.
/// AwayFromZero - nilai .5 dibulatkan menjauhi nol (12.5 -&gt; 13,
/// 27.5 -&gt; 28). Dipilih karena geometri piksel tidak mengenal
/// "banker's rounding": 27.5px harus menjadi 28px, bukan 28px
/// kadang 27px kadang 28px tergantung genap/ganjil.
///
/// Jepit faktor: [0.5, 4.0]. 96 DPI -&gt; 1.0. Di bawah 48 DPI
/// (tidak realistis untuk monitor) dijepit ke 0.5; di atas 384 DPI
/// (400%) dijepit ke 4.0 agar geometri tidak meledak pada DPI
/// ekstrem/korup. DPI &lt;= 0 atau non-finite -&gt; 1.0 (tampilan
/// §151 persis, tanpa throw).
///
/// Konstanta dasar §151 tinggal HANYA di sini. Renderer dan menu
/// tidak boleh menulis literal geometri sendiri; mereka membaca
/// konstanta ini lewat Scale/ForFactor.</summary>
public static class TrayDpiScale
{
    /// <summary>DPI acuan 96 (100%).</summary>
    public const double BaseDpi = 96.0;

    /// <summary>Faktor terkecil sesudah dijepit (50%).</summary>
    public const double MinFactor = 0.5;

    /// <summary>Faktor terbesar sesudah dijepit (400%).</summary>
    public const double MaxFactor = 4.0;

    // --- Geometri dasar renderer §151 (ModernTrayMenu.cs:360-364) ---
    public const int BaseColIcon = 12;
    public const int BaseColIconW = 22;
    public const int BaseColText = 38;
    public const int BaseDotSize = 8;
    public const int BaseRightInset = 8;
    public const int BaseHoverX = 3;
    public const int BaseHoverY = 1;
    public const int BaseHoverWidthReduction = 6;
    public const int BaseHoverHeightReduction = 2;
    public const int BaseSeparatorInset = 10;
    public const int BaseRadius = 4;
    /// <summary>Radius sudut luar menu membulat (menu contoh).</summary>
    public const int BaseOuterRadius = 12;
    public const int BaseBorderWidth = 1;

    // --- Padding / MinimumSize dasar menu §151 ---
    public const int BaseMenuPadding = 2;
    public const int BaseDropdownPadding = 2;
    public const int BaseRootMinWidth = 220;
    public const int BaseSubmenuMinWidth = 170;
    public const int BaseHeaderPadLeft = 10;
    public const int BaseHeaderPadTop = 8;
    public const int BaseHeaderPadRight = 28;
    public const int BaseHeaderPadBottom = 6;
    public const int BaseItemPadLeft = 10;
    public const int BaseItemPadTop = 6;
    public const int BaseItemPadRight = 20;
    public const int BaseItemPadBottom = 6;
    public const int BaseSubItemPadLeft = 10;
    public const int BaseSubItemPadTop = 5;
    public const int BaseSubItemPadRight = 14;
    public const int BaseSubItemPadBottom = 5;

    /// <summary>Faktor skala dari DPI mentah. DPI &lt;= 0 atau
    /// non-finite -&gt; 1.0; selebihnya dpi/96, dijepit [0.5, 4.0].
    /// Tidak pernah throw.</summary>
    public static double FactorForDpi(double dpi)
    {
        try
        {
            if (double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi <= 0)
                return 1.0;
            double factor = dpi / BaseDpi;
            if (factor < MinFactor) return MinFactor;
            if (factor > MaxFactor) return MaxFactor;
            return factor;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>Normalisasi faktor mentah (dari Graphics.DpiX/96 atau
    /// sumber lain): non-finite / &lt;= 0 -&gt; 1.0, selebihnya
    /// dijepit [0.5, 4.0]. Tidak pernah throw.</summary>
    public static double NormalizeFactor(double factor)
    {
        try
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
                return 1.0;
            if (factor < MinFactor) return MinFactor;
            if (factor > MaxFactor) return MaxFactor;
            return factor;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>Skala satu nilai dasar. Pembulatan:
    /// MidpointRounding.AwayFromZero. Faktor tak valid dinormalisasi
    /// ke 1.0 (nilai dasar kembali utuh). Tidak pernah throw.</summary>
    public static int Scale(int baseValue, double factor)
    {
        try
        {
            double f = NormalizeFactor(factor);
            return (int)Math.Round(baseValue * f, MidpointRounding.AwayFromZero);
        }
        catch
        {
            return baseValue;
        }
    }

    /// <summary>Skala satu padding (kiri, atas, kanan, bawah) dari
    /// nilai DASAR - pemanggil harus selalu mengumpan nilai dasar,
    /// bukan nilai yang sudah diskala, agar pemanggilan ulang
    /// menghasilkan nilai identik (tanpa compounding).</summary>
    public static (int Left, int Top, int Right, int Bottom) ScalePadding(
        int left, int top, int right, int bottom, double factor) =>
        (Scale(left, factor), Scale(top, factor), Scale(right, factor), Scale(bottom, factor));

    /// <summary>Skala satu ukuran (lebar, tinggi) dari nilai DASAR.</summary>
    public static (int Width, int Height) ScaleSize(int width, int height, double factor) =>
        (Scale(width, factor), Scale(height, factor));

    /// <summary>Geometri renderer untuk satu faktor.</summary>
    public static TrayMenuGeometry ForFactor(double factor) =>
        TrayMenuGeometry.FromFactor(NormalizeFactor(factor));

    /// <summary>Geometri renderer untuk satu DPI mentah.</summary>
    public static TrayMenuGeometry ForDpi(double dpi) =>
        TrayMenuGeometry.FromFactor(FactorForDpi(dpi));
}

/// <summary>Geometri renderer menu tray yang sudah diskala dari
/// konstanta dasar §151. Dibangun hanya lewat
/// <see cref="TrayDpiScale.ForFactor"/> / <see cref="TrayDpiScale.ForDpi"/>
/// agar faktor selalu ternormalisasi. Pada faktor 1.0 setiap field
/// SAMA PERSIS dengan literal §151 (dibuktikan harness, bukan asumsi).
/// BorderWidth adalah float: lebar pen diskala kontinu dengan
/// minimum 1 (pada 125% = 1.25, bukan melompat ke 2 seperti
/// pembulatan int).</summary>
public readonly record struct TrayMenuGeometry(
    double Factor,
    int ColIcon,
    int ColIconW,
    int ColText,
    int DotSize,
    int RightInset,
    int HoverX,
    int HoverY,
    int HoverWidthReduction,
    int HoverHeightReduction,
    int SeparatorInset,
    int Radius,
    float BorderWidth)
{
    internal static TrayMenuGeometry FromFactor(double factor) => new(
        factor,
        TrayDpiScale.Scale(TrayDpiScale.BaseColIcon, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseColIconW, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseColText, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseDotSize, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseRightInset, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseHoverX, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseHoverY, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseHoverWidthReduction, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseHoverHeightReduction, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseSeparatorInset, factor),
        TrayDpiScale.Scale(TrayDpiScale.BaseRadius, factor),
        Math.Max(1f, (float)factor));
}
