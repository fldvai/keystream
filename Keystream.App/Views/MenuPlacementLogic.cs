using System;
using System.Collections.Generic;

namespace Keystream.App.Views;

/// <summary>
/// §133 - pure menu-placement decision logic, deliberately WPF-FREE:
/// only <c>using System;</c> / <c>using System.Collections.Generic;</c>,
/// zero System.Windows tokens (including in comments), so this file
/// can be linked directly into the plain net8.0 wb-harness - the same
/// discipline as FloatingRecordPanelLogic.cs (which lives in this
/// same folder, hence this file's location: co-located WPF-free
/// decision logic, rendered only by the WPF glue in
/// MainWindow.xaml.cs). Do not add WPF types, dependency-object
/// parameters, or resource lookups to this file.
///
/// UNITS - every coordinate, size, and rect in this file is in DIP
/// (device-independent pixels, 1/96 inch) in SCREEN coordinates, the
/// same space <c>Window.PointToScreen</c> returns. The caller (WPF
/// glue) is responsible for converting anything that arrives in
/// device pixels into DIP BEFORE calling in: the established
/// conversion is the per-monitor one in
/// FloatingRecordPanelLogic.ComputeDipWorkAreas (each screen's pixel
/// work area converted with THAT screen's own effective DPI, queried
/// via MonitorDpi) - never a single process-wide scale. This file
/// never sees a pixel value and never converts one.
///
/// BOUND - a menu must stay inside the INTERSECTION of the main
/// window rect and the work area of the monitor the cursor is on
/// (both DIP, screen coordinates). The screenshot diagnosis of
/// 28 Okt 2026 (§132 menu overflowing BELOW the MainWindow onto the
/// desktop) is exactly what clamping to the screen work area alone
/// cannot fix: the window rect is the binding bound, the work area
/// only the outer bound. When the intersection is degenerate
/// (empty - stale work-area data), the window rect alone is used:
/// the window is on screen by definition while the user clicks in
/// it, so its rect is the trustworthy bound.
///
/// FALLBACK - any invalid input (zero/negative size, NaN/Infinity in
/// ANY input, a degenerate window rect, an effective area too small
/// to hold the 8-DIP margin on both sides) returns a placement with
/// <see cref="MenuPlacement.IsFallback"/> set, at the legacy anchor
/// (the cursor for a main menu, the parent item's right/top edge for
/// a submenu), WITHOUT throwing. The caller then uses the legacy
/// placement (MousePoint / Placement="Right") and applies no cap.
/// Never throws, never shows a popup: a placement failure must
/// degrade to the old behaviour, never to a crash or a message.
/// </summary>
public static class MenuPlacementLogic
{
    /// <summary>Margin, in DIP, kept clear inside the effective
    /// (window ∩ work area) rect on every side.</summary>
    public const double EdgeMargin = 8;

    /// <summary>Horizontal overlap, in DIP, between a submenu and its
    /// parent item: the submenu's preferred left edge sits this far
    /// LEFT of the parent's right edge. Reproduces the existing
    /// PART_Popup HorizontalOffset="-4" so the clamped position is
    /// visually identical to the old one when no flip is needed.
    /// The glue zeroes the Popup offsets when it installs the
    /// custom callback, so this overlap is applied exactly once,
    /// here, and the callback point is exact.</summary>
    public const double SubmenuHorizontalOverlap = 4;

    /// <summary>Vertical overlap, in DIP, between a submenu and its
    /// parent item: the submenu's preferred top edge sits this far
    /// ABOVE the parent's top edge. Reproduces the existing
    /// PART_Popup VerticalOffset="-6". See
    /// <see cref="SubmenuHorizontalOverlap"/> for why the glue
    /// zeroes the Popup offsets.</summary>
    public const double SubmenuVerticalOverlap = 6;

    /// <summary>A rectangle in DIP screen coordinates.</summary>
    public readonly struct DipRect
    {
        public DipRect(double left, double top, double width, double height)
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

    /// <summary>One computed placement: the menu's top-left corner
    /// in DIP screen coordinates, the height actually used (capped
    /// when the menu is taller than the effective area), and which
    /// direction was chosen. <see cref="Height"/> is the height the
    /// caller must enforce (MaxHeight) when
    /// <see cref="HeightCapped"/> is set; otherwise it equals the
    /// requested menu height. <see cref="IsFallback"/> means the
    /// inputs were unusable and the caller must use the legacy
    /// placement instead of these coordinates.</summary>
    public readonly struct MenuPlacement
    {
        public MenuPlacement(double left, double top, double height,
            bool opensUpward, bool opensLeftward, bool heightCapped, bool isFallback)
        {
            Left = left; Top = top; Height = height;
            OpensUpward = opensUpward; OpensLeftward = opensLeftward;
            HeightCapped = heightCapped; IsFallback = isFallback;
        }

        public double Left { get; }
        public double Top { get; }
        public double Height { get; }

        /// <summary>True when the menu opens ABOVE its anchor
        /// (main: bottom edge at the cursor; submenu: shifted up
        /// until its bottom edge meets the effective bottom).</summary>
        public bool OpensUpward { get; }

        /// <summary>True when the menu opens to the LEFT of its
        /// anchor (main: right edge pulled left to the effective
        /// right edge; submenu: right edge at the parent's left
        /// edge + overlap).</summary>
        public bool OpensLeftward { get; }

        public bool HeightCapped { get; }
        public bool IsFallback { get; }
    }

    /// <summary>Places the main (card) context menu. Preferred:
    /// below-right of the cursor (top-left at the cursor). If the
    /// menu does not fit below the cursor inside the effective rect
    /// (margin 8), it opens UPWARD with its bottom edge AT the
    /// cursor. If it does not fit right of the cursor, it shifts
    /// left until its right edge meets the effective right edge
    /// minus the margin. The result is then clamped fully inside
    /// the effective rect (margin 8). If the menu is taller than
    /// the effective height minus both margins, its height is
    /// capped to that value (the caller applies it as MaxHeight;
    /// the menu template scrolls) and its top is pinned to the
    /// effective top + margin. All inputs DIP screen coordinates;
    /// see the class comment for units and fallback. Never throws.
    /// Scroll-invariance (S1/S2): the cursor point and window rect
    /// are whatever the caller measured LIVE at open time, so a
    /// bottom-row card reached by scrolling flips exactly like one
    /// that was always at the bottom, and the same card scrolled
    /// to mid-window opens downward - position, never card index
    /// or scroll offset, decides.</summary>
    public static MenuPlacement PlaceMainMenu(
        double cursorX, double cursorY,
        double menuWidth, double menuHeight,
        DipRect windowRect, DipRect workAreaRect)
    {
        try
        {
            if (!ValidInputs(cursorX, cursorY, menuWidth, menuHeight, windowRect, workAreaRect))
                return Fallback(cursorX, cursorY, menuHeight);
            if (!TryEffectiveRect(windowRect, workAreaRect, out var effective))
                return Fallback(cursorX, cursorY, menuHeight);

            double height = CapHeight(menuHeight, effective, out bool capped);
            bool opensUpward = cursorY + height > effective.Bottom - EdgeMargin;
            double top = opensUpward ? cursorY - height : cursorY;
            bool opensLeftward = cursorX + menuWidth > effective.Right - EdgeMargin;
            double left = opensLeftward ? effective.Right - EdgeMargin - menuWidth : cursorX;
            (left, top) = ClampInside(left, top, menuWidth, height, effective);
            return new MenuPlacement(left, top, height, opensUpward, opensLeftward, capped, isFallback: false);
        }
        catch
        {
            return Fallback(cursorX, cursorY, menuHeight);
        }
    }

    /// <summary>Places a submenu. Preferred: to the RIGHT of the
    /// parent item, top edge at the parent's top minus the vertical
    /// overlap (reproducing Placement="Right" with offsets -4/-6).
    /// If the submenu does not fit right of the parent inside the
    /// effective rect (margin 8), it opens LEFTWARD with its right
    /// edge at the parent's left edge + the horizontal overlap.
    /// If it does not fit below the parent's top, it shifts UP
    /// until its bottom edge meets the effective bottom minus the
    /// margin - DOCUMENTED CHOICE: bottom-flush to the effective
    /// rect, NOT bottom-aligned to the parent item, because
    /// parent-bottom alignment still overflows whenever the
    /// parent sits near the bottom and the submenu is tall. The
    /// result is then clamped fully inside the effective rect
    /// (margin 8), and the height cap rule is the same as
    /// <see cref="PlaceMainMenu"/>. All inputs DIP screen
    /// coordinates; see the class comment for units and fallback.
    /// Never throws.</summary>
    public static MenuPlacement PlaceSubmenu(
        DipRect parentRect,
        double submenuWidth, double submenuHeight,
        DipRect windowRect, DipRect workAreaRect)
    {
        try
        {
            if (!IsFinite(parentRect.Left) || !IsFinite(parentRect.Top)
                || !IsFinite(parentRect.Width) || !IsFinite(parentRect.Height)
                || parentRect.Width <= 0 || parentRect.Height <= 0
                || !IsFinite(submenuWidth) || !IsFinite(submenuHeight)
                || submenuWidth <= 0 || submenuHeight <= 0
                || !ValidRect(windowRect) || !ValidRect(workAreaRect))
                return Fallback(parentRect.Right - SubmenuHorizontalOverlap,
                    parentRect.Top - SubmenuVerticalOverlap, submenuHeight);
            if (!TryEffectiveRect(windowRect, workAreaRect, out var effective))
                return Fallback(parentRect.Right - SubmenuHorizontalOverlap,
                    parentRect.Top - SubmenuVerticalOverlap, submenuHeight);

            double height = CapHeight(submenuHeight, effective, out bool capped);
            double preferredLeft = parentRect.Right - SubmenuHorizontalOverlap;
            double preferredTop = parentRect.Top - SubmenuVerticalOverlap;
            bool opensLeftward = preferredLeft + submenuWidth > effective.Right - EdgeMargin;
            double left = opensLeftward
                ? parentRect.Left + SubmenuHorizontalOverlap - submenuWidth
                : preferredLeft;
            bool opensUpward = preferredTop + height > effective.Bottom - EdgeMargin;
            double top = opensUpward ? effective.Bottom - EdgeMargin - height : preferredTop;
            (left, top) = ClampInside(left, top, submenuWidth, height, effective);
            return new MenuPlacement(left, top, height, opensUpward, opensLeftward, capped, isFallback: false);
        }
        catch
        {
            return Fallback(parentRect.Right - SubmenuHorizontalOverlap,
                parentRect.Top - SubmenuVerticalOverlap, submenuHeight);
        }
    }

    /// <summary>Selects the work area (already converted to DIP by
    /// the caller) that governs a point: the area containing the
    /// point wins; when the point is outside EVERY area, the
    /// nearest area by rect distance wins (ties keep the earliest
    /// area) - no teleport to the primary. Returns false (out
    /// left untouched) only when there are no usable areas or the
    /// point is non-finite. Never throws.</summary>
    public static bool TrySelectWorkArea(
        double x, double y, IReadOnlyList<DipRect> areas, out DipRect area)
    {
        area = default;
        try
        {
            if (!IsFinite(x) || !IsFinite(y) || areas is null || areas.Count == 0)
                return false;
            foreach (var candidate in areas)
            {
                if (!ValidRect(candidate)) continue;
                if (candidate.Contains(x, y))
                {
                    area = candidate;
                    return true;
                }
            }
            DipRect nearest = default;
            double nearestDistance = double.MaxValue;
            bool found = false;
            foreach (var candidate in areas)
            {
                if (!ValidRect(candidate)) continue;
                double dx = x < candidate.Left ? candidate.Left - x
                    : x > candidate.Right ? x - candidate.Right : 0;
                double dy = y < candidate.Top ? candidate.Top - y
                    : y > candidate.Bottom ? y - candidate.Bottom : 0;
                double distance = dx * dx + dy * dy;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate;
                    found = true;
                }
            }
            if (!found) return false;
            area = nearest;
            return true;
        }
        catch
        {
            area = default;
            return false;
        }
    }

    // --- shared internals ---

    private static bool ValidInputs(
        double cursorX, double cursorY,
        double menuWidth, double menuHeight,
        DipRect windowRect, DipRect workAreaRect)
        => IsFinite(cursorX) && IsFinite(cursorY)
            && IsFinite(menuWidth) && IsFinite(menuHeight)
            && menuWidth > 0 && menuHeight > 0
            && ValidRect(windowRect) && ValidRect(workAreaRect);

    private static bool ValidRect(DipRect rect)
        => IsFinite(rect.Left) && IsFinite(rect.Top)
            && IsFinite(rect.Width) && IsFinite(rect.Height)
            && rect.Width > 0 && rect.Height > 0;

    /// <summary>Effective bound = window ∩ work area. A degenerate
    /// intersection means stale work-area data; the window rect
    /// alone is then the bound. False when the window rect itself
    /// cannot hold the margin on both sides (too small to place
    /// any menu inside it) - the caller falls back.</summary>
    private static bool TryEffectiveRect(DipRect windowRect, DipRect workAreaRect, out DipRect effective)
    {
        double left = Math.Max(windowRect.Left, workAreaRect.Left);
        double top = Math.Max(windowRect.Top, workAreaRect.Top);
        double right = Math.Min(windowRect.Right, workAreaRect.Right);
        double bottom = Math.Min(windowRect.Bottom, workAreaRect.Bottom);
        if (right - left > 0 && bottom - top > 0)
            effective = new DipRect(left, top, right - left, bottom - top);
        else
            effective = windowRect;
        return effective.Width > 2 * EdgeMargin && effective.Height > 2 * EdgeMargin;
    }

    /// <summary>Height cap: the effective height minus both margins.
    /// A menu taller than that is capped (caller enforces via
    /// MaxHeight; the template scrolls). Never returns a
    /// non-positive height for a positive input.</summary>
    private static double CapHeight(double height, DipRect effective, out bool capped)
    {
        double available = effective.Height - 2 * EdgeMargin;
        capped = height > available;
        return capped ? available : height;
    }

    /// <summary>Fully-inside clamp, one axis at a time: the menu's
    /// top-left is kept in [area start + margin, area end - margin
    /// - size]. When the menu is as wide/tall as the usable area,
    /// it pins to the area's start + margin instead of producing a
    /// below-min value.</summary>
    private static (double Left, double Top) ClampInside(
        double left, double top, double width, double height, DipRect effective)
    {
        double minLeft = effective.Left + EdgeMargin;
        double maxLeft = effective.Right - EdgeMargin - width;
        double clampedLeft = maxLeft < minLeft
            ? minLeft
            : Math.Max(minLeft, Math.Min(left, maxLeft));
        double minTop = effective.Top + EdgeMargin;
        double maxTop = effective.Bottom - EdgeMargin - height;
        double clampedTop = maxTop < minTop
            ? minTop
            : Math.Max(minTop, Math.Min(top, maxTop));
        return (clampedLeft, clampedTop);
    }

    private static MenuPlacement Fallback(double anchorX, double anchorY, double menuHeight)
        => new(anchorX, anchorY,
            IsFinite(menuHeight) && menuHeight > 0 ? menuHeight : 0,
            opensUpward: false, opensLeftward: false,
            heightCapped: false, isFallback: true);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
