using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Keystream.App.Views;

/// <summary>
/// Shared custom-window-chrome helpers (UI overhaul, 24 Oct 2026).
/// Windows use WindowStyle=None + WindowChrome, so drag/resize stay
/// OS-backed; this helper only routes header drags and chrome buttons.
/// DragMove is never called for clicks that started on a Button, so
/// header buttons keep working.
/// </summary>
public static class ShellChrome
{
    public static void HeaderMouseDown(object sender, MouseButtonEventArgs e, Window window, bool canMaximize)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is DependencyObject src && IsInsideButton(src)) return;
        if (e.ClickCount == 2 && canMaximize)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        try { window.DragMove(); }
        catch (InvalidOperationException) { /* mouse released mid-drag: ignore */ }
    }

    private static bool IsInsideButton(DependencyObject obj)
    {
        // ButtonBase covers Button AND the ToggleButton inside a ComboBox
        // (title-bar language dropdown); ComboBox itself covers clicks on
        // non-button parts of the combo. Without this, header clicks on
        // the dropdown would DragMove the window instead of opening it.
        while (obj is not null)
        {
            if (obj is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (obj is System.Windows.Controls.ComboBox) return true;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return false;
    }

    public static void Minimize(Window window)
    {
        if (window.WindowState != WindowState.Minimized)
            window.WindowState = WindowState.Minimized;
    }
}
