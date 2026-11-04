using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Hotkey;

/// <summary>
/// Releases one key (keyup step, §65): sends ONLY the key-up stroke -
/// the counterpart of KeyDownAction. Releasing a key that is not held
/// is a harmless no-op to Windows, so an unmatched up step can never
/// corrupt state. Created by down/up recording, never from the card
/// editor. Params: "key", a single token (see KeyChord.TryParseSingleKey).
/// TestAsync is validate-only, like KeyDownAction: a bare release-test
/// could drop a modifier the user is physically holding.</summary>
public sealed class KeyUpAction : IAction
{
    public ActionDescriptor Descriptor => new("keyup", Localizer.Get("S1080"), Localizer.Get("S1071"),
        Localizer.Get("S1081"));

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
            { new SendInputNative.KeyStroke(vk, true, KeyChord.IsExtendedKey(vk)) });
        return Task.FromResult(sent == 1
            ? ActionResult.Ok()
            : ActionResult.Fail(Localizer.Format("S1143", errno)));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
    {
        var vr = Validate(parameters);
        return Task.FromResult(vr.IsValid
            ? ActionResult.Ok()
            : ActionResult.Fail(string.Join("; ", vr.Errors)));
    }
}
