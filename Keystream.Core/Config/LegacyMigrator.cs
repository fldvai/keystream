using System.Text.Json;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Core.Devices;

namespace Keystream.Core.Config;

public sealed record MigrationReport(
    int ProfilesMigrated,
    int BindingsMigrated,
    int BindingsUnassigned,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Migrates the legacy (audited app) config.json into schema v1. The legacy
/// file is default-System.Text.Json over CommunityToolkit view-models:
/// PascalCase property names, no schema version, bindings as a single
/// ActionType + Dictionary&lt;string,string&gt; ActionParams.
///
/// Two honest-migration decisions, both surfaced in the report:
///  1. The legacy deck identity (TargetDeckPath) is migrated as
///     DeviceIdKind.LegacyUnverified with RequiresReidentification = true.
///     The legacy wizard enumerated Raw Input paths while other parts of
///     the app treated the same string as a Mode A hardware id (audit B3-4),
///     so the value's namespace cannot be proven - it must never drive
///     blocking, and the user re-identifies the deck once in the new wizard.
///  2. The legacy model stored no E0 bit, so Enter vs Numpad-Enter (and
///     similar twins) cannot be told apart after migration; affected
///     bindings keep working by scan code and can be re-bound in the UI.
/// </summary>
public static class LegacyMigrator
{
    public static bool LooksLegacy(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            if (Get(root, "schemaVersion") is not null || Get(root, "SchemaVersion") is not null) return false;
            return Get(root, "TargetDeckPath") is not null || Get(root, "Profiles") is not null;
        }
        catch { return false; }
    }

    public static (DeckConfig Config, MigrationReport Report) Migrate(string legacyJson)
    {
        var warnings = new List<string>();
        var root = JsonNode.Parse(legacyJson) as JsonObject
                   ?? throw new InvalidDataException(Localizer.Get("S1022"));

        var config = new DeckConfig { SchemaVersion = ConfigSchema.CurrentVersion };

        string deckPath = GetString(root, "TargetDeckPath");
        if (deckPath.Length > 0)
        {
            config.DeckDevice = new DeviceBinding
            {
                Kind = nameof(DeviceIdKind.LegacyUnverified),
                Value = DeviceId.Normalize(deckPath),
                FriendlyName = GetString(root, "TargetDeckFriendlyName"),
                RequiresReidentification = true,
            };
            warnings.Add(Localizer.Get("S1023"));
        }

        config.Settings = new DeckSettings
        {
            PauseHotkey = GetString(root, "PauseHotkey", "F9"),
            StartWithWindows = GetBool(root, "StartWithWindows"),
            StartMinimized = GetBool(root, "StartMinimized"),
            ObsHost = GetString(root, "ObsHost", "ws://127.0.0.1:4455"), // Literal — never localized (S0827 is display text only).
            ObsPassword = GetString(root, "ObsPassword"),
            ActivePageId = GetString(root, "ActivePageId"),
        };
        config.ActiveProfileId = GetString(root, "ActiveProfileId");

        int bindingsMigrated = 0, bindingsUnassigned = 0;
        if (Get(root, "Profiles") is JsonArray profiles)
        {
            foreach (var pNode in profiles)
            {
                if (pNode is not JsonObject p) continue;
                var profile = new ProfileConfig
                {
                    Id = GetString(p, "Id", Guid.NewGuid().ToString("N")),
                    Name = GetString(p, "Name", Localizer.Get("S1016")),
                };
                if (Get(p, "AutoSwitchExe") is JsonArray exes)
                    profile.AutoSwitchExe = exes.Select(e => e is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(s => s.Length > 0).ToList();

                if (Get(p, "Pages") is JsonArray pages)
                {
                    foreach (var pgNode in pages)
                    {
                        if (pgNode is not JsonObject pg) continue;
                        var page = new PageConfig
                        {
                            Id = GetString(pg, "Id", Guid.NewGuid().ToString("N")),
                            Name = GetString(pg, "Name", Localizer.Get("S1017")),
                        };
                        if (Get(pg, "Bindings") is JsonArray bindings)
                        {
                            foreach (var bNode in bindings)
                            {
                                if (bNode is not JsonObject b) continue;
                                page.Bindings.Add(MigrateBinding(b, warnings,
                                    ref bindingsMigrated, ref bindingsUnassigned));
                            }
                        }
                        profile.Pages.Add(page);
                    }
                }
                config.Profiles.Add(profile);
            }
        }

        if (config.Profiles.Count == 0)
            warnings.Add(Localizer.Get("S1024"));
        else if (bindingsMigrated > 0)
            warnings.Add(Localizer.Get("S1025"));

        var report = new MigrationReport(config.Profiles.Count, bindingsMigrated, bindingsUnassigned, warnings);
        return (config, report);
    }

    private static BindingConfig MigrateBinding(JsonObject b, List<string> warnings,
        ref int migrated, ref int unassigned)
    {
        var binding = new BindingConfig
        {
            KeyId = GetString(b, "KeyId"),
            VirtualKey = GetUInt(b, "VKey"),
            ScanCode = (ushort)GetUInt(b, "ScanCode"),
            IsE0 = false, // not stored by the legacy format (see class docs)
            Label = GetString(b, "Label", "Unassigned"), // Literal — never localized; compared literally by BindingEditorViewModel auto-naming.
            Icon = GetString(b, "Icon"),
            BackgroundColor = GetString(b, "BackgroundColor", "#1E222D"),
            Trigger = TriggerKind.Tap,
        };

        string actionType = GetString(b, "ActionType", "None");
        if (actionType.Length == 0 || actionType.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            unassigned++;
            return binding;
        }

        string actionId = actionType.ToLowerInvariant() switch
        {
            "obs" => "obs",
            "hotkey" => "hotkey",
            "media" => "media",
            "launcher" => "launcher",
            "sound" => "sound",
            var other => other,
        };
        if (actionId == "sound")
            warnings.Add(Localizer.Format("S1026", binding.KeyId));
        else if (actionId != "obs" && actionId != "hotkey" && actionId != "media" && actionId != "launcher")
            warnings.Add(Localizer.Format("S1027", binding.KeyId, actionType, actionId));

        var stepParams = new JsonObject();
        if (Get(b, "ActionParams") is JsonObject prms)
        {
            foreach (var kv in prms)
                stepParams[kv.Key] = kv.Value is null ? null : JsonValue.Create(kv.Value.ToString());
        }
        binding.Steps.Add(new ActionStep { ActionId = actionId, Params = stepParams });
        migrated++;
        return binding;
    }

    // --- case-insensitive helpers over the legacy PascalCase shape ---

    private static JsonNode? Get(JsonObject obj, string name)
    {
        foreach (var kv in obj)
            if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    private static string GetString(JsonObject obj, string name, string fallback = "")
        => Get(obj, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;

    private static bool GetBool(JsonObject obj, string name)
        => Get(obj, name) is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static uint GetUInt(JsonObject obj, string name)
        => Get(obj, name) is JsonValue v && v.TryGetValue<uint>(out var u) ? u : 0;
}
