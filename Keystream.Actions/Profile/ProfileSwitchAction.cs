using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Profile;

/// <summary>
/// "profile" - switch the MAIN (active) profile from a deck key (v3.3.0),
/// the profile-level sibling of PageSwitchAction: all app knowledge
/// arrives through delegates so the action stays pure and testable.
/// Params: "profileId" (required), "profileName" kept for display.
/// </summary>
public sealed class ProfileSwitchAction : IAction
{
    private readonly Func<IReadOnlyList<(string Id, string Name)>> _profiles;
    private readonly Func<string> _currentProfileId;
    private readonly Func<string, bool> _switchTo;

    public ProfileSwitchAction(
        Func<IReadOnlyList<(string Id, string Name)>> profiles,
        Func<string> currentProfileId,
        Func<string, bool> switchTo)
    {
        _profiles = profiles;
        _currentProfileId = currentProfileId;
        _switchTo = switchTo;
    }

    public ActionDescriptor Descriptor => new("profile", Localizer.Get("S0594"), Localizer.Get("S1074"),
        Localizer.Get("S1091"));

    public ValidationResult Validate(JsonObject parameters)
    {
        string? profileId = ParamReader.GetString(parameters, "profileId");
        if (string.IsNullOrWhiteSpace(profileId))
            return ValidationResult.Invalid(Localizer.Get("S1201"));
        var profiles = _profiles();
        if (profiles.Count > 0 && profiles.All(p => p.Id != profileId))
            return ValidationResult.Invalid(Localizer.Get("S1202"));
        return ValidationResult.Valid;
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        var v = Validate(parameters);
        if (!v.IsValid) return Task.FromResult(ActionResult.Fail(string.Join("; ", v.Errors)));
        string targetId = ParamReader.GetString(parameters, "profileId")!;
        if (targetId == _currentProfileId())
            return Task.FromResult(ActionResult.Ok()); // already there - nothing to do
        return Task.FromResult(_switchTo(targetId)
            ? ActionResult.Ok()
            : ActionResult.Fail(Localizer.Get("S1203")));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
