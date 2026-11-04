using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Keystream.Actions.Macro;
using Keystream.Actions.Mouse;
using Keystream.App.Host;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Core.Localization;
using Keystream.Windows.Input;

namespace Keystream.App.Views;

/// <summary>§113 - the floating panel's STANDALONE handler: the
/// panel as an extension/remote of the macro modal while the modal
/// is closed. The panel owns NOTHING: no settings, no macro picker.
/// Selection (which macro), the Delay3s checkbox, the record
/// checkboxes and the mouse settings all come from the shared
/// <see cref="FloatPanelSession"/> the modal writes; this controller
/// only reads them and obeys. The <see cref="FloatPanelHost"/>
/// (Worker B's contract) owns the panel window itself and routes
/// its button events here; this controller drives the visuals back
/// through the panel and reports outcomes only through the footer
/// delegate - never a MessageBox, never a dialog.
///
/// Recording mirrors the modal's record path exactly (same hooks,
/// same <see cref="RecordStepBuilder"/>, same planner, same
/// gap-on-previous-step rules), except the steps land in a
/// working copy of the SAVED macro and are auto-saved on Stop -
/// there is no Save button outside the modal. Playback mirrors the
/// modal playtest's dispatch entry point
/// (<c>MacroAction.ExecuteAsync</c> with the snapshot delegate),
/// except the snapshot is the saved macro, never a working draft.
/// Session ownership (<see cref="FloatPanelSession.SessionActive"/>
/// / <see cref="FloatPanelSession.SessionOwner"/>) is set at every
/// countdown/recording/playback start and cleared at every end or
/// cancel, so the modal's Record/Play buttons stay disabled while
/// this controller is busy, and vice versa.
/// Threading: every entry point runs on the UI thread (panel button
/// → host → here, or a DispatcherTimer tick). Hook events are
/// marshalled through the panel's Dispatcher with BeginInvoke
/// (FIFO order, proc-time stamps for gaps), exactly like the modal.
/// The ESC panic hook is active ONLY during a countdown or a
/// playback - never while recording. That is NOT because ESC is
/// a recordable key while recording: ESC is never recorded as a
/// step in any mode (RecordStepBuilder skips 0x1B - it has no
/// token in the table and Build returns null for it), so during
/// recording ESC simply reaches the focused application, exactly
/// as it does whenever the hook is not installed. The hook itself
/// never swallows ESC (it always calls CallNextHookEx), so "ESC
/// is ordinary" is true only at the hook level: the key is
/// reported to the focused app AND, while the hook is installed,
/// to the panic handler.</summary>
public sealed class StandalonePanelController : IPanelActionHandler
{
    private enum CountdownKind
    {
        None,
        Record,
        Play,
    }

    private readonly AppHost _host;
    private readonly FloatPanelSession _session;
    private readonly Action<string, string, double> _notifyFooter;

    private FloatPanelHost? _hostPanel;
    private Dispatcher? _dispatcher;

    // Countdown: the generic PlaytestCountdown machine, reused for
    // record countdowns unchanged (§113 rule helpers in
    // FloatingRecordPanelLogic). The timer is a DispatcherTimer on
    // the UI thread; the overlay lives on the panel.
    private readonly FloatingRecordPanelLogic.PlaytestCountdown _countdown = new();
    private DispatcherTimer? _countdownTimer;
    private CountdownKind _pendingKind = CountdownKind.None;

    // Recording state (mirrors the modal's fields one-to-one).
    private KeyboardRecorder? _recorder;
    // §137 E1: snapshot of the master-hotkey chords for this
    // recording session (null outside a session) - mirrors the
    // modal's field one-to-one. Consulted by HandleRecordedKey
    // before RecordStepBuilder.Build; dropped at Stop with the
    // session (see MasterChordExclusion).
    private MasterChordExclusion? _chordExclusion;
    private bool _recorderSubscribed;
    private MouseRecorder? _mouseRecorder;
    private bool _mouseRecorderSubscribed;
    private MouseRecordPlanner? _planner;
    private VirtualScreenSnapshot? _fingerprint;
    private bool _recording;
    private bool _paused;
    private DateTimeOffset? _lastRecordAt;
    private List<ActionStep>? _workingSteps;
    private int _recordBaseCount;
    private string? _recordingMacroId;
    private string _recordingMacroName = "";

    // Playback state.
    private bool _playing;
    private CancellationTokenSource? _playbackCts;

    // §137: the per-run pause gate for the playback in flight (null
    // when idle). Created in FirePlaytest before _playing is set and
    // cleared in its finally, so TogglePlaybackPause can never flip
    // a stale gate and the hotkey pause/resume toggle always reaches
    // the run that is actually executing.
    private PlaybackPauseGate? _playbackPauseGate;

    /// <summary>§137 - the session kind for the master-shortcut
    /// toggles, read from the existing flags only. A running
    /// countdown reports its pending kind; _pendingKind is set in
    /// StartCountdown before the machine starts, so a running
    /// countdown is never None here.</summary>
    public PanelSessionKind CurrentSessionKind
    {
        get
        {
            if (_countdown.IsRunning)
                return _pendingKind == CountdownKind.Play
                    ? PanelSessionKind.PlaytestCountdown
                    : PanelSessionKind.RecordCountdown;
            if (_recording) return PanelSessionKind.Recording;
            if (_playing) return PanelSessionKind.Playback;
            return PanelSessionKind.None;
        }
    }

    // ESC panic hook (countdown + playback only), created lazily
    // and subscribed once, like the recorders.
    private EscPanicHook? _escHook;
    private bool _escHookSubscribed;

    // Deleted-macro notice: one footer notice per transition into
    // the deleted state, keyed by the macro id that was deleted.
    // Reset whenever the selection becomes valid or empty again.
    private string? _deletedNotifiedId;

    // Footer dot colours, matching the main window's Notify usage:
    // green = saved (S1553 pattern), blue = information, red =
    // failure. Duration 3.5s, the same as the S1553 footer notice.
    private const string SuccessDot = "#10B981";
    private const string InfoDot = "#38BDF8";
    private const string ErrorDot = "#E5484D";
    private const double FooterSeconds = 3.5;

    /// <summary>Stores references only - touches NO Config and NO
    /// Dispatcher at construction. The host is attached later via
    /// <see cref="AttachHost"/>.</summary>
    public StandalonePanelController(
        AppHost host,
        FloatPanelSession session,
        Action<string, string, double> notifyFooter)
    {
        _host = host;
        _session = session;
        _notifyFooter = notifyFooter;
    }

    /// <summary>True while a countdown, a recording (paused
    /// included), or a playback owned by this controller is in
    /// flight.</summary>
    public bool IsBusy => _recording || _playing || _countdown.IsRunning;

    private FloatingRecordPanel? Panel => _hostPanel?.Panel;

    private Dispatcher? UiDispatcher => _dispatcher ?? Panel?.Dispatcher;

    /// <summary>Called by the host after construction, once the host
    /// (and its panel) exists. Refreshes the panel immediately so
    /// a panel shown while the modal is closed renders the shared
    /// selection at once.</summary>
    public void AttachHost(FloatPanelHost host)
    {
        _hostPanel = host;
        _dispatcher = host.Panel?.Dispatcher ?? _dispatcher;
        RefreshPanelState();
    }

    /// <summary>Cancels a running countdown, stops both record hooks
    /// and cancels a running playback. Staged steps of a recording
    /// IN FLIGHT are discarded, never auto-saved - on the exit path
    /// the app is going away, and a half-recorded macro must not be
    /// written silently on the way out. §122 recheck (F-C3): a
    /// RETAINED failed-save tail (recording already stopped, save
    /// failed, working list kept for the next attempt) is NOT a
    /// recording in flight and must survive this call - the
    /// takeover path reaches CancelAll while a playback is running
    /// after a failed save, and discarding there would silently
    /// lose the staged steps F-C3 exists to keep.</summary>
    public void CancelAll()
    {
        try
        {
            bool wasRecording = _recording;
            StopCountdownTimer();
            _countdown.Cancel();
            _pendingKind = CountdownKind.None;
            try { Panel?.HideCountdown(); }
            catch { /* teardown must never throw */ }
            _playbackCts?.Cancel();
            _recorder?.Stop();
            _mouseRecorder?.Stop();
            _recording = false;
            _paused = false;
            _playing = false;
            if (wasRecording)
            {
                _workingSteps = null;
                _recordingMacroId = null;
                _recordingMacroName = "";
            }
            _planner = null;
            StopEscHook();
            ClearSession();
            RefreshPanelState();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.CancelAll", ex);
        }
    }

    // --- panel state push (mirrors MacroManagerWindow.RefreshFloatPanel) ---

    /// <summary>Pushes name / count / state / clock pairing to the
    /// panel, mirroring the modal's pairing exactly: READY =
    /// StopClock + SetReady + SetPlaytestEnabled + name + count
    /// (the countdown overlay is never touched here - a live
    /// countdown owns it); recording = HideCountdown +
    /// PauseClock/ResumeClock + SetPaused + name + count. Deleted
    /// (selected id with no library entry) renders the S1631 name
    /// with both buttons disabled and ONE footer notice per
    /// transition into the state; no selection renders an empty
    /// name, disabled buttons, silently. Never throws.</summary>
    public void RefreshPanelState()
    {
        try
        {
            var panel = Panel;
            if (panel is null) return;
            if (_recording && _workingSteps is not null)
            {
                panel.SetInteractionEnabled(true);
                panel.HideCountdown();
                if (_paused) panel.PauseClock();
                else panel.ResumeClock();
                panel.SetPaused(_paused);
                panel.SetMacroName(_recordingMacroName);
                panel.SetStepCount(_workingSteps.Count);
                return;
            }
            string? id = _session.SelectedMacroId;
            if (id is null)
            {
                _deletedNotifiedId = null;
                panel.SetInteractionEnabled(false);
                panel.StopClock();
                panel.SetReady();
                panel.SetPlaytestEnabled(false);
                panel.SetMacroName(string.Empty);
                panel.SetStepCount(0);
                return;
            }
            var macro = ResolveMacro(id);
            if (macro is null)
            {
                panel.SetInteractionEnabled(false);
                panel.StopClock();
                panel.SetReady();
                panel.SetPlaytestEnabled(false);
                panel.SetMacroName(Localizer.Get("S1631"));
                panel.SetStepCount(0);
                if (_deletedNotifiedId != id)
                {
                    _deletedNotifiedId = id;
                    NotifyFooter(Localizer.Get("S1631"), InfoDot);
                }
                return;
            }
            _deletedNotifiedId = null;
            panel.SetInteractionEnabled(true);
            panel.StopClock();
            panel.SetReady();
            panel.SetPlaytestEnabled(FloatingRecordPanelLogic.IsPlaytestEnabled(
                FloatingRecordPanelLogic.PanelState.Ready, macro.Steps.Count));
            panel.SetMacroName(macro.Name);
            panel.SetStepCount(macro.Steps.Count);
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.RefreshPanelState", ex);
        }
    }

    // --- IPanelActionHandler: record --------------------------------------

    /// <summary>Panel ● in READY: always a 3-second countdown
    /// first (the shared checkbox never waives it), then recording
    /// into the selected saved macro. Ignored while the modal owns
    /// the session, while busy, or when nothing is selected / the
    /// selected macro is gone (the deleted state renders instead).
    /// The macro is resolved again when the countdown fires, so a
    /// deletion during the countdown is honoured.</summary>
    public void OnPanelRecordRequested()
    {
        try
        {
            if (_session.SessionActive && _session.SessionOwner == FloatPanelSessionOwner.Modal) return;
            // §122 recheck (F-B2): recording while a STANDALONE
            // playback is running is refused with a VISIBLE footer
            // error (S1633, the same message as the modal path) -
            // never the silent IsBusy no-op below. The panel shows
            // READY buttons during playback, so this press is
            // reachable; a countdown/record in flight stays silent
            // (the overlay covers the buttons then).
            if (_playing)
            {
                NotifyFooter(Localizer.Get("S1633"), ErrorDot);
                return;
            }
            if (IsBusy) return;
            if (Panel is null) return;
            string? id = _session.SelectedMacroId;
            var macro = id is null ? null : ResolveMacro(id);
            if (macro is null)
            {
                RefreshPanelState();
                return;
            }
            if (FloatingRecordPanelLogic.ShouldCountdownForPanelRecord())
                StartCountdown(CountdownKind.Record);
            else
                StartStandaloneRecording();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.OnPanelRecordRequested", ex);
        }
    }

    /// <summary>Panel ▶ in READY: countdown iff the shared Delay3s
    /// checkbox is ON, else instant. Zero steps is guarded (the
    /// panel already disables ▶ then). Ignored while the modal owns
    /// the session or while busy.</summary>
    public void OnPanelPlaytestRequested()
    {
        try
        {
            if (_session.SessionActive && _session.SessionOwner == FloatPanelSessionOwner.Modal) return;
            if (IsBusy) return;
            if (Panel is null) return;
            string? id = _session.SelectedMacroId;
            var macro = id is null ? null : ResolveMacro(id);
            if (macro is null || macro.Steps.Count == 0)
            {
                RefreshPanelState();
                return;
            }
            if (FloatingRecordPanelLogic.ShouldCountdownForPlay(_session.Delay3s))
                StartCountdown(CountdownKind.Play);
            else
                FirePlaytest();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.OnPanelPlaytestRequested", ex);
        }
    }

    /// <summary>Panel ❚❚/▶ while recording/paused: pause/resume,
    /// mirroring the modal exactly. No-op when not recording.</summary>
    public void OnPanelPauseToggleRequested()
    {
        try
        {
            if (!_recording) return;
            if (_paused) ResumeRecording();
            else PauseRecording();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.OnPanelPauseToggleRequested", ex);
        }
    }

    /// <summary>Panel ■ while recording/paused: stop the hooks and
    /// auto-save the staged steps (silent when nothing new was
    /// recorded, never silent on a save failure). No-op when not
    /// recording.</summary>
    public void OnPanelStopRequested()
    {
        try
        {
            StopRecordingInternal(notifySaved: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.OnPanelStopRequested", ex);
        }
    }

    /// <summary>The panel was hidden (×) or closed: cancel any
    /// running countdown owned by this controller - never start
    /// playback after this call. A recording in flight keeps
    /// running (hiding is not stopping); a playback already fired
    /// runs to completion like a card press.</summary>
    public void OnPanelHidden()
    {
        try
        {
            CancelCountdown();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.OnPanelHidden", ex);
        }
    }

    // --- countdown ---------------------------------------------------------

    private void StartCountdown(CountdownKind kind)
    {
        var panel = Panel;
        var dispatcher = UiDispatcher;
        if (panel is null || dispatcher is null) return;
        StopCountdownTimer();
        _pendingKind = kind;
        _countdown.Start();
        SetSession(active: true, FloatPanelSessionOwner.Standalone);
        panel.ShowCountdown(FloatingRecordPanelLogic.PlaytestCountdown.StartValue);
        StartEscHook();
        _countdownTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Normal,
            (_, _) => OnCountdownTick(),
            dispatcher);
        _countdownTimer.Start();
    }

    private void OnCountdownTick()
    {
        try
        {
            var panel = Panel;
            if (panel is null)
            {
                CancelCountdown();
                return;
            }
            if (_countdown.Tick(out int display))
            {
                StopCountdownTimer();
                panel.HideCountdown();
                FirePendingCountdown();
                return;
            }
            panel.ShowCountdown(display);
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.OnCountdownTick", ex);
            CancelCountdown();
        }
    }

    private void FirePendingCountdown()
    {
        var kind = _pendingKind;
        _pendingKind = CountdownKind.None;
        // The session stays active across the fire: recording and
        // playback each re-assert it at their own start, and clear
        // it at their end.
        if (kind == CountdownKind.Record)
            StartStandaloneRecording();
        else if (kind == CountdownKind.Play)
            FirePlaytest();
    }

    /// <summary>Cancels a running countdown: no playback/recording
    /// fires afterwards. No-op when no countdown is running (so a
    /// hide during playback cannot clear the playback session).</summary>
    private void CancelCountdown()
    {
        if (!_countdown.IsRunning && _pendingKind == CountdownKind.None) return;
        StopCountdownTimer();
        _countdown.Cancel();
        _pendingKind = CountdownKind.None;
        try { Panel?.HideCountdown(); }
        catch { /* teardown must never throw */ }
        if (!_recording && !_playing)
        {
            StopEscHook();
            ClearSession();
        }
        RefreshPanelState();
    }

    private void StopCountdownTimer()
    {
        var timer = _countdownTimer;
        _countdownTimer = null;
        timer?.Stop();
    }

    // --- recording ----------------------------------------------------------

    private void StartStandaloneRecording()
    {
        // F-B3: the countdown's ESC panic hook MUST be stopped
        // before recording starts - this is the single choke point
        // for every path into recording (countdown fire via
        // FirePendingCountdown, and the direct call from
        // OnPanelRecordRequested), so no per-caller stop is needed
        // and none may be forgotten. StopEscHook is idempotent:
        // the direct path stops an already-stopped hook harmlessly.
        StopEscHook();
        string? id = _session.SelectedMacroId;
        var macro = id is null ? null : ResolveMacro(id);
        if (macro is null)
        {
            ClearSession();
            RefreshPanelState();
            return;
        }
        // F-C3 resume: a previous Stop whose save FAILED retained
        // the working list (see StopRecordingInternal). Resume
        // from its staged tail, rebased onto the CURRENT saved
        // macro - the saved entry may have been edited elsewhere
        // since the failed save, and the staged tail is what must
        // survive, not the stale base copy. A fresh start has an
        // empty tail, so this is byte-identical to the old code
        // for it. A retained list for a DIFFERENT macro id is not
        // resumed: starting here overwrites it (the failed-save
        // footer already reported the loss of that attempt).
        bool resumed = !_recording && _workingSteps is not null && _recordingMacroId == macro.Id;
        var retainedTail = resumed
            ? _workingSteps!.Skip(_recordBaseCount).ToList()
            : new List<ActionStep>();
        // Working copy of the SAVED macro: gaps from the first
        // staged step land on the last saved step, exactly as in
        // the modal's working table. The library entry itself is
        // untouched until Stop.
        _workingSteps = macro.Copy().Steps;
        _workingSteps.AddRange(retainedTail);
        _recordBaseCount = macro.Steps.Count;
        _recordingMacroId = macro.Id;
        _recordingMacroName = macro.Name;
        _planner = new MouseRecordPlanner(MouseRecordMapping.MapMouseRecordOptions(_session.MouseSettings));
        _lastRecordAt = null;
        _fingerprint = MouseCoordinates.TryCaptureCurrent(out var fingerprint) ? fingerprint : null;
        // §137 E1: snapshot the master chords ONCE, before the hook
        // starts - the chord set is fixed for the session the
        // filter tracks (see the modal's StartRecordingCore).
        _chordExclusion = MasterChordExclusion.FromSettings(_host.Config.Settings);
        _recorder ??= new KeyboardRecorder();
        if (!_recorderSubscribed)
        {
            _recorder.KeyEvent += OnRecorderKeyEvent;
            _recorderSubscribed = true;
        }
        if (!_recorder.Start())
        {
            // F-C3: a resumed (retained) working list survives a
            // hook failure too - only the fresh planner is dropped,
            // so the staged tail is still there for the next
            // attempt. A fresh start has nothing to retain.
            if (resumed) _planner = null;
            else DiscardRecordingState();
            NotifyFooter(Localizer.Get("S0939"), ErrorDot);
            ClearSession();
            RefreshPanelState();
            return;
        }
        if (_session.MouseEnabled)
        {
            _mouseRecorder ??= new MouseRecorder();
            if (!_mouseRecorderSubscribed)
            {
                _mouseRecorder.MouseEvent += OnRecorderMouseEvent;
                _mouseRecorderSubscribed = true;
            }
            if (!_mouseRecorder.Start())
            {
                _recorder.Stop();
                // F-C3, same as the keyboard-hook failure above:
                // a resumed working list survives; only the fresh
                // planner is dropped.
                if (resumed) _planner = null;
                else DiscardRecordingState();
                NotifyFooter(Localizer.Get("S1603"), ErrorDot);
                ClearSession();
                RefreshPanelState();
                return;
            }
        }
        _recording = true;
        _paused = false;
        CrashLog.WriteDebug($"Rekaman dimulai (panel mandiri): macro '{_recordingMacroName}'.");
        SetSession(active: true, FloatPanelSessionOwner.Standalone);
        RefreshPanelState();
    }

    private void PauseRecording()
    {
        if (!_recording || _paused) return;
        _recorder?.Stop();
        _mouseRecorder?.Stop();
        _paused = true;
        RefreshPanelState();
    }

    private void ResumeRecording()
    {
        if (!_recording || !_paused) return;
        if (_recorder?.Start() != true)
        {
            StopRecordingInternal(notifySaved: false);
            NotifyFooter(Localizer.Get("S0939"), ErrorDot);
            return;
        }
        if (_session.MouseEnabled)
        {
            if (_mouseRecorder?.Start() != true)
            {
                _recorder?.Stop();
                StopRecordingInternal(notifySaved: false);
                NotifyFooter(Localizer.Get("S1603"), ErrorDot);
                return;
            }
        }
        // The planner is NOT recreated (thinning state survives the
        // pause), and the gap clock resets so the pause itself never
        // becomes a giant DelayAfterMs on the next step. The §137 E1
        // chord filter is likewise REUSED (held/suppressed state
        // survives the pause); recreate only if it is somehow
        // missing, so a resumed session is never left unfiltered.
        _chordExclusion ??= MasterChordExclusion.FromSettings(_host.Config.Settings);
        _lastRecordAt = null;
        _paused = false;
        RefreshPanelState();
    }

    /// <summary>Stops the hooks and, when new steps were staged,
    /// appends them to a copy of the saved macro and saves with the
    /// modal's rollback pattern (backup list, restore on failure).
    /// Zero new steps → silent. A failed save → rollback + S0937
    /// footer notice, never silent, and the working steps are
    /// RETAINED for the next recording session's Stop (F-C3) -
    /// they are cleared only after a successful save, or when the
    /// macro was deleted (F-C2: staged steps discarded, S1631
    /// notice, never resurrected). The session is cleared on every
    /// path.</summary>
    private void StopRecordingInternal(bool notifySaved)
    {
        bool wasRecording = _recording;
        if (wasRecording)
            CrashLog.WriteDebug("Rekaman berhenti (panel mandiri).");
        _paused = false;
        _recording = false;
        _recorder?.Stop();
        _mouseRecorder?.Stop();
        // §137 E1: the session filter goes with the session. Any
        // modifier down still buffered in it is discarded, not
        // flushed (see the modal's StopRecording): its key-up can
        // never be recorded now that the hook stopped.
        _chordExclusion = null;
        // §119 flush-on-Stop: a pending held Move must be saved, so
        // flush BEFORE the working list is captured/nulled below and
        // before the save logic reads it - the flushed step appends
        // to the same list the save copies from. Only while a
        // recording was actually running and a planner + working
        // list exist. A non-emitted flush is discarded silently
        // (the recording is ending), and a flush failure must never
        // break stop/save: the hold is already consumed inside
        // FlushPending, so the recording simply ends without that
        // one Move, and the rollback/save logic below runs as
        // before. No second StopRecordingInternal call can
        // re-flush: _planner is nulled on every exit path below
        // (success, deleted, nothing-new, and failed-save alike),
        // and a second call has wasRecording == false anyway.
        if (wasRecording && _workingSteps is not null && _planner is not null)
        {
            try
            {
                var flush = _planner.FlushPending(
                    lastRecordAt: _lastRecordAt,
                    existingStepCount: _workingSteps.Count,
                    fingerprint: _fingerprint,
                    getForeground: () => MouseWindowHelper.TryGetForeground(out var foreground)
                        ? new ForegroundWindowSnapshot(foreground.Title, foreground.ClassName, foreground.ProcessName,
                            foreground.Left, foreground.Top, foreground.Width, foreground.Height)
                        : null,
                    recordDelayEnabled: _session.RecordDelay);
                if (flush.Outcome == MouseRecordOutcomeKind.Emitted && flush.Step is not null)
                {
                    var planned = flush.Step;
                    if (planned.GapToPreviousMs is { } gapMs && _workingSteps.Count > 0)
                        _workingSteps[^1].DelayAfterMs = gapMs;
                    _lastRecordAt = planned.NewLastRecordAt;
                    _workingSteps.Add(new ActionStep
                    {
                        ActionId = "mouse",
                        Params = planned.Params,
                        DelayAfterMs = 0,
                    });
                }
            }
            catch { /* §119: flush must never break Stop/save - see above. */ }
        }
        var working = _workingSteps;
        int baseCount = _recordBaseCount;
        string? macroId = _recordingMacroId;
        // F-C3: the working fields are NOT nulled here. They are
        // cleared only once the save below SUCCEEDS (or when there
        // is nothing to save / the macro is gone) - a failed save
        // must leave them intact for the next attempt. The session
        // is still cleared on every path: session ownership ends
        // when recording stops, retained steps or not.
        ClearSession();
        if (!wasRecording)
        {
            // Not a recording stop (stray Stop call): nothing to
            // save, and a retained failed-save list - if one
            // exists - is left untouched for its own retry.
            RefreshPanelState();
            return;
        }
        if (working is null || macroId is null)
        {
            DiscardRecordingState();
            RefreshPanelState();
            return;
        }
        if (working.Count <= baseCount)
        {
            DiscardRecordingState();
            RefreshPanelState();
            return;
        }
        // F-C2: re-resolve at Stop time. Deleted during the
        // session → BuildCopy returns null → discard the staged
        // steps with ONE S1631 footer notice and never resurrect
        // the macro. _deletedNotifiedId is set so the refresh
        // below does not notify a second time for the same
        // deletion (RefreshPanelState's own once-per-transition
        // rule); the S1631 name still renders in the panel.
        var copy = StandaloneSaveApplier.BuildCopy(ResolveMacro(macroId), working);
        if (copy is null)
        {
            DiscardRecordingState();
            _deletedNotifiedId = macroId;
            NotifyFooter(Localizer.Get("S1631"), InfoDot);
            RefreshPanelState();
            return;
        }
        if (!StandaloneSaveApplier.TrySave(_host.Config.Macros, copy, _host.Save, out string? saveError))
        {
            // F-C3: save failed → the library was already rolled
            // back inside TrySave. RETAIN the working steps, base
            // count, and macro id/name: the next
            // StartStandaloneRecording for the same macro resumes
            // from the staged tail and its Stop retries this save.
            // Only the planner is dropped (its §119 flush state
            // was consumed above). Footer S0937, as before.
            _planner = null;
            RefreshPanelState();
            NotifyFooter(Localizer.Get("S0937") + " " + saveError, ErrorDot);
            return;
        }
        // Save succeeded: only NOW may the working state be cleared.
        _workingSteps = null;
        _planner = null;
        _recordingMacroId = null;
        _recordingMacroName = "";
        if (notifySaved)
            NotifyFooter(Localizer.Format("S1553", copy.Name), SuccessDot);
        RefreshPanelState();
    }

    private void DiscardRecordingState()
    {
        _workingSteps = null;
        _planner = null;
        _chordExclusion = null;
        _recordingMacroId = null;
        _recordingMacroName = "";
        _recording = false;
        _paused = false;
    }

    private void OnRecorderKeyEvent(KeyboardRecorder.KeyRecord record)
    {
        // Same marshal as the modal: explicit dispatcher hop keeps
        // FIFO order, and gaps come from proc-time stamps, not
        // marshal time.
        var dispatcher = UiDispatcher;
        if (dispatcher is null) return;
        _ = dispatcher.BeginInvoke(() =>
        {
            try
            {
                HandleRecordedKey(record);
            }
            catch (Exception ex)
            {
                StopRecordingInternal(notifySaved: false);
                NotifyFooter(ex.Message, ErrorDot);
                CrashLog.Write("StandalonePanelController.HandleRecordedKey", ex);
            }
        });
    }

    private void HandleRecordedKey(KeyboardRecorder.KeyRecord record)
    {
        if (!_recording || _paused || _workingSteps is null) return;
        // §137 E1: the filter runs here, inside the dispatched
        // callback, before conversion - same point as the modal's
        // OnRecordedKeyEvent, in FIFO hook order. It returns zero
        // or more events (a buffered modifier down released ahead
        // of the event that resolves its press); each is converted
        // exactly as before. Events queued after Stop never reach
        // this point: the guard above drops them.
        foreach (var ev in FeedRecordedKey(record))
        {
            var (step, gap, newLastRecordAt) = RecordStepBuilder.Build(
                ev,
                _session.RecordUpDown,
                _session.RecordDelay,
                _lastRecordAt,
                _workingSteps.Count);
            if (step is null) continue; // skipped event: clock untouched
            if (gap is { } gapMs && _workingSteps.Count > 0)
                _workingSteps[^1].DelayAfterMs = gapMs;
            _lastRecordAt = newLastRecordAt;
            _workingSteps.Add(step);
            RefreshPanelState();
        }
    }

    /// <summary>§137 E1: master-chord exclusion for one hook event.
    /// No session filter (outside a recording) means pass-through,
    /// preserving the old behaviour exactly.</summary>
    private IReadOnlyList<KeyboardRecorder.KeyRecord> FeedRecordedKey(KeyboardRecorder.KeyRecord record)
        => _chordExclusion?.Feed(record) ?? new[] { record };

    private void OnRecorderMouseEvent(MouseRecorder.MouseRecord record)
    {
        var dispatcher = UiDispatcher;
        if (dispatcher is null) return;
        _ = dispatcher.BeginInvoke(() =>
        {
            try
            {
                HandleRecordedMouse(record);
            }
            catch (Exception ex)
            {
                // A conversion failure stops BOTH recorders with a
                // message - never drops an event silently, never
                // throws into the hook chain (modal's S1604 path).
                StopRecordingInternal(notifySaved: false);
                NotifyFooter(Localizer.Format("S1604", ex.GetType().Name, ex.Message), ErrorDot);
                CrashLog.Write("StandalonePanelController.HandleRecordedMouse", ex);
            }
        });
    }

    private void HandleRecordedMouse(MouseRecorder.MouseRecord record)
    {
        var panel = Panel;
        // The panel's own clicks/drags never become steps.
        if (panel is not null && panel.IsVisible
            && (panel.IsPointWithin(record.X, record.Y) || panel.IsDragging))
            return;
        if (!_recording || _paused || _workingSteps is null) return;
        var planner = _planner;
        if (planner is null) return;
        // §119: one foreground provider for both the flush and the
        // Plan call below (identical provider, identical semantics).
        // The jitter flush runs BEFORE Plan for a non-move event:
        // Plan can return only one decision, so a pending held Move
        // must land first (move, then Down/Up/wheel). A Move event
        // never flushes here - it extends or exits the held run
        // inside Plan(). Only events that pass the panel-own guard
        // above reach this point, so panel clicks never flush. The
        // flush decision is applied through the same local Apply
        // path as the Plan decision (gap, shared clock, working
        // list).
        ForegroundWindowSnapshot? GetForeground() => MouseWindowHelper.TryGetForeground(out var foreground)
            ? new ForegroundWindowSnapshot(foreground.Title, foreground.ClassName, foreground.ProcessName,
                foreground.Left, foreground.Top, foreground.Width, foreground.Height)
            : null;
        bool ApplyDecision(MouseRecordDecision decision)
        {
            if (decision.Outcome != MouseRecordOutcomeKind.Emitted || decision.Step is null) return true;
            var planned = decision.Step;
            if (planned.GapToPreviousMs is { } gapMs && _workingSteps.Count > 0)
                _workingSteps[^1].DelayAfterMs = gapMs;
            _lastRecordAt = planned.NewLastRecordAt;
            _workingSteps.Add(new ActionStep
            {
                ActionId = "mouse",
                Params = planned.Params,
                DelayAfterMs = 0,
            });
            RefreshPanelState();
            return true;
        }
        if (record.Kind != MouseRecorder.MouseEventKind.Move)
        {
            if (!ApplyDecision(planner.FlushPending(
                lastRecordAt: _lastRecordAt,
                existingStepCount: _workingSteps.Count,
                fingerprint: _fingerprint,
                getForeground: GetForeground,
                recordDelayEnabled: _session.RecordDelay)))
                return;
        }
        var decision = planner.Plan(
            MouseRecordMapping.MapRecordedMouseEvent(record),
            isOwnProcess: _session.MouseSettings.IgnoreSelfClicks
                && MouseWindowHelper.IsOwnProcessPoint(record.X, record.Y),
            upDownChecked: _session.RecordUpDown,
            recordDelayEnabled: _session.RecordDelay,
            lastRecordAt: _lastRecordAt,
            existingStepCount: _workingSteps.Count,
            fingerprint: _fingerprint,
            getForeground: GetForeground);
        if (!ApplyDecision(decision)) return;
    }

    // --- playback -----------------------------------------------------------

    /// <summary>§137 - stops a running playback through the EXACT
    /// CTS-cancel path the ESC panic handler uses (<see
    /// cref="HandleEscape"/>): a running countdown is cancelled
    /// (no fire afterwards), otherwise the playback token is
    /// cancelled. Silent, like ESC. A paused playback stops
    /// immediately: the gate's waits observe the same token, so a
    /// pause can never shield a run from its stop. No-op when idle
    /// (the countdown check and the null-conditional cancel both
    /// guard it).</summary>
    public void StopPlayback()
    {
        try
        {
            if (_countdown.IsRunning || _playing)
                CrashLog.WriteDebug("Playback dihentikan (panel mandiri, StopPlayback).");
            if (_countdown.IsRunning)
            {
                CancelCountdown();
                return;
            }
            _playbackCts?.Cancel();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.StopPlayback", ex);
        }
    }

    /// <summary>§137 - toggles the per-run pause gate. False when no
    /// playback is executing (idle, recording, or countdown - the
    /// caller shows S1729 then); true when the gate actually flipped.
    /// The gate field is only ever non-null while _playing is true
    /// (created before the flag is set, cleared in FirePlaytest's
    /// finally), so the null check is defence, not a second state.</summary>
    public bool TogglePlaybackPause()
    {
        try
        {
            if (!_playing) return false;
            var gate = _playbackPauseGate;
            if (gate is null) return false;
            bool nowPaused = gate.Toggle();
            CrashLog.WriteDebug(nowPaused ? "Playback dijeda (panel mandiri)." : "Playback dilanjutkan (panel mandiri).");
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.TogglePlaybackPause", ex);
            return false;
        }
    }

    /// <summary>One playback of the SAVED macro through the same
    /// entry point a card press uses
    /// (<c>MacroAction.ExecuteAsync</c> with the snapshot delegate),
    /// on a thread-pool thread with a real cancellation token: ESC
    /// cancels instantly and silently (no error notice, panel back
    /// to READY). A failed playback shows its error text (S0939
    /// fallback) in the footer; success is silent.</summary>
    private async void FirePlaytest()
    {
        string? id = _session.SelectedMacroId;
        var macro = id is null ? null : ResolveMacro(id);
        if (macro is null || macro.Steps.Count == 0)
        {
            ClearSession();
            RefreshPanelState();
            return;
        }
        var snapshot = macro.Copy();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;
        // §137: the per-run gate, created BEFORE _playing is set so a
        // pause hotkey arriving at any point of the run finds it.
        _playbackPauseGate = new PlaybackPauseGate();
        _playing = true;
        CrashLog.WriteDebug($"Playback dimulai (panel mandiri): macro '{snapshot.Name}'.");
        SetSession(active: true, FloatPanelSessionOwner.Standalone);
        StartEscHook();
        RefreshPanelState();
        ActionResult? result = null;
        string? thrown = null;
        bool cancelled = false;
        bool stillOwned = false;
        try
        {
            var macroAction = new MacroAction(
                () => new[] { snapshot },
                actionId => _host.ActionsById.TryGetValue(actionId, out var action) ? action : null,
                pauseGate: _playbackPauseGate);
            var parameters = new JsonObject { ["macroId"] = snapshot.Id };
            result = await Task.Run(() => macroAction.ExecuteAsync(parameters,
                new ActionContext { Cancellation = token, FiredKind = TriggerKind.Tap }));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            thrown = ex.Message;
        }
        finally
        {
            _playing = false;
            // §137: the run is over (completed, failed, or cancelled
            // - including a stop-during-pause, whose cancellation the
            // gate already honoured); the gate must not survive it.
            CrashLog.WriteDebug("Sesi playback berakhir (panel mandiri).");
            _playbackPauseGate = null;
            StopEscHook();
            // §122 recheck (Batch 1): this async unwind may finish
            // AFTER a modal takeover ended this playback (CancelAll
            // cancelled the token and cleared the session). Only
            // clear the session / refresh the panel while this
            // controller still owns it - a stale completion must
            // never wipe a modal-owned session or repaint the panel
            // over the modal's recording visuals. The CTS is this
            // run's own and is always disposed.
            stillOwned = _session.SessionActive
                && _session.SessionOwner == FloatPanelSessionOwner.Standalone;
            if (stillOwned)
                ClearSession();
            _playbackCts?.Dispose();
            _playbackCts = null;
        }
        if (!stillOwned)
            return;
        try
        {
            if (!cancelled)
            {
                if (thrown is not null) NotifyFooter(thrown, ErrorDot);
                else if (result is not null && !result.Success)
                    NotifyFooter(result.Error ?? Localizer.Get("S0939"), ErrorDot);
            }
            RefreshPanelState();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.FirePlaytest.UI", ex);
        }
    }

    // --- ESC panic hook ------------------------------------------------------

    private void StartEscHook()
    {
        try
        {
            _escHook ??= new EscPanicHook();
            if (!_escHookSubscribed)
            {
                _escHook.EscapePressed += OnEscapePressed;
                _escHookSubscribed = true;
            }
            // A refused install means "proceed without panic" (the
            // hook's own contract): playback/countdown never depend
            // on it - but the refusal IS logged, like the modal's
            // StartEscPanic, so a silently missing panic hook is
            // visible in crash.log instead of invisible (F-B3:
            // the old code ignored Start()'s return value here).
            if (!_escHook.Start())
                CrashLog.WriteWarning("EscPanicHook.Start gagal — panel mandiri berjalan tanpa panic ESC (§113).");
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.StartEscHook", ex);
        }
    }

    private void StopEscHook()
    {
        try { _escHook?.Stop(); }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.StopEscHook", ex);
        }
    }

    private void OnEscapePressed()
    {
        // The hook proc runs on the installing (UI) thread, but
        // marshal defensively: countdown/playback state is UI
        // state, and the handler must be idempotent.
        var dispatcher = UiDispatcher;
        if (dispatcher is null)
        {
            HandleEscape();
            return;
        }
        _ = dispatcher.BeginInvoke(HandleEscape);
    }

    private void HandleEscape()
    {
        try
        {
            // ESC cancels a running countdown (no fire afterwards)
            // and stops a running playback instantly. The hook is
            // not installed while recording, so ESC is not a panic
            // key then - and it is never recorded as a step either
            // (RecordStepBuilder skips 0x1B); it simply reaches the
            // focused application.
            if (_countdown.IsRunning)
            {
                CancelCountdown();
                return;
            }
            if (_playing)
                _playbackCts?.Cancel();
        }
        catch (Exception ex)
        {
            CrashLog.Write("StandalonePanelController.HandleEscape", ex);
        }
    }

    // --- helpers --------------------------------------------------------------

    private MacroConfig? ResolveMacro(string id)
        => _host.Config.Macros.FirstOrDefault(m => m.Id == id);

    private void SetSession(bool active, FloatPanelSessionOwner owner)
    {
        if (_session.SessionActive == active && _session.SessionOwner == owner) return;
        _session.SessionActive = active;
        _session.SessionOwner = owner;
        _session.NotifyChanged();
    }

    /// <summary>§122 recheck (Pekerja 3 / Batch 1): owner-guarded
    /// clear, the mirror of the modal's <c>EndModalSession</c>. A
    /// late standalone completion (the <c>FirePlaytest</c> finally
    /// after a modal takeover cancelled the token, a queued ESC
    /// handler) must NEVER clear a session another surface owns:
    /// wiping a modal-owned session would reopen the deck gate
    /// mid-playback and re-enable the modal's Record/Play buttons.
    /// Only a standalone-owned claim is cleared; any other owner
    /// (or an already-idle session) is left untouched.</summary>
    private void ClearSession()
    {
        if (_session.SessionOwner != FloatPanelSessionOwner.Standalone) return;
        SetSession(active: false, FloatPanelSessionOwner.None);
    }

    private void NotifyFooter(string message, string dotColor)
    {
        try
        {
            _notifyFooter(message, dotColor, FooterSeconds);
        }
        catch (Exception ex)
        {
            // A footer failure must never break recording/playback.
            CrashLog.Write("StandalonePanelController.NotifyFooter", ex);
        }
    }
}
