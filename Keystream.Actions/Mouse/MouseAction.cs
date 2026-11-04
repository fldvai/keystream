using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Keystream.Actions.Common;
using Keystream.Core.Actions;
using Keystream.Core.Localization;

namespace Keystream.Actions.Mouse;

/// <summary>Replays one recorded mouse step (§107): move, button down,
/// button up, click, or vertical scroll. Created by mouse recording,
/// never from the card editor - a mouse step only makes sense inside a
/// macro's event list (the KeyDown/KeyUp precedent: record-only
/// actions resolve in AppHost but have no ActionCatalog entry).
///
/// Params (exact keys): "kind" = move/down/up/click/scroll;
/// "button" = left/right/middle (only down/up/click read it);
/// "x","y" ints (absolute target, recorded coordinates);
/// "delta" int (scroll: wheel clicks, ×120 at send time);
/// fingerprint "vx","vy","vw","vh","dpi" ints (the virtual screen the
/// point was recorded on); "mode" = absolute/relative; relative fields
/// "rx","ry" ints plus "wtitle","wclass","wproc" strings.
///
/// Execution: every step moves to its target first, then sends the
/// whole step in ONE SendMouse batch (move only; down = move+down;
/// up = move+up; click = move+down+up; scroll = move+wheel).
/// A down step holds the button until a matching up step, exactly
/// like keydown/keyup. The matching release is guarded at the
/// macro level: MacroAction releases any button still held when
/// playback ends, fails, or is cancelled, so an unbalanced
/// recording cannot leave a button held after the macro. The
/// best-effort release inside this step (below) covers only a
/// partial batch within the single step itself - it is kept
/// alongside the macro-level guard, not replaced by it.
///
/// Relative mode: the target window is looked up with EnumWindows
/// (visible, non-minimized top-level windows only, own process
/// excluded), preferring an exact (ordinal) title match, then a
/// class + process-name match; the target is the window-rect origin
/// plus (rx, ry). Known limits: the rect is the outer window frame
/// (GetWindowRect), so the recorder must capture offsets against
/// that same origin; a renamed window, a window whose class/process
/// changed, or a window that no longer exists is a lookup MISS, and
/// a miss falls through to the absolute path - it never fails the
/// step on its own. Only a miss with no usable absolute coordinates
/// fails, honestly (S1581).
///
/// The point is NEVER clicked blindly: the scaled target must lie
/// inside the current virtual screen (or the recorded bounds when
/// the live capture failed, or the found window's rect when neither
/// is available). A target outside is an honest Fail (S1573) naming
/// the target and both bounds. TestAsync sends nothing: it is
/// validate-only, like KeyDownAction - a bare down-test must not
/// leave a button held with no matching release.</summary>
public sealed class MouseAction : IAction
{
    private const uint AbsoluteMove = SendInputNative.MOUSEEVENTF_MOVE
        | SendInputNative.MOUSEEVENTF_ABSOLUTE | SendInputNative.MOUSEEVENTF_VIRTUALDESK;

    public ActionDescriptor Descriptor => new("mouse", Localizer.Get("S1571"), Localizer.Get("S1071"),
        Localizer.Get("S1572"));

    public ValidationResult Validate(JsonObject parameters)
    {
        var errors = new List<string>();
        string kind = Normalized(parameters, "kind");
        if (kind is not ("move" or "down" or "up" or "click" or "scroll"))
            errors.Add(Localizer.Format("S1575", ParamReader.GetString(parameters, "kind") ?? ""));
        if (kind is "down" or "up" or "click")
        {
            string button = Normalized(parameters, "button");
            if (button is not ("left" or "right" or "middle"))
                errors.Add(Localizer.Format("S1576", ParamReader.GetString(parameters, "button") ?? ""));
        }
        string mode = Normalized(parameters, "mode");
        if (mode is not ("" or "absolute" or "relative"))
            errors.Add(Localizer.Format("S1580", ParamReader.GetString(parameters, "mode") ?? ""));
        if (mode == "relative")
        {
            if (!TryGetInt(parameters, out _, "rx"))
                errors.Add(Localizer.Format("S1577", "rx"));
            if (!TryGetInt(parameters, out _, "ry"))
                errors.Add(Localizer.Format("S1577", "ry"));
        }
        else
        {
            if (!TryGetInt(parameters, out _, "x"))
                errors.Add(Localizer.Format("S1577", "x"));
            if (!TryGetInt(parameters, out _, "y"))
                errors.Add(Localizer.Format("S1577", "y"));
        }
        if (kind == "scroll")
        {
            int delta = TryGetInt(parameters, out int d, "delta") ? d : 0;
            if (delta == 0)
                errors.Add(Localizer.Get("S1578"));
        }
        bool badFingerprint = (TryGetInt(parameters, out int vw, "vw") && vw <= 0)
            || (TryGetInt(parameters, out int vh, "vh") && vh <= 0);
        if (badFingerprint)
            errors.Add(Localizer.Get("S1579"));
        return errors.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(errors.ToArray());
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        try
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var vr = Validate(parameters);
            if (!vr.IsValid)
                return Task.FromResult(ActionResult.Fail(string.Join("; ", vr.Errors)));

            string kind = Normalized(parameters, "kind");
            string button = Normalized(parameters, "button");
            string mode = Normalized(parameters, "mode");
            int delta = TryGetInt(parameters, out int d, "delta") ? d : 0;

            bool captured = MouseCoordinates.TryCaptureCurrent(out var current);
            VirtualScreenSnapshot? recorded = BuildRecorded(parameters);

            // Phase 1: resolve the raw target and describe its source.
            int rawX, rawY;
            string sourceText;
            VirtualScreenSnapshot? windowRect = null;
            bool scaled;
            if (mode == "relative")
            {
                int rx = GetInt(parameters, "rx");
                int ry = GetInt(parameters, "ry");
                var found = FindWindow(parameters);
                if (found is not null)
                {
                    rawX = ClampToInt((long)found.Rect.Left + rx);
                    rawY = ClampToInt((long)found.Rect.Top + ry);
                    windowRect = new VirtualScreenSnapshot(
                        found.Rect.Left, found.Rect.Top,
                        found.Rect.Right - found.Rect.Left,
                        found.Rect.Bottom - found.Rect.Top, 96);
                    string label = found.Title;
                    if (string.IsNullOrEmpty(label)) label = found.ClassName;
                    if (string.IsNullOrEmpty(label)) label = found.ProcessName;
                    if (string.IsNullOrEmpty(label)) label = "?";
                    sourceText = $"\"{label}\" [{windowRect.Left},{windowRect.Top} {windowRect.Width}x{windowRect.Height}] + ({rx},{ry})";
                    scaled = false;
                }
                else if (TryGetInt(parameters, out rawX, "x") && TryGetInt(parameters, out rawY, "y"))
                {
                    // Lookup miss: fall through to the absolute path -
                    // never fail on the miss alone.
                    sourceText = DescribeBounds(recorded, captured ? current : null);
                    scaled = true;
                }
                else
                {
                    return Task.FromResult(ActionResult.Fail(Localizer.Get("S1581")));
                }
            }
            else
            {
                rawX = GetInt(parameters, "x");
                rawY = GetInt(parameters, "y");
                sourceText = DescribeBounds(recorded, captured ? current : null);
                scaled = true;
            }

            // Phase 2: scale the recorded point onto the current screen
            // (identity when the capture failed and the recorded bounds
            // are the basis - the documented fallback).
            int targetX = rawX, targetY = rawY;
            if (scaled)
            {
                VirtualScreenSnapshot? basis = captured ? current : recorded;
                if (basis is null)
                    return Task.FromResult(ActionResult.Fail(
                        Localizer.Format("S1573", rawX, rawY, sourceText, "?")));
                (targetX, targetY) = MouseCoordinates.ScalePoint(
                    rawX, rawY, recorded ?? basis, basis);
            }

            // Phase 3: the point must be inside known bounds, or no
            // click is ever sent.
            VirtualScreenSnapshot? checkBounds = captured ? current : (recorded ?? windowRect);
            if (checkBounds is null)
                return Task.FromResult(ActionResult.Fail(
                    Localizer.Format("S1573", targetX, targetY, sourceText, "?")));
            if (!MouseCoordinates.IsInside(targetX, targetY, checkBounds))
                return Task.FromResult(ActionResult.Fail(
                    Localizer.Format("S1573", targetX, targetY, sourceText, DescribeBounds(checkBounds, null))));

            // Phase 4: map to SendInput absolute coordinates. Without
            // screen extents (no capture, no fingerprint) the point
            // cannot be mapped, so it is refused rather than clicked
            // blindly - reachable only for a relative hit on a machine
            // where the metrics read failed.
            VirtualScreenSnapshot? normalizeBasis = captured ? current : recorded;
            if (normalizeBasis is null)
                return Task.FromResult(ActionResult.Fail(
                    Localizer.Format("S1573", targetX, targetY, sourceText, DescribeBounds(checkBounds, null))));
            var (dx, dy) = MouseCoordinates.ToAbsolute(targetX, targetY, normalizeBasis);

            var strokes = BuildStrokes(kind, button, dx, dy, delta);
            var (sent, errno) = SendInputNative.SendMouse(strokes);
            if (sent == (uint)strokes.Count)
                return Task.FromResult(ActionResult.Ok());
            if (kind is "down" or "up" or "click")
            {
                // A partial send may have landed the button-down
                // without its release (§45 pattern from HotkeyAction):
                // one best-effort release. Its result is ignored -
                // the original failure is what gets reported.
                SendInputNative.SendMouse(new[]
                {
                    new SendInputNative.MouseStroke(dx, dy, AbsoluteMove | UpFlag(button), 0),
                });
            }
            return Task.FromResult(ActionResult.Fail(
                Localizer.Format("S1574", sent, strokes.Count, errno)));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // An unexpected failure must surface as an honest Fail
            // with its cause, never crash the worker.
            return Task.FromResult(ActionResult.Fail(
                Localizer.Format("S1582", ex.GetType().Name, ex.Message)));
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
    {
        var vr = Validate(parameters);
        return Task.FromResult(vr.IsValid
            ? ActionResult.Ok()
            : ActionResult.Fail(string.Join("; ", vr.Errors)));
    }

    private static string Normalized(JsonObject parameters, string name)
        => ParamReader.GetString(parameters, name)?.Trim().ToLowerInvariant() ?? "";

    private static int GetInt(JsonObject parameters, string name)
        => TryGetInt(parameters, out int value, name) ? value : 0;

    /// <summary>Tolerant int read (ParamReader has no int reader):
    /// accepts int, long, and integral double backing values, plus a
    /// string that parses as an int. A present-but-unreadable value
    /// is not an int - it returns false instead of throwing.</summary>
    private static bool TryGetInt(JsonObject parameters, out int value, params string[] names)
    {
        value = 0;
        foreach (var name in names)
        {
            foreach (var kv in parameters)
            {
                if (!kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (kv.Value is not JsonValue json)
                    return false;
                try
                {
                    if (json.TryGetValue<int>(out int i)) { value = i; return true; }
                    if (json.TryGetValue<long>(out long l) && l >= int.MinValue && l <= int.MaxValue)
                    {
                        value = (int)l;
                        return true;
                    }
                    if (json.TryGetValue<double>(out double dbl) && dbl == Math.Truncate(dbl)
                        && dbl >= int.MinValue && dbl <= int.MaxValue)
                    {
                        value = (int)dbl;
                        return true;
                    }
                    if (json.TryGetValue<string>(out string? s) && int.TryParse(s, out int parsed))
                    {
                        value = parsed;
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
                return false;
            }
        }
        return false;
    }

    /// <summary>The recorded snapshot, when all four fingerprint
    /// fields are present ints with a usable size. Anything less is
    /// "no fingerprint": the caller treats the current bounds as the
    /// recorded bounds (scale 1:1). A non-positive size is unusable
    /// as a scale denominator and is likewise no fingerprint here;
    /// Validate already rejects it.</summary>
    private static VirtualScreenSnapshot? BuildRecorded(JsonObject parameters)
    {
        if (!TryGetInt(parameters, out int vx, "vx")
            || !TryGetInt(parameters, out int vy, "vy")
            || !TryGetInt(parameters, out int vw, "vw")
            || !TryGetInt(parameters, out int vh, "vh")
            || vw <= 0 || vh <= 0)
            return null;
        int dpi = TryGetInt(parameters, out int d, "dpi") && d > 0 ? d : 96;
        return new VirtualScreenSnapshot(vx, vy, vw, vh, dpi);
    }

    private static string DescribeBounds(VirtualScreenSnapshot? snapshot, VirtualScreenSnapshot? fallback)
    {
        var s = snapshot ?? fallback;
        return s is null ? "?" : $"[{s.Left},{s.Top} {s.Width}x{s.Height}]";
    }

    private static int ClampToInt(long value)
        => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private static uint DownFlag(string button) => button switch
    {
        "right" => SendInputNative.MOUSEEVENTF_RIGHTDOWN,
        "middle" => SendInputNative.MOUSEEVENTF_MIDDLEDOWN,
        _ => SendInputNative.MOUSEEVENTF_LEFTDOWN,
    };

    private static uint UpFlag(string button) => button switch
    {
        "right" => SendInputNative.MOUSEEVENTF_RIGHTUP,
        "middle" => SendInputNative.MOUSEEVENTF_MIDDLEUP,
        _ => SendInputNative.MOUSEEVENTF_LEFTUP,
    };

    /// <summary>One batch per step: every step moves first (the
    /// position is re-asserted on every stroke), then the kind's own
    /// stroke(s). Wheel data is delta × WHEEL_DELTA (120), clamped to
    /// int range so a huge recorded delta cannot wrap.</summary>
    private static List<SendInputNative.MouseStroke> BuildStrokes(
        string kind, string button, int dx, int dy, int delta)
    {
        var strokes = new List<SendInputNative.MouseStroke>
        {
            new(dx, dy, AbsoluteMove, 0),
        };
        switch (kind)
        {
            case "down":
                strokes.Add(new(dx, dy, AbsoluteMove | DownFlag(button), 0));
                break;
            case "up":
                strokes.Add(new(dx, dy, AbsoluteMove | UpFlag(button), 0));
                break;
            case "click":
                strokes.Add(new(dx, dy, AbsoluteMove | DownFlag(button), 0));
                strokes.Add(new(dx, dy, AbsoluteMove | UpFlag(button), 0));
                break;
            case "scroll":
                int wheel = (int)Math.Clamp((long)delta * 120, int.MinValue, int.MaxValue);
                strokes.Add(new(dx, dy, AbsoluteMove | SendInputNative.MOUSEEVENTF_WHEEL, wheel));
                break;
        }
        return strokes;
    }

    private sealed record WindowHit(Rect Rect, string Title, string ClassName, string ProcessName);

    /// <summary>Best-effort window lookup for relative mode. Returns
    /// null when no identity was supplied, when EnumWindows itself
    /// fails, or when no window matches - the caller falls through
    /// to the absolute path in all three cases. A window that
    /// vanishes mid-scan is skipped, never fatal.</summary>
    private static WindowHit? FindWindow(JsonObject parameters)
    {
        string? wtitle = ParamReader.GetString(parameters, "wtitle");
        string? wclass = ParamReader.GetString(parameters, "wclass");
        string? wproc = ParamReader.GetString(parameters, "wproc");
        bool hasTitle = !string.IsNullOrWhiteSpace(wtitle);
        bool hasClass = !string.IsNullOrWhiteSpace(wclass);
        bool hasProc = !string.IsNullOrWhiteSpace(wproc);
        if (!hasTitle && !hasClass && !hasProc)
            return null;
        int ownPid = -1;
        try { ownPid = Process.GetCurrentProcess().Id; }
        catch { ownPid = -1; }
        var candidates = new List<WindowHit>();
        try
        {
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                    return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0 || (int)pid == ownPid)
                    return true;
                if (!GetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    return true;
                string proc;
                try { proc = Process.GetProcessById((int)pid).ProcessName; }
                catch
                {
                    // Process vanished mid-scan: skip this window and
                    // keep enumerating the rest.
                    return true;
                }
                candidates.Add(new WindowHit(rect, WindowTitle(hwnd), WindowClass(hwnd), proc));
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // EnumWindows itself failed (not Windows, or user32.dll
            // unreadable): no window can be found, so the caller
            // falls through to the absolute path.
            return null;
        }
        if (hasTitle)
        {
            foreach (var c in candidates)
            {
                if (string.Equals(c.Title, wtitle, StringComparison.Ordinal))
                    return c;
            }
        }
        if (hasClass || hasProc)
        {
            foreach (var c in candidates)
            {
                bool classOk = !hasClass
                    || string.Equals(c.ClassName, wclass, StringComparison.OrdinalIgnoreCase);
                bool procOk = !hasProc
                    || string.Equals(NormalizeProcessName(c.ProcessName),
                        NormalizeProcessName(wproc), StringComparison.OrdinalIgnoreCase);
                if (classOk && procOk)
                    return c;
            }
        }
        return null;
    }

    private static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        string file = name.Replace('/', '\\');
        int slash = file.LastIndexOf('\\');
        if (slash >= 0)
            file = file[(slash + 1)..];
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            file = file[..^4];
        return file;
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0)
            return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private static string WindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
