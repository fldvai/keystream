using System;

namespace Keystream.App.Views;

/// <summary>
/// §111 - pure decision logic for the floating record panel,
/// deliberately WPF-FREE: only <c>using System;</c>, zero
/// System.Windows tokens (including in comments), so this file can
/// be linked directly into the plain net8.0 wb-harness - the same
/// discipline as TextBoxRevertLogic.cs. The WPF half (window,
/// timers, drag) lives in FloatingRecordPanel.xaml(.cs) and calls
/// into here; do not add WPF types, dependency-object parameters,
/// or resource lookups to this file.
/// §112 adds the READY state (grey dot, Record/Playtest buttons),
/// the permanent app-blue border constant, and the playtest
/// countdown state machine - all still decided here, rendered
/// only by the WPF half.
/// </summary>
public static class FloatingRecordPanelLogic
{
    /// <summary>Idle time before the panel fades, in seconds.</summary>
    public const int IdleFadeSeconds = 3;

    /// <summary>Window opacity while idle (faded but still readable).
    /// Never lower: the panel must stay visible at all times.</summary>
    public const double IdleOpacity = 0.6;

    /// <summary>Window opacity while active (mouse over / touched).</summary>
    public const double ActiveOpacity = 1.0;

    /// <summary>Formats a step count for the panel's count readout.
    /// Rule: negative counts clamp to "0"; counts below 100000 render
    /// as a plain invariant decimal ("14", "99999"); counts of 100000
    /// or more render as whole thousands with a "K" suffix, truncated
    /// (never rounded) - 123456 becomes "123K", 999999 becomes "999K".
    /// The caller shows the exact count in the tooltip; this string is
    /// display-only so the indicator row never widens.</summary>
    public static string FormatStepCount(int count)
    {
        if (count < 0) return "0";
        if (count < 100000)
            return count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (count / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "K";
    }

    /// <summary>Formats an elapsed time as "mm:ss" with minutes
    /// unbounded: 61 seconds becomes "01:01", 3600 seconds becomes
    /// "60:00". Fractional seconds are truncated (floor), and negative
    /// spans clamp to "00:00".</summary>
    public static string FormatElapsed(TimeSpan t)
    {
        long totalSeconds = (long)Math.Floor(t.TotalSeconds);
        if (totalSeconds < 0) totalSeconds = 0;
        long minutes = totalSeconds / 60;
        long seconds = totalSeconds % 60;
        string minuteText = minutes
            .ToString(System.Globalization.CultureInfo.InvariantCulture)
            .PadLeft(2, '0');
        string secondText = seconds
            .ToString(System.Globalization.CultureInfo.InvariantCulture)
            .PadLeft(2, '0');
        return minuteText + ":" + secondText;
    }

    // --- §112: panel state, button roles, countdown -------------------

    /// <summary>Panel border colour, in every state (§112, mockup v5).
    /// State is carried ONLY by the dot + button colours; the border
    /// never swaps to red/amber. Hex string so the XAML default and
    /// this constant cannot drift silently - the code-behind never
    /// sets the border brush at all.</summary>
    public const string PanelBorderHex = "#1E86E0";

    /// <summary>Panel states. Ready = idle: showable via the modal
    /// toggle before any recording and after Stop; the clock is
    /// stopped at 00:00 and the count is the working step count.</summary>
    public enum PanelState
    {
        Ready,
        Recording,
        Paused,
    }

    /// <summary>Role of one of the panel's two icon-only buttons. The
    /// WPF half maps a role to a gradient + icon; the mapping of
    /// state → roles lives here.</summary>
    public enum PanelButtonKind
    {
        Record,
        Playtest,
        Pause,
        Resume,
        Stop,
    }

    /// <summary>State-dot colour (hex): grey = ready, red = recording,
    /// amber = paused. The dot is the ONLY state carrier besides the
    /// buttons (§112).</summary>
    public const string ReadyDotHex = "#8A93A5";

    /// <summary>State-dot colour while recording.</summary>
    public const string RecordingDotHex = "#FF3B30";

    /// <summary>State-dot colour while paused.</summary>
    public const string PausedDotHex = "#F5A623";

    /// <summary>Dot colour for a state.</summary>
    public static string DotHexFor(PanelState state) => state switch
    {
        PanelState.Recording => RecordingDotHex,
        PanelState.Paused => PausedDotHex,
        _ => ReadyDotHex,
    };

    /// <summary>Dot corner radius: square (2) only while paused, round
    /// (5) in Ready and Recording - the §111 paused-square rule,
    /// extended with the Ready round dot.</summary>
    public static int DotCornerRadiusFor(PanelState state)
        => state == PanelState.Paused ? 2 : 5;

    /// <summary>First (left) button role for a state: Record in Ready,
    /// Pause while recording, Resume while paused.</summary>
    public static PanelButtonKind FirstButtonKindFor(PanelState state) => state switch
    {
        PanelState.Recording => PanelButtonKind.Pause,
        PanelState.Paused => PanelButtonKind.Resume,
        _ => PanelButtonKind.Record,
    };

    /// <summary>Second (right) button role for a state: Playtest in
    /// Ready, Stop while recording or paused.</summary>
    public static PanelButtonKind SecondButtonKindFor(PanelState state)
        => state == PanelState.Ready ? PanelButtonKind.Playtest : PanelButtonKind.Stop;

    /// <summary>Playtest is available ONLY in Ready with at least one
    /// working step. Zero steps = honestly disabled, no message;
    /// recording/paused states never offer it (those show Stop).</summary>
    public static bool IsPlaytestEnabled(PanelState state, int stepCount)
        => state == PanelState.Ready && stepCount > 0;

    /// <summary>Countdown may start under exactly the playtest-enabled
    /// rule. Separate name so the modal-side guard reads as intent,
    /// not as a button-state reuse.</summary>
    public static bool CanStartCountdown(PanelState state, int stepCount)
        => IsPlaytestEnabled(state, stepCount);

    /// <summary>§112 playtest countdown state machine (3 → 2 → 1 →
    /// fire). Pure tick logic: the modal owns the 1-second timer and
    /// the panel owns the number rendering; this class decides only
    /// the sequence. Start shows 3; each Tick decrements and returns
    /// false with the number to show, until the tick that reaches 0
    /// returns true (fire playback). Cancel at any tick - including
    /// before the first - leaves IsRunning false and Tick returns
    /// false forever: no fire after a cancel, in any path. Start
    /// while running restarts at 3.</summary>
    public sealed class PlaytestCountdown
    {
        /// <summary>First number shown (mockup §4: 3–2–1).</summary>
        public const int StartValue = 3;

        /// <summary>Number currently displayed (0 when idle/fired).</summary>
        public int Remaining { get; private set; }

        /// <summary>True between Start and fire/cancel.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Begins (or restarts) the countdown at 3.</summary>
        public void Start()
        {
            Remaining = StartValue;
            IsRunning = true;
        }

        /// <summary>One 1-second tick. Returns true only on the firing
        /// tick (display is then 0, meaning "fire, show nothing");
        /// otherwise returns false and display holds the next number
        /// to show. Idle/cancelled countdowns never fire.</summary>
        public bool Tick(out int display)
        {
            display = 0;
            if (!IsRunning) return false;
            Remaining--;
            if (Remaining <= 0)
            {
                IsRunning = false;
                Remaining = 0;
                return true;
            }
            display = Remaining;
            return false;
        }

        /// <summary>Cancels the countdown: no later tick fires.
        /// Idempotent.</summary>
        public void Cancel()
        {
            IsRunning = false;
            Remaining = 0;
        }
    }

    // --- §113: the 3-second delay rule (one table, three callers) ---

    /// <summary>§113 delay rule: recording from the floating panel
    /// ALWAYS counts down 3 seconds first - the panel has no
    /// setting of its own, and the shared checkbox never waives
    /// the record countdown. The <see cref="PlaytestCountdown"/>
    /// machine above is reused for record countdowns unchanged
    /// (a generic 3–2–1 machine despite the name): Start shows 3,
    /// ticks decrement, the firing tick fires, Cancel never fires.</summary>
    public static bool ShouldCountdownForPanelRecord() => true;

    /// <summary>§113 delay rule: playing from the floating panel
    /// counts down iff the shared Delay3s checkbox (owned by the
    /// macro modal, read from <c>FloatPanelSession</c>) is ON;
    /// OFF means instant playback, exactly like the modal.</summary>
    public static bool ShouldCountdownForPlay(bool delay3s) => delay3s;

    /// <summary>§113 delay rule: Record / Play from the macro modal
    /// itself follow the same shared checkbox - ON counts down,
    /// OFF is instant. Same predicate as
    /// <see cref="ShouldCountdownForPlay"/>, kept as a separate
    /// name so the modal-side call site reads as intent.</summary>
    public static bool ShouldCountdownForModalAction(bool delay3s) => delay3s;

    // --- F-C6: per-monitor DIP geometry (pure, no window types) ---
    //
    // A screen's work area arrives in device pixels; the panel's
    // position lives in device-independent pixels (DIP, 1/96 inch).
    // Converting a screen's pixel rect with a SINGLE scale (the
    // panel's own) is wrong whenever monitors disagree: on a
    // 1920px @100% primary + 1920px @150% secondary, the secondary
    // spans DIP [1920, 3200), not [1920, 3840) - the old formula
    // let Left 3300 pass unclamped, 100 DIP past the real edge,
    // and Left 3200-3668 sit fully invisible. Each screen is
    // therefore converted with ITS OWN effective DPI (queried per
    // screen by the caller), and DIP origins tile: a screen's DIP
    // left edge is the DIP right edge of the screen(s) tiled to
    // its left, so the common side-by-side / stacked layouts are
    // exact and staggered layouts degrade to the nearest tiling.
    // No process-wide DPI-awareness change is involved: these are
    // pure conversions of rectangles the caller already measured.

    /// <summary>One screen's work area in device pixels, plus that
    /// screen's own effective DPI. Produced by the caller from the
    /// OS screen list (per-screen DPI query); this class never
    /// touches OS APIs.</summary>
    public readonly struct PixelWorkArea
    {
        public PixelWorkArea(double left, double top, double width, double height, double dpiX, double dpiY)
        {
            Left = left; Top = top; Width = width; Height = height; DpiX = dpiX; DpiY = dpiY;
        }

        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
        public double DpiX { get; }
        public double DpiY { get; }
    }

    /// <summary>One screen's work area in DIP, after per-monitor
    /// conversion. <see cref="Contains"/> tests the panel's
    /// top-left point, the same rule the old clamps used.</summary>
    public readonly struct DipWorkArea
    {
        public DipWorkArea(double left, double top, double width, double height)
        {
            Left = left; Top = top; Width = width; Height = height;
        }

        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right => Left + Width;
        public double Bottom => Top + Height;

        public bool Contains(double x, double y)
            => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    /// <summary>Converts every screen's pixel work area to DIP with
    /// that screen's own DPI, tiling DIP origins left-to-right /
    /// top-to-bottom (see the section comment). Degenerate screens
    /// (non-positive size, non-positive or non-finite DPI) are
    /// skipped. Screens are converted left-to-right, top-to-bottom
    /// regardless of input order, so a tiled neighbour is always
    /// converted before the screen that tiles after it; output
    /// follows that same order (DIP areas from a clean tiling
    /// never overlap, so containment order is immaterial). The
    /// whole tiling is anchored so the virtual pixel origin maps
    /// through the primary monitor's scale (the screen containing
    /// pixel (0, 0)); a lone row starting at pixel 0 is therefore
    /// anchored at DIP 0, and a monitor left of the primary tiles
    /// into negative DIP, matching the window coordinate space.
    /// Gaps between monitors collapse (the tiling is continuous).
    /// Never throws; an empty or unusable list yields an empty
    /// array.</summary>
    public static DipWorkArea[] ComputeDipWorkAreas(IReadOnlyList<PixelWorkArea> screens)
    {
        var result = new List<DipWorkArea>();
        if (screens is null) return result.ToArray();
        var usable = new List<PixelWorkArea>();
        foreach (var screen in screens)
        {
            if (!IsFinite(screen.Left) || !IsFinite(screen.Top)
                || !IsFinite(screen.Width) || !IsFinite(screen.Height)
                || !IsFinite(screen.DpiX) || !IsFinite(screen.DpiY)
                || screen.Width <= 0 || screen.Height <= 0
                || screen.DpiX <= 0 || screen.DpiY <= 0)
                continue;
            usable.Add(screen);
        }
        if (usable.Count == 0) return result.ToArray();
        usable.Sort((a, b) =>
        {
            int byLeft = a.Left.CompareTo(b.Left);
            return byLeft != 0 ? byLeft : a.Top.CompareTo(b.Top);
        });
        // Anchor: the primary monitor (the one containing pixel
        // (0, 0)) sets the scale the virtual pixel origin maps
        // through. Fall back to the first screen when no screen
        // contains the origin (should not happen on a real
        // desktop, but the conversion must still not throw).
        double anchorDpiX = usable[0].DpiX;
        double anchorDpiY = usable[0].DpiY;
        foreach (var screen in usable)
        {
            if (screen.Left <= 0 && 0 < screen.Left + screen.Width
                && screen.Top <= 0 && 0 < screen.Top + screen.Height)
            {
                anchorDpiX = screen.DpiX;
                anchorDpiY = screen.DpiY;
                break;
            }
        }
        double minPixelLeft = usable[0].Left;
        double minPixelTop = usable[0].Top;
        foreach (var screen in usable)
        {
            if (screen.Left < minPixelLeft) minPixelLeft = screen.Left;
            if (screen.Top < minPixelTop) minPixelTop = screen.Top;
        }
        double anchorX = minPixelLeft * 96.0 / anchorDpiX;
        double anchorY = minPixelTop * 96.0 / anchorDpiY;
        var converted = new List<(double PixelRight, double PixelBottom, double DipRight, double DipBottom)>();
        foreach (var screen in usable)
        {
            double dipWidth = screen.Width * 96.0 / screen.DpiX;
            double dipHeight = screen.Height * 96.0 / screen.DpiY;
            // DIP origin: the furthest DIP edge of any already
            // converted screen tiled fully to the left / above,
            // in pre-shift coordinates (the anchor shift is
            // applied once, at construction below).
            double dipLeft = 0;
            double dipTop = 0;
            foreach (var prior in converted)
            {
                if (prior.PixelRight <= screen.Left)
                    dipLeft = Math.Max(dipLeft, prior.DipRight);
                if (prior.PixelBottom <= screen.Top)
                    dipTop = Math.Max(dipTop, prior.DipBottom);
            }
            var area = new DipWorkArea(dipLeft + anchorX, dipTop + anchorY, dipWidth, dipHeight);
            converted.Add((screen.Left + screen.Width, screen.Top + screen.Height, dipLeft + dipWidth, dipTop + dipHeight));
            result.Add(area);
        }
        return result.ToArray();
    }

    /// <summary>Fully-inside clamp, one axis at a time: area start
    /// &lt;= value &lt;= area end - size. When the panel is as large
    /// as (or larger than) the area, pin to the area's start edge
    /// instead of producing a below-min value. A degenerate area
    /// returns the input unchanged. Mirrors the two window-side
    /// ClampInside duplicates, so the harness pins the shared
    /// rule here.</summary>
    public static (double Left, double Top) ClampInsideDipArea(
        DipWorkArea area, double left, double top, double width, double height)
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

    /// <summary>Clamps a DIP position fully inside the DIP work area
    /// containing its top-left point; when the point is outside
    /// EVERY area (dragged past an edge, or its monitor unplugged),
    /// clamps into the NEAREST area instead of teleporting the
    /// panel to the primary screen - the old fallback's jump of
    /// 1600+ DIP for a point just past a secondary's edge. Returns
    /// false (outs left at the input) only when there are no
    /// usable areas, when the inputs are non-finite, or when the
    /// panel size is unusable - the caller then falls back to the
    /// primary work area, exactly as the old clamps did. Never
    /// throws.</summary>
    public static bool TryClampToDipAreas(
        double left, double top, double width, double height,
        IReadOnlyList<DipWorkArea> areas,
        out double clampedLeft, out double clampedTop)
    {
        clampedLeft = left;
        clampedTop = top;
        try
        {
            if (!IsFinite(left) || !IsFinite(top)
                || !IsFinite(width) || !IsFinite(height)
                || width <= 0 || height <= 0
                || areas is null || areas.Count == 0)
                return false;
            foreach (var area in areas)
            {
                if (area.Contains(left, top))
                {
                    (clampedLeft, clampedTop) = ClampInsideDipArea(area, left, top, width, height);
                    return true;
                }
            }
            // Outside every area: nearest area wins (rect distance,
            // squared; ties keep the earliest area). A point just
            // past a screen's edge stays on that screen instead of
            // jumping to the primary.
            DipWorkArea nearest = areas[0];
            double nearestDistance = double.MaxValue;
            foreach (var area in areas)
            {
                double dx = left < area.Left ? area.Left - left
                    : left > area.Right ? left - area.Right : 0;
                double dy = top < area.Top ? area.Top - top
                    : top > area.Bottom ? top - area.Bottom : 0;
                double distance = dx * dx + dy * dy;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = area;
                }
            }
            (clampedLeft, clampedTop) = ClampInsideDipArea(nearest, left, top, width, height);
            return true;
        }
        catch
        {
            clampedLeft = left;
            clampedTop = top;
            return false;
        }
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
