using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Actions.Common;

namespace Keystream.Actions.Browser;

/// <summary>
/// "browsergroup" - open a browser profile group (playlist) with one
/// tile press. Params: "groupId". The group itself (mode + delay +
/// entries + openMode) lives in DeckConfig.BrowserProfileGroups and is
/// edited in the group manager; this action only reads it, so editing a
/// group never requires re-saving the tile.
/// Group openMode: "New" (always open a new window per entry) or
/// "Focus" (focus the tracked window for entries that already have one,
/// open only the missing ones - a second group press adds no
/// duplicates). No "Toggle" for groups: closing a whole group at once
/// is ambiguous and destructive; a stored "Toggle" is treated as Focus.
/// Simultaneous: every entry is launched, 150 ms apart so the disk is
/// not hammered at once. Sequential: one entry per DelayMs (clamped to
/// 0–10 s). A failed entry never aborts the rest; failures are listed
/// in the returned error so the user knows exactly which profiles did
/// not open. Tracking is persisted like the single-profile action; the
/// parameterless-tracking constructor used by tests keeps it in memory.</summary>
public sealed class BrowserGroupAction : IAction
{
    private readonly Func<IReadOnlyList<BrowserProfileGroup>> _groups;
    private readonly Func<List<BrowserWindowTracking>>? _getTracking;
    private readonly Action? _saveTracking;
    private readonly List<BrowserWindowTracking> _memoryTracking = new();

    public BrowserGroupAction(Func<IReadOnlyList<BrowserProfileGroup>> groups)
        : this(groups, null, null) { }

    public BrowserGroupAction(
        Func<IReadOnlyList<BrowserProfileGroup>> groups,
        Func<List<BrowserWindowTracking>>? getTracking,
        Action? saveTracking)
    {
        _groups = groups;
        _getTracking = getTracking;
        _saveTracking = saveTracking;
    }

    public ActionDescriptor Descriptor => new("browsergroup", Localizer.Get("S0581"), Localizer.Get("S1071"),
        Localizer.Get("S0582"));

    private List<BrowserWindowTracking> Tracking => _getTracking?.Invoke() ?? _memoryTracking;

    public ValidationResult Validate(JsonObject parameters)
    {
        string groupId = ParamReader.GetString(parameters, "groupId", "GroupId") ?? "";
        return string.IsNullOrWhiteSpace(groupId)
            ? ValidationResult.Invalid(Localizer.Get("S1115"))
            : ValidationResult.Valid;
    }

    public async Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string groupId = ParamReader.GetString(parameters, "groupId", "GroupId") ?? "";
        var group = _groups().FirstOrDefault(g => g.Id == groupId);
        if (group is null)
            return ActionResult.Fail(Localizer.Get("S1116"));
        if (group.Entries.Count == 0)
            return ActionResult.Fail(Localizer.Format("S1117", group.Name));
        bool sequential = string.Equals(group.Mode, "Sequential", StringComparison.OrdinalIgnoreCase);
        bool focusMode = BrowserAction.NormalizeOpenMode(group.OpenMode) == "Focus"
            || string.Equals(group.OpenMode, "Toggle", StringComparison.OrdinalIgnoreCase);
        int delayMs = Math.Clamp(group.DelayMs, 0, 10000);
        var tracking = Tracking;
        var failed = new List<string>();
        int opened = 0, focused = 0;
        for (int i = 0; i < group.Entries.Count; i++)
        {
            var entry = group.Entries[i];
            // Manual entries track by full folder path (unique); registered
            // entries track by profile key - same convention as the
            // single-profile action, so the two can never hijack each other.
            string trackingKey = entry.ProfilePath.Length > 0 ? entry.ProfilePath : entry.ProfileKey;
            var existing = BrowserWindowTracker.Find(tracking, entry.BrowserExe, trackingKey);
            if (focusMode && existing is not null && BrowserWindowTracker.IsValid(existing))
            {
                // Focus mengenai: hanya fokus, URL diabaikan - aturan yang
                // sama seperti tile tunggal (tidak ada jalur CLI bersih
                // untuk membuka tab di jendela persis itu). Dulu Focus()
                // yang gagal (SetForegroundWindow false) tetap dihitung
                // "dibuka" (§45); kini gagal = entri gagal, dan fokus
                // dihitung terpisah dari peluncuran.
                if (BrowserWindowTracker.Focus(existing))
                    focused++;
                else
                    failed.Add(Localizer.Format("S1118", entry.ProfileDisplay));
            }
            else
            {
                // Guard per entri: URL tidak valid menggagalkan entri ini
                // saja (tanpa meluncurkan), grup tetap lanjut ke entri
                // berikutnya - tidak pernah diam-diam membuka halaman awal.
                (string? entryUrl, string? urlError) = BrowserAction.NormalizeUrl(entry.Url);
                if (urlError is not null)
                {
                    failed.Add($"{entry.ProfileDisplay} ({urlError})");
                }
                else
                {
                    var before = BrowserWindowTracker.SnapshotWindows();
                    var (ok, error) = BrowserLauncher.Launch(entry.BrowserExe, entry.Engine, entry.ProfileKey, entry.ProfilePath, entryUrl, entry.UserDataDir);
                    if (ok)
                    {
                        opened++;
                        _ = TrackInBackgroundAsync(entry.BrowserExe, trackingKey, before, context.Cancellation);
                    }
                    else failed.Add($"{entry.ProfileDisplay} ({error})");
                }
            }
            if (i < group.Entries.Count - 1)
                await Task.Delay(sequential ? delayMs : 150, context.Cancellation);
        }
        var doneParts = new List<string>();
        if (opened > 0) doneParts.Add($"{opened} profil dibuka");
        if (focused > 0) doneParts.Add($"{focused} profil difokuskan");
        string doneText = string.Join(", ", doneParts);
        if (failed.Count == 0) return ActionResult.Ok(detail: Localizer.Format("S1119", group.Name, doneText));
        if (doneParts.Count == 0)
            return ActionResult.Fail(Localizer.Format("S1120", group.Name, string.Join("; ", failed)));
        return ActionResult.Fail(
            Localizer.Format("S1121", doneText, group.Entries.Count, group.Name, string.Join("; ", failed)));
    }

    private async Task TrackInBackgroundAsync(
        string exe, string key, HashSet<IntPtr> before, CancellationToken cancellation)
    {
        try
        {
            var record = await BrowserWindowTracker.TrackNewWindowAsync(exe, key, before, cancellation);
            if (record is null) return;
            BrowserWindowTracker.Upsert(Tracking, record);
            _saveTracking?.Invoke();
        }
        catch
        {
            // Best-effort tracking only (see BrowserWindowTracker).
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
