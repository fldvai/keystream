using System.Diagnostics;
using Keystream.Core.Localization;
using Keystream.Core.Devices;
using Keystream.Core.Input;
using static Keystream.Windows.Interception.NativeInterception;

namespace Keystream.Windows.Interception;

/// <summary>
/// Mode A implementation of <see cref="IInterceptionService"/> - the only
/// blocking mechanism since Gate 0 failed Mode B and Gate A verified this
/// driver path on the user's hardware (880 deck key-downs eaten, 0 leaked,
/// 0 send failures).
///
/// Ownership discipline (the audited Mode A violated every line of this,
/// findings B1-2/B1-3/B1-10/B1-12):
///  - ALL native calls happen on the single worker thread: it creates the
///    context, sets the filter, runs the receive loop, and destroys the
///    context exactly once when the loop exits. No other thread ever
///    touches the context.
///  - Stop() only sets a flag and joins the worker (the loop polls with
///    wait_with_timeout, so shutdown is prompt and ordered).
///  - The filter predicate delegate is the rooted static from
///    NativeInterception - never a collectable local.
///  - Hardware ids are read lazily per device index and only cached when
///    non-empty; deck matching is DeviceId.Matches (same kind + exact
///    normalized value). A config whose deck id is not an
///    InterceptionHardwareId is refused with a Faulted health state
///    instead of being silently compared across namespaces.
///  - Passthrough (panic) forwards everything and raises no events.
///  - Deck presence is polled on the worker thread only (same ownership
///    rule as every other native call): every ~2s the loop enumerates
///    keyboard devices and checks whether any still reports the deck's
///    hardware id. An unplugged deck is a status change (DeckPresent
///    false), never a Faulted state and never an exception - the loop
///    keeps Running so a replug recovers without a restart.
/// </summary>
public sealed class ModeAInterceptionService : IInterceptionService
{
    private readonly object _startGate = new();
    private Thread? _worker;
    private volatile bool _stopRequested;
    private volatile bool _passthrough;
    private DeviceId _deck;

    private InterceptionState _state = InterceptionState.Stopped;
    private string? _lastError;
    private long _eventsReceived;
    private long _eventsEaten;
    private long _lastEventTicks; // DateTimeOffset.UtcNow.Ticks
    private volatile bool _deckPresent = true;

    public event Action<KeyEvent>? DeckKeyEvent;

    public ServiceHealth Health => new(
        _state,
        _lastError,
        Interlocked.Read(ref _eventsReceived),
        Interlocked.Read(ref _eventsEaten),
        _lastEventTicks == 0 ? null : new DateTimeOffset(Interlocked.Read(ref _lastEventTicks), TimeSpan.Zero),
        _deckPresent);

    public bool Passthrough
    {
        get => _passthrough;
        set => _passthrough = value;
    }

    public void Start(DeviceId deck)
    {
        lock (_startGate)
        {
            if (_worker is { IsAlive: true })
                throw new InvalidOperationException(Localizer.Get("S1226"));

            if (deck.Kind != DeviceIdKind.InterceptionHardwareId || !deck.IsValid)
            {
                _state = InterceptionState.Faulted;
                _lastError = Localizer.Format("S1227", deck.Kind);
                throw new InvalidOperationException(_lastError);
            }

            _deck = deck;
            _stopRequested = false;
            _state = InterceptionState.Starting;
            _lastError = null;
            _worker = new Thread(WorkerMain) { IsBackground = true, Name = "MKD-Interception" };
            _worker.Start();
        }
    }

    public void Stop()
    {
        Thread? worker;
        lock (_startGate) worker = _worker;
        if (worker is null) return;

        _stopRequested = true;
        if (!worker.Join(TimeSpan.FromSeconds(3)))
        {
            _state = InterceptionState.Faulted;
            _lastError = Localizer.Get("S1228");
        }
        lock (_startGate)
        {
            if (ReferenceEquals(_worker, worker)) _worker = null;
        }
    }

    private void WorkerMain()
    {
        IntPtr context = IntPtr.Zero;
        var deckByDevice = new Dictionary<int, bool>();
        var pressedKeys = new HashSet<KeyIdentity>();
        try
        {
            try { context = interception_create_context(); }
            catch (DllNotFoundException) { Fail(Localizer.Get("S1220")); return; }
            catch (BadImageFormatException) { Fail(Localizer.Get("S1221")); return; }

            if (context == IntPtr.Zero)
            {
                Fail(Localizer.Get("S1229"));
                return;
            }

            interception_set_filter(context, KeyboardPredicate, FILTER_KEY_ALL);
            _state = InterceptionState.Running;

            long lastPollTicks = 0; // 0 => the first loop pass polls immediately
            while (!_stopRequested)
            {
                if (Stopwatch.GetTimestamp() - lastPollTicks >= 2 * Stopwatch.Frequency)
                {
                    lastPollTicks = Stopwatch.GetTimestamp();
                    PollDeckPresence(context, deckByDevice);
                }

                int device = interception_wait_with_timeout(context, 200);
                if (device == 0) continue;
                if (interception_is_keyboard(device) == 0) continue;

                var stroke = default(InterceptionKeyStroke);
                if (interception_receive(context, device, ref stroke, 1) <= 0) continue;

                Interlocked.Increment(ref _eventsReceived);
                Interlocked.Exchange(ref _lastEventTicks, DateTimeOffset.UtcNow.Ticks);

                bool isDeck = IsDeckDevice(context, device, deckByDevice);
                if (!isDeck || _passthrough)
                {
                    // Forwarded UNCHANGED - the whole stroke struct goes back.
                    if (interception_send(context, device, ref stroke, 1) <= 0)
                        _lastError = Localizer.Get("S1230");
                    continue;
                }

                // Deck stroke: eaten by NOT sending it back.
                Interlocked.Increment(ref _eventsEaten);
                RaiseDeckEvent(device, stroke, pressedKeys);
            }
        }
        catch (Exception ex)
        {
            Fail(Localizer.Format("S1231", ex.GetType().Name, ex.Message));
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                interception_destroy_context(context);
                context = IntPtr.Zero;
            }
            if (_state != InterceptionState.Faulted)
                _state = InterceptionState.Stopped;
        }
    }

    private void Fail(string message)
    {
        _state = InterceptionState.Faulted;
        _lastError = message;
    }

    /// <summary>Presence poll (worker thread only). A keyboard that stops
    /// reporting the deck's hardware id is unplugged, not broken: the loop
    /// stays Running and only this flag flips, so the UI can say "deck
    /// terputus" instead of showing a healthy green dot over a dead deck.
    /// On any change the per-index deck cache is cleared - after an
    /// unplug/replug the driver may hand the same index to a different
    /// keyboard, and a stale cached verdict would misidentify it.</summary>
    private void PollDeckPresence(IntPtr context, Dictionary<int, bool> deckByDevice)
    {
        bool present = false;
        for (int device = 1; device <= 20; device++)
        {
            if (interception_is_keyboard(device) == 0) continue;
            string hwId = GetHardwareId(context, device);
            if (hwId.Length == 0) continue;
            if (_deck.Matches(DeviceId.FromInterceptionHardwareId(hwId))) { present = true; break; }
        }
        if (present != _deckPresent)
        {
            _deckPresent = present;
            deckByDevice.Clear();
        }
    }

    private bool IsDeckDevice(IntPtr context, int device, Dictionary<int, bool> cache)
    {
        if (cache.TryGetValue(device, out bool known)) return known;
        string hwId = GetHardwareId(context, device);
        if (hwId.Length == 0) return false; // unknown yet: forward, retry next stroke (never cache "")
        bool isDeck = _deck.Matches(DeviceId.FromInterceptionHardwareId(hwId));
        cache[device] = isDeck;
        return isDeck;
    }

    private void RaiseDeckEvent(int device, InterceptionKeyStroke stroke, HashSet<KeyIdentity> pressedKeys)
    {
        bool isUp = (stroke.state & KEY_STATE_UP) != 0;
        bool isE0 = (stroke.state & KEY_STATE_E0) != 0;
        bool isE1 = (stroke.state & KEY_STATE_E1) != 0;
        var identity = new KeyIdentity(stroke.code, isE0, isE1);

        bool isRepeat = false;
        if (!isUp)
        {
            isRepeat = !pressedKeys.Add(identity); // already present -> auto-repeat
        }
        else
        {
            pressedKeys.Remove(identity);
        }

        uint vk = MapVirtualKey(stroke.code, MAPVK_VSC_TO_VK_EX);
        var e = new KeyEvent(
            _deck,
            identity,
            vk,
            isUp ? KeyDirection.Up : KeyDirection.Down,
            isRepeat,
            Stopwatch.GetTimestamp());
        DeckKeyEvent?.Invoke(e);
    }

    public void Dispose() => Stop();
}
