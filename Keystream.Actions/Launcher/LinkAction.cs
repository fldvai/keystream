using System.Diagnostics;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Launcher;

/// <summary>
/// Opens a web address in the default browser - split out of the old
/// combined "launcher" action (v2.9.24, the user's request) so apps and
/// links each get an editor that fits them. A missing scheme is forgiven:
/// "youtube.com" is opened as "https://youtube.com".
/// Params: "url" (also accepts legacy "target").
/// </summary>
public sealed class LinkAction : IAction
{
    public ActionDescriptor Descriptor => new("link", Localizer.Get("S1084"), Localizer.Get("S1071"),
        Localizer.Get("S1085"));

    public static string NormalizeUrl(string raw)
    {
        string url = raw.Trim();
        if (url.Length == 0 || url.Contains("://")) return url;
        int colon = url.IndexOf(':');
        if (colon > 0 && url[..colon].All(char.IsLetter)) return url; // mailto: etc.
        return "https://" + url;
    }

    public ValidationResult Validate(JsonObject parameters)
    {
        string? url = ParamReader.GetString(parameters, "url", "Url", "target", "Target");
        return string.IsNullOrWhiteSpace(url)
            ? ValidationResult.Invalid(Localizer.Get("S1162"))
            : ValidationResult.Valid;
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string? raw = ParamReader.GetString(parameters, "url", "Url", "target", "Target");
        if (string.IsNullOrWhiteSpace(raw))
            return Task.FromResult(ActionResult.Fail(Localizer.Get("S1163")));
        string url = NormalizeUrl(raw);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return Task.FromResult(ActionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1164", url, ex.Message)));
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
