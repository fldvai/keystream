using System;
using System.Collections.Generic;

namespace Keystream.Core.Localization;

/// <summary>Runtime language switch (12 languages) for the whole app.
///
/// A single static source of truth: App startup applies the saved setting
/// before any window is created, the title-bar globe button toggles it,
/// and every UI string resolves through <see cref="Get"/> /
/// <see cref="Format"/> so a switch re-renders without restart.
/// WPF bindings refresh via <see cref="LanguageChanged"/> (the App-side
/// LocSource re-raises it as PropertyChanged); ViewModels with computed
/// display strings subscribe directly.
/// The registry <see cref="Languages"/> is the single source of truth
/// for the supported codes, in canonical order. Unknown keys fall back
/// to the English value, then to the key itself - a missing translation
/// never renders blank or throws.
/// </summary>
public static class Localizer
{
    public static event Action? LanguageChanged;

    /// <summary>Supported languages in canonical order: code + endonym.
    /// The single source of truth for language codes.</summary>
    public static IReadOnlyList<(string Code, string Endonym)> Languages { get; } =
        new (string Code, string Endonym)[]
        {
            ("id", "Bahasa"),
            ("en", "English"),
            ("es", "Español"),
            ("pt", "Português"),
            ("fr", "Français"),
            ("de", "Deutsch"),
            ("ru", "Русский"),
            ("zh", "中文 (简体)"),
            ("ja", "日本語"),
            ("ko", "한국어"),
            ("ar", "العربية"),
            ("tr", "Türkçe"),
        };

    private static string _language = "en";

    /// <summary>Current language code, one of <see cref="Languages"/>. Defaults to "en".</summary>
    public static string Language => _language;

    /// <summary>Title-bar toggle label for the ACTIVE language: the
    /// upper-cased code ("ID", "EN", "ES", ... - all codes are 2 chars,
    /// so the toggle button never changes width).</summary>
    public static string Code => _language.ToUpperInvariant();

    /// <summary>Normalizes a persisted or supplied language value to one
    /// of the 12 registry codes: null/empty/whitespace -> "en"; otherwise
    /// Trim + case-fold, an exact match wins, anything else -> "en"
    /// (including region variants such as "pt-BR" or "zh-CN").</summary>
    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";
        var normalized = language.Trim().ToLowerInvariant();
        foreach (var (code, _) in Languages)
            if (code == normalized) return code;
        return "en";
    }

    public static void SetLanguage(string? language)
    {
        var next = NormalizeLanguage(language);
        if (next == _language) return;
        _language = next;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        var table = Table(_language);
        if (table.TryGetValue(key, out var value)) return value;
        if (Strings.En.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);

    private static IReadOnlyDictionary<string, string> Table(string code) => code switch
    {
        "id" => Strings.Id,
        "en" => Strings.En,
        "es" => Strings.Es,
        "pt" => Strings.Pt,
        "fr" => Strings.Fr,
        "de" => Strings.De,
        "ru" => Strings.Ru,
        "zh" => Strings.Zh,
        "ja" => Strings.Ja,
        "ko" => Strings.Ko,
        "ar" => Strings.Ar,
        "tr" => Strings.Tr,
        _ => Strings.En,
    };
}
