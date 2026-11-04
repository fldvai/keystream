namespace Keystream.App.Views;

/// <summary>§137 - what the current panel handler is doing, as one
/// value, so the master-shortcut toggles in MainViewModel can decide
/// stop-vs-start without a second session-ownership model and without
/// reaching into either handler's private fields. Read from the
/// handlers' existing private state only (recording flag, playback
/// flag, countdown machine + pending kind); it never sets anything.
/// Recording includes a PAUSED recording (hooks stopped, session still
/// owned); Playback means steps are actually executing (post-countdown).
/// The two countdown kinds stay distinct: a recording toggle cancels a
/// record countdown but leaves a playtest countdown alone, and vice
/// versa. WPF-free enum, in the same file as the handler contract.</summary>
public enum PanelSessionKind
{
    /// <summary>No countdown, recording, or playback in flight.</summary>
    None,

    /// <summary>Recording (paused included) owned by the handler.</summary>
    Recording,

    /// <summary>Playback steps executing (post-countdown).</summary>
    Playback,

    /// <summary>3-second countdown that will fire into recording.</summary>
    RecordCountdown,

    /// <summary>3-second countdown that will fire into playback.</summary>
    PlaytestCountdown,
}

/// <summary>§113 - panel button actions. Implemented by the macro
/// modal (attached mode, while it is open) and by the standalone
/// panel controller (while the modal is closed). The FloatPanelHost
/// routes the panel's button events to whichever handler is current;
/// neither handler owns the panel window itself. A handler that
/// starts a countdown/playback must also honour
/// <see cref="OnPanelHidden"/> (cancel the countdown: no playback
/// after the panel is gone).</summary>
public interface IPanelActionHandler
{
    /// <summary>§137 - the handler's current session kind, read from
    /// its existing state. The master-shortcut toggles branch on this
    /// (stop a recording, refuse a recording during playback, cancel
    /// the matching countdown, start when idle).</summary>
    PanelSessionKind CurrentSessionKind { get; }

    /// <summary>Panel ● pressed (READY state). Attached: record
    /// with the forced 3-second countdown. Standalone: record with
    /// the forced 3-second countdown into the selected saved macro.</summary>
    void OnPanelRecordRequested();

    /// <summary>Panel ▶ pressed (READY state). Plays per the shared
    /// Delay3s checkbox (countdown iff ON, else instant).</summary>
    void OnPanelPlaytestRequested();

    /// <summary>Panel ❚❚/▶ pressed while recording/paused.
/// </summary>
    void OnPanelPauseToggleRequested();

    /// <summary>Panel ■ pressed while recording/paused.</summary>
    void OnPanelStopRequested();

    /// <summary>The panel was hidden (×) or closed. Cancel any
    /// running countdown owned by this handler; never start
    /// playback after this call.</summary>
    void OnPanelHidden();

    /// <summary>§137 - stops a running playback through the EXACT
    /// CTS-cancel path the ESC panic hook uses (cancel a running
    /// countdown first when one is in flight, else cancel the
    /// playback token). Silent, like ESC: no error notice. No-op
    /// when nothing is playing. Must never be blocked by a pause:
    /// a paused playback stops immediately via the same token.</summary>
    void StopPlayback();

    /// <summary>§137 - toggles the §137 pause gate of a running
    /// playback (pause ↔ resume). Returns true when a playback was
    /// actually toggled; false when no playback is active (the caller
    /// then shows the S1729 notice). Never blocks the UI thread:
    /// the gate only suspends the playback's own awaits.</summary>
    bool TogglePlaybackPause();
}
