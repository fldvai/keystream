using System.Text.Json;
using System.Text.Json.Serialization;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;

namespace Keystream.Core.Config;

/// <summary>
/// The portable `.ksprofile` package (PRD §9): one profile, shareable as a
/// single file. Deliberately NOT signed in this version - a package is
/// data, and importing one never overwrites anything: the host adds it as
/// a new profile. §171 Task 6: the package MAY also carry the single
/// shared Global page as an optional root "global" field (whole
/// PageConfig, verbatim). Old apps never read that field - Parse only
/// looks at "format"/"packageVersion"/"profile" - so the field is
/// ignored by them, and packageVersion stays 1. Import asks what to do
/// with it (replace/merge/skip, default skip) and never overwrites the
/// importer's Global silently.
/// The format is versioned and validated on import:
///  - the "format" marker must be "ksprofile" (legacy "mkdprofile"
///    packages from before the Keystream rename are also accepted);
///  - "packageVersion" newer than supported is refused, not half-read;
///  - the profile must have at least one page and every binding a scan
///    code (bindings without one are dropped, counted in the report);
///  - action ids the importing app does not know are kept verbatim but
///    reported as warnings, so a package shared from a richer install
///    degrades honestly instead of silently doing nothing.
/// </summary>
public static class ProfilePackage
{
    public const string FormatMarker = "ksprofile";
    public const string LegacyFormatMarker = "mkdprofile";
    public const int CurrentPackageVersion = 1;
    public const string FileExtension = ".ksprofile";

    /// <summary>Tulis ekspor secara atomik: tulis file temp dulu, lalu
    /// pindah (overwrite) ke tujuan. Dulu WriteAllText langsung ke
    /// tujuan: disk penuh di tengah tulis meninggalkan .ksprofile
    /// parsial yang nanti ditolak saat diimpor (§45).</summary>
    public static void SaveToFile(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    public sealed record PackageFile(
        string Format,
        int PackageVersion,
        string ExportedBy,
        ProfileConfig Profile,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        PageConfig? Global = null);

    public sealed record ImportReport(
        ProfileConfig Profile,
        int BindingsImported,
        int BindingsDropped,
        IReadOnlyList<string> Warnings,
        PageConfig? Global = null,
        int GlobalImported = 0,
        int GlobalDropped = 0);

    /// <summary>§171 Task 6: when globalPage carries at least one
    /// binding, the package also holds it verbatim under "global".
    /// Empty/absent Global keeps the package byte-identical to the
    /// pre-Task-6 shape (no "global" field at all).</summary>
    public static string Serialize(ProfileConfig profile, string exportedBy = "Keystream",
        PageConfig? globalPage = null)
    {
        PageConfig? global = globalPage is not null && globalPage.Bindings.Count > 0
            ? globalPage : null;
        var package = new PackageFile(FormatMarker, CurrentPackageVersion, exportedBy, profile, global);
        return JsonSerializer.Serialize(package, ConfigStore.JsonOptions);
    }

    /// <summary>Parses and validates a package. Throws InvalidDataException
    /// with a user-readable reason for anything structurally unacceptable;
    /// softer problems (dropped bindings, unknown actions) come back as
    /// warnings in the report. knownActionIds may be null to skip the
    /// unknown-action check.</summary>
    public static ImportReport Parse(string json, IReadOnlySet<string>? knownActionIds = null)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                   ?? throw new InvalidDataException(Localizer.Get("S1042"));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Localizer.Format("S1028", ex.Message));
        }

        string format = GetString(root, "format");
        if (format != FormatMarker && format != LegacyFormatMarker)
            throw new InvalidDataException(Localizer.Format("S1043", FileExtension, format));

        int version = GetInt(root, "packageVersion");
        if (version > CurrentPackageVersion)
            throw new InvalidDataException(
                Localizer.Format("S1044", version, CurrentPackageVersion));

        if (root["profile"] is not JsonObject profileNode)
            throw new InvalidDataException(Localizer.Get("S1045"));

        ProfileConfig profile;
        try
        {
            profile = profileNode.Deserialize<ProfileConfig>(ConfigStore.JsonOptions)
                      ?? throw new InvalidDataException(Localizer.Get("S1046"));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Localizer.Format("S1047", ex.Message));
        }

        // A-02/A-03: normalisasi yang sama seperti jalur Load —
        // explicit null ("pages": null, "bindings": null, "steps": null,
        // ...) menjadi daftar kosong, jadi pemeriksaan di bawah tak lagi
        // NRE dan Save sesudah impor tak mempersisten null ke tick.
        ConfigStore.NormalizeProfile(profile);

        if (profile.Pages.Count == 0)
            throw new InvalidDataException(Localizer.Get("S1048"));

        // A-03: Id halaman diregenerasi seperti halaman baru
        // (Guid.NewGuid().ToString("N")) — Id paket tak boleh bentrok
        // dengan halaman yang sudah ada di config pengimpor.
        foreach (var page in profile.Pages)
            page.Id = Guid.NewGuid().ToString("N");

        var warnings = new List<string>();
        int imported = 0, dropped = 0;
        foreach (var page in profile.Pages)
        {
            var kept = new List<BindingConfig>();
            foreach (var binding in page.Bindings)
            {
                if (binding.ScanCode == 0)
                {
                    dropped++;
                    continue;
                }
                kept.Add(binding);
                imported++;
                if (knownActionIds is not null)
                {
                    foreach (var step in binding.Steps)
                    {
                        if (!knownActionIds.Contains(step.ActionId))
                            warnings.Add(Localizer.Format("S1049", binding.KeyId, step.ActionId));
                    }
                }
            }
            page.Bindings = kept;
        }

        // §171 Task 6: optional "global" section - same validation as
        // a profile page (keyless bindings dropped + counted, unknown
        // actions warned with the same S1049). Missing, null, or
        // non-object = absent (old packages behave byte-identically:
        // no prompt, Global untouched). A present-but-malformed object
        // rejects the package like a malformed profile. An empty
        // section (zero kept bindings) is treated as absent: no prompt.
        PageConfig? global = null;
        int globalImported = 0, globalDropped = 0;
        if (root["global"] is JsonObject globalNode)
        {
            PageConfig parsedGlobal;
            try
            {
                parsedGlobal = globalNode.Deserialize<PageConfig>(ConfigStore.JsonOptions)
                    ?? throw new InvalidDataException(Localizer.Get("S1046"));
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(Localizer.Format("S1047", ex.Message));
            }
            // A-02/A-03: normalisasi yang sama seperti halaman profil.
            ConfigStore.NormalizePage(parsedGlobal);
            var keptGlobal = new List<BindingConfig>();
            foreach (var binding in parsedGlobal.Bindings)
            {
                if (binding.ScanCode == 0)
                {
                    globalDropped++;
                    continue;
                }
                keptGlobal.Add(binding);
                globalImported++;
                if (knownActionIds is not null)
                {
                    foreach (var step in binding.Steps)
                    {
                        if (!knownActionIds.Contains(step.ActionId))
                            warnings.Add(Localizer.Format("S1049", binding.KeyId, step.ActionId));
                    }
                }
            }
            parsedGlobal.Bindings = keptGlobal;
            if (globalImported > 0)
                global = parsedGlobal;
        }

        return new ImportReport(profile, imported, dropped, warnings,
            global, globalImported, globalDropped);
    }

    private static string GetString(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static int GetInt(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var node) && node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
}
