using System;
using System.Collections.Generic;

namespace Keystream.Setup.Localization;

/// <summary>Runtime language switch (12 languages) for the Setup and
/// Uninstall wizards. Mirrors the app's Localizer contract
/// (parameterless <see cref="LanguageChanged"/>, fire-on-change-only,
/// synchronous; fallback chosen -> English -> key) because Setup and
/// Uninstall are standalone exes with NO reference to Keystream.Core
/// and cannot reuse it.
/// <para>The registry <see cref="Languages"/> duplicates the app's
/// language list (same 12 codes + endonyms, byte-consistent with
/// <c>Keystream.Core.Localization.Localizer.Languages</c>). The
/// duplication is REQUIRED: no shared assembly exists between the
/// app and Setup. NO other Setup/Uninstall file may define its own
/// language list; every language choice goes through this class.
/// </para></summary>
public static class SetupLocalizer
{
    public static event Action? LanguageChanged;

    /// <summary>Supported languages in canonical order: code + endonym.
    /// Duplicated from the app's registry by necessity (see class
    /// doc); keep byte-consistent with it.</summary>
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

    /// <summary>Normalizes a persisted or supplied language value to one
    /// of the 12 registry codes: null/empty/whitespace -> "en"; otherwise
    /// Trim + case-fold, an exact match wins, anything else -> "en"
    /// (including region variants such as "pt-BR" or "zh-CN"). Same
    /// rule as the app's Localizer.NormalizeLanguage.</summary>
    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "en";
        var normalized = language.Trim().ToLowerInvariant();
        foreach (var (code, _) in Languages)
            if (code == normalized) return code;
        return "en";
    }

    public static void SetLanguage(string? language)
    {
        var next = Normalize(language);
        if (next == _language) return;
        _language = next;
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        var table = Table(_language);
        if (table.TryGetValue(key, out var value)) return value;
        if (SetupStrings.En.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);

    private static IReadOnlyDictionary<string, string> Table(string code) => code switch
    {
        "id" => SetupStrings.Id,
        "en" => SetupStrings.En,
        "es" => SetupStrings.Es,
        "pt" => SetupStrings.Pt,
        "fr" => SetupStrings.Fr,
        "de" => SetupStrings.De,
        "ru" => SetupStrings.Ru,
        "zh" => SetupStrings.Zh,
        "ja" => SetupStrings.Ja,
        "ko" => SetupStrings.Ko,
        "ar" => SetupStrings.Ar,
        "tr" => SetupStrings.Tr,
        _ => SetupStrings.En,
    };
}
