using System;
using System.Reflection;

namespace Keystream.Core.Localization;

/// <summary>Right-to-left layout for windows: Arabic ("ar") renders
/// RightToLeft, every other language LeftToRight. A window's
/// FlowDirection is inherited by its whole visual tree, so setting it
/// once per Window mirrors the wizard/app chrome for Arabic.
/// <para>Core has NO dependency on Setup or App, and NO WPF reference
/// (net8.0, WPF-free): the helper therefore takes a
/// <see cref="Func{TResult}"/> current-language provider and reaches
/// the WPF types (<c>System.Windows.Window</c>,
/// <c>System.Windows.Application</c>, <c>System.Windows.FlowDirection</c>)
/// by reflection over the assemblies already loaded by the WPF host
/// process. In a non-WPF process the lookups find nothing and both
/// entry points silently no-op; no call here ever throws.
/// </para>
/// <para>Class name note: the file is FlowDirection.cs but the type is
/// <see cref="FlowDirectionHelper"/> — a type named FlowDirection in
/// this namespace would collide with the
/// <c>System.Windows.FlowDirection</c> enum in every WPF file that
/// imports both namespaces.
/// </para>
/// <para>NOT covered: physical margin/alignment mirroring defects
/// (checkbox indent, path TextBox bidi, ProgressBar fill direction)
/// are PC-screenshot verification items, not fixed here. The
/// <c>FlowDirection.LeftToRight</c> in BrowserIconComposer is icon
/// rasterisation (a FormattedText argument), not a Window, and is
/// untouched and unaffected by this helper.
/// </para></summary>
public static class FlowDirectionHelper
{
    private static bool _handlerInstalled;
    private static Func<string>? _provider;

    /// <summary>Arabic is the only right-to-left language in the 12-language
    /// registry. The comparison is case-insensitive; the provider is
    /// expected to hand back an already-normalized code.</summary>
    public static bool IsRightToLeft(string? language) =>
        string.Equals(language?.Trim(), "ar", StringComparison.OrdinalIgnoreCase);

    /// <summary>Registers a Window.Loaded class handler (once per
    /// process) that sets each newly loaded window's FlowDirection
    /// from the current language at load time. Windows opened later
    /// (dialogs included) pick it up on Loaded; windows already open
    /// when the language changes are covered by
    /// <see cref="RefreshAllOpenWindows"/> via the language-changed
    /// event. Silently no-ops when WPF is not loaded.</summary>
    public static void InstallClassHandler(Func<string> currentLanguage)
    {
        if (currentLanguage is null) return;
        if (_handlerInstalled) return;
        try
        {
            Type? windowType = FindWpfType("System.Windows.Window", "PresentationFramework");
            Type? eventManagerType = FindWpfType("System.Windows.EventManager", "PresentationFramework");
            Type? routedEventType = FindWpfType("System.Windows.RoutedEvent", "PresentationFramework");
            Type? routedHandlerType = FindWpfType("System.Windows.RoutedEventHandler", "PresentationFramework");
            if (windowType is null || eventManagerType is null
                || routedEventType is null || routedHandlerType is null)
                return;

            FieldInfo? loadedEventField = windowType.GetField(
                "LoadedEvent", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (loadedEventField?.GetValue(null) is not object loadedEvent) return;

            MethodInfo? register = eventManagerType.GetMethod(
                "RegisterClassHandler",
                new[] { typeof(Type), routedEventType, typeof(Delegate), typeof(bool) });
            if (register is null) return;

            MethodInfo? handlerMethod = typeof(FlowDirectionHelper).GetMethod(
                nameof(OnWindowLoaded), BindingFlags.NonPublic | BindingFlags.Static);
            if (handlerMethod is null) return;
            Delegate handler = Delegate.CreateDelegate(routedHandlerType, handlerMethod);

            _provider = currentLanguage;
            register.Invoke(null, new object[] { windowType, loadedEvent, handler, false });
            _handlerInstalled = true;
        }
        catch
        {
            // Reflection against a missing/changed WPF surface must
            // never break startup; RefreshAllOpenWindows still covers
            // windows open at language-change time.
            _provider = null;
        }
    }

    /// <summary>Sets FlowDirection on every currently open window from
    /// <paramref name="language"/>. Called on every language change so
    /// already-open windows mirror immediately. Silently no-ops when
    /// WPF is not loaded or no Application exists; a single window
    /// failing never stops the rest.</summary>
    public static void RefreshAllOpenWindows(string language)
    {
        try
        {
            Type? appType = FindWpfType("System.Windows.Application", "PresentationFramework");
            if (appType is null) return;
            PropertyInfo? currentProperty = appType.GetProperty(
                "Current", BindingFlags.Public | BindingFlags.Static);
            object? app = currentProperty?.GetValue(null);
            if (app is null) return;
            PropertyInfo? windowsProperty = appType.GetProperty(
                "Windows", BindingFlags.Public | BindingFlags.Instance);
            if (windowsProperty?.GetValue(app) is not System.Collections.IEnumerable windows) return;
            foreach (object? window in windows)
            {
                if (window is null) continue;
                try { ApplyDirection(window, language); }
                catch { }
            }
        }
        catch
        {
        }
    }

    private static void OnWindowLoaded(object sender, object e)
    {
        try
        {
            Func<string>? provider = _provider;
            if (provider is null) return;
            ApplyDirection(sender, provider());
        }
        catch
        {
        }
    }

    private static void ApplyDirection(object window, string? language)
    {
        PropertyInfo? property = window.GetType().GetProperty(
            "FlowDirection", BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite) return;
        Type? enumType = FindWpfType("System.Windows.FlowDirection", "WindowsBase");
        if (enumType is null) return;
        object value = Enum.Parse(enumType, IsRightToLeft(language) ? "RightToLeft" : "LeftToRight");
        property.SetValue(window, value);
    }

    private static Type? FindWpfType(string fullName, string assemblyHint)
    {
        try
        {
            Type? direct = Type.GetType(fullName + ", " + assemblyHint, throwOnError: false);
            if (direct is not null) return direct;
        }
        catch
        {
        }
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? found;
                try { found = assembly.GetType(fullName, throwOnError: false); }
                catch { continue; }
                if (found is not null) return found;
            }
        }
        catch
        {
        }
        return null;
    }
}
