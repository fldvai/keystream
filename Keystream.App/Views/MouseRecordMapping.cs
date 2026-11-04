using System;
using Keystream.Actions.Mouse;
using Keystream.App.ViewModels;
using Keystream.Windows.Input;

namespace Keystream.App.Views;

/// <summary>§113 - the two plain-data translations between the
/// recorder/settings world and <c>MouseRecordPlanner</c>, moved
/// verbatim from <c>MacroManagerWindow</c> (which now delegates here
/// with one-line bodies). No decision lives here: the switches only
/// rename vocabulary, exactly as they did inline. The planner keeps
/// every predicate (ignore-self, scope, thinning, cap, params,
/// gap); a future enum value still fails loudly here instead of
/// mapping silently.</summary>
public static class MouseRecordMapping
{
    /// <summary>Plain-data translation only (no decision): recorder
    /// enums → planner enums. The decision predicates live in the
    /// planner; these switches only rename the vocabulary.</summary>
    public static RecordedMouseEvent MapRecordedMouseEvent(MouseRecorder.MouseRecord p) => new(
        p.Kind switch
        {
            MouseRecorder.MouseEventKind.Move => RecordedMouseKind.Move,
            MouseRecorder.MouseEventKind.Down => RecordedMouseKind.Down,
            MouseRecorder.MouseEventKind.Up => RecordedMouseKind.Up,
            MouseRecorder.MouseEventKind.Wheel => RecordedMouseKind.Wheel,
            _ => throw new ArgumentOutOfRangeException(nameof(p.Kind)),
        },
        p.Button switch
        {
            MouseRecorder.MouseButton.None => RecordedMouseButton.None,
            MouseRecorder.MouseButton.Left => RecordedMouseButton.Left,
            MouseRecorder.MouseButton.Right => RecordedMouseButton.Right,
            MouseRecorder.MouseButton.Middle => RecordedMouseButton.Middle,
            _ => throw new ArgumentOutOfRangeException(nameof(p.Button)),
        },
        p.X, p.Y, p.Delta, p.At);

    /// <summary>Plain-data translation only (no decision): App
    /// settings → planner options snapshot. Exhaustive: a future
    /// scope value fails loudly here instead of mapping silently.</summary>
    public static MouseRecordOptions MapMouseRecordOptions(MouseRecordSettings settings) => new(
        settings.Scope switch
        {
            MouseScope.ClicksOnly => RecordedMouseScope.ClicksOnly,
            MouseScope.ClicksAndMoves => RecordedMouseScope.ClicksAndMoves,
            MouseScope.ClicksMovesAndScroll => RecordedMouseScope.ClicksMovesAndScroll,
            _ => throw new ArgumentOutOfRangeException(nameof(settings.Scope)),
        },
        settings.MoveThresholdPx,
        settings.MinMoveDelayMs,
        settings.CoordinateMode == MouseCoordinateMode.RelativeWindow
            ? RecordedMouseCoordinateMode.RelativeWindow
            : RecordedMouseCoordinateMode.Absolute,
        settings.IgnoreSelfClicks,
        settings.Verbatim);
}
