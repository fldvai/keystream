using System;
using System.Windows;
using System.Windows.Media;
using Keystream.App.Host;

namespace Keystream.App.Views;

/// <summary>§113 - owner of the floating record panel WINDOW. The panel
/// is only an extension/remote of the macro modal: it has no settings
/// and no macro picker of its own. This host owns window lifetime,
/// position and event routing ONLY; it owns no recording/playback
/// state and never restarts clocks or state - the current handler
/// pushes visuals through <see cref="Panel"/>.
///
/// Routing: while the macro modal is open it attaches itself via
/// <see cref="AttachModal"/> and receives the panel's button events;
/// otherwise the standalone controller (Worker A) handles them. The
/// modal never owns the window: detaching it must not close or hide
/// the panel, and closing the modal must not either.
///
/// Lifetime: the panel window is created lazily on the first
/// <see cref="ShowPanel"/>, always on the UI thread, with NO owner
/// window assigned. That is REQUIRED, not incidental: an owned
/// window is hidden when its owner hides and closed when its owner
/// closes, and the panel must survive BOTH the modal closing (P1)
/// and the main window hiding to the tray. Real exit closes it via
/// <see cref="ClosePanel"/>, called only from the app-exit teardown
/// (AppHost.Dispose); Application Shutdown then closes any residual
/// window as the final guarantee.
///
/// Restore rule (§111/§112, preserved): a panel the user hid with
/// the × control reappears ONLY when a handler explicitly calls
/// <see cref="ShowPanel"/> (a state transition: pause/resume,
/// stop-to-READY, recording start). Plain refreshes never call
/// ShowPanel and never resurrect a user-hidden panel.
///
/// A float failure must NEVER crash the app: every show/hide/close
/// path swallows into CrashLog, the same discipline as the §111
/// modal-side partial this replaces.</summary>
public sealed class FloatPanelHost
{
    private readonly AppHost _host;
    private readonly FloatPanelSession _session;
    private readonly StandalonePanelController _standalone;
    private readonly Action<string, string, double> _notifyFooter;
    private IPanelActionHandler? _attachedModal;
    private bool _userHidden;

    /// <summary>Creates the host. Stores references ONLY and constructs
    /// nothing window-related; the panel window is created lazily by
    /// <see cref="ShowPanel"/>. Attaches this host to the standalone
    /// controller so it can route through the host.</summary>
    public FloatPanelHost(
        AppHost host,
        FloatPanelSession session,
        StandalonePanelController standalone,
        Action<string, string, double> notifyFooter)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _standalone = standalone ?? throw new ArgumentNullException(nameof(standalone));
        _notifyFooter = notifyFooter ?? throw new ArgumentNullException(nameof(notifyFooter));
        _standalone.AttachHost(this);
    }

    /// <summary>The composition root that owns this host.</summary>
    public AppHost Host => _host;

    /// <summary>Shared run-lifetime state between the modal and the panel.</summary>
    public FloatPanelSession Session => _session;

    /// <summary>Footer-notice sink, forwarded to the main window.</summary>
    public Action<string, string, double> NotifyFooter => _notifyFooter;

    /// <summary>The panel window, once created. Null until the first
    /// <see cref="ShowPanel"/> and again after <see cref="ClosePanel"/>
    /// or an external close. Handlers push visuals (name, count,
    /// state, clock) through this reference; they must NOT create
    /// the window - only <see cref="ShowPanel"/> does.</summary>
    public FloatingRecordPanel? Panel { get; private set; }

    /// <summary>True while the panel window exists and is visible.</summary>
    public bool IsPanelVisible => Panel is not null && Panel.IsVisible;

    /// <summary>True while the user has hidden the panel with × and no
    /// handler has explicitly re-shown it since (see the restore
    /// rule in the class summary).</summary>
    public bool IsUserHidden => _userHidden;

    /// <summary>Raised after <see cref="ShowPanel"/> shows the panel
    /// (session already updated), so the modal can refresh its toggle
    /// highlight. Subscribers must not call ShowPanel from this event.</summary>
    public event Action? PanelShown;

    /// <summary>Whichever handler currently owns the panel's button
    /// actions: the attached modal when one is attached, otherwise
    /// the standalone controller.</summary>
    private IPanelActionHandler CurrentHandler => _attachedModal ?? _standalone;

    /// <summary>§137 - true while the macro modal is attached (open)
    /// and therefore owns panel routing. The master-shortcut toggles
    /// read this to decide whether the modal's working draft governs
    /// (attached) or only saved library entries count as targets.</summary>
    public bool IsModalAttached => _attachedModal is not null;

    /// <summary>§137 - the current handler's session kind (see
    /// <see cref="IPanelActionHandler.CurrentSessionKind"/>). The
    /// master-shortcut toggles branch on this; it reads existing
    /// handler state and never creates a second ownership model.</summary>
    public PanelSessionKind CurrentSessionKind => CurrentHandler.CurrentSessionKind;

    /// <summary>§137 - routes a record request to the current handler
    /// (the EXACT panel ● path: modal records into its working draft,
    /// standalone into the selected saved macro). Never throws out of
    /// the host: handler failures are already swallowed inside, and
    /// this last guard keeps a hotkey failure out of WndProc.</summary>
    public void StartCurrentRecording()
    {
        try { CurrentHandler.OnPanelRecordRequested(); }
        catch (Exception ex) { CrashLog.Write("FloatPanelHost.StartCurrentRecording", ex); }
    }

    /// <summary>§137 - routes a record stop to the current handler
    /// (the EXACT panel ■ path, including auto-save/rollback/footer
    /// rules). Call only when <see cref="CurrentSessionKind"/> is
    /// Recording; the handlers' own guards make a stray call safe.</summary>
    public void StopCurrentRecording()
    {
        try { CurrentHandler.OnPanelStopRequested(); }
        catch (Exception ex) { CrashLog.Write("FloatPanelHost.StopCurrentRecording", ex); }
    }

    /// <summary>§137 - routes a playback request to the current handler
    /// (the EXACT panel ▶ path, through MacroAction.ExecuteAsync with
    /// the snapshot delegate). The handlers' own guards refuse it
    /// while recording or busy.</summary>
    public void StartCurrentPlayback()
    {
        try { CurrentHandler.OnPanelPlaytestRequested(); }
        catch (Exception ex) { CrashLog.Write("FloatPanelHost.StartCurrentPlayback", ex); }
    }

    /// <summary>§137 - stops the current handler's playback through
    /// its CTS-cancel path (see
    /// <see cref="IPanelActionHandler.StopPlayback"/>).</summary>
    public void StopCurrentPlayback()
    {
        try { CurrentHandler.StopPlayback(); }
        catch (Exception ex) { CrashLog.Write("FloatPanelHost.StopCurrentPlayback", ex); }
    }

    /// <summary>§137 - cancels the current handler's running countdown
    /// through its existing OnPanelHidden contract (countdown-cancel
    /// only; never stops a recording or a playback). Call only for
    /// the matching countdown kind.</summary>
    public void CancelCurrentCountdown()
    {
        try { CurrentHandler.OnPanelHidden(); }
        catch (Exception ex) { CrashLog.Write("FloatPanelHost.CancelCurrentCountdown", ex); }
    }

    /// <summary>§137 - toggles the current handler's playback pause
    /// gate. False when no playback is active (the caller shows
    /// S1729); true when the gate flipped.</summary>
    public bool ToggleCurrentPlaybackPause()
    {
        try { return CurrentHandler.TogglePlaybackPause(); }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.ToggleCurrentPlaybackPause", ex);
            return false;
        }
    }

    /// <summary>Shows the panel, creating it on first use. The window
    /// is created with no owner (see the class summary for why).
    /// Position comes from the session when set, else the top-right
    /// of the work area (the §112 default, verbatim); a re-show keeps
    /// the last position. Clocks and state are never restarted here -
    /// handlers push visuals. Clears the user-hidden flag.</summary>
    public void ShowPanel()
    {
        try
        {
            if (Panel is null)
            {
                var panel = new FloatingRecordPanel();
                // Deliberately no owner window: see the class summary.
                panel.PauseToggleRequested += OnPanelPauseToggleRequested;
                panel.StopRequested += OnPanelStopRequested;
                panel.HideRequested += OnPanelHideRequested;
                panel.RecordRequested += OnPanelRecordRequested;
                panel.PlaytestRequested += OnPanelPlaytestRequested;
                panel.CountdownCancelRequested += OnPanelCountdownCancelRequested;
                panel.LocationChanged += OnPanelLocationChanged;
                panel.Closed += OnPanelClosed;
                Panel = panel;
            }

            // §118: first show uses the top-right default; later
            // shows reuse the last position (kept fresh by
            // OnPanelLocationChanged + SaveSessionPosition) AFTER
            // validation and screen-aware clamping - the stored
            // values are never trusted as-is. ResolveShowPosition
            // never throws and never returns a non-finite or
            // off-screen position.
            (double left, double top) = ResolveShowPosition(Panel!);
            Panel.Left = left;
            Panel.Top = top;
            Panel.Show();
            _userHidden = false;
            _session.PanelVisible = true;
            _session.NotifyChanged();
            PanelShown?.Invoke();
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.ShowPanel", ex);
        }
    }

    /// <summary>§118: resolves the position ShowPanel assigns.
    /// Never throws and never returns a non-finite position.
    /// Ladder: (1) non-finite stored values → default; (2) finite
    /// values clamped fully inside the work area of the screen
    /// containing the stored top-left point, or the nearest work
    /// area when the point is outside every screen (F-C6: no
    /// teleport to the primary for a point just past an edge);
    /// (3) no usable screen or the screen API unusable → clamp
    /// against the primary work area; (4) degenerate work area →
    /// default. A non-finite value is never forwarded to
    /// Panel.Left/Top.</summary>
    private (double Left, double Top) ResolveShowPosition(FloatingRecordPanel panel)
    {
        try
        {
            PanelSize(panel, out double width, out double height);
            double left = _session.PanelLeft;
            double top = _session.PanelTop;
            if (!IsFinite(left) || !IsFinite(top))
                return DefaultPosition(width, height);
            if (TryClampToScreenWorkArea(panel, left, top, width, height,
                    out double clampedLeft, out double clampedTop))
                return (clampedLeft, clampedTop);
            try
            {
                var area = SystemParameters.WorkArea;
                if (IsFinite(area.Left) && IsFinite(area.Top)
                    && IsFinite(area.Width) && IsFinite(area.Height)
                    && area.Width > 0 && area.Height > 0)
                    return ClampInside(area, left, top, width, height);
            }
            catch { /* fall through to the default */ }
            return DefaultPosition(width, height);
        }
        catch
        {
            // The fallback itself must not throw either.
            try
            {
                PanelSize(panel, out double width, out double height);
                return DefaultPosition(width, height);
            }
            catch { return (0, 0); }
        }
    }

    /// <summary>§118: the §112 default, unchanged in shape -
    /// top-right of the primary work area with a 16 DIP margin -
    /// but sized from the panel instead of literals. Never throws;
    /// (0, 0) is the unreachable last resort.</summary>
    private static (double Left, double Top) DefaultPosition(double width, double height)
    {
        try
        {
            var area = SystemParameters.WorkArea;
            if (IsFinite(area.Left) && IsFinite(area.Top)
                && IsFinite(area.Right) && IsFinite(area.Bottom)
                && area.Width > 0 && area.Height > 0)
            {
                double left = Math.Max(area.Left, area.Right - width - 16);
                double top = Math.Max(area.Top, area.Top + 16);
                return (left, top);
            }
        }
        catch { /* fall through */ }
        return (0, 0);
    }

    /// <summary>§118: the panel's live size from its Width/Height
    /// (set by XAML). The 172/130 literals below are ONLY the
    /// fallback when the properties are unset or non-finite -
    /// never hardcode the live size (§118g).</summary>
    private static void PanelSize(FloatingRecordPanel panel, out double width, out double height)
    {
        width = panel.Width;
        height = panel.Height;
        if (!IsFinite(width) || width <= 0) width = 172;
        if (!IsFinite(height) || height <= 0) height = 130;
    }

    /// <summary>§118 (F-C6): clamps a position fully inside the work
    /// area of the screen containing the given top-left point.
    /// Screen work areas come from System.Windows.Forms.Screen
    /// (device pixels) and are converted to DIP with EACH screen's
    /// OWN effective DPI, queried per screen via
    /// <see cref="MonitorDpi"/> at the screen's centre pixel - never
    /// with this panel's DPI for every screen (the §118 approximation
    /// that mis-clamped by up to 640 DIP on a 100% + 150% pair).
    /// The pure conversion + clamp live in
    /// <see cref="FloatingRecordPanelLogic"/> (harness-pinned). When
    /// a screen's DPI cannot be queried, that screen falls back to
    /// the panel's PresentationSource scale (the old behaviour for
    /// that screen only); when there is no PresentationSource yet
    /// (before the first Show) the DPI queries still work, so this
    /// no longer returns false pre-Show. A point outside every
    /// screen clamps into the nearest screen's work area (no
    /// teleport to the primary). Returns false (leaving the outs
    /// untouched) when there are no usable screens or on any
    /// error. Never throws.</summary>
    private static bool TryClampToScreenWorkArea(
        FloatingRecordPanel panel, double left, double top,
        double width, double height,
        out double clampedLeft, out double clampedTop)
    {
        clampedLeft = left;
        clampedTop = top;
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens is null || screens.Length == 0) return false;
            // Fallback scale for screens whose DPI query fails:
            // the panel's own transform, as before. Null before
            // the first Show - then only queried DPIs are used.
            var source = PresentationSource.FromVisual(panel);
            var transform = source?.CompositionTarget?.TransformFromDevice;
            double fallbackScaleX = 0, fallbackScaleY = 0;
            if (transform is not null
                && IsFinite(transform.Value.M11) && IsFinite(transform.Value.M22)
                && transform.Value.M11 > 0 && transform.Value.M22 > 0)
            {
                fallbackScaleX = transform.Value.M11;
                fallbackScaleY = transform.Value.M22;
            }
            var pixelAreas = new List<FloatingRecordPanelLogic.PixelWorkArea>();
            foreach (var screen in screens)
            {
                if (screen is null) continue;
                var pixels = screen.WorkingArea;
                if (pixels.Width <= 0 || pixels.Height <= 0) continue;
                double dpiX, dpiY;
                if (MonitorDpi.TryGetDpiForPixelPoint(
                        pixels.Left + pixels.Width / 2, pixels.Top + pixels.Height / 2,
                        out uint qx, out uint qy))
                {
                    dpiX = qx;
                    dpiY = qy;
                }
                else if (fallbackScaleX > 0 && fallbackScaleY > 0)
                {
                    dpiX = fallbackScaleX * 96.0;
                    dpiY = fallbackScaleY * 96.0;
                }
                else
                {
                    continue;
                }
                pixelAreas.Add(new FloatingRecordPanelLogic.PixelWorkArea(
                    pixels.Left, pixels.Top, pixels.Width, pixels.Height, dpiX, dpiY));
            }
            if (pixelAreas.Count == 0) return false;
            var dipAreas = FloatingRecordPanelLogic.ComputeDipWorkAreas(pixelAreas);
            return FloatingRecordPanelLogic.TryClampToDipAreas(
                left, top, width, height, dipAreas, out clampedLeft, out clampedTop);
        }
        catch
        {
            clampedLeft = left;
            clampedTop = top;
            return false;
        }
    }

    /// <summary>§118: fully-inside clamp, one axis at a time:
    /// area.Start ≤ value ≤ area.End − size. When the panel is as
    /// large as (or larger than) the work area, pin to the area's
    /// start edge instead of producing a below-min value. A
    /// degenerate area returns the input unchanged.</summary>
    private static (double Left, double Top) ClampInside(
        Rect area, double left, double top, double width, double height)
    {
        if (!IsFinite(area.Left) || !IsFinite(area.Top)
            || !IsFinite(area.Width) || !IsFinite(area.Height))
            return (left, top);
        double clampedLeft = width >= area.Width
            ? area.Left
            : Math.Max(area.Left, Math.Min(left, area.Right - width));
        double clampedTop = height >= area.Height
            ? area.Top
            : Math.Max(area.Top, Math.Min(top, area.Bottom - height));
        return (clampedLeft, clampedTop);
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>F-C7: re-clamps the panel after the monitor layout
    /// changed (WM_DISPLAYCHANGE, via the main window's WndProc).
    /// Strictly a no-op unless the panel window exists AND is
    /// currently visible: a panel the user hid with × is never
    /// resurrected, and a panel that was never shown is never
    /// created here. Only Left/Top move, via the panel's own
    /// drag-end clamp (same per-monitor conversion, same silence);
    /// no state, clock, or session flag is touched, and the
    /// position save still flows through the existing
    /// LocationChanged subscription when Left/Top actually change.
    /// Never throws.</summary>
    public void ReclampVisiblePanel()
    {
        try
        {
            var panel = Panel;
            if (panel is null || !panel.IsVisible) return;
            panel.ReclampInsideWorkArea();
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.ReclampVisiblePanel", ex);
        }
    }

    /// <summary>Hides the panel. Cancel-safe: the current handler is
    /// told first (via OnPanelHidden, so running countdowns die
    /// before the window hides), then the position is saved and the
    /// window hidden. Sets the user-hidden flag and marks the session
    /// not-visible. Idempotent: hiding an already-hidden panel only
    /// re-asserts the flags and does not re-notify the handler.</summary>
    public void HidePanel()
    {
        try
        {
            var panel = Panel;
            if (panel is null) return;
            if (panel.IsVisible)
            {
                try { CurrentHandler.OnPanelHidden(); }
                catch (Exception ex)
                {
                    CrashLog.Write("FloatPanelHost.HidePanel.OnPanelHidden", ex);
                }
            }
            SaveSessionPosition();
            try { panel.Hide(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.HidePanel", ex);
            }
            _userHidden = true;
            _session.PanelVisible = false;
            _session.NotifyChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.HidePanel", ex);
        }
    }

    /// <summary>§120 Batch 1 - single-owner takeover, routing only.
    /// Ends an active standalone-owned session ORDERLY, before the
    /// modal attaches and takes routing. Called from the modal ctor
    /// (before AttachModal), never while the modal is attached: after
    /// this returns, no standalone countdown/recording/playback is
    /// still owned, so the modal can claim the session safely.
    /// Sequence through the EXISTING handler contract only (no new
    /// standalone API): (1) OnPanelHidden cancels a running
    /// countdown (no-op otherwise - it never stops a recording or
    /// a playback); (2) OnPanelStopRequested stops a recording and
    /// auto-saves it exactly like a normal ■ Stop (no-op when not
    /// recording, and it clears a stale session claim either way);
    /// (3) CancelAll ONLY while still busy afterwards - that state
    /// can only be a playback in flight, whose CTS it cancels (a
    /// recording would already have ended at step 2, so its staged
    /// steps are never discarded by this call). All three calls run
    /// synchronously on the UI thread, so no modal action can
    /// interleave between them and the attach that follows. A
    /// playback's async unwind may still finish after the attach;
    /// its completion refresh renders the saved state, which the
    /// freshly opened modal's working copy already matches.</summary>
    public void EndStandaloneSessionForModalTakeover()
    {
        try
        {
            if (!_session.IsStandaloneSessionActive && !_standalone.IsBusy) return;
            try { _standalone.OnPanelHidden(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.EndStandaloneSessionForModalTakeover.OnPanelHidden", ex);
            }
            try { _standalone.OnPanelStopRequested(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.EndStandaloneSessionForModalTakeover.OnPanelStopRequested", ex);
            }
            if (_standalone.IsBusy)
            {
                try { _standalone.CancelAll(); }
                catch (Exception ex)
                {
                    CrashLog.Write("FloatPanelHost.EndStandaloneSessionForModalTakeover.CancelAll", ex);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.EndStandaloneSessionForModalTakeover", ex);
        }
    }

    /// <summary>Routes panel button events to the attached modal
    /// while it is open. Routing only.</summary>
    public void AttachModal(IPanelActionHandler modal)
    {
        _attachedModal = modal ?? throw new ArgumentNullException(nameof(modal));
    }

    /// <summary>Stops routing to the modal and falls back to the
    /// standalone controller. Detaches ONLY if the given instance is
    /// the currently attached one. Must NOT close or hide the panel:
    /// the panel stays alive after the modal closes (P1).
    /// §120 F-C1: detaching also refreshes the panel to the SAVED
    /// state through the standalone controller, so after the modal
    /// closes with an unsaved draft the panel never keeps showing
    /// the draft's name/count. Skipped while the standalone
    /// controller is busy or a modal session is still active (the
    /// panel then keeps its last visuals until the owning session
    /// refreshes it itself); never resurrects a hidden panel.</summary>
    public void DetachModal(IPanelActionHandler modal)
    {
        if (ReferenceEquals(_attachedModal, modal))
        {
            _attachedModal = null;
            try
            {
                if (!_standalone.IsBusy && !_session.IsModalSessionActive)
                    _standalone.RefreshPanelState();
            }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.DetachModal.Refresh", ex);
            }
        }
    }

    /// <summary>App-exit ONLY: cancels any running countdown via the
    /// current handler, stops the clock, unsubscribes, closes the
    /// window and drops the reference. Called only from the exit
    /// teardown (AppHost.Dispose). Idempotent.</summary>
    public void ClosePanel()
    {
        try
        {
            var panel = Panel;
            if (panel is null) return;
            // §113 coordinator integration: exit teardown must also
            // stop standalone hooks/playback explicitly (OnPanelHidden
            // is countdown-only); CancelAll is internally guarded.
            try { _standalone.CancelAll(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.ClosePanel.CancelAll", ex);
            }
            try { CurrentHandler.OnPanelHidden(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.ClosePanel.OnPanelHidden", ex);
            }
            SaveSessionPosition();
            UnsubscribePanel(panel);
            Panel = null;
            _userHidden = false;
            try { panel.StopClock(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.ClosePanel.StopClock", ex);
            }
            try { panel.Close(); }
            catch (Exception ex)
            {
                CrashLog.Write("FloatPanelHost.ClosePanel", ex);
            }
            _session.PanelVisible = false;
            _session.NotifyChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.ClosePanel", ex);
        }
    }

    private void SaveSessionPosition()
    {
        var panel = Panel;
        if (panel is null) return;
        try
        {
            // §118: never persist a non-finite position - keep the
            // last valid stored values instead (and report no change).
            if (!IsFinite(panel.Left) || !IsFinite(panel.Top)) return;
            _session.PanelLeft = panel.Left;
            _session.PanelTop = panel.Top;
            _session.NotifyChanged();
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.SaveSessionPosition", ex);
        }
    }

    private void UnsubscribePanel(FloatingRecordPanel panel)
    {
        try
        {
            panel.PauseToggleRequested -= OnPanelPauseToggleRequested;
            panel.StopRequested -= OnPanelStopRequested;
            panel.HideRequested -= OnPanelHideRequested;
            panel.RecordRequested -= OnPanelRecordRequested;
            panel.PlaytestRequested -= OnPanelPlaytestRequested;
            panel.CountdownCancelRequested -= OnPanelCountdownCancelRequested;
            panel.LocationChanged -= OnPanelLocationChanged;
            panel.Closed -= OnPanelClosed;
        }
        catch { /* teardown must never throw */ }
    }

    private void OnPanelPauseToggleRequested(object? sender, EventArgs e)
    {
        try { CurrentHandler.OnPanelPauseToggleRequested(); }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.OnPanelPauseToggleRequested", ex);
        }
    }

    private void OnPanelStopRequested(object? sender, EventArgs e)
    {
        try { CurrentHandler.OnPanelStopRequested(); }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.OnPanelStopRequested", ex);
        }
    }

    private void OnPanelRecordRequested(object? sender, EventArgs e)
    {
        try { CurrentHandler.OnPanelRecordRequested(); }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.OnPanelRecordRequested", ex);
        }
    }

    private void OnPanelPlaytestRequested(object? sender, EventArgs e)
    {
        try { CurrentHandler.OnPanelPlaytestRequested(); }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.OnPanelPlaytestRequested", ex);
        }
    }

    /// <summary>§120 F-B1: a click on the countdown overlay cancels
    /// the running countdown to idle - it is NOT a hide (the panel
    /// stays visible) and it never restarts the countdown. Routed
    /// through the existing <see
    /// cref="IPanelActionHandler.OnPanelHidden"/> contract, whose
    /// meaning is exactly "cancel any countdown owned by this
    /// handler; never start playback after this call": both handlers
    /// implement it as countdown-cancel only, so reusing it here
    /// needs no new handler API and cannot stop a recording or a
    /// playback. ×/ESC cancellation is untouched.</summary>
    private void OnPanelCountdownCancelRequested(object? sender, EventArgs e)
    {
        try { CurrentHandler.OnPanelHidden(); }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.OnPanelCountdownCancelRequested", ex);
        }
    }

    private void OnPanelHideRequested(object? sender, EventArgs e) => HidePanel();

    private void OnPanelLocationChanged(object? sender, EventArgs e) => SaveSessionPosition();

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        // Defensive: the panel closed itself (not via ClosePanel).
        // Drop the reference so a later ShowPanel creates a fresh
        // panel instead of re-showing a dead window.
        try
        {
            if (sender is FloatingRecordPanel panel)
                UnsubscribePanel(panel);
        }
        catch { /* teardown must never throw */ }
        try
        {
            if (sender is not null && ReferenceEquals(sender, Panel))
            {
                Panel = null;
                _userHidden = false;
                _session.PanelVisible = false;
                _session.NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("FloatPanelHost.OnPanelClosed", ex);
        }
    }
}
