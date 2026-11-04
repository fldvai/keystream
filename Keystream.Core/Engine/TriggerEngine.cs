using Keystream.Core.Config;
using Keystream.Core.Input;

namespace Keystream.Core.Engine;

public sealed record TriggerFired(KeyIdentity Key, TriggerKind Kind, long AtMs);

/// <summary>
/// K5 - pure trigger state machine. Turns raw deck key events into
/// Tap / LongPress / DoubleTap decisions for the keys that have those
/// triggers bound. No threads, no timers, no I/O: the caller feeds events
/// with explicit timestamps and calls <see cref="Tick"/> periodically
/// (~every 25–50 ms) so time-based decisions (long-press while held,
/// tap-vs-double-tap disambiguation) can fire. Every rule is therefore
/// unit-testable with scripted timelines - the audited codebase had no
/// trigger engine at all (bindings fired raw on key-down, so long-press
/// and double-tap were impossible to express).
///
/// Semantics (documented because products differ here):
///  - Tap fires on key-UP when the press was shorter than LongPressMs -
///    unless DoubleTap is also bound for the key, in which case the tap is
///    held pending until the double-tap window expires without a second
///    press (decided in Tick). This delay is the unavoidable price of
///    having both triggers on one key.
///  - LongPress fires exactly once per press, the moment the held duration
///    crosses LongPressMs (via Tick, or retroactively when the UP event
///    reveals the crossing). The subsequent UP fires nothing.
///  - DoubleTap fires on the second key-DOWN when it lands within
///    DoubleTapWindowMs of the first tap's UP. The second press fires
///    nothing else.
///  - Auto-repeat DOWNs never retrigger anything.
///  - ScrollDial (§156) mirrors Momentary: fires once on the initial
///    DOWN (repeats add nothing), owns the press, and its UP fires
///    ScrollDialRelease via the early path even after a binding-map
///    change. ScrollDial is checked before Momentary on DOWN, so when
///    both are bound on one key ScrollDial wins.
/// </summary>
public sealed class TriggerEngine
{
    public int LongPressMs { get; init; } = 400;
    public int DoubleTapWindowMs { get; init; } = 300;

    private IReadOnlyDictionary<KeyIdentity, IReadOnlySet<TriggerKind>> _bound =
        new Dictionary<KeyIdentity, IReadOnlySet<TriggerKind>>();

    private readonly Dictionary<KeyIdentity, KeyState> _states = new();

    private sealed class KeyState
    {
        public bool IsDown;
        public long DownAtMs;
        public bool LongFired;
        public bool IsSecondPress;
        public long? LastTapUpMs;      // last completed short press (for double-tap detection)
        public bool TapPending;        // a tap waiting out the double-tap window
        public long TapPendingUpMs;
        public bool MomentaryActive;   // a Momentary press is being held
        public bool DialActive;        // §156: a ScrollDial press is being held
    }

    /// <summary>Replaces the trigger map (active page changed). In-flight
    /// presses keep their state; pending taps for keys no longer bound with
    /// Tap are dropped silently by Tick/OnEvent checks.</summary>
    public void SetBindings(IReadOnlyDictionary<KeyIdentity, IReadOnlySet<TriggerKind>> bound)
        => _bound = bound;

    /// <summary>Resets internal state for a key (e.g. when used as a combination modifier so release does not trigger Tap).</summary>
    public void ResetKey(KeyIdentity key) => _states.Remove(key);

    /// <summary>Clears ALL trigger state (Panic/passthrough, §45):
    /// a key held when passthrough engages never delivers its UP to
    /// the engine, so without this its next DOWN is misread as a
    /// repeat and its Momentary/Tap state sticks. §156: _states holds
    /// the DialActive flag too, so clearing it also ends any dial hold
    /// at this layer (DeckEngine.ResetForPassthrough raises the
    /// hold-ended event alongside).</summary>
    public void ResetAll() => _states.Clear();

    private KeyState StateFor(KeyIdentity key)
    {
        if (!_states.TryGetValue(key, out var st))
        {
            st = new KeyState();
            _states[key] = st;
        }
        return st;
    }

    public IReadOnlyList<TriggerFired> OnEvent(KeyEvent e, long nowMs)
    {
        var fired = new List<TriggerFired>();
        // §156: a held ScrollDial key MUST release even when the binding
        // map has changed mid-hold - same reason as the Momentary path
        // below (a page switch replaces the map, so the key may not be
        // bound as ScrollDial, or at all, anymore). Without this early
        // path the wheel capture would stick forever. Checked BEFORE
        // the Momentary path and BEFORE the _bound lookup, exactly
        // like the Momentary path; DialActive and MomentaryActive can
        // never both be set (DOWN precedence admits only one owner),
        // so the order only matters defensively.
        if (e.Direction == KeyDirection.Up
            && _states.TryGetValue(e.Key, out var dheld) && dheld.DialActive)
        {
            dheld.DialActive = false;
            dheld.IsDown = false;
            fired.Add(new TriggerFired(e.Key, TriggerKind.ScrollDialRelease, nowMs));
            return fired;
        }
        // A held Momentary key MUST release even when the binding map has
        // changed mid-hold - activating a layer replaces the active page,
        // so the key may not be bound as Momentary (or at all) anymore.
        // Without this early path the layer would stick forever.
        if (e.Direction == KeyDirection.Up
            && _states.TryGetValue(e.Key, out var mheld) && mheld.MomentaryActive)
        {
            mheld.MomentaryActive = false;
            mheld.IsDown = false;
            fired.Add(new TriggerFired(e.Key, TriggerKind.MomentaryRelease, nowMs));
            return fired;
        }
        if (!_bound.TryGetValue(e.Key, out var bound) || bound.Count == 0)
            return fired;
        var st = StateFor(e.Key);

        if (e.Direction == KeyDirection.Down)
        {
            // §156: ScrollDial is checked BEFORE Momentary - when both
            // are bound on one key, ScrollDial wins the press. It owns
            // the press entirely: fire once on the initial DOWN
            // (auto-repeats add nothing); the UP fires the release via
            // the early path above. No step job is enqueued for either
            // event (DeckEngine capture control only); wheel deltas
            // arrive later as coalesced ScrollDial jobs via Tick drain.
            if (bound.Contains(TriggerKind.ScrollDial))
            {
                if (e.IsRepeat || st.IsDown) return fired;
                st.IsDown = true;
                st.DialActive = true;
                st.LongFired = true; // suppress tap/long-press resolution on UP
                fired.Add(new TriggerFired(e.Key, TriggerKind.ScrollDial, nowMs));
                return fired;
            }
            if (bound.Contains(TriggerKind.Momentary))
            {
                // Momentary owns the press: fire once on the initial DOWN
                // (auto-repeats add nothing); the UP fires the release via
                // the early path above.
                if (e.IsRepeat || st.IsDown) return fired;
                st.IsDown = true;
                st.MomentaryActive = true;
                st.LongFired = true; // suppress tap/long-press resolution on UP
                fired.Add(new TriggerFired(e.Key, TriggerKind.Momentary, nowMs));
                return fired;
            }
            if (bound.Contains(TriggerKind.Repeat))
            {
                // Repeat owns the press: fire on the initial DOWN and on
                // every auto-repeat DOWN; the UP resolves nothing (the
                // LongFired flag suppresses tap/long-press resolution).
                st.IsDown = true;
                st.LongFired = true;
                fired.Add(new TriggerFired(e.Key, TriggerKind.Repeat, nowMs));
                return fired;
            }
            if (e.IsRepeat || st.IsDown) return fired;
            st.IsDown = true;
            st.DownAtMs = nowMs;
            st.LongFired = false;
            st.IsSecondPress = false;

            bool withinDoubleWindow = st.LastTapUpMs is long upAt
                                        && nowMs - upAt <= DoubleTapWindowMs;
            // A-06: second-press swallow (IsSecondPress, honoured on UP)
            // applies only when DoubleTap is bound - header contract.
            // Tap-only: every short UP fires Tap.
            if (withinDoubleWindow && bound.Contains(TriggerKind.DoubleTap))
            {
                st.LastTapUpMs = null;
                st.IsSecondPress = true;
                if (st.TapPending)
                {
                    st.TapPending = false; // the pending tap became a double tap
                }
                fired.Add(new TriggerFired(e.Key, TriggerKind.DoubleTap, nowMs));
            }
        }
        else
        {
            if (!st.IsDown) return fired;
            st.IsDown = false;
            long held = nowMs - st.DownAtMs;
            bool wasSecond = st.IsSecondPress;
            st.IsSecondPress = false;

            if (!st.LongFired && held >= LongPressMs && bound.Contains(TriggerKind.LongPress))
            {
                // No Tick happened while held; the UP reveals the crossing.
                st.LongFired = true;
                fired.Add(new TriggerFired(e.Key, TriggerKind.LongPress, nowMs));
                return fired;
            }
            if (st.LongFired || wasSecond) return fired;
            if (held >= LongPressMs) return fired; // held too long for a tap, no LongPress bound

            // A completed short press.
            st.LastTapUpMs = nowMs;
            if (bound.Contains(TriggerKind.Tap) && bound.Contains(TriggerKind.DoubleTap))
            {
                st.TapPending = true;
                st.TapPendingUpMs = nowMs;
            }
            else if (bound.Contains(TriggerKind.Tap))
            {
                fired.Add(new TriggerFired(e.Key, TriggerKind.Tap, nowMs));
            }
        }
        return fired;
    }

    public IReadOnlyList<TriggerFired> Tick(long nowMs)
    {
        var fired = new List<TriggerFired>();
        foreach (var (key, st) in _states)
        {
            if (!_bound.TryGetValue(key, out var bound)) continue;

            if (st.IsDown && !st.LongFired
                && bound.Contains(TriggerKind.LongPress)
                && nowMs - st.DownAtMs >= LongPressMs)
            {
                st.LongFired = true;
                fired.Add(new TriggerFired(key, TriggerKind.LongPress, nowMs));
            }

            if (st.TapPending && nowMs - st.TapPendingUpMs > DoubleTapWindowMs)
            {
                st.TapPending = false;
                if (bound.Contains(TriggerKind.Tap))
                    fired.Add(new TriggerFired(key, TriggerKind.Tap, nowMs));
            }

            if (st.LastTapUpMs is long upAt && nowMs - upAt > DoubleTapWindowMs)
                st.LastTapUpMs = null; // window expired; forget the tap
        }
        return fired;
    }
}
