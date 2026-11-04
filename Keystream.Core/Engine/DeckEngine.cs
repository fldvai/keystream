using System.Diagnostics;
using Keystream.Core.Config;
using Keystream.Core.Input;

namespace Keystream.Core.Engine;

/// <summary>
/// The glue the application hosts: KeyEvent in → TriggerEngine decisions →
/// ActionDispatcher jobs out. Owns the active page's resolved bindings and
/// converts QPC timestamps to milliseconds for the trigger state machine.
/// Threading: ProcessEvent is called from the interception event (driver
/// worker thread) - it only does dictionary lookups, state-machine steps
/// and a non-blocking channel write, so the driver thread is never held by
/// action execution. Tick should be called by the host every ~25 ms.
/// </summary>
public sealed class DeckEngine
{
    private readonly TriggerEngine _triggers = new();
    private readonly ActionDispatcher _dispatcher;
    private readonly Func<bool>? _sessionActive;
    private BindingResolver.ResolvedPage _page = BindingResolver.Resolve(new DeckConfig());
    private readonly object _gate = new();

    /// <summary>Momentary bindings currently held, by key. Activating a
    /// layer switches the active page, so when the release fires, the
    /// CURRENT page's map no longer contains the binding that started
    /// the hold - the release must be dispatched to the remembered one.</summary>
    private readonly Dictionary<KeyIdentity, BindingConfig> _momentaryHeld = new();
    /// <summary>§156: ScrollDial bindings currently held, by key -
    /// mirrors <see cref="_momentaryHeld"/>. While a dial is held, wheel
    /// deltas accumulate in <see cref="_pendingDialDelta"/> and are
    /// drained by Tick as coalesced ScrollDial jobs for the remembered
    /// binding. The pending slot belongs to ONE dial at a time (see
    /// <see cref="_dialPendingKey"/>): if a second dial starts while a
    /// first is held, it is tracked here but deltas stay attributed to
    /// the first until it ends.</summary>
    private readonly Dictionary<KeyIdentity, BindingConfig> _dialHeld = new();
    /// <summary>§156: the dial that owns the pending delta slot - the
    /// first dial currently held. Null when no dial is held. The
    /// remainder of a partially-accumulated notch is discarded when
    /// this dial ends (and the slot passes, empty, to a remaining dial,
    /// if any).</summary>
    private KeyIdentity? _dialPendingKey;
    /// <summary>§156: raw accumulated wheel delta in WHEEL_DELTA units
    /// (120 per notch, signed) for <see cref="_dialPendingKey"/>, not
    /// yet drained. Only whole notches are emitted; the sub-notch
    /// remainder stays here across drains.</summary>
    private int _pendingDialDelta;

    /// <summary>§156 seam (Worker B subscribes): a dial hold started
    /// (key, binding) or ended (key, null). Raised OUTSIDE
    /// <see cref="_gate"/> - Dispatch/ResetForPassthrough snapshot what
    /// to raise under the lock and raise after releasing it, so a
    /// handler that calls back into DeckEngine (e.g. AddDialDelta)
    /// can never deadlock on the gate.</summary>
    public event Action<KeyIdentity, BindingConfig?>? DialHoldChanged;
    private readonly HashSet<KeyIdentity> _heldDeckKeys = new();
    private readonly HashSet<KeyIdentity> _keysConsumedByCombo = new();
    private readonly HashSet<KeyIdentity> _firstKeysThatFiredCombo = new();
    /// <summary>Keys whose press began on the PREVIOUS page (page changed
    /// mid-press, e.g. hotkey toggle while holding a deck key). Their UP
    /// (and repeats) must be dropped, never interpreted as fresh taps on
    /// the new page. A key leaves this set on its UP. Momentary-held keys
    /// are deliberately excluded: their release must still reach the
    /// TriggerEngine early path so the remembered layer binding fires its
    /// MomentaryRelease instead of sticking. §156: dial-held keys are
    /// excluded for the same reason - their release must reach the
    /// ScrollDial early path so the capture ends instead of sticking.</summary>
    private readonly HashSet<KeyIdentity> _keysSuppressedAfterPageLoad = new();

    /// <summary>§121 F-C5: when supplied and true, a panel/modal
    /// session (countdown, recording or playback) is active and deck
    /// keypresses must be ignored - deck execution and panel/modal
    /// sessions are mutually exclusive. §172: AppHost ORs capture
    /// suppression (SuppressEngineEvents) into the same predicate, so
    /// the ONE gate also covers every key-capture session (dashboard
    /// capture, editor reassign/combination listening, Settings
    /// hotkey capture): while a capture is armed, trigger output
    /// maturing in Tick is consumed, never deferred, and any event
    /// that still reaches ProcessEvent is treated exactly like a
    /// session event (DOWNs dropped, only release unwinds pass).
    /// The host supplies a plain predicate (AppHost: () =&gt;
    /// FloatPanel.SessionActive || SuppressEngineEvents) so Core
    /// never references App types. The predicate is a plain-bool read
    /// on the driver thread; racing a session/capture boundary
    /// benignly drops or admits at most one edge event - the same
    /// pattern as the host's SuppressEngineEvents gate. Modal/playtest
    /// execution bypasses this engine entirely (Task.Run straight to
    /// MacroAction.ExecuteAsync) and is unaffected.</summary>
    public DeckEngine(ActionDispatcher dispatcher, Func<bool>? sessionActive = null)
    {
        _dispatcher = dispatcher;
        _sessionActive = sessionActive;
    }

    public TriggerEngine Triggers => _triggers;

    /// <summary>Resets all in-flight key state when passthrough engages
    /// (Panic, §45). Held keys never deliver their UP while passthrough
    /// is on, so every held/combo/suppressed record is stale; the
    /// remembered Momentary bindings are dropped with it (their release
    /// can no longer fire - the layer page itself is unwound by
    /// PageSwitchAction.ResetMomentary, called alongside this). §156:
    /// the remembered dial bindings are dropped the same way, and a
    /// hold-ended event is raised for each active dial - no UP will
    /// ever arrive, so the capture-stop signal must still go out
    /// (the hook stops on this event). Pending wheel delta is
    /// discarded with the hold.</summary>
    public void ResetForPassthrough()
    {
        List<KeyIdentity> endedDialKeys;
        lock (_gate)
        {
            _triggers.ResetAll();
            _heldDeckKeys.Clear();
            _keysConsumedByCombo.Clear();
            _firstKeysThatFiredCombo.Clear();
            _keysSuppressedAfterPageLoad.Clear();
            _momentaryHeld.Clear();
            endedDialKeys = _dialHeld.Keys
                .OrderBy(k => k.ScanCode).ThenBy(k => k.IsE0).ThenBy(k => k.IsE1).ToList();
            _dialHeld.Clear();
            _dialPendingKey = null;
            _pendingDialDelta = 0;
        }
        // Outside _gate: a handler calling back into DeckEngine must
        // never deadlock (see DialHoldChanged).
        foreach (var key in endedDialKeys)
            DialHoldChanged?.Invoke(key, null);
    }

    /// <summary>Peringatan dari Resolve terakhir (binding dibuang
    /// diam-diam, §45). Dibaca footer lewat AppHost sekali per
    /// perubahan - bukan per tick.</summary>
    public IReadOnlyList<string> LastResolveWarnings => _page.Warnings;

    public void LoadPage(DeckConfig config, string? pageId = null)
    {
        lock (_gate)
        {
            _page = BindingResolver.Resolve(config, pageId);
            _triggers.SetBindings(_page.TriggerMap);
            // Batch 1 fix: a press that started on the old page must not
            // resolve against the new page. Remember held/consumed/combo
            // keys as suppressed until their UP, and drop their pending
            // trigger state (TapPending etc.) so Tick cannot fire them
            // against the new page either. Momentary-held keys keep their
            // state so their release still unwinds the layer (see field
            // comment on _keysSuppressedAfterPageLoad). §156: dial-held
            // keys are excluded identically, and _dialHeld / the pending
            // delta slot are untouched - a dial survives a page load.
            foreach (var key in _heldDeckKeys
                         .Concat(_keysConsumedByCombo)
                         .Concat(_firstKeysThatFiredCombo)
                         .Where(k => !_momentaryHeld.ContainsKey(k) && !_dialHeld.ContainsKey(k))
                         .ToList())
            {
                _keysSuppressedAfterPageLoad.Add(key);
                _triggers.ResetKey(key);
            }
            _heldDeckKeys.Clear();
            _keysConsumedByCombo.Clear();
            _firstKeysThatFiredCombo.Clear();
        }
    }

    public void ProcessEvent(KeyEvent e)
    {
        // §121 F-C5: while a session is active, deck keypresses are
        // ignored. DOWNs are dropped entirely - no held/combo state,
        // no trigger state, no enqueue. UPs still run the existing
        // bookkeeping + trigger resolution below, but only
        // MomentaryRelease / ScrollDialRelease fires pass: a DOWN
        // processed before the session started must resolve silently
        // instead of leaving stale IsDown state behind (which would
        // swallow the next press or fire a late LongPress after the
        // session ends), and a release is an unwind of that
        // pre-session hold, not a new execution - swallowing it
        // would leave the layer page stuck (§122) or the wheel
        // capture stuck (§156). Every other fire stays swallowed.
        bool blocked = _sessionActive?.Invoke() == true;
        if (blocked && e.IsDown) return;

        long nowMs = QpcToMs(e.TimestampQpc);

        // Batch 1 fix: auto-repeats of a key whose press began on the
        // previous page are dropped along with its UP (see LoadPage).
        if (e.IsDown && e.IsRepeat)
        {
            lock (_gate)
            {
                if (_keysSuppressedAfterPageLoad.Contains(e.Key)) return;
            }
        }

        if (e.IsDown && !e.IsRepeat)
        {
            (BindingConfig PrimaryBinding, KeyCombinationConfig Combo)? matchedCombo = null;
            KeyIdentity matchedFirstKey = default;

            lock (_gate)
            {
                // Batch 1 fix: HashSet enumeration order is not a user
                // rule. When two modifiers are held, the lowest scancode
                // (then non-E0 before E0) wins, deterministically.
                foreach (var heldKey in _heldDeckKeys
                             .OrderBy(k => k.ScanCode).ThenBy(k => k.IsE0))
                {
                    if (_page.Combinations.TryGetValue((heldKey, e.Key), out var comboPair))
                    {
                        matchedCombo = comboPair;
                        matchedFirstKey = heldKey;
                        break;
                    }
                }
            }

            if (matchedCombo is not null)
            {
                lock (_gate)
                {
                    _keysConsumedByCombo.Add(e.Key);
                    _firstKeysThatFiredCombo.Add(matchedFirstKey);
                }

                var steps = matchedCombo.Value.Combo.Steps.Count > 0
                    ? matchedCombo.Value.Combo.Steps
                    : new List<ActionStep> { matchedCombo.Value.Combo.ToStep() };

                var comboBinding = new BindingConfig
                {
                    KeyId = matchedCombo.Value.PrimaryBinding.KeyId,
                    KeyName = $"{matchedCombo.Value.PrimaryBinding.KeyName} + {matchedCombo.Value.Combo.SecondKeyName}",
                    Label = string.IsNullOrWhiteSpace(matchedCombo.Value.Combo.Label)
                        ? matchedCombo.Value.Combo.ActionTarget
                        : matchedCombo.Value.Combo.Label,
                    Steps = steps,
                };

                EnqueueTap(comboBinding);
                return;
            }

            // §168: chord-mapped binding - the held-modifier mask +
            // main key, decided at the main key's DOWN. Checked after
            // combinations (the older feature wins on a clash), before
            // the key joins the held set and reaches TriggerEngine, so
            // the main key's own single never fires for a chord press.
            // NO new engine state: the mask is computed on the spot
            // from the held set.
            BindingConfig? matchedChord = null;
            List<KeyIdentity>? chordModifiers = null;
            lock (_gate)
            {
                byte heldMask = ChordMask.FromHeldKeys(_heldDeckKeys);
                if (heldMask != ChordMask.None
                    && _page.ChordBindings.TryGetValue((heldMask, e.Key), out var chordBinding))
                {
                    matchedChord = chordBinding;
                    chordModifiers = _heldDeckKeys.Where(k => ChordMask.BitFor(k) != ChordMask.None).ToList();
                }
            }

            if (matchedChord is not null)
            {
                lock (_gate)
                {
                    _keysConsumedByCombo.Add(e.Key);
                    // Only the held MODIFIERS are marked fired-combo:
                    // their singles are suppressed (ResetKey on UP).
                    // A non-modifier bystander key keeps its own single.
                    foreach (var modifier in chordModifiers!)
                        _firstKeysThatFiredCombo.Add(modifier);
                }
                EnqueueTap(matchedChord);
                return;
            }

            lock (_gate) { _heldDeckKeys.Add(e.Key); }
        }
        else if (!e.IsDown)
        {
            bool wasConsumed;
            bool didFireCombo;
            lock (_gate)
            {
                // Batch 1 fix: release of a press that began on the
                // previous page is dropped, never a fresh tap there.
                if (_keysSuppressedAfterPageLoad.Remove(e.Key)) return;
                _heldDeckKeys.Remove(e.Key);
                wasConsumed = _keysConsumedByCombo.Remove(e.Key);
                didFireCombo = _firstKeysThatFiredCombo.Remove(e.Key);
            }

            if (wasConsumed) return;

            if (didFireCombo)
            {
                lock (_gate) _triggers.ResetKey(e.Key);
                return;
            }
        }

        IReadOnlyList<TriggerFired> fired;
        lock (_gate) fired = _triggers.OnEvent(e, nowMs);
        // §122: while blocked, only release fires pass (see
        // the comment at the top of this method). Tick can never
        // produce a release (TriggerEngine.Tick only fires
        // LongPress/Tap), so no equivalent filter is needed there.
        Dispatch(fired, releasesOnly: blocked);
    }

    public void Tick(long timestampQpc)
    {
        IReadOnlyList<TriggerFired> fired;
        lock (_gate) fired = _triggers.Tick(QpcToMs(timestampQpc));
        // §121 F-C5: Tick still advances trigger state while a
        // session is active - a LongPress/TapPending that matures
        // mid-session is consumed here, never deferred to after the
        // session - but nothing is dispatched. §172: identical while
        // capture suppression is active (same predicate) - a pending
        // fire maturing mid-capture is consumed, never fired late
        // after the capture ends.
        if (_sessionActive?.Invoke() == true)
        {
            // §156: dial input during a locked session is discarded,
            // never applied late - clear the pending slot here, after
            // the session check, instead of draining it.
            lock (_gate) _pendingDialDelta = 0;
            return;
        }
        Dispatch(fired);
        // §156: drain the coalesced dial delta (one job per Tick at
        // most) after the trigger fires, only while no session is
        // active (see the branch above).
        DrainDialDelta();
    }

    /// <summary>§156 seam (Worker B's hook feeds this): accumulates raw
    /// wheel delta in WHEEL_DELTA units (120 per notch, signed) into the
    /// pending slot for the currently held dial. No-op when no dial is
    /// held - stray wheel input never creates a job. Deliberately does
    /// NOT check the session predicate: accumulation during a session
    /// is discarded by Tick (see Tick), so a delta can never be applied
    /// late after the session ends.</summary>
    public void AddDialDelta(int deltaUnits)
    {
        lock (_gate)
        {
            if (_dialPendingKey is null) return;
            _pendingDialDelta += deltaUnits;
        }
    }

    /// <summary>§156: ends one dial hold. Caller MUST hold
    /// <see cref="_gate"/>. Removes the remembered binding, discards
    /// the pending remainder when the ended dial owned the slot, and
    /// passes the (empty) slot to a remaining dial, if any, chosen
    /// deterministically (lowest scancode, then non-E0 before E0).</summary>
    private void EndDialHoldLocked(KeyIdentity key)
    {
        _dialHeld.Remove(key);
        if (_dialPendingKey == key)
        {
            _pendingDialDelta = 0;
            _dialPendingKey = _dialHeld.Keys
                .OrderBy(k => k.ScanCode).ThenBy(k => k.IsE0).ThenBy(k => k.IsE1)
                .Cast<KeyIdentity?>().FirstOrDefault();
        }
    }

    /// <summary>§156: emits at most ONE coalesced job for the held dial's
    /// binding: whole notches only - <c>pending / 120</c>, C# integer
    /// division truncating toward zero, so a negative accumulation
    /// yields a negative notch count with the remainder keeping the
    /// sign (e.g. -300 units → job WheelDelta -240, remainder -60).
    /// The remainder (&lt; 120 units, signed) stays in the slot for the
    /// next drain. A zero-notch accumulation emits nothing and keeps
    /// the whole remainder. The job carries WheelDelta in WHEEL_DELTA
    /// units (notches × 120) with FiredKind ScrollDial, set by
    /// ExecuteStepAsync in the dispatcher.</summary>
    private void DrainDialDelta()
    {
        DispatchJob? job = null;
        lock (_gate)
        {
            if (_dialPendingKey is null)
            {
                // Invariant: no dial held means an empty slot; clear
                // defensively so a stale remainder can never fire later.
                _pendingDialDelta = 0;
            }
            else if (!_dialHeld.TryGetValue(_dialPendingKey.Value, out var binding))
            {
                // Broken invariant (pending key without a held binding):
                // drop the slot rather than firing for a dead dial.
                _pendingDialDelta = 0;
                _dialPendingKey = null;
            }
            else
            {
                int notches = _pendingDialDelta / 120; // truncates toward zero
                if (notches != 0)
                {
                    int units = notches * 120;
                    _pendingDialDelta -= units;
                    job = new DispatchJob(binding, TriggerKind.ScrollDial, units);
                }
            }
        }
        if (job is not null) _dispatcher.Enqueue(job);
    }

    /// <summary>§168: the ONE Tap enqueue both the combination path
    /// and the chord-mapped path share - a binding's own steps fired
    /// as Tap. No second enqueue block.</summary>
    private void EnqueueTap(BindingConfig binding)
        => _dispatcher.Enqueue(new DispatchJob(binding, TriggerKind.Tap));

    private void Dispatch(IReadOnlyList<TriggerFired> fired, bool releasesOnly = false)
    {
        // §156: dial hold events are snapshotted under _gate and raised
        // AFTER this loop, outside the lock (see DialHoldChanged).
        var dialEvents = new List<(KeyIdentity Key, BindingConfig? Binding)>();
        foreach (var f in fired)
        {
            // §122: releasesOnly (blocked UP path) lets only the
            // release unwinds through; every other kind is
            // a new execution and stays swallowed mid-session.
            // §156: ScrollDialRelease passes exactly like
            // MomentaryRelease - ending a pre-session dial hold is an
            // unwind, not a new execution, and swallowing it would
            // leave the wheel capture stuck.
            if (releasesOnly && f.Kind != TriggerKind.MomentaryRelease && f.Kind != TriggerKind.ScrollDialRelease) continue;
            if (f.Kind == TriggerKind.MomentaryRelease)
            {
                BindingConfig? held;
                lock (_gate) { _momentaryHeld.Remove(f.Key, out held); }
                if (held is not null)
                    _dispatcher.Enqueue(new DispatchJob(held, f.Kind));
                continue;
            }
            if (f.Kind == TriggerKind.ScrollDialRelease)
            {
                // Capture control only: no step job is ever enqueued
                // for a release. The pending remainder is discarded by
                // EndDialHoldLocked; the ended event goes out below,
                // even with no remembered binding (idempotent stop for
                // the capture hook).
                lock (_gate) EndDialHoldLocked(f.Key);
                dialEvents.Add((f.Key, null));
                continue;
            }
            BindingResolver.ResolvedPage page;
            lock (_gate) page = _page;
            if (page.Bindings.TryGetValue((f.Key, f.Kind), out var binding))
            {
                if (f.Kind == TriggerKind.Momentary)
                    lock (_gate) _momentaryHeld[f.Key] = binding;
                if (f.Kind == TriggerKind.ScrollDial)
                {
                    // Capture control only: remember the binding and
                    // raise hold-started; NO step job for the DOWN.
                    // Steps run only from Tick-drained delta jobs.
                    lock (_gate)
                    {
                        _dialHeld[f.Key] = binding;
                        if (_dialPendingKey is null)
                        {
                            _dialPendingKey = f.Key;
                            _pendingDialDelta = 0;
                        }
                    }
                    dialEvents.Add((f.Key, binding));
                    continue;
                }
                _dispatcher.Enqueue(new DispatchJob(binding, f.Kind));
            }
        }
        foreach (var (key, binding) in dialEvents)
            DialHoldChanged?.Invoke(key, binding);
    }

    private static long QpcToMs(long qpc) => qpc * 1000 / Stopwatch.Frequency;
}
