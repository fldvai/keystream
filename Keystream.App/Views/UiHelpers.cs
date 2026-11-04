using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Keystream.App.Views;

/// <summary>
/// Turns a tile colour (#RRGGBB) into a vertical gradient of that colour
/// (full → 60% shade), so tiles read as one designed surface instead of
/// flat stickers. Falls back to the dark tile colour on bad input.
/// </summary>
public sealed class HexGradientConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out var b))
            return new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x3A));

        var top = Color.FromRgb(r, g, b);
        var bottom = Color.FromRgb(
            (byte)(r * 0.60), (byte)(g * 0.60), (byte)(b * 0.60));
        return new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// A tile colour darkened by a factor (ConverterParameter, default 0.45).
/// Used for the keycap's side edge under the gradient face, so a tile
/// reads as a raised physical key rather than a flat card.
/// </summary>
public sealed class HexShadeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double factor = parameter is string text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : 0.45;
        var hex = value as string;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out var b))
            return new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1C));
        return new SolidColorBrush(Color.FromRgb(
            (byte)(r * factor), (byte)(g * factor), (byte)(b * factor)));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Readable ink on top of a tile colour (luminance-based).
/// Dark faces keep today's look (White, or muted #8A93A5 with parameter
/// "muted"); bright faces (luma &gt; 130 - light blue, orange, green,
/// yellow) switch to near-black ink so label/icon stay readable.
/// Invalid input falls back to the dark-face colour (the gradient
/// converter's fallback face is dark too, so they stay consistent).</summary>
public sealed class HexInkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool muted = parameter as string == "muted";
        var hex = value as string;
        bool bright = false;
        if (!string.IsNullOrWhiteSpace(hex) && hex.Length == 7 && hex[0] == '#'
            && byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out var b))
        {
            double luma = 0.299 * r + 0.587 * g + 0.114 * b;
            bright = luma > 130;
        }
        if (muted)
            return new SolidColorBrush(bright
                ? Color.FromRgb(0x4B, 0x55, 0x63)
                : Color.FromRgb(0x8A, 0x93, 0xA5));
        return new SolidColorBrush(bright
            ? Color.FromRgb(0x10, 0x14, 0x1D)
            : Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Null → Collapsed, non-null → Visible (parameter "Invert"
/// flips it). Used to switch between a tile's vector icon and its
/// free-form glyph fallback.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool has = value is not null;
        if (parameter as string == "Invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>§151 (P2) - path-string ikon-file → ImageSource yang TIDAK
/// menahan handle file. Dulu binding <c>Image.Source</c> langsung ke
/// string path (MainWindow.xaml, BindingEditorWindow.xaml) sehingga
/// konverter bawaan WPF memakai BitmapCacheOption.OnDemand dan decoder
/// WIC menahan handle PNG selama tile terlihat - File.Delete atas PNG
/// itu gagal sharing-violation saat Reset Data (§150/F1). Converter ini
/// memuat lewat FileStream yang dibuka + di-dispose sendiri dengan
/// CacheOption.OnLoad (pola yang sudah terbukti di jalur import:
/// IconStore.cs:61,89 dan FaviconFetcher.cs:79) lalu Freeze(), sehingga
/// handle dilepas begitu EndInit selesai dan gambar aman dipakai
/// lintas thread. Keputusan fallback didelegasikan seluruhnya ke
/// FileIconImageLogic.ResolveLoadablePath (berkas WPF-free, diuji
/// harness): input hilang/tak-valid mengembalikan null - sama seperti
/// hari ini FileIconPath mengembalikan null dan Image.Source menjadi
/// null - dan converter ini TIDAK PERNAH melempar: file rusak/tak
/// terbaca saat decode juga jatuh ke null (slot kosong, seperti decode
/// gagal hari ini), bukan crash. Jalur glyph katalog (IconReference /
/// IconView / TextBlock glyph) tidak disentuh: converter ini hanya
/// dipakai binding Image.Source path-file.</summary>
public sealed class FileIconImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            string? path = FileIconImageLogic.ResolveLoadablePath(value);
            if (path is null) return null!;
            using (var stream = File.OpenRead(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            // File hilang di antara pemeriksaan dan buka, tak-terbaca,
            // atau rusak sehingga decode OnLoad gagal: fallback null,
            // persis perilaku hari ini - tidak pernah melempar ke binding.
            return null!;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Immersive dark title bar (DWM) so the OS window chrome matches the
/// app's dark theme instead of a white strip on top. Best effort: older
/// Windows builds just keep their default title bar.
/// </summary>
public static class WindowTheming
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyDarkTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int on = 1;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref on, sizeof(int));
    }
}
