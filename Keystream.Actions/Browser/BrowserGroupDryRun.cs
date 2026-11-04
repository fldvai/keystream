using Keystream.Core.Config;
using Keystream.Core.Localization;

namespace Keystream.Actions.Browser;

/// <summary>One dry-run verdict for a group entry. Ok with an empty Note
/// means fully verified; Ok with a Note means usable but one check was
/// skipped (browser not in the detected list, so its profile list could
/// not be read); !Ok means the entry would fail at execution time.</summary>
public sealed record DryRunEntryResult(string Display, bool Ok, string Note);

/// <summary>Validates a browser profile group WITHOUT launching anything.
/// Pure file-system reads plus URL validation: exe existence, manual
/// folder existence + detectability, URL validity, and - when the entry's
/// browser is in the detected list - profile-key membership in its live
/// profile list. Used by the group manager's "Periksa grup" button; kept
/// here (not in the view-model) so the harness can test it.</summary>
public static class BrowserGroupDryRun
{
    public static IReadOnlyList<DryRunEntryResult> CheckGroup(
        BrowserProfileGroup group, IEnumerable<CustomBrowserConfig>? customs)
    {
        var detected = BrowserDiscovery.DetectBrowsers(customs);
        var results = new List<DryRunEntryResult>();
        // A-02: grup/entri null (explicit-null di config) tak lagi NRE;
        // entri null dibuang seperti di Normalize, grup null = 0 hasil.
        if (group is null) return results;
        foreach (var entry in group.Entries ?? Enumerable.Empty<BrowserGroupEntry>())
        {
            if (entry is null) continue;
            string profileDisplay = entry.ProfileDisplay ?? "";
            string profileKey = entry.ProfileKey ?? "";
            string display = profileDisplay.Length > 0 ? profileDisplay : profileKey;
            if (string.IsNullOrWhiteSpace(entry.BrowserExe))
            {
                results.Add(new DryRunEntryResult(display, false, Localizer.Get("S1101")));
                continue;
            }
            if (!File.Exists(entry.BrowserExe))
            {
                results.Add(new DryRunEntryResult(display, false, Localizer.Format("S1122", entry.BrowserExe)));
                continue;
            }
            (_, string? urlError) = BrowserAction.NormalizeUrl(entry.Url);
            if (urlError is not null)
            {
                results.Add(new DryRunEntryResult(display, false, urlError));
                continue;
            }
            string profilePath = entry.ProfilePath ?? "";
            if (profilePath.Length > 0)
            {
                if (!Directory.Exists(profilePath))
                {
                    results.Add(new DryRunEntryResult(display, false, Localizer.Format("S1123", profilePath)));
                    continue;
                }
                if (!BrowserDiscovery.TryDetectProfileFolder(profilePath, out _, out _))
                {
                    results.Add(new DryRunEntryResult(display, false, Localizer.Get("S0800")));
                    continue;
                }
                results.Add(new DryRunEntryResult(display, true, ""));
                continue;
            }
            if (string.IsNullOrWhiteSpace(profileKey))
            {
                results.Add(new DryRunEntryResult(display, false, Localizer.Get("S1103")));
                continue;
            }
            var browser = detected.FirstOrDefault(b =>
                string.Equals(b.ExePath, entry.BrowserExe, StringComparison.OrdinalIgnoreCase));
            if (browser is null)
            {
                results.Add(new DryRunEntryResult(display, true, Localizer.Get("S1124")));
                continue;
            }
            bool found = BrowserDiscovery.GetProfiles(browser).Any(p => p.Key == profileKey);
            results.Add(found
                ? new DryRunEntryResult(display, true, "")
                : new DryRunEntryResult(display, false, Localizer.Format("S1125", profileKey)));
        }
        return results;
    }

    public static string Summarize(IReadOnlyList<DryRunEntryResult> results)
        => results.Count == 0
            ? Localizer.Get("S0803")
            : Localizer.Format("S1126", results.Count(r => r.Ok), results.Count);
}
