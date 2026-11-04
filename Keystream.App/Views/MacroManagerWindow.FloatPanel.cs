using System;
using System.Windows;
using System.Windows.Media;
using Keystream.App.Host;
using Keystream.Core.Localization;

namespace Keystream.App.Views;

/// <summary>§111 (Worker C) - modal-side integration of the floating
/// record panel. §113: the modal NO LONGER owns the panel window at
/// all. Lifetime, position and event routing belong to
/// <see cref="FloatPanelHost"/> (reachable via
/// <see cref="AppHost.PanelHost"/>); this partial owns ONLY the
/// modal's side of the contract: attaching/detaching as the panel
/// handler, pushing name / step count / state through the host's
/// <see cref="FloatPanelHost.Panel"/>, the toggle-button visual, the
/// shared-session wiring (selection + record settings + gating), and
/// the <see cref="IPanelActionHandler"/> implementation. The panel is
/// ONLY an extension/remote of the modal: it has no settings and no
/// picker, the modal writes everything to
/// <see cref="FloatPanelSession"/>, and the panel obeys.
///
/// Method names/signatures are kept from §111/§112
/// (ShowFloatPanel/HideFloatPanel/CloseFloatPanel/RefreshFloatPanel/
/// RefreshPanelToggleButton + the OnFloat* handlers) but every one
/// delegates window lifetime to the host. Closing the modal
/// (Batal/Simpan/×) detaches the modal and NEVER closes or hides the
/// panel (P1). A float failure must NEVER abort recording or crash
/// the modal: show/refresh swallow into CrashLog.
/// <see cref="_floatPanel"/> is only a synced cache of the host's
/// panel reference, refreshed on every access path - the host is the
/// source of truth, and it may be null (panel never shown, or closed
/// at app exit) without any modal path throwing.</summary>
public partial class MacroManagerWindow : IPanelActionHandler
{
    private FloatingRecordPanel? _floatPanel;

    /// <summary>The host-owned panel window, or null when it does not
    /// exist. Never create the window from the modal - only
    /// <see cref="FloatPanelHost.ShowPanel"/> does.</summary>
    private FloatingRecordPanel? HostPanel => _host.PanelHost?.Panel;

    /// <summary>Displayed macro name, read the same way NameBox shows
    /// it (NameBox.Text is the editing name; _working is the fallback
    /// before the editor is populated).</summary>
    private string CurrentFloatMacroName()
    {
        string name = NameBox?.Text ?? "";
        if (string.IsNullOrWhiteSpace(name))
            name = _working?.Name ?? "";
        return name;
    }

    // --- shared session -------------------------------------------------

    /// <summary>§113: writes the selected macro id to the shared
    /// session (null = no selection). Single write point, called from
    /// LoadWorkingIntoEditor (every selection/New/Duplicate path ends
    /// there) and ClearEditor. Never throws into the caller: a session
    /// write failure must not break selection.</summary>
    private void WriteSelectedMacro(string? id)
    {
        try
        {
            var session = _host.FloatPanel;
            session.SelectedMacroId = id;
            session.NotifyChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.WriteSelectedMacro", ex);
        }
    }

    /// <summary>§113: marks a modal-owned session active (countdown,
    /// recording, playback). Idempotent; the standalone controller
    /// reads this to gate its own buttons. §120 B1b: single-owner
    /// enforcement - a standalone-owned session is NEVER overwritten
    /// here (the mirror of <see cref="EndModalSession"/>'s owner
    /// guard, which was previously asymmetric). The takeover in the
    /// ctor ends a standalone session before the modal attaches, so
    /// this guard is defence-in-depth; a refused claim simply leaves
    /// the standalone session owning the panel.</summary>
    private void BeginModalSession()
    {
        try
        {
            var session = _host.FloatPanel;
            if (!session.CanModalTakeOwnership()) return;
            session.SessionActive = true;
            session.SessionOwner = FloatPanelSessionOwner.Modal;
            session.NotifyChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.BeginModalSession", ex);
        }
    }

    /// <summary>§113: ends a modal-owned session. The owner guard is
    /// load-bearing: a standalone-owned session must NEVER be cleared
    /// by a modal teardown path (StopRecording, modal close).</summary>
    private void EndModalSession()
    {
        try
        {
            var session = _host.FloatPanel;
            if (session.SessionOwner != FloatPanelSessionOwner.Modal) return;
            session.SessionActive = false;
            session.SessionOwner = FloatPanelSessionOwner.None;
            session.NotifyChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.EndModalSession", ex);
        }
    }

    /// <summary>§113: session.Changed subscriber (UI thread; the
    /// session doc guarantees the mutating thread is the UI thread,
    /// but marshal defensively anyway).</summary>
    private void OnFloatSessionChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            try { Dispatcher.BeginInvoke(() => OnFloatSessionChanged()); }
            catch { /* teardown must never throw */ }
            return;
        }
        try { RefreshSessionGating(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnFloatSessionChanged", ex);
        }
    }

    /// <summary>§113: modal-side session gating. While a STANDALONE
    /// session is active (panel recording/playing with the modal
    /// closed, then the modal reopened over it - or a standalone
    /// session started while the modal is open), the modal's Record
    /// and ▶ buttons stay disabled and the S1630 notice shows.
    /// Otherwise the buttons follow their normal rules and the notice
    /// hides. The ⤢ toggle NEVER gates (always active). Called from
    /// session.Changed, the ctor, RefreshStepButtons, and
    /// SetEditorEnabled (so the editor lock cannot clobber it).</summary>
    private void RefreshSessionGating()
    {
        bool standaloneActive = false;
        try
        {
            var session = _host.FloatPanel;
            standaloneActive = session.SessionActive
                && session.SessionOwner == FloatPanelSessionOwner.Standalone;
        }
        catch { /* gating must never throw; fall through to normal rules */ }
        if (standaloneActive)
        {
            RecordButton.IsEnabled = false;
            PlayMacroButton.IsEnabled = false;
            NoticeText.Visibility = Visibility.Visible;
            return;
        }
        // Normal rules: Record stays usable as Stop while recording
        // (SetEditorEnabled never touches it); ▶ needs a working
        // macro with steps and an idle modal (not recording, paused,
        // capturing, or already playing).
        RecordButton.IsEnabled = _working is not null;
        PlayMacroButton.IsEnabled = _working is not null
            && _stepRows.Count > 0
            && !_recording && !_paused && !_capturing && !_playtestRunning;
        NoticeText.Visibility = Visibility.Collapsed;
    }

    /// <summary>§113: real-close teardown (Closed, not the
    /// confirm-cancel path in Window_Closing): unsubscribe, detach
    /// from the host (idempotent - Window_Closing already detached
    /// on a confirmed close), stop the panic hook, drop the CTS and
    /// the modal session, and clear the panel-surface delegate. The
    /// panel itself is NEVER closed or hidden here.</summary>
    private void OnModalClosed(object? sender, EventArgs e)
    {
        try { _host.FloatPanel.Changed -= OnFloatSessionChanged; }
        catch { /* teardown must never throw */ }
        try { _host.PanelHost.PanelShown -= RefreshPanelToggleButton; }
        catch { /* teardown must never throw */ }
        // §122 recheck (F-C1): end the modal-owned session BEFORE
        // detaching, for the same reason as in Window_Closing -
        // DetachModal's saved-state refresh is skipped while a modal
        // session is still active, which would leave the panel
        // showing a stale draft after an abnormal close.
        EndModalSession();
        try { _host.PanelHost.DetachModal(this); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnModalClosed.Detach", ex);
        }
        StopEscPanic();
        // CTS disposal stays with the running playback
        // (EndPlaytestSession) when one is in flight: disposing the
        // source under a captured token is unnecessary risk. A
        // countdown was already disposed by CancelPlaytest in
        // Window_Closing; an idle modal has nothing to dispose.
        if (!_playtestRunning && !_playtestCountdown.IsRunning) DisposePlaytestCts();
        _isFloatSurfacePoint = null;
        _floatPanel = null;
    }

    // --- host-delegated lifetime ----------------------------------------

    private void ShowFloatPanel()
    {
        try
        {
            _host.PanelHost.ShowPanel();
            _floatPanel = HostPanel;
            // §111: clicks on the panel surface (or while it is
            // being dragged) must not be recorded as mouse steps.
            // Delegate type is Func<int, int, bool> (main file); it
            // reads the host panel dynamically, so it stays correct
            // across host-driven recreations.
            _isFloatSurfacePoint = (x, y) =>
            {
                var panel = HostPanel;
                return panel is not null && panel.IsVisible
                    && (panel.IsPointWithin(x, y) || panel.IsDragging);
            };
            RefreshFloatPanel();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.ShowFloatPanel", ex);
        }
        RefreshPanelToggleButton();
    }

    private void HideFloatPanel()
    {
        // Hiding cancels a running countdown: no playback after the
        // panel (and its ×) is gone. HidePanel re-notifies via
        // OnPanelHidden (idempotent) before hiding.
        CancelPlaytest();
        try
        {
            _host.PanelHost.HidePanel();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.HideFloatPanel", ex);
        }
        RefreshPanelToggleButton();
    }

    private void CloseFloatPanel()
    {
        // §113: DETACH ONLY. No window close, no hide: the panel
        // stays alive after the modal closes (P1). The countdown is
        // cancelled so no playback can fire after the modal is gone.
        CancelPlaytest();
        try { _host.PanelHost.DetachModal(this); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.CloseFloatPanel", ex);
        }
        _floatPanel = null;
        _isFloatSurfacePoint = null;
        RefreshPanelToggleButton();
    }

    private void RefreshFloatPanel(bool restoreHidden = false)
    {
        try
        {
            _floatPanel = HostPanel;
            if (_floatPanel is null) return;
            // §120 B1c: owner-aware refresh. While a STANDALONE
            // session owns the panel, a modal refresh must touch
            // NOTHING - no StopClock (the standalone clock would
            // reset to 00:00 permanently), no SetReady, no modal
            // name/count. Plain modal refreshes (■ Stop while idle,
            // a cancelled close, step updates) return here and the
            // standalone session's own visuals survive untouched.
            // A hidden panel is likewise never resurrected from here
            // while the standalone session owns it. ❚❚ is untouched
            // by this rule: pause/resume only run while the modal
            // itself is recording, when no standalone session can
            // own the panel.
            if (_host.FloatPanel.IsStandaloneSessionActive) return;
            // RESTORE RULE (§111 coordinator fix, §112 extended,
            // §113 delegated): a hidden panel comes back ONLY on an
            // explicit ShowPanel at a recording state transition
            // (callers pass restoreHidden: true). The host owns the
            // user-hidden flag - never resurrect from here when the
            // panel was never created, and never show on a plain
            // step update (restoreHidden false), or × would be
            // useless.
            if (restoreHidden && _host.PanelHost.IsUserHidden)
            {
                _host.PanelHost.ShowPanel();
                _floatPanel = HostPanel;
                if (_floatPanel is null) return;
            }
            if (!_recording)
            {
                // READY (§112): clock stopped/reset to 00:00, grey
                // dot, Record/Playtest buttons, count = working steps.
                // No countdown overlay is touched here: a countdown
                // runs in this state and owns its own overlay.
                _floatPanel.StopClock();
                _floatPanel.SetReady();
                _floatPanel.SetPlaytestEnabled(FloatingRecordPanelLogic.IsPlaytestEnabled(
                    FloatingRecordPanelLogic.PanelState.Ready, _stepRows.Count)
                    && !_playtestRunning);
                _floatPanel.SetMacroName(CurrentFloatMacroName());
                _floatPanel.SetStepCount(_stepRows.Count);
                return;
            }
            // CLOCK PAIRING (coordinator fix - Worker D finding): the
            // panel clock is frozen exactly while paused. PauseClock /
            // ResumeClock are Stopwatch Stop/Start, so re-pairing on
            // every refresh is idempotent and safe. A countdown can
            // never be live while recording (starting a recording
            // cancels it), but HideCountdown here is the belt to the
            // CancelPlaytest braces in the record-start path.
            _floatPanel.HideCountdown();
            if (_paused) _floatPanel.PauseClock();
            else _floatPanel.ResumeClock();
            _floatPanel.SetPaused(_paused);
            _floatPanel.SetMacroName(CurrentFloatMacroName());
            _floatPanel.SetStepCount(_stepRows.Count);
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.RefreshFloatPanel", ex);
        }
    }

    private void RefreshPanelToggleButton()
    {
        var button = FloatPanelToggleButton;
        if (button is null) return;
        // §112: the toggle is ALWAYS active (idle, recording, paused
        // alike) and always fully opaque - it is exempt from the
        // editor lock (SetEditorEnabled never touches it) and from
        // the idle-disabled rule, and §113 gating never touches it
        // either. Only the visible-state highlight (cyan border)
        // varies, read from the host panel.
        button.IsEnabled = true;
        button.Opacity = 1.0;
        var panel = HostPanel;
        _floatPanel = panel;
        if (panel is not null && panel.IsVisible)
        {
            button.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5B, 0xE4, 0xFF));
            button.BorderThickness = new Thickness(1.5);
        }
        else
        {
            // BorderBrush/BorderThickness are NOT local values in the
            // XAML for this button (unlike RecordButton.Style, which
            // IS set locally to DangerButton - see the ClearValue
            // warning in StopRecording), so ClearValue restores the
            // implicit button style's border.
            button.ClearValue(System.Windows.Controls.Button.BorderBrushProperty);
            button.ClearValue(System.Windows.Controls.Button.BorderThicknessProperty);
        }
    }

    private void FloatPanelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // §112: no recording guard. An idle click SHOWS the panel in
        // READY state; a visible click HIDES it, in any state.
        var panel = HostPanel;
        if (panel is not null && panel.IsVisible)
            HideFloatPanel();
        else
            ShowFloatPanel();
    }

    // --- IPanelActionHandler (§113) --------------------------------------
    // The host routes the panel's button events here while the modal
    // is attached. The panel has no settings of its own: every action
    // below reads the modal's editor + the shared session.

    /// <summary>Panel ● pressed (READY). Forced 3-second countdown,
    /// ALWAYS - the Delay3s checkbox never applies to panel ● (only
    /// to ▶ Main and to modal Rekam). Fires into the same
    /// RecordButton path as a modal Rekam.</summary>
    public void OnPanelRecordRequested()
    {
        try
        {
            if (_recording || _paused) return;
            // §120 F-B2 (same guard as RecordButton_Click): recording
            // while a modal playback is running is refused with a
            // VISIBLE error, never a silent no-op. A playtest
            // countdown is not _playtestRunning yet and is still
            // cancelled-then-recorded by StartActionCountdown's
            // caller path (see RecordButton_Click); this guard only
            // fires once playback has actually started.
            if (_playtestRunning)
            {
                ShowError(Localizer.Get("S1633"));
                return;
            }
            if (_working is null) return;
            StartActionCountdown(CountdownKind.Record, StartRecordingCore, forceCountdown: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnPanelRecordRequested", ex);
        }
    }

    /// <summary>Panel ▶ pressed (READY). Plays per the shared Delay3s
    /// checkbox (countdown iff ON, else instant) - the §112 always-3s
    /// rule for ▶ is SUPERSEDED.</summary>
    public void OnPanelPlaytestRequested()
    {
        try { StartPlaytestCountdown(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnPanelPlaytestRequested", ex);
        }
    }

    /// <summary>Panel ❚❚/▶ pressed while recording/paused.</summary>
    public void OnPanelPauseToggleRequested()
    {
        try
        {
            if (_paused) ResumeRecording();
            else PauseRecording();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnPanelPauseToggleRequested", ex);
        }
    }

    /// <summary>Panel ■ pressed while recording/paused.</summary>
    public void OnPanelStopRequested()
    {
        try { StopRecording(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnPanelStopRequested", ex);
        }
    }

    /// <summary>The panel was hidden (×) or closed. Cancel any running
    /// countdown owned by this handler; never start playback after
    /// this call. The toggle highlight is refreshed on the next
    /// dispatcher turn, AFTER the host has actually hidden the
    /// window (HidePanel notifies before hiding).</summary>
    public void OnPanelHidden()
    {
        try { CancelPlaytest(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnPanelHidden", ex);
        }
        try { Dispatcher.BeginInvoke(() => RefreshPanelToggleButton()); }
        catch { /* teardown must never throw */ }
    }

    // --- legacy OnFloat* handlers (kept signatures) ----------------------
    // §111/§112 event handlers, kept for compat. The host now owns the
    // panel's event subscriptions and routes through the
    // IPanelActionHandler methods above; these remain as the direct
    // implementations the handler methods delegate to where the
    // shapes differ, plus defensive teardown.

    private void OnFloatPauseToggleRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_paused) ResumeRecording();
            else PauseRecording();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnFloatPauseToggleRequested", ex);
        }
    }

    private void OnFloatStopRequested(object? sender, EventArgs e)
    {
        try { StopRecording(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnFloatStopRequested", ex);
        }
    }

    private void OnFloatRecordRequested(object? sender, EventArgs e)
    {
        try
        {
            // ● exists only in READY; the guard is defence against a
            // stale event. Delegates to the handler route so the
            // forced-3s panel rule (§113) applies here too - NOT to
            // RecordButton_Click, which follows the checkbox.
            OnPanelRecordRequested();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnFloatRecordRequested", ex);
        }
    }

    private void OnFloatPlaytestRequested(object? sender, EventArgs e)
    {
        try { StartPlaytestCountdown(); }
        catch (Exception ex)
        {
            CrashLog.Write("MacroManagerWindow.OnFloatPlaytestRequested", ex);
        }
    }

    private void OnFloatHideRequested(object? sender, EventArgs e)
    {
        HideFloatPanel();
    }

    private void OnFloatLocationChanged(object? sender, EventArgs e)
    {
        // No-op in §113: position is owned by the host (session
        // PanelLeft/Top, saved on the host's LocationChanged). Kept
        // so the §111/§112 handler set stays complete.
    }

    private void OnFloatClosed(object? sender, EventArgs e)
    {
        // Defensive: the panel closed itself (not via the host's
        // ClosePanel, which unsubscribes and drops the reference
        // itself). Drop the modal-side refs so a later ShowFloatPanel
        // re-syncs from the host instead of using a dead window.
        _floatPanel = null;
        _isFloatSurfacePoint = null;
        RefreshPanelToggleButton();
    }
}
