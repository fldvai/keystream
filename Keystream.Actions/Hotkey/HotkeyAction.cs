using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Hotkey;

/// <summary>
/// Sends a virtual hotkey chord via SendInput (shared SendInputNative
/// helper) - the standard way to fire F13–F24 (keys no physical keyboard
/// has, so they never collide with real typing) or any modifier
/// combination. One batch: modifiers down, main key down/up, modifiers up
/// in reverse. Partial sends are honest failures.
/// Params: "keys" (v2) or legacy "Key" - a chord string, see KeyChord.
/// </summary>
public sealed class HotkeyAction : IAction
{
    public ActionDescriptor Descriptor => new("hotkey", Localizer.Get("S1076"), Localizer.Get("S1071"),
        Localizer.Get("S1077"));

    public ValidationResult Validate(JsonObject parameters)
    {
        string? keys = ParamReader.GetString(parameters, "keys", "Key");
        return KeyChord.TryParse(keys, out _, out var error)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(Localizer.Format("S1137", error));
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string? keys = ParamReader.GetString(parameters, "keys", "Key");
        if (!KeyChord.TryParse(keys, out var chord, out var error))
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1138", error)));

        var strokes = new List<SendInputNative.KeyStroke>();
        foreach (var mod in chord!.Modifiers)
            strokes.Add(new SendInputNative.KeyStroke(mod, false, KeyChord.IsExtendedKey(mod)));
        bool mainExtended = chord.MainExtended || KeyChord.IsExtendedKey(chord.MainKey);
        strokes.Add(new SendInputNative.KeyStroke(chord.MainKey, false, mainExtended));
        strokes.Add(new SendInputNative.KeyStroke(chord.MainKey, true, mainExtended));
        for (int i = chord.Modifiers.Count - 1; i >= 0; i--)
            strokes.Add(new SendInputNative.KeyStroke(chord.Modifiers[i], true, KeyChord.IsExtendedKey(chord.Modifiers[i])));

        var (sent, errno) = SendInputNative.SendKeys(strokes);
        if (sent == (uint)strokes.Count)
            return Task.FromResult(ActionResult.Ok());
        // Parsial (§45): modifier-down (dan mungkin main-down) bisa
        // sudah mendarat tanpa pasangannya - lepas semuanya (main lalu
        // modifier terbalik) agar Ctrl/Shift/tombol utama tidak nyangkut.
        var ups = new List<SendInputNative.KeyStroke>
        {
            new(chord.MainKey, true, mainExtended),
        };
        for (int i = chord.Modifiers.Count - 1; i >= 0; i--)
            ups.Add(new(chord.Modifiers[i], true, KeyChord.IsExtendedKey(chord.Modifiers[i])));
        SendInputNative.SendKeys(ups);
        return Task.FromResult(ActionResult.Fail(
            Localizer.Format("S1139", sent, strokes.Count, errno)));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
