using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Setup.Localization;
using Microsoft.Win32;

namespace Keystream.Setup;

/// <summary>Saved-language bridge between the Setup/Uninstall wizards
/// and the app. When a main config.json exists, both sides share the
/// SAME value, <c>settings.language</c> in
/// <c>%AppData%\Keystream\config.json</c> (the app's
/// <c>DeckSettings.Language</c>, default "en"). When no main
/// config.json exists, Setup instead writes the chosen code to the
/// registry handoff value (see the A13 NOTE below) so the app's
/// FreshDefault seeding is preserved; the app consumes it once.
/// <para>Shared source: compiled into both Keystream.Setup and
/// Keystream.Uninstall via Link. Never throws: every failure path
/// returns "en" (read) or leaves the file untouched (merge), so a
/// language-persistence problem can never break Setup or Uninstall.
/// </para>
/// <para>NOTE (A13 registry handoff): a Setup-created minimal file
/// would make the app's <c>ConfigStore.Load</c> return
/// <c>Source=Main</c> (the file parses as a valid DeckConfig), so
/// FreshDefault seeding — the 7 default Global cards — would be
/// bypassed on a fresh install that picks a language at Finish.
/// <c>ApplyLanguage</c> therefore never creates a config file when
/// none exists: <c>LanguageHandoff.DecideSetupAction</c> routes that
/// case to the registry value instead, consumed once by the app on
/// FreshDefault. When a Main config is present the language is
/// merged into the file and any lingering registry value is
/// cleared. A corrupt/unreadable file is left untouched, as in
/// <c>MergeLanguage</c>.
/// </para></summary>
public static class ConfigLanguage
{
    private static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keystream");

    private static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Reads the saved language, read-only (no writes, no
    /// directory creation). The "settings" object and the "language"
    /// key are both located case-insensitively. ANY failure (absent
    /// file/dir, corrupt JSON, non-object root, non-object settings,
    /// missing/wrong-typed language) returns "en", never throws.</summary>
    public static string ReadSavedLanguage()
    {
        try
        {
            string path = ConfigPath;
            if (!File.Exists(path)) return "en";
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            if (root is not JsonObject rootObj) return "en";
            JsonObject? settings = FindObjectCaseInsensitive(rootObj, "settings");
            if (settings is null) return "en";
            string? raw = FindStringCaseInsensitive(settings, "language");
            if (raw is null) return "en";
            return SetupLocalizer.Normalize(raw);
        }
        catch
        {
            return "en";
        }
    }

    /// <summary>Persists <paramref name="code"/> as the wizard's
    /// chosen language without ever creating a config file when
    /// none exists: when no Main config exists the language is
    /// written to the registry handoff value (consumed once by the
    /// app on FreshDefault, so default-card seeding survives); when
    /// a Main config exists the language is merged into the file and
    /// any lingering registry value is cleared. The routing
    /// decision is <c>LanguageHandoff.DecideSetupAction</c>; the
    /// code is normalized once here, before either path. Never
    /// throws: both paths are best-effort (see
    /// <c>MergeLanguage</c>, <c>WritePendingLanguage</c> and
    /// <c>ClearPendingLanguage</c>).</summary>
    public static void ApplyLanguage(string? code)
    {
        string normalized = SetupLocalizer.Normalize(code);
        if (LanguageHandoff.DecideSetupAction(File.Exists(ConfigPath)) == LanguageHandoff.SetupAction.MergeIntoFile)
        {
            MergeLanguage(normalized);
            ClearPendingLanguage();
        }
        else
        {
            WritePendingLanguage(normalized);
        }
    }

    /// <summary>Persists <paramref name="code"/> as
    /// <c>settings.language</c>, preserving every other node:
    /// <list type="bullet">
    /// <item>Directory is created first; an absent file is written as
    /// exactly <c>{"schemaVersion":1,"settings":{"language":"&lt;code&gt;"}}</c>
    /// (indented).</item>
    /// <item>A present file is parsed and the EXISTING "settings" node
    /// (located case-insensitively) is edited in place — never a
    /// duplicate differently-cased settings object. ONLY the language
    /// value is set (lowercase "language" key when added; an existing
    /// differently-cased language key is updated in place, never
    /// duplicated). Every other node is left untouched.</item>
    /// <item>The commit is atomic: temp file in the same directory +
    /// flush-to-disk + move-overwrite, mirroring
    /// <c>ConfigStore.Save</c>; never a direct WriteAllText. The temp
    /// file is the transient ".tmp" staging file consumed by the move
    /// (same as ConfigStore.Save); no backup rotation is performed and
    /// config.json.bak1-3 / .bakstage are never touched.</item>
    /// <item>A corrupt/unreadable file, a non-object root, or a
    /// non-object "settings" value leaves the file UNTOUCHED: no
    /// write, no delete, no throw (explicit checks, not a swallowed
    /// exception).</item>
    /// <item>No-op when the existing value already equals the code.</item>
    /// </list></summary>
    public static void MergeLanguage(string? code)
    {
        string normalized = SetupLocalizer.Normalize(code);
        try
        {
            string dir = ConfigDirectory;
            string path = ConfigPath;
            Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
                var minimal = new JsonObject
                {
                    ["schemaVersion"] = JsonValue.Create(1),
                    ["settings"] = new JsonObject { ["language"] = JsonValue.Create(normalized) },
                };
                WriteAtomically(path, minimal.ToJsonString(Indented));
                return;
            }

            // Corrupt/unreadable: explicit check, leave untouched.
            JsonNode? root;
            try { root = JsonNode.Parse(File.ReadAllText(path)); }
            catch { return; }
            if (root is not JsonObject rootObj) return; // Non-object root: untouched.

            // Locate "settings" case-insensitively. A present but
            // non-object settings value leaves the file untouched; a
            // missing settings object is created (lowercase key).
            string? settingsKey = FindKeyCaseInsensitive(rootObj, "settings");
            JsonObject? settings = settingsKey is null
                ? null
                : rootObj[settingsKey] as JsonObject;
            if (settingsKey is not null && settings is null) return;
            if (settings is null)
            {
                settings = new JsonObject();
                rootObj["settings"] = settings;
            }

            // No-op when the existing value already equals the code
            // (compared after the same normalization the read path uses).
            string? languageKey = FindKeyCaseInsensitive(settings, "language");
            string? existing = languageKey is null
                ? null
                : FindStringCaseInsensitive(settings, "language");
            if (existing is not null
                && string.Equals(SetupLocalizer.Normalize(existing), normalized, StringComparison.Ordinal))
                return;

            if (languageKey is not null)
                settings[languageKey] = JsonValue.Create(normalized);
            else
                settings["language"] = JsonValue.Create(normalized);

            WriteAtomically(path, rootObj.ToJsonString(Indented));
        }
        catch
        {
            // Language persistence must never break Setup/Uninstall.
        }
    }

    /// <summary>Writes the normalized language code to the
    /// registry handoff value under the uninstall key, for the app
    /// to consume once on FreshDefault. NEVER creates config.json
    /// (or its directory). Swallows ALL errors, never throws.</summary>
    private static void WritePendingLanguage(string normalized)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ShortcutHelper.UninstallKeyPath);
            key?.SetValue(LanguageHandoff.RegistryValueName, normalized, RegistryValueKind.String);
        }
        catch
        {
            // Language persistence must never break Setup/Uninstall.
        }
    }

    /// <summary>Deletes the registry handoff value when present,
    /// so a merged file never leaves a stale pending language
    /// behind. Swallows ALL errors, never throws.</summary>
    private static void ClearPendingLanguage()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShortcutHelper.UninstallKeyPath, writable: true);
            key?.DeleteValue(LanguageHandoff.RegistryValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Language persistence must never break Setup/Uninstall.
        }
    }

    private static string? FindKeyCaseInsensitive(JsonObject obj, string name)
    {
        foreach (var prop in obj)
            if (string.Equals(prop.Key, name, StringComparison.OrdinalIgnoreCase))
                return prop.Key;
        return null;
    }

    /// <summary>Returns the named child object, or null when the key is
    /// absent or its value is not a JSON object. (A JSON null is not
    /// an object, so "settings": null reads as absent here, matching
    /// the merge rule that a non-object settings value is untouched —
    /// the read path simply falls back to "en".)</summary>
    private static JsonObject? FindObjectCaseInsensitive(JsonObject obj, string name)
    {
        string? key = FindKeyCaseInsensitive(obj, name);
        return key is null ? null : obj[key] as JsonObject;
    }

    private static string? FindStringCaseInsensitive(JsonObject obj, string name)
    {
        string? key = FindKeyCaseInsensitive(obj, name);
        if (key is null) return null;
        if (obj[key] is JsonValue value && value.TryGetValue<string>(out string? text))
            return text;
        return null;
    }

    private static void WriteAtomically(string path, string json)
    {
        string tempPath = path + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(fs, leaveOpen: true))
            {
                writer.Write(json);
                writer.Flush();
            }
            fs.Flush(flushToDisk: true);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}
