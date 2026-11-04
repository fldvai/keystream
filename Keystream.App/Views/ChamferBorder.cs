using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;

namespace Keystream.App.Views;

/// <summary>
/// A sleek cyber-deck container that clips child content and renders a
/// heavy hardware chassis with 45-degree chamfered corners, double metallic bevels,
/// and ambient specular rim lighting.
/// </summary>
public class ChamferBorder : Decorator
{
    public static readonly DependencyProperty ChamferProperty =
        DependencyProperty.Register(nameof(Chamfer), typeof(double), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(double), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty InnerBevelBrushProperty =
        DependencyProperty.Register(nameof(InnerBevelBrush), typeof(Brush), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpecularTopHighlightProperty =
        DependencyProperty.Register(nameof(SpecularTopHighlight), typeof(bool), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsHexagonProperty =
        DependencyProperty.Register(nameof(IsHexagon), typeof(bool), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ClipChildProperty =
        DependencyProperty.Register(nameof(ClipChild), typeof(bool), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Chamfer
    {
        get => (double)GetValue(ChamferProperty);
        set => SetValue(ChamferProperty, value);
    }

    public bool IsHexagon
    {
        get => (bool)GetValue(IsHexagonProperty);
        set => SetValue(IsHexagonProperty, value);
    }

    public bool ClipChild
    {
        get => (bool)GetValue(ClipChildProperty);
        set => SetValue(ClipChildProperty, value);
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public double BorderThickness
    {
        get => (double)GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public Brush? InnerBevelBrush
    {
        get => (Brush?)GetValue(InnerBevelBrushProperty);
        set => SetValue(InnerBevelBrushProperty, value);
    }

    public bool SpecularTopHighlight
    {
        get => (bool)GetValue(SpecularTopHighlightProperty);
        set => SetValue(SpecularTopHighlightProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        Thickness pad = Padding;
        double t = BorderThickness;
        double hPad = pad.Left + pad.Right + t * 2;
        double vPad = pad.Top + pad.Bottom + t * 2;

        if (Child != null)
        {
            Size innerConstraint = new Size(
                Math.Max(0, constraint.Width - hPad),
                Math.Max(0, constraint.Height - vPad));
            Child.Measure(innerConstraint);
            return new Size(Child.DesiredSize.Width + hPad, Child.DesiredSize.Height + vPad);
        }
        return new Size(hPad, vPad);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Thickness pad = Padding;
        double t = BorderThickness;
        Rect childRect = new Rect(
            pad.Left + t, pad.Top + t,
            Math.Max(0, arrangeSize.Width - (pad.Left + pad.Right + t * 2)),
            Math.Max(0, arrangeSize.Height - (pad.Top + pad.Bottom + t * 2)));

        if (Child != null)
        {
            Child.Arrange(childRect);

            // Clip child so its backgrounds/headers never bleed out of the chamfer/hexagon corners
            double c = Math.Max(0, Chamfer - t);
            double w = childRect.Width;
            double h = childRect.Height;
            if (ClipChild && w > 2 * c && h > 2 * c && c > 0)
            {
                var clip = IsHexagon ? BuildHexagonGeometry(w, h, c) : BuildChamferGeometry(w, h, c);
                clip.Freeze();
                Child.Clip = clip;
            }
            else
            {
                Child.Clip = null;
            }
        }
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        double c = Chamfer;
        double t = BorderThickness;

        if (w <= 2 * c || h <= 2 * c || c <= 0)
        {
            if (Background != null) dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            return;
        }

        double halfT = t / 2.0;
        var geom = IsHexagon
            ? BuildHexagonGeometry(w - t, h - t, Math.Max(0, c - halfT))
            : BuildChamferGeometry(w - t, h - t, Math.Max(0, c - halfT));
        geom.Transform = new TranslateTransform(halfT, halfT);

        Pen? pen = null;
        if (BorderBrush != null && t > 0)
        {
            pen = new Pen(BorderBrush, t);
            if (pen.CanFreeze) pen.Freeze();
        }

        // Draw main chamfered/hexagon body with background and outer border
        dc.DrawGeometry(Background, pen, geom);

        // Draw sculpted inner bevel contour if InnerBevelBrush is set
        if (InnerBevelBrush != null && w > 2 * (c + 6) && h > 2 * (c + 6))
        {
            double inset = t + 2.5;
            var innerGeom = IsHexagon
                ? BuildHexagonGeometry(w - inset * 2, h - inset * 2, Math.Max(0, c - inset))
                : BuildChamferGeometry(w - inset * 2, h - inset * 2, Math.Max(0, c - inset));
            innerGeom.Transform = new TranslateTransform(inset, inset);
            var innerPen = new Pen(InnerBevelBrush, 1.4);
            if (innerPen.CanFreeze) innerPen.Freeze();
            dc.DrawGeometry(null, innerPen, innerGeom);
        }

        // Ambient specular metallic rim highlight along top edge and top chamfers
        if (SpecularTopHighlight && w > 2 * c && c > 0)
        {
            var topPath = new StreamGeometry();
            using (var ctx = topPath.Open())
            {
                if (IsHexagon)
                {
                    double slope = c > 0 && c <= (w - t) / 2 ? c : (h - t) * 0.4;
                    ctx.BeginFigure(new Point(halfT, (h - t) / 2.0 + halfT), isFilled: false, isClosed: false);
                    ctx.LineTo(new Point(slope + halfT, halfT), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(w - slope - halfT, halfT), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(w - halfT, (h - t) / 2.0 + halfT), isStroked: true, isSmoothJoin: false);
                }
                else
                {
                    ctx.BeginFigure(new Point(halfT, c), isFilled: false, isClosed: false);
                    ctx.LineTo(new Point(c, halfT), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(w - c, halfT), isStroked: true, isSmoothJoin: false);
                    ctx.LineTo(new Point(w - halfT, c), isStroked: true, isSmoothJoin: false);
                }
            }
            topPath.Freeze();

            var topGradient = new LinearGradientBrush(
                Color.FromArgb(170, 205, 220, 240),
                Color.FromArgb(40, 110, 130, 160),
                new Point(0, 0), new Point(0, 1));
            var topPen = new Pen(topGradient, Math.Max(1.0, t * 0.45));
            if (topPen.CanFreeze) topPen.Freeze();
            dc.DrawGeometry(null, topPen, topPath);
        }
    }

    private static StreamGeometry BuildHexagonGeometry(double w, double h, double c)
    {
        double slope = c > 0 && c <= w / 2 ? c : Math.Min(w * 0.25, h * 0.45);
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(slope, 0), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(w - slope, 0), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(w, h / 2.0), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(w - slope, h), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(slope, h), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(0, h / 2.0), isStroked: true, isSmoothJoin: false);
        }
        return geom;
    }

    private static StreamGeometry BuildChamferGeometry(double w, double h, double c)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(c, 0), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(w - c, 0), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(w, c), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(w, h - c), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(w - c, h), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(c, h), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(0, h - c), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(0, c), isStroked: true, isSmoothJoin: false);
        }
        return geom;
    }
}
