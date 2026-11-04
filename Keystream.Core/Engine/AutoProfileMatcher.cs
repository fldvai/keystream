using Keystream.Core.Config;

namespace Keystream.Core.Engine;

/// <summary>Pure auto-switch matching: given the foreground app's process
/// name, find the profile — or the mode (page) — that claims it. Entries
/// in AutoSwitchExe are matched case-insensitively, with or without the
/// ".exe" suffix (the legacy UI stored plain exe names like "obs64.exe";
/// users also type "obs64"). Pure so the rule is unit-testable; the
/// Windows host supplies the foreground process name.
/// Mode lookup (FindPageForProcess) uses the SAME normalization and
/// first-match-wins order as the profile lookup: profiles in config
/// order, pages within each profile in page order. The host checks mode
/// matches FIRST, so a mode claim beats a profile claim for the same
/// process; a process claimed by two modes lands on the first in order,
/// deterministically.</summary>
public static class AutoProfileMatcher
{
    /// <summary>Shared normalization, factored out of FindProfileForProcess
    /// as a pure refactor (same operations, same order): trim, then strip
    /// exactly one ".exe" suffix (case-insensitive). Callers skip
    /// null/whitespace inputs before calling this. Internal (not
    /// private) so <see cref="SwitchBackState"/> in this assembly can
    /// normalize trigger sets with the identical rule instead of a
    /// second copy; the host never calls it directly.</summary>
    internal static string NormalizeExeName(string value)
    {
        string trimmed = value.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    private static bool EntryMatches(string entry, string bareProcessName)
        => string.Equals(NormalizeExeName(entry), bareProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Null trigger lists (hand-edited config with an
    /// explicit null) are treated as empty rather than throwing,
    /// like <see cref="FindPageForProcess"/>; well-formed configs
    /// are unaffected.</summary>
    public static ProfileConfig? FindProfileForProcess(DeckConfig config, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        string bare = NormalizeExeName(processName);
        return config.Profiles.FirstOrDefault(profile => profile.AutoSwitchExe is not null && profile.AutoSwitchExe.Any(entry =>
        {
            if (string.IsNullOrWhiteSpace(entry)) return false;
            return EntryMatches(entry, bare);
        }));
    }

    /// <summary>Mode-level lookup: the first page, in profile order then
    /// page order, whose AutoSwitchExe claims <paramref name="processName"/>.
    /// Returns the owning profile together with the page, since a switch
    /// must set both ActiveProfileId and ActivePageId. The config-level
    /// GlobalPage is never scanned (it lives outside every profile's
    /// Pages), so it can never match. Null trigger lists (hand-edited
    /// config with an explicit null) are treated as empty rather than
    /// throwing; well-formed configs are unaffected.</summary>
    public static (ProfileConfig Profile, PageConfig Page)? FindPageForProcess(DeckConfig config, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        string bare = NormalizeExeName(processName);
        foreach (var profile in config.Profiles)
        {
            if (profile.Pages is null) continue;
            foreach (var page in profile.Pages)
            {
                if (page.AutoSwitchExe is null) continue;
                foreach (var entry in page.AutoSwitchExe)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    if (EntryMatches(entry, bare)) return (profile, page);
                }
            }
        }
        return null;
    }
}

/// <summary>What the armed switch-back check decides on a foreground
/// change, evaluated by <see cref="SwitchBackState.Evaluate"/> before
/// any normal auto-switch matching runs.</summary>
public enum SwitchBackDecision
{
    /// <summary>Still on the armed target and the foreground is still
    /// inside the trigger set: stay armed, consume the tick (no
    /// normal matching).</summary>
    StayArmed,
    /// <summary>The foreground left the trigger set while still on the
    /// armed target: the host fires the return and the state is
    /// already disarmed.</summary>
    FireReturn,
    /// <summary>Not armed, or the current (profile, page) diverged from
    /// the armed target (a manual switch): proceed to normal
    /// matching. The state is disarmed when it was armed.</summary>
    DisarmAndContinue,
}

/// <summary>The outcome of <see cref="SwitchBackState.Evaluate"/>,
/// carrying the return point with it: <see cref="Evaluate"/> disarms
/// on <see cref="SwitchBackDecision.FireReturn"/>, so the ids the
/// host must switch back to travel in this value instead of being
/// read back from the (already cleared) state. Empty on every other
/// decision.</summary>
public readonly record struct SwitchBackEvaluation(
    SwitchBackDecision Decision, string ReturnProfileId, string ReturnPageId);
/// serialized; a restart clears it because the host owns one instance
/// per process lifetime). One arm at most: any auto-switch replaces
/// the previous arm — the host calls <see cref="Arm"/> when the
/// switch's target flag is ON and <see cref="Disarm"/> when it is OFF,
/// and only after the switch's Save succeeded, so a rolled-back
/// switch never touches the arm. A no-op guard hit (already on the
/// target) never reaches the host's arm/disarm calls, so it changes
/// nothing. The trigger set is the switched-to target's FULL trigger
/// entry list, normalized with the same rule as
/// <see cref="AutoProfileMatcher"/> matching (trim + strip one
/// ".exe", case-insensitive), so switching between two listed apps
/// stays armed and only leaving the whole set fires the return.</summary>
public sealed class SwitchBackState
{
    public bool IsArmed { get; private set; }
    public string ReturnProfileId { get; private set; } = "";
    public string ReturnPageId { get; private set; } = "";
    public string TargetProfileId { get; private set; } = "";
    public string TargetPageId { get; private set; } = "";

    private HashSet<string> _triggerBareSet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The armed trigger set, bare names only. Exposed
    /// read-only for diagnostics/tests; the live set is replaced
    /// wholesale by <see cref="Arm"/>.</summary>
    public IReadOnlyCollection<string> TriggerBareSet => _triggerBareSet;

    /// <summary>Arm (or re-arm, replacing any previous arm) with the
    /// state active immediately before the switch as the return
    /// point, the switched-to target, and the target's full trigger
    /// entry list. Null/whitespace entries are skipped; an empty
    /// trigger list arms an empty set (unreachable in practice: the
    /// matcher cannot match an empty list).</summary>
    public void Arm(string returnProfileId, string returnPageId,
        string targetProfileId, string targetPageId, IEnumerable<string>? triggerEntries)
    {
        ReturnProfileId = returnProfileId;
        ReturnPageId = returnPageId;
        TargetProfileId = targetProfileId;
        TargetPageId = targetPageId;
        _triggerBareSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (triggerEntries is not null)
            foreach (var entry in triggerEntries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                _triggerBareSet.Add(AutoProfileMatcher.NormalizeExeName(entry));
            }
        IsArmed = true;
    }

    public void Disarm()
    {
        IsArmed = false;
        ReturnProfileId = "";
        ReturnPageId = "";
        TargetProfileId = "";
        TargetPageId = "";
        _triggerBareSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Decide on a foreground change, given the CURRENT
    /// (profile, page) and the new foreground process name (raw, as
    /// reported — normalized here with the matcher rule). Order is
    /// the contract: (a) divergence from the armed target disarms
    /// silently (manual override wins); (b) leaving the trigger set
    /// fires the return (and disarms, with the return point carried
    /// in the result); (c) otherwise stay armed. A null/empty
    /// foreground name is not in any trigger set, so it fires the
    /// return when armed and on target (the host never calls with
    /// null — its early-return filters it — but the rule stays
    /// total).</summary>
    public SwitchBackEvaluation Evaluate(string currentProfileId, string currentPageId, string? foregroundProcessName)
    {
        if (!IsArmed) return new SwitchBackEvaluation(SwitchBackDecision.DisarmAndContinue, "", "");
        if (!string.Equals(currentProfileId, TargetProfileId, StringComparison.Ordinal)
            || !string.Equals(currentPageId, TargetPageId, StringComparison.Ordinal))
        {
            Disarm();
            return new SwitchBackEvaluation(SwitchBackDecision.DisarmAndContinue, "", "");
        }
        string bare = string.IsNullOrWhiteSpace(foregroundProcessName)
            ? ""
            : AutoProfileMatcher.NormalizeExeName(foregroundProcessName);
        if (!_triggerBareSet.Contains(bare))
        {
            string returnProfileId = ReturnProfileId;
            string returnPageId = ReturnPageId;
            Disarm();
            return new SwitchBackEvaluation(SwitchBackDecision.FireReturn, returnProfileId, returnPageId);
        }
        return new SwitchBackEvaluation(SwitchBackDecision.StayArmed, "", "");
    }
}
