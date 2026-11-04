using System.Text.Json.Nodes;
using Keystream.Core.Config;

namespace Keystream.Core.Actions;

/// <summary>Outcome of an action execution. The audited actions returned
/// void and swallowed every failure, so the UI could claim success for a
/// command that never ran (findings B2-1/B2-4). Every execution now reports
/// honestly: success, an error the user can read, and the measured
/// duration.</summary>
public sealed record ActionResult(bool Success, string? Error, long DurationMs, string? Detail = null)
{
    public static ActionResult Ok(long durationMs = 0, string? detail = null) => new(true, null, durationMs, detail);
    public static ActionResult Fail(string error, long durationMs = 0, string? detail = null) => new(false, error, durationMs, detail);
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static readonly ValidationResult Valid = new(true, Array.Empty<string>());
    public static ValidationResult Invalid(params string[] errors) => new(false, errors);
}

public sealed record ActionDescriptor(string Id, string DisplayName, string Category, string Description);

/// <summary>Host hand-off for an execution. Deliberately minimal in Phase 1;
/// Phase 3 adds typed service accessors (OBS client, audio, launcher) here
/// instead of letting actions reach for statics or singletons.</summary>
public sealed class ActionContext
{
    public required CancellationToken Cancellation { get; init; }

    /// <summary>The trigger whose firing caused this execution (v3.3.0).
    /// Paired kinds (Momentary on key-down, MomentaryRelease on key-up)
    /// let an action implement hold/release semantics. Defaults to Tap
    /// for executions not caused by a trigger (editor test runs).</summary>
    public TriggerKind FiredKind { get; init; } = TriggerKind.Tap;

    /// <summary>§156: signed mouse-wheel delta for this execution, in
    /// WHEEL_DELTA units (120 per notch; positive = wheel up, negative
    /// = wheel down). Only ScrollDial drain jobs carry a non-zero value
    /// (see DeckEngine.Tick / AddDialDelta); every other execution is 0.
    /// Actions that consume it divide by 120 to count notches.</summary>
    public int WheelDelta { get; init; } = 0;
}

/// <summary>
/// K3 - the honest action contract. Parameters are a <see cref="JsonObject"/>
/// so unknown/extra fields from other app versions survive a round-trip
/// instead of breaking deserialization (the legacy Dictionary&lt;string,
/// string&gt; model silently lost any non-string parameter).
/// Rules: Validate is pure and fast; ExecuteAsync must honor cancellation
/// and must never throw for an expected failure (return
/// <see cref="ActionResult.Fail"/>); TestAsync performs the user-visible
/// "test this button" run under the same rules.
/// </summary>
public interface IAction
{
    ActionDescriptor Descriptor { get; }

    ValidationResult Validate(JsonObject parameters);

    Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context);

    Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context);
}
