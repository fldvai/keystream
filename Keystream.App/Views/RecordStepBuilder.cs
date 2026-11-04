using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Keystream.Actions.Hotkey;
using Keystream.Actions.Macro;
using Keystream.Core.Config;
using Keystream.Core.Localization;
using Keystream.Windows.Input;

namespace Keystream.App.Views;

/// <summary>§113 - the modal's key-event → step conversion, factored
/// out of <c>MacroManagerWindow.OnRecordedKeyEvent</c> so the
/// standalone panel controller can record with byte-identical
/// semantics. WPF-FREE: the only inputs are the hook record (plain
/// virtual-key data), booleans, and timestamps - no
/// <c>System.Windows</c> types anywhere, including in comments. The
/// VK→token table below is plain int→string data, derived from the
/// modal's <c>EventToken</c>/<c>KeyToToken</c> pair (which went
/// through <c>KeyInterop.KeyFromVirtualKey</c>): every VK listed here
/// maps to the WKey <c>KeyToToken</c> mapped to the same token
/// string, and every unlisted VK returns null exactly as
/// <c>KeyToToken</c>'s default case did (Esc, PrintScreen, IME keys
/// and the like are skipped, never guessed). Modifier VKs (left,
/// right and generic) resolve to the same "Ctrl"/"Shift"/"Alt"/
/// "Win" tokens <c>EventToken</c> produced. Localized tokens
/// (Numpad, PageUp, arrows, CapsLock, ...) resolve through the same
/// <c>Localizer</c> keys <c>KeyToToken</c> used, so a recorded chord
/// still parses through <c>KeyChord</c> in either language.
/// Semantics preserved exactly: tap mode (only DOWN non-modifier
/// → hotkey chord from the recorder's modifier snapshot + token,
/// <c>KeyChord</c>-validated), up/down mode (every event →
/// keydown/keyup step), gap clamped 0..<c>MaxDelayMs</c> written to
/// the PREVIOUS step (returned, never applied here), and a skipped
/// event (tap-mode UP/modifier, unmapped key, or invalid chord)
/// returning a null step and leaving the gap clock untouched.
/// There is no step-count limit.</summary>
public static class RecordStepBuilder
{
    // Win32 virtual-key codes (left/right/generic modifier variants).
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    /// <summary>True for a modifier virtual key (either side, plus
    /// the generic VK_SHIFT/VK_CONTROL/VK_MENU codes the hook never
    /// emits but a synthetic record could carry). Mirrors the
    /// modal's <c>IsModifierKey</c> over WKey.</summary>
    public static bool IsModifierVirtualKey(int virtualKey) => virtualKey is
        VkShift or VkControl or VkMenu
        or VkLShift or VkRShift
        or VkLControl or VkRControl
        or VkLMenu or VkRMenu
        or VkLWin or VkRWin;

    /// <summary>Virtual-key code → chord token, or null for a key
    /// the chord parser cannot represent. This is the whole table:
    /// the modal's <c>EventToken</c> + <c>KeyToToken</c>, re-expressed
    /// over the VK codes the hook actually reports.</summary>
    public static string? TokenForVirtualKey(int virtualKey)
    {
        if (IsModifierVirtualKey(virtualKey))
        {
            return virtualKey switch
            {
                VkLShift or VkRShift or VkShift => "Shift",
                VkLControl or VkRControl or VkControl => "Ctrl",
                VkLMenu or VkRMenu or VkMenu => "Alt",
                _ => "Win",
            };
        }
        // Letters and digits: VK codes are the ASCII codes.
        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            return ((char)virtualKey).ToString();
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
            return ((char)virtualKey).ToString();
        // Numpad digits: same "Numpad" prefix KeyToToken used (S0947).
        if (virtualKey >= 0x60 && virtualKey <= 0x69)
            return Localizer.Get("S0947") + (virtualKey - 0x60).ToString();
        // Function keys F1..F24.
        if (virtualKey >= 0x70 && virtualKey <= 0x87)
            return "F" + (virtualKey - 0x70 + 1).ToString();
        return virtualKey switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => Localizer.Get("S0951"),
            0x22 => Localizer.Get("S0952"),
            0x26 => Localizer.Get("S0482"),
            0x28 => Localizer.Get("S0483"),
            0x25 => Localizer.Get("S0953"),
            0x27 => Localizer.Get("S0954"),
            0x14 => Localizer.Get("S0955"),
            0x91 => Localizer.Get("S0956"),
            0x90 => Localizer.Get("S0957"),
            0x13 => "Pause",
            0x5D => Localizer.Get("S0053"),
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            _ => null,
        };
    }

    /// <summary>Converts one recorded key event to a macro step.
    /// Returns a null step for every event the modal skipped, with
    /// the gap clock returned unchanged: tap-mode UP, tap-mode
    /// modifier, unmapped key, or invalid chord. On an emitted step, the
    /// gap (when the Record Delay rule applies: checkbox on, a
    /// previous event exists, and at least one step exists) is
    /// returned for the caller to write onto the PREVIOUS step, and
    /// the new clock value is the event's own proc-time stamp - the
    /// caller stores it only on emit, never on a skip.</summary>
    public static (ActionStep? Step, int? GapToPreviousMs, DateTimeOffset? NewLastRecordAt) Build(
        KeyboardRecorder.KeyRecord record,
        bool upDownChecked,
        bool recordDelayEnabled,
        DateTimeOffset? lastRecordAt,
        int existingStepCount)
    {
        string? token = TokenForVirtualKey(record.VirtualKey);
        if (token is null)
            return (null, null, lastRecordAt);
        ActionStep step;
        if (upDownChecked)
        {
            step = new ActionStep
            {
                ActionId = record.Down ? "keydown" : "keyup",
                Params = new JsonObject { ["key"] = token },
                DelayAfterMs = 0,
            };
        }
        else
        {
            // Tap mode: only a DOWN of a non-modifier becomes a
            // step; the modifier snapshot lives on in the record
            // for the next chord.
            if (!record.Down || IsModifierVirtualKey(record.VirtualKey))
                return (null, null, lastRecordAt);
            var parts = new List<string>();
            if (record.Ctrl) parts.Add("Ctrl");
            if (record.Shift) parts.Add("Shift");
            if (record.Alt) parts.Add("Alt");
            if (record.Win) parts.Add("Win");
            parts.Add(token);
            string chord = string.Join("+", parts);
            if (!KeyChord.TryParse(chord, out _, out _))
                return (null, null, lastRecordAt);
            step = new ActionStep
            {
                ActionId = "hotkey",
                Params = new JsonObject { ["keys"] = chord },
                DelayAfterMs = 0,
            };
        }
        int? gap = null;
        if (recordDelayEnabled && lastRecordAt is not null && existingStepCount > 0)
        {
            gap = (int)Math.Clamp(
                (record.At - lastRecordAt.Value).TotalMilliseconds, 0, MacroAction.MaxDelayMs);
        }
        return (step, gap, record.At);
    }
}

/// <summary>§137 E1 - record-time exclusion of the master-hotkey
/// chords. The low-level recording hook NEVER swallows a key
/// (<see cref="KeyboardRecorder"/> always calls CallNextHookEx), so
/// pressing a master chord while recording - above all the Toggle
/// Recording chord used to STOP a recording - would otherwise be
/// recorded as macro steps, and replaying such a step would re-send
/// the master chord itself. This filter drops exactly those events,
/// and nothing else: it only affects what is RECORDED, never hook
/// forwarding.
/// WPF-FREE like <see cref="RecordStepBuilder"/>: the only inputs
/// are hook records and chord strings, so the harness links this
/// file unchanged. One instance per recording session, created
/// from a snapshot of the settings at recording start (the chord
/// set is fixed for the session); both recording consumers consult
/// it inside their dispatched handler, BEFORE
/// <see cref="RecordStepBuilder.Build"/>.
/// Why stateful, and why buffer modifier downs: in up/down mode a
/// chord's modifier keydowns are emitted as steps BEFORE the main
/// key arrives, and only the main-key down completes the chord -
/// so a stateless per-event drop would leave unmatched modifier
/// keydown steps behind. Modifier downs are therefore BUFFERED
/// until the press resolves: a main-key down completing a master
/// chord discards the buffer and suppresses every later up of that
/// press; any other resolution (a non-master main key, or a
/// modifier up) releases the buffer in original order, so ordinary
/// chords record byte-identically to before. In tap mode the
/// buffered downs would be skipped by the builder anyway, so the
/// same code path is harmless there. Comparison is by chord
/// IDENTITY - the modifier set from the record's snapshot flags
/// plus the main virtual key, canonicalized through
/// <see cref="KeyChord"/> - never by bare VK: a lone Ctrl press is
/// not the Ctrl+F12 chord, and Ctrl+Shift+F12 is not Ctrl+F12.
/// A buffered modifier down still pending when the session ends is
/// DISCARDED with the filter instance (consumers drop the
/// reference at Stop): its key-up can never be recorded once the
/// hook stops, so emitting it would leave exactly the unmatched
/// keydown this filter exists to prevent. Consequence, intended:
/// a step identical to an assigned master chord cannot be recorded
/// while that chord is assigned - unassign the chord first.
/// Only chords <see cref="KeyChord"/> can parse are excluded, which
/// is exactly the set RegisterHotKey can register; an unparseable
/// or empty chord text contributes nothing.</summary>
public sealed class MasterChordExclusion
{
    // Modifier bitmask: Ctrl=1, Shift=2, Alt=4, Win=8. Side-agnostic
    // (left/right variants collapse), matching RegisterHotKey's
    // MOD_CONTROL/MOD_SHIFT/MOD_ALT/MOD_WIN handling.
    private readonly HashSet<(int Modifiers, int MainKey)> _chords;
    // Virtual keys currently physically down, as seen by this
    // filter (mirrors the recorder's down-set for events that
    // reached the dispatched handler, including buffered ones).
    private readonly HashSet<int> _held = new();
    // Keys belonging to a press already judged to be a master
    // chord: their remaining events (above all the ups) are dropped.
    private readonly HashSet<int> _suppressed = new();
    // Modifier downs not yet resolved: released in order when the
    // press turns out not to be a master chord, discarded when it
    // turns out to be one.
    private readonly List<KeyboardRecorder.KeyRecord> _pending = new();

    public MasterChordExclusion(IEnumerable<string?> chordTexts)
    {
        _chords = new HashSet<(int, int)>();
        foreach (var text in chordTexts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!KeyChord.TryParse(text, out var chord, out _) || chord is null) continue;
            _chords.Add((ModifierMask(chord.Modifiers), chord.MainKey));
        }
    }

    /// <summary>The seven master chords, snapshotted once at
    /// recording start. Empty/unassigned chords contribute nothing.</summary>
    public static MasterChordExclusion FromSettings(DeckSettings settings) => new(new[]
    {
        settings.PauseHotkey,
        settings.ToggleGlobalHotkey,
        settings.NextProfileHotkey,
        settings.PrevProfileHotkey,
        settings.ToggleRecordingHotkey,
        settings.TogglePlaybackHotkey,
        settings.PauseResumePlaybackHotkey,
    });

    public bool HasChords => _chords.Count > 0;

    private static int ModifierMask(IEnumerable<ushort> modifiers)
    {
        int mask = 0;
        foreach (var mod in modifiers)
        {
            if (mod == 0x11) mask |= 1; // Ctrl
            else if (mod == 0x10) mask |= 2; // Shift
            else if (mod == 0x12) mask |= 4; // Alt
            else if (mod == 0x5B || mod == 0x5C) mask |= 8; // Win
        }
        return mask;
    }

    private static int SnapshotMask(KeyboardRecorder.KeyRecord record)
        => (record.Ctrl ? 1 : 0) | (record.Shift ? 2 : 0) | (record.Alt ? 4 : 0) | (record.Win ? 8 : 0);

    /// <summary>Feeds one hook event; returns the events the consumer
    /// must pass to <see cref="RecordStepBuilder.Build"/>, in
    /// original order - zero or more. An event completing a master
    /// chord returns empty and discards any buffered modifier downs
    /// of that press; a modifier down returns empty until resolved;
    /// every other event is returned (buffered modifier downs are
    /// released ahead of it when the press resolves as ordinary).
    /// With no chords configured this is a pure pass-through, so an
    /// empty chord set changes nothing. Call only from the
    /// dispatched handler, in hook order: the held/suppressed
    /// tracking assumes the FIFO order the dispatcher guarantees.</summary>
    public IReadOnlyList<KeyboardRecorder.KeyRecord> Feed(KeyboardRecorder.KeyRecord record)
    {
        if (_chords.Count == 0) return new[] { record };
        if (record.Down)
        {
            // A down for a key tracked as suppressed can only be a
            // FRESH press: the previous press ended while the hook
            // was down (pause/resume restarts the hook, and the
            // recorder never reports the intervening up).
            _suppressed.Remove(record.VirtualKey);
            _held.Add(record.VirtualKey);
            if (RecordStepBuilder.IsModifierVirtualKey(record.VirtualKey))
            {
                // Buffer: unknowable yet whether this modifier will
                // complete a master chord. Replace a stale buffered
                // down for the same key (hook restart re-report).
                _pending.RemoveAll(r => r.VirtualKey == record.VirtualKey);
                _pending.Add(record);
                return Array.Empty<KeyboardRecorder.KeyRecord>();
            }
            // Main-key down completes a chord: the snapshot flags
            // carry every modifier held at this instant, including
            // one held before recording started (the recorder seeds
            // its snapshot at Start), so press order is irrelevant.
            if (_chords.Contains((SnapshotMask(record), record.VirtualKey)))
            {
                foreach (var vk in _held)
                    if (RecordStepBuilder.IsModifierVirtualKey(vk)) _suppressed.Add(vk);
                _suppressed.Add(record.VirtualKey);
                _pending.Clear();
                return Array.Empty<KeyboardRecorder.KeyRecord>();
            }
            if (_pending.Count == 0) return new[] { record };
            var released = new List<KeyboardRecorder.KeyRecord>(_pending) { record };
            _pending.Clear();
            return released;
        }
        // Key up: a suppressed key's up is dropped, so no partial
        // keydown/keyup pair is ever left behind in up/down mode.
        bool wasSuppressed = _suppressed.Remove(record.VirtualKey);
        _held.Remove(record.VirtualKey);
        if (wasSuppressed) return Array.Empty<KeyboardRecorder.KeyRecord>();
        // A modifier up ends its press without completing a chord:
        // release the buffered downs ahead of it so the pair stays
        // ordered (other still-held modifiers release too - their
        // downs precede this up in real time, and any later main
        // key is judged from its own snapshot flags).
        if (_pending.Count > 0 && RecordStepBuilder.IsModifierVirtualKey(record.VirtualKey))
        {
            var released = new List<KeyboardRecorder.KeyRecord>(_pending) { record };
            _pending.Clear();
            return released;
        }
        return new[] { record };
    }
}
