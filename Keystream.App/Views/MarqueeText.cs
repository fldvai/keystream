using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SystemFonts = System.Windows.SystemFonts;

namespace Keystream.App.Views;

/// <summary>
/// A sleek text container that displays text within a static bounded view,
/// aligning text when it fits, and automatically running a smooth ping-pong
/// marquee animation when the text exceeds its container bounds.
/// </summary>
public class MarqueeText : Border
{
    private readonly Canvas _canvas;
    private readonly TextBlock _textBlock;
    private readonly TranslateTransform _transform = new();

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeText),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(MarqueeText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(MarqueeText),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(MarqueeText),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(MarqueeText),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged));

    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(MarqueeText),
            new FrameworkPropertyMetadata(TextAlignment.Center, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public MarqueeText()
    {
        ClipToBounds = true;
        _canvas = new Canvas { ClipToBounds = false };
        _textBlock = new TextBlock
        {
            RenderTransform = _transform,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
        };
        _canvas.Children.Add(_textBlock);
        Child = _canvas;

        Loaded += (_, _) => UpdateMarquee(ActualWidth, ActualHeight);
        SizeChanged += (_, e) => UpdateMarquee(e.NewSize.Width, e.NewSize.Height);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeText m)
        {
            m.InvalidateMeasure();
            m.UpdateMarquee(m.ActualWidth, m.ActualHeight);
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _textBlock.Text = Text ?? "";
        _textBlock.FontFamily = FontFamily;
        _textBlock.FontSize = FontSize;
        _textBlock.FontWeight = FontWeight;
        _textBlock.Foreground = Foreground;

        _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double h = Math.Ceiling(_textBlock.DesiredSize.Height);
        double w = double.IsInfinity(constraint.Width) ? 0 : constraint.Width;
        return new Size(w, Math.Max(h, FontSize * 1.3));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        _canvas.Arrange(new Rect(0, 0, arrangeSize.Width, arrangeSize.Height));
        UpdateMarquee(arrangeSize.Width, arrangeSize.Height);
        return arrangeSize;
    }

    public void UpdateMarquee(double viewWidth, double viewHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0) return;

        _textBlock.Text = Text ?? "";
        _textBlock.FontFamily = FontFamily;
        _textBlock.FontSize = FontSize;
        _textBlock.FontWeight = FontWeight;
        _textBlock.Foreground = Foreground;

        _textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = _textBlock.DesiredSize.Width;
        double textHeight = _textBlock.DesiredSize.Height;

        double top = Math.Max(0, (viewHeight - textHeight) / 2.0);
        Canvas.SetTop(_textBlock, top);

        // Always cancel any prior active animation first
        _transform.BeginAnimation(TranslateTransform.XProperty, null);

        // Fits within view bounds or empty string
        if (textWidth <= viewWidth || string.IsNullOrEmpty(Text))
        {
            if (TextAlignment == TextAlignment.Center)
                _transform.X = Math.Max(0, (viewWidth - textWidth) / 2.0);
            else if (TextAlignment == TextAlignment.Right)
                _transform.X = Math.Max(0, viewWidth - textWidth);
            else
                _transform.X = 0;
            return;
        }

        // Text exceeds view bounds -> Run ping-pong marquee animation
        _transform.X = 0;
        double overflow = textWidth - viewWidth + 10;
        double scrollSeconds = Math.Max(1.4, overflow / 24.0);

        var anim = new DoubleAnimationUsingKeyFrames();
        // 0.0s to 1.0s: hold start position
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0))));

        // 1.0s to (1.0s + scrollSeconds): scroll to end of text
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(-overflow,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0 + scrollSeconds)),
            new KeySpline(0.25, 0.1, 0.25, 1.0)));

        // hold at end for 1.0s
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0 + scrollSeconds + 1.0))));

        // scroll back to start
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.0 + scrollSeconds + 1.0 + scrollSeconds)),
            new KeySpline(0.25, 0.1, 0.25, 1.0)));

        anim.RepeatBehavior = RepeatBehavior.Forever;

        // Apply directly to the transform's X property!
        _transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
