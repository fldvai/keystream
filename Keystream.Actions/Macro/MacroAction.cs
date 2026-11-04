using System.Diagnostics;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Actions.Common;
using Keystream.Actions.Hotkey;
using Keystream.Core.Actions;
using Keystream.Core.Config;

namespace Keystream.Actions.Macro;

/// <summary>§137 - the ONE shared pause signal for a macro playback
/// run. WPF-free (only System types), so the wb-harness can link it
/// through MacroAction.cs with no new project wiring. One instance
/// per run: the handler that started the run (modal playtest or
/// standalone panel playback) owns it and the master-shortcut
/// pause/resume toggle flips it; MacroAction only ever awaits it
/// BETWEEN steps and inside step delays, never mid-step, so no held
/// key/mouse can be stranded by a pause. Cancellation ALWAYS wins:
/// every wait observes the run's <see
/// cref="CancellationToken"/>, so a stop (CTS cancel) during a pause
/// returns immediately and MacroAction's finally still releases held
/// keys/buttons. Pause/Resume/Toggle are idempotent and safe to call
/// from any thread; rapid toggles cannot corrupt step order because
/// the gate carries no position - MacroAction's loop index is the
/// only position, and it only advances after a step + delay complete.
/// A fresh instance starts unpaused; a stale instance from a finished
/// run is never reused (each run creates its own).</summary>
public sealed class PlaybackPauseGate
{
    private readonly object _gate = new();
    private bool _paused;
    private TaskCompletionSource<object?>? _resumeSource;

    /// <summary>True while a pause is in effect.</summary>
    public bool IsPaused
    {
        get { lock (_gate) return _paused; }
    }

    /// <summary>Enters the paused state. No-op when already paused
    /// (the pending resume waiter is kept, never replaced).</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_paused) return;
            _paused = true;
            _resumeSource = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Leaves the paused state, releasing every waiter. No-op
    /// when not paused.</summary>
    public void Resume()
    {
        TaskCompletionSource<object?>? source;
        lock (_gate)
        {
            if (!_paused) return;
            _paused = false;
            source = _resumeSource;
            _resumeSource = null;
        }
        source?.TrySetResult(null);
    }

    /// <summary>Flips the state atomically and returns the NEW state
    /// (true = now paused). One lock, one decision: rapid toggles from
    /// the single UI-thread hotkey path can never interleave a
    /// pause/resume pair into a lost resume.</summary>
    public bool Toggle()
    {
        TaskCompletionSource<object?>? source;
        bool paused;
        lock (_gate)
        {
            if (_paused)
            {
                _paused = false;
                source = _resumeSource;
                _resumeSource = null;
                paused = false;
            }
            else
            {
                _paused = true;
                _resumeSource = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                source = null;
                paused = true;
            }
        }
        source?.TrySetResult(null);
        return paused;
    }

    /// <summary>Returns immediately when not paused; otherwise waits
    /// until <see cref="Resume"/> (or <see cref="Toggle"/> to
    /// resumed) or until <paramref name="cancellationToken"/> cancels
    /// - whichever comes first. Re-checks the flag after every wake
    /// so a pause pressed again before the waiter resumes is honoured.</summary>
    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? resumeTask;
            lock (_gate)
            {
                if (!_paused) return;
                resumeTask = _resumeSource?.Task;
            }
            if (resumeTask is null) continue; // Pause/Resume raced; re-read the flag.
            await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A pausable delay: <paramref name="delayMs"/> of
    /// UNPAUSED time, in ≤20 ms slices. Time spent paused does not
    /// consume the delay - resume continues from the exact remaining
    /// position, it never restarts or skips the delay. Cancellation
    /// throws <see cref="OperationCanceledException"/> immediately,
    /// including while paused (the wait above observes the token).
    /// Zero/negative delays return immediately, like Task.Delay
    /// callers already assume.</summary>
    public async Task DelayAsync(int delayMs, CancellationToken cancellationToken)
    {
        if (delayMs <= 0) return;
        long remaining = delayMs;
        while (remaining > 0)
        {
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            int slice = (int)Math.Min(remaining, 20);
            var sw = Stopwatch.StartNew();
            await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            remaining -= sw.ElapsedMilliseconds;
        }
    }
}

/// <summary>Runs a saved macro (§62): looks the macro up by Id in the
/// global library, then executes its steps in order, Cycles times,
/// through the SAME IAction instances the dispatcher uses for tiles -
/// so a macro step behaves exactly like the same action on a card,
/// including its honest failure reporting. No second engine: steps are
/// hotkey taps (HotkeyAction) and, since §65, keydown/keyup holds
/// (KeyDownAction/KeyUpAction) produced by down/up recording.
/// ExecuteAsync tracks keys pressed down and releases any still held
/// when the macro ends, fails, or is cancelled - a macro that ends
/// mid-hold must never leave Ctrl/Shift stuck. Since the Batch 4
/// fix, the same guard covers MOUSE buttons: a mouse "down" step
/// with no matching "up" (unbalanced recording, a failed up step
/// that sent nothing, cancellation mid-hold) is released in the
/// same finally, per button, so a button can never stay physically
/// held after playback ends.
/// Since §69, every step delay is scaled by the macro's
/// SpeedMultiplier (2× = delays halved, 0.5× = delays doubled; a
/// delay of 0 stays 0). Holds scale too: the delay after a ↓ step
/// IS the hold duration, so a faster macro holds keys shorter.
/// The library, the action lookup, and the key-release sender arrive as
/// Func delegates, the same pattern as BrowserGroupAction - the action
/// never reaches for statics or singletons. Nested macros are refused
/// in Validate: a macro step whose ActionId is "macro" can never
/// execute, so a macro can neither call itself nor loop through
/// another macro. Since §137, an optional per-run
/// <see cref="PlaybackPauseGate"/> (constructor) freezes execution
/// between steps and inside delays; the finally below still releases
/// held keys/buttons on a stop-during-pause, because cancellation
/// always wins over the pause.</summary>
public sealed class MacroAction : IAction
{
    public const int MaxDelayMs = 10000;
    public const int MaxCycles = 10;

    private readonly Func<IReadOnlyList<MacroConfig>>? _getMacros;
    private readonly Func<string, IAction?>? _resolveAction;
    // Key-release sender, injectable so the Linux harness can observe
    // the auto-release guard without touching real SendInput (user32.dll
    // does not exist there). Production passes nothing and gets
    // SendInputNative.SendKeys.
    private readonly Func<IReadOnlyList<SendInputNative.KeyStroke>, (uint Sent, int Errno)> _sendKeys;
    // Mouse-release sender, same pattern as _sendKeys (Batch 4):
    // the mouse guard's release strokes go through this delegate so
    // the harness can observe them without real SendInput.
    // Production passes nothing and gets SendInputNative.SendMouse.
    // NOTE: this delegate is ONLY the guard's release path. The
    // mouse steps themselves still send through MouseAction's own
    // SendInputNative.SendMouse call - this guard never re-sends
    // or replaces a step, it only releases what a step held.
    private readonly Func<IReadOnlyList<SendInputNative.MouseStroke>, (uint Sent, int Errno)> _sendMouse;
    // §137 pause gate: null = unpausable (the dispatcher's deck-press
    // runs and editor tests, which never pass one). A playtest run
    // passes its own per-run gate so the master-shortcut pause/resume
    // toggle can freeze it between steps and inside delays.
    private readonly PlaybackPauseGate? _pauseGate;

    public MacroAction() : this(null, null) { }

    public MacroAction(
        Func<IReadOnlyList<MacroConfig>>? getMacros,
        Func<string, IAction?>? resolveAction,
        Func<IReadOnlyList<SendInputNative.KeyStroke>, (uint Sent, int Errno)>? sendKeys = null,
        Func<IReadOnlyList<SendInputNative.MouseStroke>, (uint Sent, int Errno)>? sendMouse = null,
        PlaybackPauseGate? pauseGate = null)
    {
        _getMacros = getMacros;
        _resolveAction = resolveAction;
        _sendKeys = sendKeys ?? SendInputNative.SendKeys;
        _sendMouse = sendMouse ?? SendInputNative.SendMouse;
        _pauseGate = pauseGate;
    }

    public ActionDescriptor Descriptor => new("macro", Localizer.Get("S0093"), Localizer.Get("S1071"),
        Localizer.Get("S1086"));

    /// <summary>§69: a speed is usable when it is a finite number above
    /// zero. Anything else (0, negative, NaN, Infinity - only possible
    /// from a hand-edited config, the editor only offers 0.5–3×)
    /// is rejected by Validate; ScaleDelay additionally treats it as
    /// 1.0 so a direct caller can never divide by zero.</summary>
    public static bool IsValidSpeed(double speed)
        => !double.IsNaN(speed) && !double.IsInfinity(speed) && speed > 0;

    /// <summary>§69: one step's effective delay at a playback speed.
    /// Pure arithmetic (harness-tested, no clock): 0 stays 0, 2×
    /// halves, 0.5× doubles, halves round away from zero (101 ms at
    /// 2× = 51 ms). An invalid speed behaves as 1.0. Note the
    /// effective delay may exceed MaxDelayMs (10000 ms at 0.5× =
    /// 20000 ms) - that IS half speed, not a bound violation: the
    /// bound applies to stored delays, and Validate checks those.</summary>
    public static int ScaleDelay(int delayMs, double speed)
    {
        if (delayMs <= 0) return 0;
        if (!IsValidSpeed(speed)) speed = 1.0;
        return (int)Math.Round(delayMs / speed, MidpointRounding.AwayFromZero);
    }

    /// <summary>Batch 4: reads a mouse step's hold shape. True only
    /// for down/up/click steps with a known button (left/right/
    /// middle), both normalized to lowercase. Anything else (move,
    /// scroll, an unknown kind/button) is not a hold and returns
    /// false - Validate has already rejected such steps before
    /// execution, so this is a defensive second read, never a
    /// substitute for validation.</summary>
    private static bool TryParseMouseHold(JsonObject parameters, out string kind, out string button)
    {
        kind = ParamReader.GetString(parameters, "kind")?.Trim().ToLowerInvariant() ?? "";
        button = ParamReader.GetString(parameters, "button")?.Trim().ToLowerInvariant() ?? "";
        return kind is "down" or "up" or "click"
            && button is "left" or "right" or "middle";
    }

    private static uint MouseUpFlag(string button) => button switch
    {
        "right" => SendInputNative.MOUSEEVENTF_RIGHTUP,
        "middle" => SendInputNative.MOUSEEVENTF_MIDDLEUP,
        _ => SendInputNative.MOUSEEVENTF_LEFTUP,
    };

    public ValidationResult Validate(JsonObject parameters)
    {
        string? macroId = ParamReader.GetString(parameters, "macroId");
        if (string.IsNullOrWhiteSpace(macroId))
            return ValidationResult.Invalid(Localizer.Get("S1165"));
        if (_getMacros is null)
            return ValidationResult.Invalid(Localizer.Get("S1166"));
        var macro = _getMacros().FirstOrDefault(m => m.Id == macroId);
        if (macro is null)
            return ValidationResult.Invalid(Localizer.Get("S1167"));
        if (macro.Steps.Count == 0)
            return ValidationResult.Invalid(Localizer.Format("S1168", macro.Name));
        if (macro.Cycles < 1 || macro.Cycles > MaxCycles)
            return ValidationResult.Invalid(Localizer.Format("S1170", macro.Name, MaxCycles));
        if (!IsValidSpeed(macro.SpeedMultiplier))
            return ValidationResult.Invalid(Localizer.Format("S1171", macro.Name));
        for (int i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            if (string.Equals(step.ActionId, "macro", StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Invalid(
                    Localizer.Format("S1172", macro.Name, i + 1));
            if (step.DelayAfterMs < 0 || step.DelayAfterMs > MaxDelayMs)
                return ValidationResult.Invalid(
                    Localizer.Format("S1173", macro.Name, i + 1, MaxDelayMs));
            if (_resolveAction is not null)
            {
                var inner = _resolveAction(step.ActionId);
                if (inner is null)
                    return ValidationResult.Invalid(
                        Localizer.Format("S1174", macro.Name, i + 1, step.ActionId));
                var vr = inner.Validate(step.Params);
                if (!vr.IsValid)
                    return ValidationResult.Invalid(
                        Localizer.Format("S1175", macro.Name, i + 1, string.Join("; ", vr.Errors)));
            }
        }
        return ValidationResult.Valid;
    }

    public async Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        // Re-validated here, not assumed from the caller: the dispatcher
        // validates first, but editor Test and future callers must not
        // depend on that.
        var vr = Validate(parameters);
        if (!vr.IsValid)
            return ActionResult.Fail(string.Join("; ", vr.Errors));
        // FirstOrDefault, not First: the library can change between
        // Validate and Execute (user deletes a macro mid-run on the UI
        // thread) - that must be an honest failure, never a throw.
        var macro = _getMacros!().FirstOrDefault(m => m.Id == ParamReader.GetString(parameters, "macroId"));
        if (macro is null)
            return ActionResult.Fail(Localizer.Get("S1167"));

        var sw = Stopwatch.StartNew();
        int executed = 0;
        // §69: validated above, so this is always a valid speed here
        // (ScaleDelay would still guard a direct caller).
        double speed = macro.SpeedMultiplier;
        // §65 guard: keys this macro pressed down and never released
        // (up step deleted, recording stopped early, mid-macro failure,
        // or dispatcher cancellation). A balanced macro (every ↓ has
        // its ↑) leaves this empty, so the guard is a no-op for it.
        var held = new HashSet<ushort>();
        // Batch 4 guard, same shape: mouse buttons this macro
        // pressed down (a "down" step) and never released (no
        // matching "up"). Per button name, normalized. A balanced
        // mouse recording leaves this empty too.
        var heldMouse = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            for (int cycle = 0; cycle < macro.Cycles; cycle++)
            {
                for (int i = 0; i < macro.Steps.Count; i++)
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    // §137: pause applies ONLY between steps - a step
                    // never starts while paused, and a step already
                    // running is never interrupted. Cancellation wins
                    // over the pause (the wait observes the token).
                    if (_pauseGate is not null)
                        await _pauseGate.WaitWhilePausedAsync(context.Cancellation).ConfigureAwait(false);
                    var step = macro.Steps[i];
                    var inner = _resolveAction?.Invoke(step.ActionId);
                    if (inner is null)
                        return ActionResult.Fail(
                            Localizer.Format("S1174", macro.Name, i + 1, step.ActionId),
                            sw.ElapsedMilliseconds);
                    ActionResult result;
                    try
                    {
                        result = await inner.ExecuteAsync(step.Params, context).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // A throwing inner action must not kill the worker
                        // (§45): convert to the macro's honest failure.
                        return ActionResult.Fail(
                            Localizer.Format("S1176", macro.Name, i + 1, ex.GetType().Name, ex.Message),
                            sw.ElapsedMilliseconds);
                    }
                    executed++;
                    // §122 recheck (Batch 4): a FAILED mouse down/click
                    // must still be tracked as held. The step's batch
                    // may have landed the down without its up (partial
                    // SendMouse), or failed before sending at all -
                    // either way the finally below sends an up-only
                    // stroke, which Windows ignores for a button that
                    // is not actually held, so tracking it is safe and
                    // leaving it untracked could leave a button held.
                    // A failed UP leaves the set untouched: the button
                    // is still held in reality, so it stays tracked.
                    if (string.Equals(step.ActionId, "mouse", StringComparison.OrdinalIgnoreCase)
                        && TryParseMouseHold(step.Params, out string failKind, out string failButton)
                        && (failKind == "down" || failKind == "click"))
                        heldMouse.Add(failButton);
                    if (!result.Success)
                        return ActionResult.Fail(
                            Localizer.Format("S1177", macro.Name, i + 1, result.Error),
                            sw.ElapsedMilliseconds);
                    // Track holds only for steps that SUCCEEDED: a failed
                    // keydown sent nothing, so it must not be "released".
                    if ((string.Equals(step.ActionId, "keydown", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(step.ActionId, "keyup", StringComparison.OrdinalIgnoreCase))
                        && KeyChord.TryParseSingleKey(ParamReader.GetString(step.Params, "key"), out ushort vk, out _))
                    {
                        if (string.Equals(step.ActionId, "keydown", StringComparison.OrdinalIgnoreCase))
                            held.Add(vk);
                        else
                            held.Remove(vk);
                    }
                    // Mouse holds, same "only SUCCEEDED" rule: a
                    // failed down sent nothing, a failed up released
                    // nothing. A "down" holds its button until a
                    // matching "up"; a "click" sends down+up in one
                    // batch, so it ends released even when the
                    // button was held by an earlier down. Move and
                    // scroll never hold. Only the "mouse" action
                    // sends button input (MouseAction is the sole
                    // SendMouse caller), so no other ActionId can
                    // add to this set.
                    if (string.Equals(step.ActionId, "mouse", StringComparison.OrdinalIgnoreCase)
                        && TryParseMouseHold(step.Params, out string mouseKind, out string mouseButton))
                    {
                        if (mouseKind == "down")
                            heldMouse.Add(mouseButton);
                        else
                            heldMouse.Remove(mouseButton);
                    }
                    // Delay AFTER every step, including the last of each
                    // cycle - same rule as the dispatcher's own step delay,
                    // scaled by the macro's speed (§69). §137: with a
                    // pause gate the delay is pausable - paused time
                    // does not consume it, so resume continues from the
                    // exact remaining delay; without a gate the delay
                    // is the original Task.Delay, byte for byte.
                    int scaled = ScaleDelay(step.DelayAfterMs, speed);
                    if (_pauseGate is not null)
                        await _pauseGate.DelayAsync(scaled, context.Cancellation).ConfigureAwait(false);
                    else if (scaled > 0)
                        await Task.Delay(scaled, context.Cancellation).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // Runs on success, honest failure, and cancellation alike:
            // release anything still held, or Ctrl/Shift stays down and
            // every later key behaves as Ctrl+/Shift+. An up stroke for
            // a key that is not held is a harmless no-op to Windows.
            // §122 recheck (Batch 4): the two guards are isolated from
            // each other - a throwing key sender must never skip the
            // mouse releases (or vice versa). A release sender that
            // throws has nothing better to report through: the macro's
            // own outcome is already decided.
            try
            {
                foreach (ushort vk in held)
                    _sendKeys(new[] { new SendInputNative.KeyStroke(vk, true, KeyChord.IsExtendedKey(vk)) });
            }
            catch { /* release failure must never break the unwind */ }
            // Same guard for mouse buttons (Batch 4): a button
            // pressed down with no matching up is released here,
            // or it stays physically held and every later click
            // drags or selects. The release stroke carries ONLY
            // the up flag - no move, no absolute coordinates - so
            // the cursor does not move; Windows ignores dx/dy on
            // a button-only input. One stroke per held button; a
            // button this macro never pressed down is never
            // touched. The result is ignored, like the key
            // releases above: the macro's own outcome is already
            // decided, and a failed release has nothing better
            // to report through.
            try
            {
                foreach (string button in heldMouse)
                    _sendMouse(new[] { new SendInputNative.MouseStroke(0, 0, MouseUpFlag(button), 0) });
            }
            catch { /* release failure must never break the unwind */ }
        }
        return ActionResult.Ok(sw.ElapsedMilliseconds,
            Localizer.Format("S1178", macro.Name, executed, macro.Cycles));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
