using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Keystream.App.Services;

/// <summary>
/// Composite tile icon for a browser GROUP (§95): several browser exe
/// icons merged into one 256×256 image, saved as the card's AutoIcon
/// like any single-browser icon. Layout (fit + center, never stretched):
/// 1 icon → the icon itself; 2 → left/right halves; 3 → left half +
/// top-right + bottom-right quadrants; 4 → 2×2 quadrants; 5+ → first
/// 3 icons + a "+N" badge cell (N = browsers beyond the 3 shown).
/// Duplicate exes are merged first (a group with two Thorium profiles
/// is one Thorium icon, not two identical cells); exes whose icon
/// cannot be extracted are skipped silently; if none extracts, null
/// (the caller keeps the catalog icon - the built-in icon is only
/// ever the last resort, never a replacement for a real browser icon).
/// Rendered with DrawingVisual on the UI thread (the editor runs there);
/// the result goes through IconStore.ImportBitmap like any auto icon.
/// </summary>
public static class BrowserIconComposer
{
    private const int Size = 256;
    private const int Half = Size / 2;

    public static BitmapSource? Compose(IReadOnlyList<string> exePaths)
    {
        var distinct = exePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0) return null;

        var icons = new List<BitmapSource>();
        foreach (string exe in distinct)
        {
            var bmp = AppIconExtractor.Extract(exe);
            if (bmp is not null) icons.Add(bmp);
        }
        if (icons.Count == 0) return null;
        if (icons.Count == 1) return icons[0];

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            bool badge = distinct.Count > 4;
            if (icons.Count == 2 && !badge)
            {
                DrawFitted(dc, icons[0], new Rect(0, 0, Half, Size));
                DrawFitted(dc, icons[1], new Rect(Half, 0, Half, Size));
            }
            else if (icons.Count == 3 && !badge)
            {
                DrawFitted(dc, icons[0], new Rect(0, 0, Half, Size));
                DrawFitted(dc, icons[1], new Rect(Half, 0, Half, Half));
                DrawFitted(dc, icons[2], new Rect(Half, Half, Half, Half));
            }
            else if (!badge)
            {
                DrawFitted(dc, icons[0], new Rect(0, 0, Half, Half));
                DrawFitted(dc, icons[1], new Rect(Half, 0, Half, Half));
                DrawFitted(dc, icons[2], new Rect(0, Half, Half, Half));
                DrawFitted(dc, icons[3], new Rect(Half, Half, Half, Half));
            }
            else
            {
                DrawFitted(dc, icons[0], new Rect(0, 0, Half, Half));
                DrawFitted(dc, icons[1], new Rect(Half, 0, Half, Half));
                DrawFitted(dc, icons[2], new Rect(0, Half, Half, Half));
                var cell = new Rect(Half, Half, Half, Half);
                dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x21, 0x2E)),
                    null, cell, 16, 16);
                var text = new FormattedText(
                    "+" + (distinct.Count - 3),
                    CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal,
                        FontWeights.Bold, FontStretches.Normal),
                    56, System.Windows.Media.Brushes.White, 1.0);
                dc.DrawText(text, new System.Windows.Point(
                    cell.X + (cell.Width - text.Width) / 2,
                    cell.Y + (cell.Height - text.Height) / 2));
            }
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>Draws the icon scaled to fit the cell, centered, aspect kept.</summary>
    private static void DrawFitted(DrawingContext dc, BitmapSource image, Rect cell)
    {
        double scale = Math.Min(cell.Width / image.PixelWidth, cell.Height / image.PixelHeight);
        double w = image.PixelWidth * scale;
        double h = image.PixelHeight * scale;
        dc.DrawImage(image, new Rect(
            cell.X + (cell.Width - w) / 2,
            cell.Y + (cell.Height - h) / 2,
            w, h));
    }
}
