using System.Text.Json.Nodes;
using Keystream.Actions.Macro;

namespace Keystream.Actions.Mouse;

/// <summary>Kind of one recorded mouse event, in plain data terms.
/// Mirrors <c>MouseRecorder.MouseEventKind</c> (Keystream.Windows,
/// hook code) without depending on it, so this file compiles in the
/// Linux harness: the window translates the recorder's enums into
/// these values before calling <see cref="MouseRecordPlanner"/>.</summary>
public enum RecordedMouseKind
{
    Move,
    Down,
    Up,
    Wheel,
}

/// <summary>Button of one recorded mouse event, in plain data terms.
/// Mirrors <c>MouseRecorder.MouseButton</c> without depending on it.
/// <see cref="None"/> is the recorder's value for moves and wheel
/// events; Down/Up always carry a real button (recorder contract).</summary>
public enum RecordedMouseButton
{
    None,
    Left,
    Right,
    Middle,
}

/// <summary>Recording scope, in plain data terms. Mirrors
/// <c>MouseRecordSettings</c>' scope (<c>MouseScope</c>, Keystream.App)
/// without depending on the App layer: the window translates its
/// settings into <see cref="MouseRecordOptions"/> before calling
/// <see cref="MouseRecordPlanner"/>.</summary>
public enum RecordedMouseScope
{
    ClicksOnly,
    ClicksAndMoves,
    ClicksMovesAndScroll,
}

/// <summary>Coordinate reference of a recording, in plain data terms.
/// Mirrors <c>MouseRecordSettings</c>' mode
/// (<c>MouseCoordinateMode</c>, Keystream.App) without depending on
/// the App layer.</summary>
public enum RecordedMouseCoordinateMode
{
    Absolute,
    RelativeWindow,
}

/// <summary>Plain-data snapshot of the mouse recording settings for
/// one session (§107/§108). A snapshot, not a live reference: the
/// window builds it from <c>MouseRecordSettings</c> at recording
/// start (and refreshes <see cref="MouseRecordPlanner.Options"/>
/// when the settings dialog commits), so a mid-session settings
/// change can never split one recording across two setting sets -
/// the gear is disabled while recording, exactly as before.</summary>
public sealed record MouseRecordOptions(
    RecordedMouseScope Scope,
    int MoveThresholdPx,
    int MinMoveDelayMs,
    RecordedMouseCoordinateMode CoordinateMode,
    bool IgnoreSelfClicks,
    bool Verbatim);

/// <summary>One recorded mouse event, in plain data terms. Mirrors
/// <c>MouseRecorder.MouseRecord</c> (Kind, Button, X, Y, Delta, At)
/// without depending on hook code: <c>At</c> is the hook proc-time
/// timestamp, so gaps are computed from event time, not marshal
/// time, exactly as the window did inline before extraction.</summary>
public sealed record RecordedMouseEvent(
    RecordedMouseKind Kind,
    RecordedMouseButton Button,
    int X,
    int Y,
    int Delta,
    DateTimeOffset At);

/// <summary>Plain-data snapshot of the foreground window at record
/// time, for relative-mode steps. Mirrors
/// <c>MouseWindowHelper.ForegroundWindowInfo</c> minus its window
/// handle (never stored in a step): the window translates the
/// helper's record into this value inside the provider delegate it
/// passes to <see cref="MouseRecordPlanner.Plan"/>.</summary>
public sealed record ForegroundWindowSnapshot(
    string Title,
    string ClassName,
    string ProcessName,
    int Left,
    int Top,
    int Width,
    int Height);

/// <summary>Outcome of one <see cref="MouseRecordPlanner.Plan"/>
/// call: the event was dropped (nothing is recorded and neither
/// clock moves) or emitted as one macro step.</summary>
public enum MouseRecordOutcomeKind
{
    Dropped,
    Emitted,
}

/// <summary>Why a <see cref="MouseRecordOutcomeKind.Dropped"/> event
/// was dropped. One reason per drop site of the pre-extraction
/// inline logic, in the same evaluation order: ignore-self, scope,
/// zero-delta wheel, tap-mode Down, missing button, thinning.
/// <see cref="None"/> is used only for non-dropped outcomes.
/// §119 adds the jitter-collapse reasons (<see cref="JitterHeld"/>,
/// <see cref="JitterCollapsed"/>, <see cref="NoPendingHold"/>):
/// jitter handling exists ONLY when <c>Verbatim</c> is true (see
/// <see cref="MouseRecordPlanner"/>), so a held/collapsed drop can
/// never be confused with a thinning drop.</summary>
public enum MouseRecordDropReason
{
    None,
    IgnoreSelf,
    Scope,
    ZeroDeltaWheel,
    TapModeDown,
    MissingButton,
    Thinned,
    /// <summary>§119: a verbatim (Persis) Move held back because it
    /// stayed inside the jitter radius of the last emitted
    /// position. The event is neither emitted nor forgotten: it
    /// becomes the pending hold, resolved by
    /// <see cref="MouseRecordPlanner.FlushPending"/>.</summary>
    JitterHeld,
    /// <summary>§119: a pending held run flushed away because its
    /// last held position ended within the collapse threshold of
    /// the last emitted position (net-zero wiggle).</summary>
    JitterCollapsed,
    /// <summary>§119: <see cref="MouseRecordPlanner.FlushPending"/>
    /// called with no pending held move - no effect.</summary>
    NoPendingHold,
}

/// <summary>One emitted mouse step, fully decided: the MouseAction
/// param contract values plus the Record Delay gap to write onto
/// the PREVIOUS step (null when no gap applies) and the new shared
/// gap-clock value the caller must store. <see cref="Button"/> is
/// "left"/"right"/"middle" for down/up/click steps and null for
/// move/scroll steps (no button key is written for those).
/// <see cref="Params"/> is the exact <c>JsonObject</c> the window
/// used to build inline: kind/x/y always, button for down/up/click,
/// delta for scroll, the fingerprint group (vx/vy/vw/vh/dpi) when a
/// fingerprint was supplied, and mode absolute, or mode relative
/// with rx/ry + window identity when relative mode was selected
/// and a foreground window was available.</summary>
public sealed record PlannedMouseStep(
    string Kind,
    string? Button,
    int X,
    int Y,
    int Delta,
    int? GapToPreviousMs,
    DateTimeOffset NewLastRecordAt,
    JsonObject Params);

/// <summary>Result of one <see cref="MouseRecordPlanner.Plan"/> call.
/// <see cref="Step"/> is non-null exactly when
/// <see cref="Outcome"/> is <see cref="MouseRecordOutcomeKind.Emitted"/>.
/// <see cref="DropReason"/> is
/// <see cref="MouseRecordDropReason.None"/> unless
/// <see cref="Outcome"/> is <see cref="MouseRecordOutcomeKind.Dropped"/>.</summary>
public sealed class MouseRecordDecision
{
    public MouseRecordOutcomeKind Outcome { get; }
    public MouseRecordDropReason DropReason { get; }
    public PlannedMouseStep? Step { get; }

    private MouseRecordDecision(MouseRecordOutcomeKind outcome, MouseRecordDropReason dropReason, PlannedMouseStep? step)
    {
        Outcome = outcome;
        DropReason = dropReason;
        Step = step;
    }

    internal static MouseRecordDecision Dropped(MouseRecordDropReason reason)
        => new(MouseRecordOutcomeKind.Dropped, reason, null);

    internal static MouseRecordDecision Emitted(PlannedMouseStep step)
        => new(MouseRecordOutcomeKind.Emitted, MouseRecordDropReason.None, step);
}

/// <summary>Pure decision logic for mouse recording (§107/§108),
/// extracted verbatim from
/// <c>MacroManagerWindow.ConvertRecordedMouseEvent</c> so the Linux
/// harness can exercise the REAL logic (§109 Order B). No WPF
/// types, no P/Invoke, no hook code: every Windows-only fact
/// arrives as plain data (see <see cref="Plan"/> parameters), and
/// no exception is swallowed here - a throwing foreground provider
/// propagates to the caller's handler, exactly as a throwing
/// <c>TryGetForeground</c> did inline.
///
/// Stateful-planner choice: the planner owns ONLY the thinning
/// state (last EMITTED position/time), which is inherently session
/// state and mirrors the window's former
/// <c>_lastEmittedMouse*</c> fields one-to-one. The shared Record
/// Delay clock (<c>_lastRecordAt</c>, also advanced by keyboard
/// recording) and the live step count stay owned by the window:
/// they arrive per call and the new clock value returns inside
/// <see cref="PlannedMouseStep.NewLastRecordAt"/>, so the keyboard
/// path is untouched and the count can never go stale. The caller
/// must write <c>NewLastRecordAt</c> back to its shared clock on
/// every emit, and write nothing on a drop.
/// <see cref="Reset"/> clears the thinning state; the caller calls
/// it (via a fresh planner) at each recording start.
///
/// §119 jitter collapse (Persis ONLY): when
/// <c>Options.Verbatim</c> is true, a Move that stays inside the
/// jitter radius (<see cref="MouseRecordOptions.MoveThresholdPx"/>,
/// 12px by default, Chebyshev - the same distance and threshold
/// the thinning rule uses) of the last EMITTED position is HELD,
/// not emitted: it becomes the pending hold (see
/// <see cref="FlushPending"/>), moves neither the shared gap clock
/// nor the thinning reference, and returns
/// <see cref="MouseRecordDropReason.JitterHeld"/>. This choice is
/// deliberate: verbatim mode is the flooding mode (every pixel
/// becomes a step), so it is the only mode that needs the guard;
/// non-verbatim mode already thins with the 12px/50ms rule, and
/// its behaviour is UNCHANGED - no hold is ever created there.
/// A held run ends in exactly two ways: a Move that leaves the
/// radius emits itself through the normal emit path (the held
/// run is superseded, never emitted in addition), or a flush
/// (see <see cref="FlushPending"/>) emits one Move to the last
/// held position when it ended more than
/// <see cref="CollapseThresholdPx"/> from the last emitted
/// position, or discards the run as net-zero otherwise. Down/Up,
/// wheel, and keyboard events are NEVER held or discarded by
/// this rule: a non-move <see cref="Plan"/> call never touches
/// the pending hold (it cannot return two steps), so the callers
/// flush explicitly BEFORE calling <see cref="Plan"/> for a
/// non-move event; keyboard recording never calls the planner at
/// all, so it never flushes - a keyboard step may advance the
/// shared clock past the held event's time, and a later flush
/// emit then clamps its gap to 0, exactly like any late event.
/// A fresh planner is built at every recording start, so no stale
/// pending hold can survive across sessions; <see cref="Reset"/>
/// also clears the hold for the same reason.</summary>
public sealed class MouseRecordPlanner
{
    /// <summary>§119 collapse threshold (Chebyshev): a flushed held
    /// run whose last held position is at most this far from the
    /// last emitted position is net-zero wiggle and is discarded
    /// (<see cref="MouseRecordDropReason.JitterCollapsed"/>).
    /// Fixed at 4px by the §119 spec - deliberately NOT
    /// <see cref="MouseRecordOptions.MoveThresholdPx"/>: the hold
    /// radius and the net-zero radius answer different questions.</summary>
    public const int CollapseThresholdPx = 4;
    /// <summary>Settings snapshot this planner decides with. Settable
    /// so the window can push a committed settings-dialog result
    /// into a planner that outlives a session; a fresh planner
    /// built at recording start always carries the current
    /// settings.</summary>
    public MouseRecordOptions Options { get; set; }

    /// <summary>Record Delay gap clamp ceiling
    /// (<c>MacroAction.MaxDelayMs</c> by default). Parameterized so
    /// a simulation can exercise the clamp without feeding real
    /// delays; the window always passes the default.</summary>
    public int MaxDelayMs { get; }

    // Thinning reference: position + time of the last EMITTED mouse
    // step (any kind). A dropped event updates neither this nor the
    // shared gap clock.
    private int _lastEmittedMouseX;
    private int _lastEmittedMouseY;
    private bool _hasLastEmittedMousePosition;
    private DateTimeOffset? _lastEmittedMouseAt;

    // §119 pending hold: the latest Move held back by the jitter
    // rule (verbatim only). At most one held event exists: each
    // held Move overwrites the previous one, because only the last
    // held position matters when the run is flushed. A held event
    // is a plain recorded event, kept verbatim so a flush emit
    // carries its exact coordinates and proc-time timestamp.
    private RecordedMouseEvent? _pendingHeldMove;
    private bool _hasPendingHeldMove;

    public MouseRecordPlanner(
        MouseRecordOptions options,
        int maxDelayMs = MacroAction.MaxDelayMs)
    {
        Options = options;
        MaxDelayMs = maxDelayMs;
    }

    /// <summary>Clears the thinning state: the next move is the
    /// first move of a session and always emits. Also clears a
    /// §119 pending hold, so a reset planner can never flush a
    /// held move from before the reset. Does not touch the shared
    /// gap clock (the caller owns and resets it).</summary>
    public void Reset()
    {
        _hasLastEmittedMousePosition = false;
        _lastEmittedMouseAt = null;
        _pendingHeldMove = null;
        _hasPendingHeldMove = false;
    }

    /// <summary>Resolves a §119 pending held run (§119 flush).
    /// Called by the window (a) before <see cref="Plan"/> for the
    /// FIRST non-move event that arrives while a hold is pending,
    /// so the flushed Move lands before that event's step, and
    /// (b) at Stop, so a last held Move is saved instead of lost.
    /// Parameters mean exactly what the same-named
    /// <see cref="Plan"/> parameters mean (the held event itself
    /// supplies the coordinates/timestamp; no
    /// <c>isOwnProcess</c>/<c>upDownChecked</c> is needed - a held
    /// event already passed the ignore-self gate when it arrived,
    /// and it is always a Move). With no pending hold this returns
    /// <see cref="MouseRecordDropReason.NoPendingHold"/> and has
    /// no effect. With a hold, the hold is consumed FIRST, before
    /// any other outcome - a throw in the emit path can never
    /// resurrect it. A hold ending within
    /// <see cref="CollapseThresholdPx"/> (Chebyshev) of the last
    /// emitted position is discarded as net-zero
    /// (<see cref="MouseRecordDropReason.JitterCollapsed"/>) and
    /// touches neither clock; otherwise the held event emits
    /// through the SAME emit path <see cref="Plan"/> uses (params,
    /// gap from the held event's own timestamp -
    /// the shared clock did not advance during the hold, so the
    /// gap naturally covers the whole held interval - thinning
    /// reference advance), so a flush emit is indistinguishable
    /// from an ordinary emit.</summary>
    public MouseRecordDecision FlushPending(
        DateTimeOffset? lastRecordAt,
        int existingStepCount,
        VirtualScreenSnapshot? fingerprint,
        Func<ForegroundWindowSnapshot?>? getForeground,
        bool recordDelayEnabled)
    {
        if (!_hasPendingHeldMove || _pendingHeldMove is null)
            return MouseRecordDecision.Dropped(MouseRecordDropReason.NoPendingHold);
        RecordedMouseEvent held = _pendingHeldMove;
        _pendingHeldMove = null;
        _hasPendingHeldMove = false;
        if (_hasLastEmittedMousePosition
            && Chebyshev(held.X, held.Y, _lastEmittedMouseX, _lastEmittedMouseY) <= CollapseThresholdPx)
            return MouseRecordDecision.Dropped(MouseRecordDropReason.JitterCollapsed);
        return EmitStep("move", null, held,
            recordDelayEnabled, lastRecordAt, existingStepCount, fingerprint, getForeground);
    }

    private static int Chebyshev(int x1, int y1, int x2, int y2)
        => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    /// <summary>Decides one recorded mouse event. Evaluation order
    /// is the pre-extraction order, lettered as in the old inline
    /// code: (a) ignore-self, (b) scope filter, zero-delta wheel,
    /// kind/button resolution, (c) thinning (Move only),
    /// (c2) §119 jitter hold (verbatim Move only - a held Move
    /// returns <see cref="MouseRecordDropReason.JitterHeld"/> and
    /// is resolved only by <see cref="FlushPending"/>; a non-move
    /// call NEVER touches the pending hold),
    /// (e) params, shared Record Delay gap. Dropped events
    /// return before any state changes.
    ///
    /// <paramref name="isOwnProcess"/> is the raw
    /// <c>MouseWindowHelper.IsOwnProcessPoint</c> result (fail-open:
    /// false on any API failure); the
    /// <c>IgnoreSelfClicks</c> setting is applied here, inside the
    /// decision. <paramref name="upDownChecked"/> is the live
    /// "Rekam Up/Down" checkbox; split mode is
    /// <c>Verbatim || upDownChecked</c>, decided here.
    /// <paramref name="recordDelayEnabled"/> is the live "Rekam
    /// Delay" checkbox. <paramref name="lastRecordAt"/> is the
    /// shared gap clock (null before the first recorded step of
    /// either kind); <paramref name="existingStepCount"/> is the
    /// live step-row count, shared with keyboard steps.
    /// <paramref name="fingerprint"/> is the virtual-screen
    /// snapshot captured at recording start (null when the capture
    /// failed - steps then carry no fingerprint, 1:1 replay).
    /// <paramref name="getForeground"/> is invoked at most once,
    /// and only when relative mode is selected AND the event has
    /// passed every drop gate (the old inline call site); a
    /// null provider or a null return means "no foreground window",
    /// which falls back to absolute mode with no identity fields.
    /// Row creation, the dirty flag, and renumbering stay in the
    /// window: they need the WPF step table and user-facing
    /// messages, and are not decision logic.</summary>
    public MouseRecordDecision Plan(
        RecordedMouseEvent ev,
        bool isOwnProcess,
        bool upDownChecked,
        bool recordDelayEnabled,
        DateTimeOffset? lastRecordAt,
        int existingStepCount,
        VirtualScreenSnapshot? fingerprint,
        Func<ForegroundWindowSnapshot?>? getForeground)
    {
        bool verbatim = Options.Verbatim;

        // (a) Ignore-self: a point on Keystream's own window records
        // nothing and must not consume a Record Delay gap.
        if (Options.IgnoreSelfClicks && isOwnProcess)
            return MouseRecordDecision.Dropped(MouseRecordDropReason.IgnoreSelf);

        // (b) Scope filter: dropped events do not touch the shared
        // gap clock. Skipped entirely when verbatim: effective
        // scope is all (clicks + moves + scroll).
        bool isMove = ev.Kind == RecordedMouseKind.Move;
        bool isWheel = ev.Kind == RecordedMouseKind.Wheel;
        if (!verbatim)
        {
            if (Options.Scope == RecordedMouseScope.ClicksOnly && (isMove || isWheel))
                return MouseRecordDecision.Dropped(MouseRecordDropReason.Scope);
            if (Options.Scope == RecordedMouseScope.ClicksAndMoves && isWheel)
                return MouseRecordDecision.Dropped(MouseRecordDropReason.Scope);
        }

        // A zero-delta wheel event carries no information; dropping
        // it without touching the shared gap clock is correct.
        // Unconditional, including verbatim.
        if (isWheel && ev.Delta == 0)
            return MouseRecordDecision.Dropped(MouseRecordDropReason.ZeroDeltaWheel);

        // Kind/button resolution, from the same checkbox the
        // keyboard path uses (UpDownBox): checked = Down/Up steps;
        // unchecked = Down dropped, Up becomes one click step.
        // Verbatim forces split even in tap mode.
        bool split = verbatim || upDownChecked;
        string kind;
        string? button = null;
        if (isMove)
        {
            kind = "move";
        }
        else if (isWheel)
        {
            kind = "scroll";
        }
        else if (ev.Kind == RecordedMouseKind.Down)
        {
            if (!split)
                return MouseRecordDecision.Dropped(MouseRecordDropReason.TapModeDown);
            if (ev.Button == RecordedMouseButton.None)
                return MouseRecordDecision.Dropped(MouseRecordDropReason.MissingButton);
            kind = "down";
            button = ButtonParam(ev.Button);
        }
        else // Up
        {
            if (ev.Button == RecordedMouseButton.None)
                return MouseRecordDecision.Dropped(MouseRecordDropReason.MissingButton);
            kind = split ? "up" : "click";
            button = ButtonParam(ev.Button);
        }

        // (c) Thinning (Move only): emit only when the cursor moved
        // at least MoveThresholdPx (Chebyshev distance - the largest
        // axis) from the last EMITTED mouse position AND at least
        // MinMoveDelayMs elapsed since the last emitted mouse step.
        // The first move of a session always emits (no previous
        // position). A thinned-out move touches neither clock.
        // Bypassed entirely when verbatim.
        if (!verbatim && isMove && _hasLastEmittedMousePosition)
        {
            int distance = Chebyshev(ev.X, ev.Y, _lastEmittedMouseX, _lastEmittedMouseY);
            bool farEnough = distance >= Options.MoveThresholdPx;
            bool slowEnough = _lastEmittedMouseAt is null
                || (ev.At - _lastEmittedMouseAt.Value).TotalMilliseconds >= Options.MinMoveDelayMs;
            if (!farEnough || !slowEnough)
                return MouseRecordDecision.Dropped(MouseRecordDropReason.Thinned);
        }

        // (c2) §119 jitter hold (verbatim Move only; non-verbatim
        // never reaches this - see the class doc for why the guard
        // lives only in the flooding mode). A Move inside the
        // jitter radius of the last EMITTED position is held: it
        // overwrites any earlier held Move (only the last held
        // position matters), touches neither clock nor the
        // thinning reference, and returns JitterHeld. The anchor
        // is ALWAYS the last emitted position, never the last
        // held one - a slow drift that never leaves the radius is
        // one held run, not a chain. The first move of a session
        // (no emitted position yet) always emits. A Move that
        // leaves the radius clears the held run and falls through
        // to the normal emit path below: the exit Move IS the
        // step, so the held run is never emitted in addition.
        // Non-move events never reach this gate and never touch
        // the hold - the caller flushes explicitly before them.
        if (verbatim && isMove && _hasLastEmittedMousePosition)
        {
            if (Chebyshev(ev.X, ev.Y, _lastEmittedMouseX, _lastEmittedMouseY) < Options.MoveThresholdPx)
            {
                _pendingHeldMove = ev;
                _hasPendingHeldMove = true;
                return MouseRecordDecision.Dropped(MouseRecordDropReason.JitterHeld);
            }
            _pendingHeldMove = null;
            _hasPendingHeldMove = false;
        }

        return EmitStep(kind, button, ev,
            recordDelayEnabled, lastRecordAt, existingStepCount, fingerprint, getForeground);
    }

    /// <summary>Shared emit path for <see cref="Plan"/> and
    /// <see cref="FlushPending"/>: (e) params, then the shared
    /// Record Delay gap, then the thinning reference advance.
    /// Order is the pre-extraction order and is identical for
    /// both callers; the thinning reference advances only on a
    /// completed emit.</summary>
    private MouseRecordDecision EmitStep(
        string kind,
        string? button,
        RecordedMouseEvent ev,
        bool recordDelayEnabled,
        DateTimeOffset? lastRecordAt,
        int existingStepCount,
        VirtualScreenSnapshot? fingerprint,
        Func<ForegroundWindowSnapshot?>? getForeground)
    {
        bool verbatim = Options.Verbatim;

        // (e) Params per the MouseAction contract. x/y are ALWAYS
        // stored (the absolute fallback at playback needs them);
        // fingerprint is stored as a group whenever it was
        // capturable (missing = 1:1 replay). Relative mode stores
        // rx/ry + window identity; any other outcome is absolute,
        // identity fields omitted.
        JsonObject parameters = BuildParams(kind, button, ev, fingerprint, getForeground);

        // Shared Record Delay gap rule, EXACTLY as the keyboard
        // handler's: the gap goes on the PREVIOUS step, clamped
        // 0..MaxDelayMs, only when the checkbox is on, a previous
        // event exists, and a step exists; then the shared clock
        // advances to this event. In the MOUSE decision only,
        // Verbatim also forces the gap (real gaps always recorded);
        // the keyboard handler still follows its checkbox alone.
        int? gap = null;
        if ((recordDelayEnabled || verbatim) && lastRecordAt is not null && existingStepCount > 0)
        {
            gap = (int)Math.Clamp(
                (ev.At - lastRecordAt.Value).TotalMilliseconds, 0, MaxDelayMs);
        }

        // Thinning reference advances on every EMITTED mouse step,
        // of any kind - never on a dropped event (all drops return
        // above, before this point).
        _lastEmittedMouseX = ev.X;
        _lastEmittedMouseY = ev.Y;
        _hasLastEmittedMousePosition = true;
        _lastEmittedMouseAt = ev.At;

        return MouseRecordDecision.Emitted(new PlannedMouseStep(
            kind, button, ev.X, ev.Y, ev.Delta, gap, ev.At, parameters));
    }

    private JsonObject BuildParams(
        string kind,
        string? button,
        RecordedMouseEvent ev,
        VirtualScreenSnapshot? fingerprint,
        Func<ForegroundWindowSnapshot?>? getForeground)
    {
        var parameters = new JsonObject
        {
            ["kind"] = kind,
            ["x"] = ev.X,
            ["y"] = ev.Y,
        };
        if (kind is "down" or "up" or "click")
            parameters["button"] = button;
        if (kind == "scroll")
            parameters["delta"] = ev.Delta;
        if (fingerprint is { } snapshot)
        {
            parameters["vx"] = snapshot.Left;
            parameters["vy"] = snapshot.Top;
            parameters["vw"] = snapshot.Width;
            parameters["vh"] = snapshot.Height;
            parameters["dpi"] = snapshot.Dpi;
        }
        ForegroundWindowSnapshot? foreground = null;
        if (Options.CoordinateMode == RecordedMouseCoordinateMode.RelativeWindow)
            foreground = getForeground?.Invoke();
        if (foreground is not null)
        {
            parameters["mode"] = "relative";
            parameters["rx"] = ev.X - foreground.Left;
            parameters["ry"] = ev.Y - foreground.Top;
            parameters["wtitle"] = foreground.Title;
            parameters["wclass"] = foreground.ClassName;
            parameters["wproc"] = foreground.ProcessName;
        }
        else
        {
            parameters["mode"] = "absolute";
        }
        return parameters;
    }

    private static string ButtonParam(RecordedMouseButton button) => button switch
    {
        RecordedMouseButton.Right => "right",
        RecordedMouseButton.Middle => "middle",
        _ => "left",
    };
}
