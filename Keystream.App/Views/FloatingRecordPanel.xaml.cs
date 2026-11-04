using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Keystream.App.Host;

namespace Keystream.App.Views;

/// <summary>Floating record panel (§111, mockup v4 LOCKED; §112
/// revision, mockup v5): a small square Topmost window that floats
/// over the app being recorded - and, since §112, also floats while
/// idle as a READY panel the user can summon any time with the modal
/// toggle to prepare before recording. Three rows only - state dot +
/// macro name + hide control, two icon-only buttons, indicators below
/// the buttons. State is colour only (grey dot = ready, red dot =
/// recording, amber square dot = paused); there are no status words
/// in the UI. The border is the app blue #1E86E0 in EVERY state
/// (FloatingRecordPanelLogic.PanelBorderHex) and is never changed
/// in code; only the dot + button colours carry state.
///
/// The panel owns NO recording state: it raises
/// <see cref="PauseToggleRequested"/>, <see cref="StopRequested"/>,
/// <see cref="RecordRequested"/>, <see cref="PlaytestRequested"/> and
/// <see cref="HideRequested"/>, and the caller drives the visuals back
/// through <see cref="SetReady"/>, <see cref="SetPaused"/>,
/// <see cref="SetStepCount"/>, <see cref="SetPlaytestEnabled"/> and
/// the clock methods. Button roles follow
/// FloatingRecordPanelLogic per state (Ready = Record/Playtest).
/// The hide control only raises an event - the caller hides the
/// panel, so a hidden panel never stops the recording. The window
/// has no Owner (§113: it must survive modal close and tray-hide);
/// it never shows a dialog, and never steals focus on appear
/// (ShowActivated=false).</summary>
public partial class FloatingRecordPanel : Window
{
    /// <summary>Raised when the pause/resume button is pressed (while
    /// recording/paused). The caller owns the state change and calls
    /// <see cref="SetPaused"/> plus PauseClock/ResumeClock to reflect
    /// it.</summary>
    public event EventHandler? PauseToggleRequested;

    /// <summary>Raised when the stop button is pressed (while
    /// recording/paused).</summary>
    public event EventHandler? StopRequested;

    /// <summary>Raised when the record button is pressed in the READY
    /// state. The caller runs the SAME start path as the modal's
    /// RecordButton and calls <see cref="SetPaused"/> to switch the
    /// panel to the recording state.</summary>
    public event EventHandler? RecordRequested;

    /// <summary>Raised when the playtest button is pressed in the READY
    /// state (only enabled with at least one working step). The caller
    /// owns the countdown and playback, and renders the countdown via
    /// <see cref="ShowCountdown"/> / <see cref="HideCountdown"/>.</summary>
    public event EventHandler? PlaytestRequested;

    /// <summary>Raised when the hide (×) control is pressed. The
    /// caller hides the panel; the recording keeps running.</summary>
    public event EventHandler? HideRequested;

    /// <summary>§120 F-B1: raised when the countdown overlay itself
    /// is clicked while a countdown is showing. The overlay covers
    /// the two buttons, so a press aimed at ●/▶ during a countdown
    /// lands here, not on the buttons. The caller cancels its
    /// countdown to idle (never restarts it, never hides the panel);
    /// the buttons stay inert under the overlay, guarded by
    /// <c>_countdownVisible</c> as before.</summary>
    public event EventHandler? CountdownCancelRequested;

    /// <summary>True from drag start until mouse-up (or deactivation).
    /// The caller can use it to tell a drag apart from a click that
    /// merely passes over the panel.</summary>
    public bool IsDragging { get; private set; }

    // Clock mechanism: a Stopwatch owns elapsed time, so the readout
    // freezes exactly while paused (Stopwatch stopped) and resumes
    // from the frozen value (Stopwatch started again - never reset
    // except by StopClock). A ~250ms DispatcherTimer only
    // refreshes the time TextBlock while the clock is running and the
    // window is visible; it never runs while the window is closed or
    // hidden, while the Stopwatch keeps counting underneath a hidden
    // panel so the elapsed time stays correct on re-show. In the
    // READY state the caller calls StopClock: the readout is 00:00
    // and the Stopwatch is reset.
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _clockTimer;
    private bool _clockRunning;

    // Auto-fade mechanism: a DispatcherTimer with the interval from
    // FloatingRecordPanelLogic. Any touch (enter, move, button press,
    // drag start) restores full opacity and restarts the timer. A tick
    // with the mouse elsewhere drops opacity to the 0.6 floor and
    // stops the timer until the next touch; a tick with the mouse over
    // the window restarts the timer instead of fading. Fading applies
    // to the whole window via Window.Opacity.
    private readonly DispatcherTimer _fadeTimer;

    // §112: rendered state, decided by FloatingRecordPanelLogic.
    // The border is NOT part of this state (always app blue).
    private FloatingRecordPanelLogic.PanelState _state = FloatingRecordPanelLogic.PanelState.Ready;
    private bool _playtestEnabled;
    private bool _countdownVisible;

    // §113: interaction kill-switch (deleted-macro state, driven by
    // the standalone controller via SetInteractionEnabled). Both
    // buttons stay disabled while false, whatever SetReady/SetPaused/
    // SetPlaytestEnabled later render - ApplyState consults it on
    // every render. The hide (×) control is unaffected.
    private bool _interactionEnabled = true;

    public FloatingRecordPanel()
    {
        InitializeComponent();
        Opacity = FloatingRecordPanelLogic.ActiveOpacity;

        _clockTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(250),
            DispatcherPriority.Normal,
            (_, _) => { if (_clockRunning) RefreshClockText(); },
            Dispatcher);
        _clockTimer.Stop();

        _fadeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(FloatingRecordPanelLogic.IdleFadeSeconds),
            DispatcherPriority.Normal,
            (_, _) => OnFadeTick(),
            Dispatcher);
        _fadeTimer.Stop();
    }

    /// <summary>Freezes the elapsed clock at its current value. The
    /// readout stays on the frozen value until <see cref="ResumeClock"/>.</summary>
    public void PauseClock()
    {
        _stopwatch.Stop();
        _clockRunning = false;
        _clockTimer.Stop();
        RefreshClockText();
    }

    /// <summary>Resumes the elapsed clock from the frozen value.</summary>
    public void ResumeClock()
    {
        _stopwatch.Start();
        _clockRunning = true;
        RefreshClockText();
        StartClockTimerIfVisible();
    }

    /// <summary>Stops the elapsed clock and resets the readout to
    /// 00:00. The READY-state clock pairing.</summary>
    public void StopClock()
    {
        _stopwatch.Stop();
        _stopwatch.Reset();
        _clockRunning = false;
        _clockTimer.Stop();
        RefreshClockText();
    }

    /// <summary>Sets the macro name (ellipsis in the UI) and its
    /// tooltip to the full name. The name is data, not a UI string.</summary>
    public void SetMacroName(string name)
    {
        string text = name ?? string.Empty;
        NameTextBlock.Text = text;
        NameTextBlock.ToolTip = string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Sets the step count readout (abbreviated via
    /// <see cref="FloatingRecordPanelLogic.FormatStepCount"/>) and its
    /// tooltip to the exact, unabbreviated count.</summary>
    public void SetStepCount(int count)
    {
        CountTextBlock.Text = FloatingRecordPanelLogic.FormatStepCount(count);
        CountTextBlock.ToolTip = Math.Max(count, 0).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Renders the READY state: grey round dot, ● Record +
    /// ▶ Playtest buttons (Playtest enabled only when
    /// <see cref="SetPlaytestEnabled"/> last said so). Does not touch
    /// the clock - the caller pairs it with StopClock - and never
    /// touches the border (always app blue).</summary>
    public void SetReady()
    {
        _state = FloatingRecordPanelLogic.PanelState.Ready;
        ApplyState();
    }

    /// <summary>Swaps the colour-only state visuals between recording
    /// and paused: red round dot + amber pause button while running;
    /// amber square dot + green resume button while paused. Does not
    /// touch the clock - the caller pairs it with PauseClock /
    /// ResumeClock - and never touches the border (always app blue,
    /// §112: the red/amber border swap from §111 is gone).</summary>
    public void SetPaused(bool paused)
    {
        _state = paused
            ? FloatingRecordPanelLogic.PanelState.Paused
            : FloatingRecordPanelLogic.PanelState.Recording;
        ApplyState();
    }

    /// <summary>Enables/disables the ▶ Playtest button (READY state
    /// only; the button does not exist in the other states). The
    /// caller re-evaluates on every panel refresh; zero working steps
    /// means disabled with no message. Remembered across state
    /// changes so returning to READY restores the right enablement.</summary>
    public void SetPlaytestEnabled(bool enabled)
    {
        _playtestEnabled = enabled;
        ApplyState();
    }

    /// <summary>Enables/disables BOTH panel buttons (deleted-macro
    /// state, driven by the standalone controller). Remembered across
    /// <see cref="ApplyState"/> renders: a later SetReady/SetPaused/
    /// SetPlaytestEnabled cannot re-enable the buttons while disabled.
    /// SetInteractionEnabled(true) restores the ApplyState enablement.
    /// The hide (×) control is never disabled by this.</summary>
    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        ApplyState();
    }

    /// <summary>Shows the playtest countdown overlay with the given
    /// number (3–2–1, numbers only). The overlay covers the button
    /// row + indicators; the × in the top row stays above it and
    /// clickable. While visible, the two buttons are inert: a
    /// press on the overlay is handled by
    /// <see cref="CountdownOverlay_MouseLeftButtonDown"/> (raises
    /// <see cref="CountdownCancelRequested"/>, never reaches the
    /// buttons or the drag handler), and the click handlers ignore
    /// presses as a second line of defence. Kept fully opaque for
    /// the countdown duration.</summary>
    public void ShowCountdown(int number)
    {
        CountdownNumberTextBlock.Text = number.ToString(CultureInfo.InvariantCulture);
        CountdownOverlay.Visibility = Visibility.Visible;
        _countdownVisible = true;
        WakeFade();
    }

    /// <summary>Hides the countdown overlay. Idempotent.</summary>
    public void HideCountdown()
    {
        CountdownOverlay.Visibility = Visibility.Collapsed;
        _countdownVisible = false;
    }

    private void ApplyState()
    {
        StateDot.Background = _state switch
        {
            FloatingRecordPanelLogic.PanelState.Recording => RecordingDotBrush(),
            FloatingRecordPanelLogic.PanelState.Paused => AmberBrush(),
            _ => GreyBrush(),
        };
        StateDot.CornerRadius = new CornerRadius(FloatingRecordPanelLogic.DotCornerRadiusFor(_state));
        ApplyButton(FirstButton, FirstButtonIcon,
            FloatingRecordPanelLogic.FirstButtonKindFor(_state), enabled: _interactionEnabled);
        bool secondEnabled = (_state != FloatingRecordPanelLogic.PanelState.Ready || _playtestEnabled)
            && _interactionEnabled;
        ApplyButton(SecondButton, SecondButtonIcon,
            FloatingRecordPanelLogic.SecondButtonKindFor(_state), secondEnabled);
    }

    private void ApplyButton(System.Windows.Controls.Button button, IconView icon, FloatingRecordPanelLogic.PanelButtonKind kind, bool enabled)
    {
        string gradientKey;
        string iconKey;
        switch (kind)
        {
            case FloatingRecordPanelLogic.PanelButtonKind.Record:
                gradientKey = "StopGradient"; iconKey = "record"; break;
            case FloatingRecordPanelLogic.PanelButtonKind.Playtest:
                gradientKey = "PlaytestGradient"; iconKey = "play-triangle"; break;
            case FloatingRecordPanelLogic.PanelButtonKind.Resume:
                gradientKey = "ResumeStateGradient"; iconKey = "play"; break;
            case FloatingRecordPanelLogic.PanelButtonKind.Stop:
                gradientKey = "StopGradient"; iconKey = "stop"; break;
            default: // Pause
                gradientKey = "PauseStateGradient"; iconKey = "pause"; break;
        }
        if (TryFindResource(gradientKey) is System.Windows.Media.Brush gradient)
            button.Background = gradient;
        icon.Icon = iconKey;
        button.IsEnabled = enabled;
    }

    /// <summary>True when the given virtual-screen pixel point falls
    /// inside this window's current screen bounds. PointToScreen
    /// converts the window's device-independent bounds to device
    /// pixels, so the comparison is correct at any DPI scaling. On
    /// any exception (e.g. no window handle yet) returns false.</summary>
    public bool IsPointWithin(int x, int y)
    {
        try
        {
            if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return false;
            System.Windows.Point topLeft = PointToScreen(new System.Windows.Point(0, 0));
            System.Windows.Point bottomRight = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
            return x >= topLeft.X && x <= bottomRight.X
                && y >= topLeft.Y && y <= bottomRight.Y;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>§118: clamps the window fully inside the work area
    /// of the screen containing its top-left point (taskbar
    /// excluded), for use after a drag ends. Silent: no popup, no
    /// state change, never throws. Unlike
    /// the former initial positioning (virtual screen: may
    /// straddle monitors or sit under the taskbar), this requires
    /// the WHOLE panel inside ONE screen's work area; a point
    /// outside every screen clamps into the nearest work area
    /// (F-C6: no teleport to the primary), and a degenerate work
    /// area leaves the position untouched rather than jumping the
    /// panel mid-interaction. Never
    /// touches <see cref="IsDragging"/>: callers clear it, and
    /// Window_Deactivated may also clear it mid-drag, which this
    /// method respects by not re-setting it. The screen/DPI logic
    /// shares the pure conversion + clamp in
    /// <see cref="FloatingRecordPanelLogic"/> with the host's
    /// restore clamp (same formula, same fallback ladder).</summary>
    private void ClampInsideWorkArea()
    {
        try
        {
            if (!IsLoaded) return;
            double width = ActualWidth > 0 && IsFiniteValue(ActualWidth) ? ActualWidth : Width;
            double height = ActualHeight > 0 && IsFiniteValue(ActualHeight) ? ActualHeight : Height;
            // Width/Height come from XAML; 172/130 are ONLY the
            // fallback when neither the rendered nor the declared
            // size is usable - never hardcode the live size.
            if (!IsFiniteValue(width) || width <= 0) width = 172;
            if (!IsFiniteValue(height) || height <= 0) height = 130;
            double left = Left;
            double top = Top;
            // Nothing sane to clamp: leave the position alone; the
            // host re-resolves it on the next ShowPanel.
            if (!IsFiniteValue(left) || !IsFiniteValue(top)) return;
            if (TryClampToScreenWorkArea(left, top, width, height,
                    out double clampedLeft, out double clampedTop))
            {
                Left = clampedLeft;
                Top = clampedTop;
                return;
            }
            var area = SystemParameters.WorkArea;
            if (IsFiniteValue(area.Left) && IsFiniteValue(area.Top)
                && IsFiniteValue(area.Width) && IsFiniteValue(area.Height)
                && area.Width > 0 && area.Height > 0)
            {
                (double clampedWorkLeft, double clampedWorkTop) =
                    ClampInside(area, left, top, width, height);
                Left = clampedWorkLeft;
                Top = clampedWorkTop;
            }
        }
        catch (Exception)
        {
            // Positioning must never crash the panel.
        }
    }

    /// <summary>F-C7: re-clamps this window inside the work area of
    /// the screen now containing it, after the monitor layout
    /// changed (called by the host, which guarantees the window
    /// exists and is visible - a hidden panel is never resurrected
    /// through here). Reuses <see cref="ClampInsideWorkArea"/>
    /// unchanged: silent, no popup, never throws, and a no-op
    /// when the window is not loaded yet.</summary>
    public void ReclampInsideWorkArea()
    {
        try
        {
            ClampInsideWorkArea();
        }
        catch (Exception)
        {
            // Positioning must never crash the panel.
        }
    }

    /// <summary>§118: screen-aware clamp, mirroring the host's
    /// restore clamp. Screen work areas (device pixels, from
    /// System.Windows.Forms.Screen) are converted to DIP with EACH
    /// screen's OWN effective DPI, queried per screen via
    /// <see cref="MonitorDpi"/> - F-C6: never with this window's
    /// DPI for every screen (the old formula, same pattern as
    /// <see cref="IsPointWithin"/>'s PointToScreen conversion, was
    /// wrong by up to 640 DIP on a 100% + 150% pair). The pure
    /// conversion + clamp live in
    /// <see cref="FloatingRecordPanelLogic"/> (harness-pinned);
    /// this window's transform is only the fallback scale for a
    /// screen whose DPI query fails. A point outside every screen
    /// clamps into the nearest screen's work area. Returns false
    /// when there are no usable screens or on any error. Never
    /// throws.</summary>
    private bool TryClampToScreenWorkArea(
        double left, double top, double width, double height,
        out double clampedLeft, out double clampedTop)
    {
        clampedLeft = left;
        clampedTop = top;
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens is null || screens.Length == 0) return false;
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice;
            double fallbackScaleX = 0, fallbackScaleY = 0;
            if (transform is not null
                && IsFiniteValue(transform.Value.M11) && IsFiniteValue(transform.Value.M22)
                && transform.Value.M11 > 0 && transform.Value.M22 > 0)
            {
                fallbackScaleX = transform.Value.M11;
                fallbackScaleY = transform.Value.M22;
            }
            var pixelAreas = new System.Collections.Generic.List<FloatingRecordPanelLogic.PixelWorkArea>();
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

    private static (double Left, double Top) ClampInside(
        Rect area, double left, double top, double width, double height)
    {
        if (!IsFiniteValue(area.Left) || !IsFiniteValue(area.Top)
            || !IsFiniteValue(area.Width) || !IsFiniteValue(area.Height))
            return (left, top);
        double clampedLeft = width >= area.Width
            ? area.Left
            : Math.Max(area.Left, Math.Min(left, area.Right - width));
        double clampedTop = height >= area.Height
            ? area.Top
            : Math.Max(area.Top, Math.Min(top, area.Bottom - height));
        return (clampedLeft, clampedTop);
    }

    private static bool IsFiniteValue(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    protected override void OnClosed(EventArgs e)
    {
        _clockTimer.Stop();
        _fadeTimer.Stop();
        _clockRunning = false;
        IsDragging = false;
        base.OnClosed(e);
    }

    private void RefreshClockText()
        => TimeTextBlock.Text = FloatingRecordPanelLogic.FormatElapsed(_stopwatch.Elapsed);

    private void StartClockTimerIfVisible()
    {
        if (_clockRunning && IsVisible) _clockTimer.Start();
    }

    private void OnFadeTick()
    {
        if (IsMouseOver)
        {
            // Mouse is over the window: stay fully opaque, check again
            // after another idle interval.
            RestartFadeTimer();
            return;
        }
        Opacity = FloatingRecordPanelLogic.IdleOpacity;
        _fadeTimer.Stop();
    }

    private void WakeFade()
    {
        Opacity = FloatingRecordPanelLogic.ActiveOpacity;
        RestartFadeTimer();
    }

    private void RestartFadeTimer()
    {
        _fadeTimer.Stop();
        _fadeTimer.Start();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        WakeFade();
        StartClockTimerIfVisible();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // A drag cannot outlive the window losing activation; the
        // mouse-up may never arrive here.
        IsDragging = false;
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Opacity = FloatingRecordPanelLogic.ActiveOpacity;
            RestartFadeTimer();
            StartClockTimerIfVisible();
        }
        else
        {
            _clockTimer.Stop();
            _fadeTimer.Stop();
        }
    }

    private void Panel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => WakeFade();

    private void Panel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => WakeFade();

    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WakeFade();
        if (e.ChangedButton != MouseButton.Left) return;
        // Buttons and the hide control must never drag: ignore any
        // press whose source is a Button or inside one.
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
        IsDragging = true;
        try
        {
            DragMove();
        }
        catch (Exception)
        {
            // DragMove throws when misused (e.g. no button pressed);
            // a drag failure must never crash the panel.
        }
        finally
        {
            // DragMove returns when the drag ends (mouse-up).
            IsDragging = false;
            // §118: clamp only once the drag has ended, so the panel
            // can never be left partly or wholly off-screen. Silent
            // (no popup), never throws, and runs even when DragMove
            // threw. Does not touch IsDragging beyond the line above.
            ClampInsideWorkArea();
        }
    }

    private void Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // §118: a real drag's terminating mouse-up is consumed by
        // DragMove(), which clamps in its finally block above - so
        // an Up arriving while IsDragging is still set must not
        // clamp mid-drag. This handler covers only the no-drag path
        // (e.g. DragMove threw immediately); clamping there is
        // idempotent when the panel is already inside the work area.
        if (IsDragging) return;
        IsDragging = false;
        ClampInsideWorkArea();
    }

    private void FirstButton_Click(object sender, RoutedEventArgs e)
    {
        WakeFade();
        // Inert during the countdown: the overlay covers this button,
        // and this guard is the second line of defence.
        if (_countdownVisible) return;
        if (_state == FloatingRecordPanelLogic.PanelState.Ready)
            RecordRequested?.Invoke(this, EventArgs.Empty);
        else
            PauseToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SecondButton_Click(object sender, RoutedEventArgs e)
    {
        WakeFade();
        if (_countdownVisible) return;
        if (_state == FloatingRecordPanelLogic.PanelState.Ready)
            PlaytestRequested?.Invoke(this, EventArgs.Empty);
        else
            StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        WakeFade();
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>§120 F-B1: a click on the countdown overlay cancels
    /// the countdown to idle. Handled here so the press does not
    /// bubble to the root drag surface (a cancel click must not
    /// start a drag); the overlay still covers the buttons, and the
    /// button guards below stay untouched.</summary>
    private void CountdownOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WakeFade();
        if (!_countdownVisible) return;
        e.Handled = true;
        CountdownCancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        try
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is System.Windows.Controls.Button) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
        catch (Exception)
        {
            // Unknown ancestry: do not drag rather than risk dragging
            // from a button press.
            return true;
        }
    }

    private static SolidColorBrush GreyBrush()
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x93, 0xA5));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush AmberBrush()
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xA6, 0x23));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush RecordingDotBrush()
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x30));
        brush.Freeze();
        return brush;
    }
}
