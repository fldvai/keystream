using System.Text.Json.Nodes;

namespace Keystream.Actions.Common;

/// <summary>Reads action parameters tolerantly: keys are matched
/// case-insensitively and both the v2 names and the legacy PascalCase names
/// are accepted (the migrator preserves legacy parameter dictionaries
/// verbatim, e.g. "Key", "Command", "SceneName", "Target").</summary>
public static class ParamReader
{
    public static string? GetString(JsonObject parameters, params string[] names)
    {
        // A-02: Params: null (explicit-null di config) dibaca sebagai
        // "tidak ada parameter", bukan NRE.
        if (parameters is null || names is null) return null;
        foreach (var name in names)
        {
            foreach (var kv in parameters)
            {
                if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && kv.Value is JsonValue value
                    && value.TryGetValue<string>(out var s))
                    return s;
            }
        }
        return null;
    }

    public static bool GetBool(JsonObject parameters, bool fallback, params string[] names)
    {
        if (parameters is null || names is null) return fallback;
        foreach (var name in names)
        {
            foreach (var kv in parameters)
            {
                if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && kv.Value is JsonValue value)
                {
                    if (value.TryGetValue<bool>(out var b))
                        return b;
                    if (value.TryGetValue<string>(out var s) && bool.TryParse(s, out var sb))
                        return sb;
                }
            }
        }
        return fallback;
    }
}
