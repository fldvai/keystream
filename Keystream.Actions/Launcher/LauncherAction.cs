using System.Diagnostics;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Launcher;

/// <summary>
/// Launches an application, file, folder, or URL via the shell
/// (UseShellExecute), so http(s) links open in the browser and documents
/// open in their associated app. §170: a missing file is caught first by
/// LauncherTargetResolver and fails with S1810 naming the file; a failed
/// launch that the pre-check cannot determine still throws from
/// Process.Start and is converted into an honest Fail with the OS
/// message (S1161) - the legacy LauncherAction swallowed the same cases.
/// Params: "target" (v2) or legacy "Target"; optional "arguments"
/// (legacy "Arguments") and "workingDirectory".
/// </summary>
public sealed class LauncherAction : IAction
{
    public ActionDescriptor Descriptor => new("launcher", Localizer.Get("S1082"), Localizer.Get("S1071"),
        Localizer.Get("S1083"));

    public ValidationResult Validate(JsonObject parameters)
    {
        string? target = ParamReader.GetString(parameters, "target", "Target");
        return string.IsNullOrWhiteSpace(target)
            ? ValidationResult.Invalid(Localizer.Get("S1159"))
            : ValidationResult.Valid;
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string? target = ParamReader.GetString(parameters, "target", "Target");
        if (string.IsNullOrWhiteSpace(target))
            return Task.FromResult(ActionResult.Fail(Localizer.Get("S1160")));

        // §170 Task 3: missing .exe fails here with a dedicated message
        // naming the file (S1810) instead of raw OS exception text.
        // URLs skip the check; Unknown (ambiguous) proceeds as before.
        if (!LauncherTargetResolver.IsUrlLike(target)
            && LauncherTargetResolver.CheckTarget(target) == LauncherTargetStatus.NotFound)
        {
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1810", target)));
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = target,
                Arguments = ParamReader.GetString(parameters, "arguments", "Arguments") ?? "",
                UseShellExecute = true,
            };
            string? workDir = ParamReader.GetString(parameters, "workingDirectory", "WorkingDirectory");
            if (!string.IsNullOrWhiteSpace(workDir)) info.WorkingDirectory = workDir;

            Process.Start(info);
            return Task.FromResult(ActionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1161", target, ex.Message)));
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
