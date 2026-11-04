using System.ComponentModel;
using Keystream.Core.Localization;

namespace Keystream.App.Localization;

/// <summary>WPF bridge for <see cref="Localizer"/> (§86, binding form revised §87).
/// A single shared source whose indexer resolves a string key; on every
/// language change it raises PropertyChanged with an empty name, so ALL
/// indexer bindings re-resolve without restarting the app.
///
/// XAML usage: <c>{Binding [S0001], Source={x:Static loc:LocSource.Instance}, Mode=OneWay}</c>
/// (xmlns:loc = "clr-namespace:Keystream.App.Localization").
/// NOTE (§87): this must stay a plain built-in Binding. An earlier revision
/// used a custom MarkupExtension returning a Binding ({loc:Loc ...}); that
/// crashed at startup - the BAML loader passed the Binding object to
/// DependencyObject.SetValue for TextBlock.Text instead of calling
/// SetBinding ("'System.Windows.Data.Binding' is not a valid value for
/// property 'Text'"). Never reintroduce a custom extension that returns
/// a Binding; the built-in Binding extension is the only supported path.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() =>
        Localizer.LanguageChanged += () =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public string this[string key] => Localizer.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
