using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Keystream.Actions.Hotkey;
using Keystream.Actions.Macro;
using Keystream.App.Host;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Core.Localization;
using Keystream.Windows.Input;

namespace Keystream.App.Views;

/// <summary>§112 - Playtest from the floating panel's READY state.
/// ▶ exists only in READY and only with at least one working step
/// (FloatingRecordPanelLogic.IsPlaytestEnabled). Press ▶ → a
/// 3-second countdown overlay on the panel (numbers only, timer-based
/// - the UI thread is never blocked, so the user can click the target
/// app during the countdown) → the WORKING macro (current step rows +
/// Cycles/Speed from the editor, including unsaved edits and drafts)
/// executes ONCE, exactly as a card would.
///
/// Dispatch path - why not a literal dispatcher Enqueue: a card press
/// travels DeckEngine.Dispatch → ActionDispatcher.Enqueue(new
/// DispatchJob(binding, TriggerKind.Tap)) (DeckEngine.cs) → the
/// dispatcher's worker runs ExecuteStepAsync, which resolves the
/// "macro" step and calls MacroAction.ExecuteAsync with an
/// ActionContext (ActionDispatcher.cs). But MacroAction looks its
/// macro up BY ID IN THE SAVED LIBRARY (Config.Macros), so an
/// Enqueue of the working macro's id would play the stale SAVED
/// copy - or fail outright for an unsaved draft whose id is not in
/// the library yet. Playtest therefore reuses the IDENTICAL entry
/// point the dispatcher calls - MacroAction.ExecuteAsync, with the
/// same inner-action resolution from _host.ActionsById and the same
/// ActionContext shape (FiredKind = Tap) - but constructed with a
/// get-macros delegate that returns a one-element snapshot of the
/// WORKING macro. Cycles, SpeedMultiplier scaling, inner-step
/// validation and the held-key release guard are then MacroAction's
/// own, byte for byte the card path; only the library lookup source
/// differs, and only for this one run. The snapshot is built with
/// the same step/delay/chord validation as TrySaveMacro (S0935 /
/// S0936), without mutating the editor: a corrupt delay or chord
/// fails honestly BEFORE the countdown starts.
///
/// UI-thread rules, explicit: the countdown timer is a DispatcherTimer
/// created on the UI thread (1s ticks, overlay rendering only); the
/// playback itself runs on a thread-pool thread via Task.Run - the
/// same off-UI-thread posture as the dispatcher's worker, so delays
/// never freeze the modal - and the result is marshalled back with
/// Dispatcher.BeginInvoke. A failed playback surfaces through the
/// modal's existing ErrorText path, never silently.
/// Cancelled by × (HideFloatPanel), by ● Record / modal Rekam
/// (record-start path), or by modal close (CloseFloatPanel /
/// Window_Closing) - CancelPlaytest stops the timer and the countdown
/// state machine, so no playback fires after any cancel. A playback
/// already fired runs to completion like a card press; only its
/// post-run UI update is suppressed once the modal is closing.
///
/// §113: ONE countdown runner (StartActionCountdown) serves all four
/// cells of the delay matrix - modal Rekam, modal ▶, panel ● (forced
/// 3s, always), panel ▶ (per checkbox). The countdown shows on the
/// modal overlay AND the panel overlay when the panel is visible.
/// The §112 always-3s rule for ▶ is SUPERSEDED: ▶ follows the shared
/// Delay3s checkbox (countdown iff ON, else instant); only panel ●
/// stays always-3s. ESC panic: a low-level hook (EscPanicHook, never
/// swallowing) runs ONLY during a countdown or a playback; ESC
/// cancels a countdown or cancels the playback token, and the
/// cancellation path returns silently to READY. Playback runs under
/// a CancellationTokenSource threaded into the ActionContext, so a
/// mid-playback ESC stops within one step/delay and MacroAction's
/// held-key guard still releases held keys.</summary>
public partial class MacroManagerWindow
{
    /// <summary>What a countdown will fire. Record and Playtest share
    /// the one 3-2-1 state machine; the kind only decides what
    /// happens at the firing tick (hook/CTS ownership ends for
    /// Record, continues into playback for Playtest).</summary>
    private enum CountdownKind
    {
        Record,
        Playtest,
    }

    // Countdown timer (UI thread) + the WPF-free state machine that
    // decides the tick sequence. _playtestSnapshot is the validated
    // working copy, held only between countdown start and fire.
    // _countdownFireAction is the single pending fire action for
    // EITHER kind (record or playtest) - at most one countdown runs
    // at a time, and CancelPlaytest clears both.
    private DispatcherTimer? _playtestTimer;
    private readonly FloatingRecordPanelLogic.PlaytestCountdown _playtestCountdown = new();
    private MacroConfig? _playtestSnapshot;
    private CountdownKind _countdownKind;
    private Action? _countdownFireAction;

    // §113: ESC panic + playback cancellation. The CTS is created at
    // countdown/instant-fire time and its token is threaded into the
    // ActionContext; the hook is started at the same time and stopped
    // the moment the countdown/playback ends (finally paths, cancel,
    // modal close). _playtestRunning gates re-entrant ▶ presses and
    // the ▶ button state while a playback is in flight.
    private CancellationTokenSource? _playtestCts;
    private bool _playtestRunning;
    // §137: the per-run pause gate for the playback in flight (null
    // when idle). Created in RunPlaytestAsync before _playtestRunning
    // is set and cleared in EndPlaytestSession, so
    // TogglePlaybackPause can never flip a stale gate.
    private PlaybackPauseGate? _playbackPauseGate;
    private EscPanicHook? _escPanicHook;
    private bool _escPanicSubscribed;
    private bool _escPanicActive;

    /// <summary>Set in Window_Closing: once the modal is closing, a
    /// fired playback must not touch the (closing) UI afterwards.</summary>
    private bool _playtestUiSuppressed;

    /// <summary>§113: modal ▶ button (PlayMacroButton in the checkbox
    /// row). Same entry as panel ▶: snapshot validation, then the
    /// unified countdown/instant path. Guards inside
    /// StartPlaytestCountdown reject recording/paused/capturing and
    /// empty macros; this handler only routes + logs.</summary>
    private void PlayMacroButton_Click(object sender, RoutedEventArgs e)
    {
        try { StartPlaytestCountdown(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.PlayMacroButton_Click", ex);
        }
    }

    private void StartPlaytestCountdown()
    {
        try
        {
            if (_recording || _paused || _capturing || _playtestRunning) return;
            // §120 B1b: single-owner enforcement for the modal ▶
            // button (the panel ▶ route is guarded in
            // StartActionCountdown). Silent: the button is disabled
            // by session gating while a standalone session is
            // active.
            if (!_host.FloatPanel.CanModalTakeOwnership()) return;
            if (_working is null || _stepRows.Count == 0) return;
            if (_playtestCountdown.IsRunning) return;
            if (!FloatingRecordPanelLogic.CanStartCountdown(
                    FloatingRecordPanelLogic.PanelState.Ready, _stepRows.Count)) return;
            // Validate BEFORE the countdown: a corrupt delay/chord is
            // an immediate honest error, not a countdown that fires
            // into a failure.
            if (!TryBuildPlaytestSnapshot(out var snapshot, out string? error))
            {
                if (error is not null) ShowError(error);
                return;
            }
            if (snapshot is null) return;
            // §113: ▶ follows the shared Delay3s checkbox - countdown
            // iff ON, instant fire iff OFF. The snapshot is held in
            // _playtestSnapshot only for the countdown path; the
            // instant path passes it straight through.
            if (_host.FloatPanel.Delay3s)
            {
                _playtestSnapshot = snapshot;
                StartActionCountdown(CountdownKind.Playtest, () =>
                {
                    var pending = _playtestSnapshot;
                    _playtestSnapshot = null;
                    if (pending is not null) RunPlaytestAsync(pending);
                });
            }
            else
            {
                BeginPlaytestRunOwnership();
                RunPlaytestAsync(snapshot);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.StartPlaytestCountdown", ex);
        }
    }

    /// <summary>§113: the ONE countdown runner for all four cells.
    /// A countdown runs iff <paramref name="forceCountdown"/> is true
    /// (panel ●, always 3s) or the shared Delay3s checkbox is ON;
    /// otherwise the fire action runs immediately with no countdown,
    /// no hook and no CTS (record-instant needs none; the playtest
    /// instant path sets its own ownership up before calling
    /// RunPlaytestAsync and never reaches the instant branch here).
    /// A countdown runs with the editor still enabled; the modal
    /// overlay and (when visible) the panel overlay show the same
    /// number from one tick handler. The countdown counts as a
    /// modal-owned session from its start until fire/cancel/end.
    /// Re-entrant while a countdown or playback runs: no-op.</summary>
    private void StartActionCountdown(CountdownKind kind, Action fireAction, bool forceCountdown = false)
    {
        if (_playtestCountdown.IsRunning || _playtestRunning || _recording || _paused) return;
        // §120 B1b: single-owner enforcement - never start a modal
        // countdown (record or playtest, forced or checkbox) while
        // a standalone session owns the panel. Covers the panel ●
        // and panel ▶ routes, which arrive here without passing
        // RecordButton_Click / StartPlaytestCountdown.
        if (!_host.FloatPanel.CanModalTakeOwnership()) return;
        if (!forceCountdown && !_host.FloatPanel.Delay3s)
        {
            fireAction();
            return;
        }
        _countdownKind = kind;
        _countdownFireAction = fireAction;
        BeginPlaytestRunOwnership();
        _playtestCountdown.Start();
        ShowCountdownOverlays(FloatingRecordPanelLogic.PlaytestCountdown.StartValue);
        _playtestTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Normal,
            (_, _) => OnPlaytestTimerTick(),
            Dispatcher);
        _playtestTimer.Start();
    }

    private void OnPlaytestTimerTick()
    {
        try
        {
            // Modal closing without the cancel path reaching us:
            // cancel rather than fire into a closing window.
            if (_playtestUiSuppressed)
            {
                CancelPlaytest();
                return;
            }
            if (!_playtestCountdown.IsRunning)
            {
                StopPlaytestTimer();
                return;
            }
            if (_playtestCountdown.Tick(out int display))
            {
                StopPlaytestTimer();
                var fire = _countdownFireAction;
                var kind = _countdownKind;
                _countdownFireAction = null;
                HideCountdownOverlays();
                if (kind == CountdownKind.Record)
                {
                    // Recording starts: ESC does nothing during
                    // recording, so the hook ends here. The session
                    // stays modal-owned (StartRecordingCore re-asserts
                    // it); the CTS was never consumed by recording.
                    StopEscPanic();
                    DisposePlaytestCts();
                }
                // Playtest fire keeps hook + CTS + session alive until
                // RunPlaytestAsync completes (EndPlaytestSession).
                fire?.Invoke();
                return;
            }
            ShowCountdownOverlays(display);
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnPlaytestTimerTick", ex);
            CancelPlaytest();
        }
    }

    /// <summary>Cancels a running countdown (no playback/recording
    /// fires) and hides both overlays. Idempotent; safe to call with
    /// no countdown running and after the modal started closing.
    /// §113: also cancels a RECORD countdown (the unified runner) -
    /// CancelPlaytest is the single cancel point for both kinds.
    /// Ownership (hook/CTS/session) is released ONLY when a countdown
    /// was actually running: a playback already fired keeps running
    /// to completion, and a recording in progress keeps its session.</summary>
    private void CancelPlaytest()
    {
        try
        {
            bool hadCountdown = _playtestCountdown.IsRunning;
            StopPlaytestTimer();
            _playtestCountdown.Cancel();
            _playtestSnapshot = null;
            _countdownFireAction = null;
            HideCountdownOverlays();
            if (hadCountdown)
            {
                CrashLog.WriteDebug("Hitung mundur dibatalkan (modal, CancelPlaytest).");
                StopEscPanic();
                DisposePlaytestCts();
                EndModalSession();
                RefreshSessionGating();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.CancelPlaytest", ex);
        }
    }

    private void StopPlaytestTimer()
    {
        var timer = _playtestTimer;
        _playtestTimer = null;
        timer?.Stop();
    }

    /// <summary>Shows the same countdown number on the modal overlay
    /// and, when the panel exists, on the panel overlay. Either side
    /// failing must not break the other.</summary>
    private void ShowCountdownOverlays(int number)
    {
        try
        {
            CountdownOverlayText.Text = number.ToString(CultureInfo.InvariantCulture);
            CountdownOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.ShowCountdownOverlays", ex);
        }
        try { HostPanel?.ShowCountdown(number); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.ShowCountdownOverlays.Panel", ex);
        }
    }

    /// <summary>Hides both countdown overlays. Idempotent.</summary>
    private void HideCountdownOverlays()
    {
        try { CountdownOverlay.Visibility = Visibility.Collapsed; }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.HideCountdownOverlays", ex);
        }
        try { HostPanel?.HideCountdown(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.HideCountdownOverlays.Panel", ex);
        }
    }

    // --- ESC panic + CTS ownership (§113) -------------------------------

    /// <summary>Creates the playback CTS, marks the modal session
    /// active, and starts the ESC hook. Called at countdown start and
    /// at instant-fire time; the matching release is EndPlaytestSession
    /// (playback), the fire tick (record), or CancelPlaytest
    /// (countdown cancelled).</summary>
    private void BeginPlaytestRunOwnership()
    {
        DisposePlaytestCts();
        _playtestCts = new CancellationTokenSource();
        BeginModalSession();
        StartEscPanic();
    }

    /// <summary>Installs the low-level ESC hook (idempotent). A failed
    /// install proceeds WITHOUT panic and is logged: playback must
    /// never depend on the hook to run. The hook never swallows ESC,
    /// so §110 field-revert and chord capture (both WPF-level) are
    /// unaffected.</summary>
    private void StartEscPanic()
    {
        if (_escPanicActive) return;
        try
        {
            _escPanicHook ??= new EscPanicHook();
            if (!_escPanicSubscribed)
            {
                _escPanicHook.EscapePressed += OnEscPanicPressed;
                _escPanicSubscribed = true;
            }
            if (_escPanicHook.Start())
                _escPanicActive = true;
            else
                CrashLog.WriteWarning("EscPanicHook.Start gagal — playback berjalan tanpa panic ESC (§113).");
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.StartEscPanic", ex);
        }
    }

    /// <summary>Uninstalls the ESC hook. Idempotent; safe at any time,
    /// including from inside the hook's own EscapePressed handler
    /// (CallNextHookEx tolerates a zeroed handle).</summary>
    private void StopEscPanic()
    {
        try { _escPanicHook?.Stop(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.StopEscPanic", ex);
        }
        _escPanicActive = false;
    }

    private void DisposePlaytestCts()
    {
        var cts = _playtestCts;
        _playtestCts = null;
        try { cts?.Dispose(); }
        catch { /* dispose must never throw */ }
    }

    /// <summary>ESC pressed while the hook is active (countdown or
    /// playback only - the hook is never installed at idle, so idle
    /// ESC does nothing new). During a countdown: cancel it, no fire.
    /// During playback: cancel the token; MacroAction stops within
    /// one step/delay and releases held keys. Marshalled to the UI
    /// thread defensively (the hook already fires on it).</summary>
    private void OnEscPanicPressed()
    {
        if (!Dispatcher.CheckAccess())
        {
            try { Dispatcher.BeginInvoke(() => OnEscPanicPressed()); }
            catch { /* handler must never throw */ }
            return;
        }
        try
        {
            if (_playtestCountdown.IsRunning)
            {
                CancelPlaytest();
                return;
            }
            _playtestCts?.Cancel();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnEscPanicPressed", ex);
        }
    }

    /// <summary>Playback/session release: hook stopped, CTS disposed,
    /// modal session ended, ▶ gating refreshed. Runs on the UI thread
    /// from RunPlaytestAsync's completion marshal (and is safe to run
    /// even when the modal is closing - it touches no closing UI
    /// beyond the panel overlay guards).</summary>
    private void EndPlaytestSession()
    {
        CrashLog.WriteDebug("Sesi playback berakhir (modal).");
        _playtestRunning = false;
        // §137: the run is over; its gate must not survive it.
        _playbackPauseGate = null;
        StopEscPanic();
        DisposePlaytestCts();
        EndModalSession();
        RefreshSessionGating();
    }

    /// <summary>§137 - the session kind for the master-shortcut
    /// toggles, read from the existing fields only. A running
    /// countdown reports its kind; the countdown, recording, and
    /// playback states are mutually exclusive by the StartActionCountdown
    /// / RunPlaytestAsync guards, so the priority order below can
    /// never mislabel a live state.</summary>
    public PanelSessionKind CurrentSessionKind
    {
        get
        {
            if (_playtestCountdown.IsRunning)
                return _countdownKind == CountdownKind.Playtest
                    ? PanelSessionKind.PlaytestCountdown
                    : PanelSessionKind.RecordCountdown;
            if (_recording) return PanelSessionKind.Recording;
            if (_playtestRunning) return PanelSessionKind.Playback;
            return PanelSessionKind.None;
        }
    }

    /// <summary>§137 - stops a running playback through the EXACT
    /// CTS-cancel path the ESC panic handler uses (<see
    /// cref="OnEscPanicPressed"/>): a running countdown is cancelled
    /// (no fire afterwards), otherwise the playback token is
    /// cancelled. Silent, like ESC. A paused playback stops
    /// immediately: the gate's waits observe the same token, so a
    /// pause can never shield a run from its stop. UI-thread only,
    /// like every other handler entry point.</summary>
    public void StopPlayback()
    {
        try
        {
            if (_playtestCountdown.IsRunning)
            {
                CancelPlaytest();
                return;
            }
            if (_playtestRunning)
                CrashLog.WriteDebug("Playback dihentikan (modal, StopPlayback).");
            _playtestCts?.Cancel();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.StopPlayback", ex);
        }
    }

    /// <summary>§137 - toggles the per-run pause gate. False when no
    /// playback is executing (idle, recording, or countdown - the
    /// caller shows S1729 then); true when the gate actually flipped.
    /// UI-thread only; the gate itself is thread-safe.</summary>
    public bool TogglePlaybackPause()
    {
        try
        {
            if (!_playtestRunning) return false;
            var gate = _playbackPauseGate;
            if (gate is null) return false;
            bool nowPaused = gate.Toggle();
            CrashLog.WriteDebug(nowPaused ? "Playback dijeda (modal)." : "Playback dilanjutkan (modal).");
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.TogglePlaybackPause", ex);
            return false;
        }
    }

    /// <summary>Builds the working-macro snapshot for one playtest run,
    /// mirroring TrySaveMacro's step loop (hotkey rows rebuilt from
    /// Chord + DelayText; other rows cloned from Original with the
    /// edited delay) WITHOUT mutating _working, the rows, or the
    /// library. Cycles/Speed come from the editor controls, as saved
    /// would. Returns false with the same error TrySaveMacro would
    /// show (S0935 bad delay, S0936 bad chord) when the working
    /// macro cannot play honestly.</summary>
    private bool TryBuildPlaytestSnapshot(out MacroConfig? snapshot, out string? error)
    {
        snapshot = null;
        error = null;
        var steps = new List<ActionStep>();
        for (int i = 0; i < _stepRows.Count; i++)
        {
            var row = _stepRows[i];
            if (!int.TryParse(row.DelayText, out int delay) || delay < 0 || delay > MacroAction.MaxDelayMs)
            {
                error = Localizer.Format("S0935", i + 1, MacroAction.MaxDelayMs);
                return false;
            }
            if (row.IsHotkey)
            {
                if (!KeyChord.TryParse(row.Chord, out _, out _))
                {
                    error = Localizer.Format("S0936", i + 1, row.Chord);
                    return false;
                }
                steps.Add(new ActionStep
                {
                    ActionId = "hotkey",
                    Params = new JsonObject { ["keys"] = row.Chord },
                    DelayAfterMs = delay,
                });
            }
            else
            {
                steps.Add(new ActionStep
                {
                    ActionId = row.Original.ActionId,
                    Params = (JsonObject)row.Original.Params.DeepClone(),
                    DelayAfterMs = delay,
                });
            }
        }
        int cycles = CyclesBox.SelectedItem is int c ? c : 1;
        string name = NameBox.Text.Trim();
        if (name.Length == 0) name = _working?.Name ?? "";
        snapshot = new MacroConfig
        {
            Id = _working!.Id,
            Name = name,
            Cycles = cycles,
            SpeedMultiplier = ParseSpeed(SpeedBox.SelectedItem as string),
            Steps = steps,
        };
        return true;
    }

    /// <summary>Fires one playback of the snapshot through
    /// MacroAction.ExecuteAsync on a thread-pool thread (see the
    /// class comment for why the snapshot delegate, not a literal
    /// Enqueue, is the faithful card path for an unsaved working
    /// macro). The §113 CTS token replaces CancellationToken.None, so
    /// ESC stops the run: MacroAction rethrows
    /// OperationCanceledException, which is caught SEPARATELY here
    /// and returns silently to READY (error state untouched, panel
    /// refreshed) - never an error message. The result returns to
    /// the UI thread: failure shows via ErrorText, success clears a
    /// stale error, and the panel returns to READY via
    /// RefreshFloatPanel. Ownership always ends in the completion
    /// marshal, even on the suppressed (closing) path.</summary>
    private async void RunPlaytestAsync(MacroConfig snapshot)
    {
        // §137: the per-run gate, created BEFORE _playtestRunning is
        // set so a pause hotkey arriving at any point of the run
        // finds it. A pause pressed during the countdown does NOT
        // reach this gate: TogglePlaybackPause returns false while
        // the countdown runs (no playback yet), so the caller shows
        // S1729 - the gate only ever freezes a run already fired.
        _playbackPauseGate = new PlaybackPauseGate();
        _playtestRunning = true;
        CrashLog.WriteDebug($"Playback dimulai (modal): macro '{snapshot.Name}'.");
        RefreshSessionGating();
        ActionResult? result = null;
        string? thrown = null;
        bool cancelled = false;
        CancellationToken token;
        try { token = _playtestCts?.Token ?? CancellationToken.None; }
        catch { token = CancellationToken.None; }
        try
        {
            var macroAction = new MacroAction(
                () => new[] { snapshot },
                id => _host.ActionsById.TryGetValue(id, out var action) ? action : null,
                pauseGate: _playbackPauseGate);
            var parameters = new JsonObject { ["macroId"] = snapshot.Id };
            result = await Task.Run(() => macroAction.ExecuteAsync(parameters,
                new ActionContext { Cancellation = token, FiredKind = TriggerKind.Tap }))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ESC panic (or any token cancellation): silent return to
            // READY. MacroAction's finally already released held keys.
            cancelled = true;
        }
        catch (Exception ex)
        {
            // MacroAction returns Fail for expected failures; a throw
            // here is unexpected - still honest, never swallowed.
            thrown = ex.Message;
        }
        try
        {
            // Fire-and-forget marshal by design: the playback task
            // has already completed; only the UI update is queued.
            _ = Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    EndPlaytestSession();
                    if (_playtestUiSuppressed) return;
                    if (cancelled)
                    {
                        RefreshFloatPanel();
                        return;
                    }
                    if (thrown is not null) ShowError(thrown);
                    else if (result is not null && !result.Success)
                        ShowError(result.Error ?? Localizer.Get("S0939"));
                    else HideError();
                    RefreshFloatPanel();
                }
                catch (Exception ex)
                {
                    CrashLog.Write("MacroManagerWindow.RunPlaytestAsync.UI", ex);
                }
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.RunPlaytestAsync", ex);
        }
    }
}
