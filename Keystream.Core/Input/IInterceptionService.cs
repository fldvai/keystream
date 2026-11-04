using Keystream.Core.Devices;

namespace Keystream.Core.Input;

public enum InterceptionState
{
    Stopped,
    Starting,
    Running,
    Faulted,
}

/// <summary>K7 (contract part) - an honest health surface. The audited code
/// reported "running" while its worker had died or was never started; the
/// product must be able to show the user the real state, including the last
/// error and proof-of-life counters. DeckPresent is the same honesty rule
/// applied to the keyboard itself: true when a keyboard matching the deck
/// hardware id was seen in the latest presence poll. Running + !DeckPresent
/// means the driver loop is alive but the deck keyboard is currently
/// absent (unplugged) - a status, never a crash.</summary>
public sealed record ServiceHealth(
    InterceptionState State,
    string? LastError,
    long EventsReceived,
    long EventsEaten,
    DateTimeOffset? LastEventUtc,
    bool DeckPresent);

/// <summary>
/// The interception contract behind which Mode A (the Gate 0 decision) is
/// implemented in Phase 2. Contract obligations for implementations:
///  - Non-deck strokes are forwarded to the OS UNCHANGED (same scan code,
///    state bits and information) on the driver worker and never surface
///    through <see cref="DeckKeyEvent"/>.
///  - Deck strokes are eaten (never forwarded) and surface ONLY through
///    <see cref="DeckKeyEvent"/>, raised on the worker thread; consumers
///    must not block it (the engine dispatches asynchronously, Phase 2).
///  - Start/Stop are orderly: after Stop returns, the native context is
///    destroyed exactly once and no further events are raised (the audited
///    Mode A destroyed the context from another thread mid-receive, B1-3).
///  - <see cref="Passthrough"/> is the panic switch: when true, deck
///    strokes are forwarded like any other keyboard (deck becomes a normal
///    keyboard) and no events are raised.
/// </summary>
public interface IInterceptionService : IDisposable
{
    ServiceHealth Health { get; }

    event Action<KeyEvent>? DeckKeyEvent;

    void Start(DeviceId deck);

    void Stop();

    bool Passthrough { get; set; }
}
