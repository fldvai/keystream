using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBox = System.Windows.Controls.TextBox;

namespace Keystream.App.Views;

/// <summary>
/// §110 - field-level cancel for TextBoxes (Esc only). ONE mechanism
/// for every TextBox, registered once from App startup (see
/// TextBoxRevert.Register); no per-window code, no template change,
/// no new strings. The pure string decisions live WPF-free in
/// TextBoxRevertLogic.cs (harness-linkable); this file holds only
/// the attached properties and the class handlers, and calls
/// into it.
///
/// Behaviour:
///  - Esc while a TextBox is focused restores the text the box had
///    when focus entered it, keeps focus in the box, caret at end.
///    If the text is unchanged, Esc does nothing (not swallowed).
///    There is no restore on losing focus: an emptied box stays
///    empty until Save validation rejects it, exactly as before.
///
/// Esc guard: revert runs on BUBBLING KeyDown with
/// handledEventsToo=false, so any PreviewKeyDown tunneling handler
/// that sets Handled=true wins outright - chord capture
/// (BindingEditorWindow, MacroManagerWindow) and hotkey recording
/// (SettingsWindow) all swallow Esc in PreviewKeyDown before the
/// bubble ever starts. The ONE exception is the picker filter box:
/// SearchablePanelPicker.FilterBox_KeyDown is a bubbling KeyDown
/// instance handler, and class handlers run BEFORE instance
/// handlers, so without an opt-out this class handler would fire
/// first, restore the filter text, set Handled=true, and the
/// picker would never close on Esc. That box is therefore
/// excluded with EnableEscapeRestore="False" (its Esc still
/// closes the panel exactly as before). Do NOT modify
/// FilterBox_KeyDown itself.
///
/// PageEdit IsCancel interaction (MUST-CHECK on PC - see report):
/// PageEditWindow's Batal button has IsCancel="True", so WPF Esc
/// activates it and closes the dialog; that activation is
/// framework-internal and no handler for it exists in this tree,
/// so the ordering against this TextBox class handler cannot be
/// proven from code on this VM. PREDICTED behaviour: the class
/// handler runs at the TextBox (start of the bubble) before the
/// event reaches window level, so a first Esc with changed text
/// reverts the name and swallows the key (dialog stays open) and
/// a second Esc closes the dialog; Esc closing immediately on
/// the first press remains possible if IsCancel acts earlier
/// (message/preview level).
///
/// Delay binding: the macro step-row delay TextBox binds
/// DelayText with UpdateSourceTrigger=PropertyChanged
/// (MacroManagerWindow.xaml), so restoring Text in the Esc handler
/// propagates through the existing binding to
/// MacroStepRow.DelayText - no row-property fallback is needed,
/// and UpdateSource is never called directly.
///
/// Dirty note (accepted): restoring NameBox text fires
/// TextChanged, so MacroManagerWindow._dirty stays true after an
/// Esc restore - accepted per the §110 brief; no attempt is
/// made at dirty-perfection here.
///
/// Snapshot limits (per contract): the snapshot refreshes on EVERY
/// focus entry only. A programmatic Text set while the box already
/// has focus (e.g. MacroManagerWindow loading another macro into
/// NameBox) does not refresh it; Esc after such a load restores the
/// text from the last focus entry. Undo is untouched: the global
/// TextBox style does not disable it and this helper does not rely
/// on it.
/// </summary>
public static class TextBoxRevert
{
    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.RegisterAttached(
            "Snapshot", typeof(string), typeof(TextBoxRevert), new PropertyMetadata(null));

    public static string? GetSnapshot(DependencyObject obj) => (string?)obj.GetValue(SnapshotProperty);

    public static void SetSnapshot(DependencyObject obj, string? value) => obj.SetValue(SnapshotProperty, value);

    /// <summary>
    /// Opt-out for the single bubbling-KeyDown Esc path
    /// (SearchablePanelPicker FilterBox, set to False there and
    /// nowhere else): when false, the Esc handler returns early
    /// and the box's own KeyDown handler owns Esc. Default true:
    /// Esc revert is on for every TextBox unless opted out.
    /// </summary>
    public static readonly DependencyProperty EnableEscapeRestoreProperty =
        DependencyProperty.RegisterAttached(
            "EnableEscapeRestore", typeof(bool), typeof(TextBoxRevert), new PropertyMetadata(true));

    public static bool GetEnableEscapeRestore(DependencyObject obj) => (bool)obj.GetValue(EnableEscapeRestoreProperty);

    public static void SetEnableEscapeRestore(DependencyObject obj, bool value) => obj.SetValue(EnableEscapeRestoreProperty, value);

    private static bool _registered;

    /// <summary>Registers the TextBox class handlers once. Called once from App.OnStartup.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(TextBox),
            UIElement.GotFocusEvent, new RoutedEventHandler(OnGotFocus));
        EventManager.RegisterClassHandler(typeof(TextBox),
            UIElement.KeyDownEvent, new System.Windows.Input.KeyEventHandler(OnKeyDown), handledEventsToo: false);
    }

    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.IsReadOnly) return;
        SetSnapshot(tb, tb.Text);
    }

    private static void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled) return;
        if (sender is not TextBox tb) return;
        if (!GetEnableEscapeRestore(tb)) return;
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (tb.IsReadOnly) return;
        if (TextBoxRevertLogic.NeedsEscapeRestore(tb.Text, GetSnapshot(tb)))
        {
            tb.Text = TextBoxRevertLogic.GetEscapeRestoreText(GetSnapshot(tb));
            tb.CaretIndex = tb.Text.Length;
            tb.SelectionLength = 0;
            e.Handled = true;
        }
    }
}
