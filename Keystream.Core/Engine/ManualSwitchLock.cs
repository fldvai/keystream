using System;
using System.Collections.Generic;

namespace Keystream.Core.Engine;

/// <summary>What the manual-switch lock decides on a foreground change,
/// evaluated by <see cref="ManualSwitchLock.Evaluate"/> after the
/// switch-back armed checks and once the winning auto-switch match
/// (if any) is known.</summary>
public enum ManualLockDecision
{
    /// <summary>Normal matching proceeds: the foreground left the
    /// locked set, no manual move is holding, or the move happened
    /// while the foreground was outside the trigger set (entering
    /// the set must fire).</summary>
    Proceed,
    /// <summary>A manual move made while inside the trigger set is
    /// holding: suppress auto-switching, consume the tick.</summary>
    Suppress,
}

/// <summary>Manual-switch lock: the rule that a manual (profile, page)
/// move beats auto-switch until the trigger app is left. Pure
/// in-memory state, never serialized — a restart clears it, and the
/// host owns one instance per process lifetime.
///
/// The host reports every successful auto action through
/// <see cref="NoteAutoSet"/>; any other (profile, page) change seen
/// on a later tick is a manual move. The move itself leaves no tick
/// trace (it happens between foreground changes), so the lock keys
/// on the PREVIOUS evaluated tick: the move counts as "inside" only
/// when the foreground at the last observation was inside that
/// tick's winning match set, and the lock engages only while the
/// foreground is still inside that SAME set. Leaving the set is
/// observed on the leaving tick itself: a holding lock unlocks
/// there, and the prev-record the tick leaves behind is outside
/// the set, so re-entering fires normally. That holds for the
/// armed path only because the host observes armed ticks too:
/// StayArmed ticks call Evaluate with the armed trigger set, and
/// the FireReturn (leaving) tick falls through to normal
/// matching, whose Evaluate records the outside foreground
/// after the return's NoteAutoSet. A StayArmed observation
/// cannot engage the lock: the current (profile, page) is the
/// armed target, which is also the auto baseline, so there is
/// no divergence. The one known blur is a manual move made within
/// the same ~700ms timer interval as a foreground change: the
/// previous observation is then stale-inside and the lock can
/// engage on entry; it self-heals on the next leave/re-enter.
///
/// The lock never coexists with an armed switch-back: it engages
/// only on the DisarmAndContinue path (unarmed, or diverged and
/// disarmed by the armed check), and while locked no switch can
/// happen, so no new arm can form. StayArmed ticks also call
/// Evaluate, for observation only; they cannot engage the lock
/// (no divergence there - see above).</summary>
public sealed class ManualSwitchLock
{
    private bool _hasBaseline;
    private string _autoProfileId = "";
    private string _autoPageId = "";

    private bool _locked;
    private HashSet<string> _lockedSet = new(StringComparer.OrdinalIgnoreCase);

    private string _prevForegroundBare = "";
    private HashSet<string> _prevMatchSet = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the lock is currently holding.</summary>
    public bool IsLocked => _locked;

    /// <summary>Record a (profile, page) established by an auto action
    /// (auto-switch or switch-back return), after its Save succeeded.
    /// A rolled-back switch must never call this.</summary>
    public void NoteAutoSet(string profileId, string pageId)
    {
        _autoProfileId = profileId;
        _autoPageId = pageId;
        _hasBaseline = true;
    }

    /// <summary>Decide on a foreground change. The host calls this once
    /// per evaluated tick with the winning match's FULL trigger entry
    /// list (page entries for a mode match, profile entries for a
    /// profile match), or null when nothing matched — the null call
    /// still updates the previous-tick record and observes leaving
    /// the locked set. The first call adopts the current position
    /// as the baseline, so a manual move made before any auto action
    /// never counts as divergence.</summary>
    public ManualLockDecision Evaluate(string currentProfileId, string currentPageId,
        string? foregroundProcessName, IEnumerable<string>? matchedTriggerEntries)
    {
        if (!_hasBaseline)
        {
            _autoProfileId = currentProfileId;
            _autoPageId = currentPageId;
            _hasBaseline = true;
        }

        string bare = string.IsNullOrWhiteSpace(foregroundProcessName)
            ? ""
            : AutoProfileMatcher.NormalizeExeName(foregroundProcessName);

        var currentSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (matchedTriggerEntries is not null)
            foreach (var entry in matchedTriggerEntries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                currentSet.Add(AutoProfileMatcher.NormalizeExeName(entry));
            }

        ManualLockDecision decision;
        if (_locked && !_lockedSet.Contains(bare))
        {
            // Left the locked set: unlock. The engagement check below
            // cannot re-lock on this tick: it requires bare to be
            // inside the previous set, and bare just left the locked
            // family (or the previous set was already updated past it).
            _locked = false;
            _lockedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (_locked)
        {
            decision = ManualLockDecision.Suppress;
        }
        else
        {
            bool diverged = !string.Equals(currentProfileId, _autoProfileId, StringComparison.Ordinal)
                || !string.Equals(currentPageId, _autoPageId, StringComparison.Ordinal);
            if (diverged
                && _prevMatchSet.Contains(_prevForegroundBare)
                && _prevMatchSet.Contains(bare))
            {
                // The manual move happened while inside the previous
                // tick's trigger set, and the foreground is still
                // inside that same set: hold the manual choice.
                _lockedSet = new HashSet<string>(_prevMatchSet, StringComparer.OrdinalIgnoreCase);
                _locked = true;
                decision = ManualLockDecision.Suppress;
            }
            else
            {
                decision = ManualLockDecision.Proceed;
            }
        }

        _prevForegroundBare = bare;
        _prevMatchSet = currentSet;
        return decision;
    }
}
