namespace Keystream.App.Host;

/// <summary>§151 P3 - the decision record of a reset quiesce: what was
/// running before <see cref="AppHost.BeginResetQuiesce"/> stopped it,
/// so <see cref="AppHost.RestoreAfterFailedReset"/> restarts exactly
/// what was running - nothing more. Deliberately WPF-free (bools
/// only): the timer/service actuation lives in AppHost, the
/// restore DECISIONS live here and are pinned by the wb-harness.
/// A snapshot is single-use: once marked restored it must never
/// drive a second restore (the save gate would be released twice
/// and the service started twice).</summary>
public sealed class ResetQuiesceState
{
    /// <summary>Whether the 25 ms engine tick timer was enabled when
    /// quiesce began. False means "leave stopped" on restore -
    /// quiesce must never start a timer that was already stopped.</summary>
    public bool TickTimerWasEnabled { get; init; }

    /// <summary>Whether the 500 ms health timer was enabled.</summary>
    public bool HealthTimerWasEnabled { get; init; }

    /// <summary>Whether the 700 ms foreground-watcher timer was enabled.</summary>
    public bool ForegroundTimerWasEnabled { get; init; }

    /// <summary>Whether the interception service reported Running
    /// when quiesce began. Stopped/Faulted/Starting are all "was
    /// not running": restore must not start a service the user
    /// (or a fault) had left off.</summary>
    public bool ServiceWasRunning { get; init; }

    /// <summary>True once <see cref="MarkRestored"/> has run; the
    /// AppHost restore path checks this before acting, so a second
    /// restore call is a no-op.</summary>
    public bool Restored { get; private set; }

    /// <summary>Whether the service should be restarted on restore:
    /// only when it was running before AND the stored deck identity
    /// is still a valid interception hardware id that does not
    /// require re-identification - the same conditions AppHost.Start
    /// applies at startup. A service that was off, faulted, or
    /// whose deck needs re-identification stays off; the health
    /// surface shows that honestly.</summary>
    public bool ShouldRestartService(bool deckValid, bool requiresReidentification)
        => ServiceWasRunning && deckValid && !requiresReidentification;

    /// <summary>Marks this snapshot consumed. Idempotent: marking
    /// twice keeps Restored true and changes nothing else.</summary>
    public void MarkRestored() => Restored = true;
}
