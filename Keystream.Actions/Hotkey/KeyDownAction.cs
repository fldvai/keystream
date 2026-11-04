using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Hotkey;

/// <summary>
/// Holds one key down (keydown step, §65): sends ONLY the key-down
/// stroke - the matching keyup step (or MacroAction's auto-release
/// guard) releases it. Created by down/up recording, never from the
/// card editor: a hold only makes sense inside a macro's event list.
/// Params: "key", a single token (see KeyChord.TryParseSingleKey).
/// TestAsync never sends: a bare down-test would leave the key held
/// with no matching release. Macro tests still send for real, through
/// MacroAction.ExecuteAsync (which owns the guard).</summary>
public sealed class KeyDownAction : IAction
{
    public ActionDescriptor Descriptor => new("keydown", Localizer.Get("S1078"), Localizer.Get("S1071"),
        Localizer.Get("S1079"));

    public ValidationResult Validate(JsonObject parameters)
    {
        string? key = ParamReader.GetString(parameters, "key");
        return KeyChord.TryParseSingleKey(key, out _, out _)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(Localizer.Format("S1140", key));
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string? key = ParamReader.GetString(parameters, "key");
        if (!KeyChord.TryParseSingleKey(key, out ushort vk, out _))
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1141", key)));
        var (sent, errno) = SendInputNative.SendKeys(new[]
            { new SendInputNative.KeyStroke(vk, false, KeyChord.IsExtendedKey(vk)) });
        return Task.FromResult(sent == 1
            ? ActionResult.Ok()
            : ActionResult.Fail(Localizer.Format("S1142", errno)));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
    {
        var vr = Validate(parameters);
        return Task.FromResult(vr.IsValid
            ? ActionResult.Ok()
            : ActionResult.Fail(string.Join("; ", vr.Errors)));
    }
}
