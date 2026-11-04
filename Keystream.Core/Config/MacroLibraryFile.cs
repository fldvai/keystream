using System.Text.Json;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;

namespace Keystream.Core.Config;

/// <summary>Macro library file format (§81): one versioned JSON envelope
/// so a macro library can be backed up, moved to another PC, or shared
/// without touching config.json by hand. No UI here so the whole
/// mechanism is unit-testable on any platform (see macro-io-test):
///
///   { "app": "Keystream", "type": "macro-library", "version": 1,
///     "macros": [ MacroConfig, ... ] }   (camelCase, indented - the
///     same shape ConfigStore writes, so a macro round-trips exactly)
///
/// Import rules (strict where silence would lose data, lenient where a
/// safe fallback already exists):
/// - A bare JSON array of macros is also accepted (hand-made files).
/// - File-level failures (not JSON, wrong "type", unsupported
///   "version", no "macros" array) throw InvalidDataException - the
///   caller shows it, never imports half a file.
/// - Per-macro failures REJECT only that macro (name empty, 0 steps,
///   cycles outside 1–10, a step without ActionId, a delay
///   outside 0–10000, or a type error) and are reported by name; the
///   valid siblings still import. Unknown ActionIds are ALLOWED -
///   playback already fails honestly on them, and a macro from a newer
///   version must not be silently dropped.
/// - Speed follows the §69 playback rule (finite and &gt; 0, else 1.0) -
///   mirrored here because Core cannot reference MacroAction.
/// - Id: preserved when free (so export → delete → import restores
///   card links); on collision a fresh Id is generated. Name: on
///   collision (case-insensitive, against the library and earlier
///   imports in the same file) suffixed " (impor)", " (impor) 2", …
///   - the same numbering pattern as Duplikat's " (salinan)".
/// Cards are never touched by import: importing cannot rewire a card,
/// and deleting an imported macro is still guarded by MacroUsage.</summary>
public static class MacroLibraryFile
{
    public const int FormatVersion = 1;
    public const int MaxDelayMs = 10000;
    public const int MaxCycles = 10;

    public sealed record ImportResult(
        IReadOnlyList<MacroConfig> Imported,
        IReadOnlyList<string> Rejected);

    private static readonly JsonSerializerOptions ImportOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Export(IReadOnlyList<MacroConfig> macros)
    {
        var envelope = new JsonObject
        {
            ["app"] = "Keystream",
            ["type"] = "macro-library",
            ["version"] = FormatVersion,
            ["macros"] = JsonSerializer.SerializeToNode(macros, ConfigStore.JsonOptions),
        };
        return envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static ImportResult Import(string text, IReadOnlyList<MacroConfig> existing)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(Localizer.Format("S1028", ex.Message), ex);
        }

        JsonArray? array;
        if (root is JsonArray bare)
        {
            array = bare;
        }
        else if (root is JsonObject envelope)
        {
            if (envelope["type"] is JsonValue typeValue
                && typeValue.TryGetValue<string>(out var type)
                && type != "macro-library")
                throw new InvalidDataException(
                    Localizer.Format("S1029", type));
            if (envelope["version"] is JsonValue versionValue
                && versionValue.TryGetValue<int>(out var version)
                && version != FormatVersion)
                throw new InvalidDataException(
                    Localizer.Format("S1030", version, FormatVersion));
            if (envelope["macros"] is not JsonArray macros)
                throw new InvalidDataException(Localizer.Get("S1031"));
            array = macros;
        }
        else
        {
            throw new InvalidDataException(
                Localizer.Get("S1032"));
        }

        var imported = new List<MacroConfig>();
        var rejected = new List<string>();
        var usedIds = new HashSet<string>(existing.Select(m => m.Id), StringComparer.Ordinal);
        var usedNames = new HashSet<string>(
            existing.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var element in array)
        {
            string displayName = Localizer.Get("S1033");
            if (element is JsonObject obj
                && obj["name"] is JsonValue nameValue
                && nameValue.TryGetValue<string>(out var rawName)
                && !string.IsNullOrWhiteSpace(rawName))
                displayName = rawName;

            MacroConfig? macro;
            try
            {
                macro = element.Deserialize<MacroConfig>(ImportOptions);
            }
            catch (Exception ex)
            {
                rejected.Add(Localizer.Format("S1034", displayName, ex.Message));
                continue;
            }

            if (macro is null)
            {
                rejected.Add(Localizer.Format("S1035", displayName));
                continue;
            }

            string? problem = Validate(macro);
            if (problem is not null)
            {
                rejected.Add($"{macro.Name}: {problem}");
                continue;
            }

            // Speed: §69 fallback (finite and > 0, else 1.0).
            if (double.IsNaN(macro.SpeedMultiplier)
                || double.IsInfinity(macro.SpeedMultiplier)
                || macro.SpeedMultiplier <= 0)
                macro.SpeedMultiplier = 1.0;

            // Id: preserve when free, else fresh (never overwrite).
            if (string.IsNullOrWhiteSpace(macro.Id) || !usedIds.Add(macro.Id))
                macro.Id = Guid.NewGuid().ToString("N");

            // Name: suffix on collision, same numbering as Duplikat.
            if (!usedNames.Add(macro.Name))
            {
                string baseName = macro.Name + Localizer.Get("S0379");
                string name = baseName;
                int n = 2;
                while (!usedNames.Add(name))
                    name = $"{baseName} {n++}";
                macro.Name = name;
            }

            imported.Add(macro);
        }

        return new ImportResult(imported, rejected);
    }

    /// <summary>Structural validation only (no ActionId existence check -
    /// unknown ids fail honestly at playback). Returns the problem, or
    /// null when the macro is importable.</summary>
    private static string? Validate(MacroConfig macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name))
            return Localizer.Get("S1036");
        // A-15: Steps: null ditolak sebagai makro 0-langkah (bukan NRE);
        // penolakan tetap per-makro di Import.
        if (macro.Steps is null || macro.Steps.Count == 0)
            return Localizer.Get("S1037");
        if (macro.Cycles < 1 || macro.Cycles > MaxCycles)
            return Localizer.Format("S1039", MaxCycles);
        for (int i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            if (step is null || string.IsNullOrWhiteSpace(step.ActionId))
                return Localizer.Format("S1040", i + 1);
            step.Params ??= new JsonObject();
            if (step.DelayAfterMs < 0 || step.DelayAfterMs > MaxDelayMs)
                return Localizer.Format("S1041", i + 1, MaxDelayMs);
        }
        return null;
    }
}
