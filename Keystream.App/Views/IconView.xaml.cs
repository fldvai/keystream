using System.Windows;

namespace Keystream.App.Views;

/// <summary>
/// Renders one <see cref="AppIcons"/> vector by key, stroked in the
/// control's Foreground. This is the ONLY way icons are drawn in the
/// app (v2.9.35 consistency rule) - set Width/Height and Foreground.
/// </summary>
public partial class IconView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(IconView),
        new PropertyMetadata(null, (d, _) => ((IconView)d).Refresh()));

    public IconView() => InitializeComponent();

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    private void Refresh() => GlyphPath.Data = AppIcons.Get(Icon);
}
