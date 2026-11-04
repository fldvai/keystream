using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Actions.Common;
using Keystream.Actions.Hotkey;

namespace Keystream.Actions.Basic;

/// <summary>
/// "basic" - the everyday editing commands every application already
/// understands, as one-press deck actions (the user's fixed list, v3.3.0).
/// Eight of them are ordinary Ctrl chords and are executed by composing
/// the Hotkey action, so there is exactly one chord-sending path in the
/// app. Web Search is the exception: it is the browser-search virtual
/// key (VK_BROWSER_SEARCH) sent directly, like the media keys.
/// Params: "command": Cut, Copy, Paste, Undo, Redo, All, Find, New,
/// Open, Print, Save, WebSearch (case/space-insensitive).
/// </summary>
public sealed class BasicAction : IAction
{
    public ActionDescriptor Descriptor => new("basic", Localizer.Get("S0575"), Localizer.Get("S1071"),
        Localizer.Get("S0576"));

    private readonly HotkeyAction _hotkey = new();

    public static readonly IReadOnlyDictionary<string, string> Chords =
        new Dictionary<string, string>
        {
            ["cut"] = "Ctrl+X",
            ["copy"] = "Ctrl+C",
            ["paste"] = "Ctrl+V",
            ["undo"] = "Ctrl+Z",
            ["redo"] = "Ctrl+Y",
            ["all"] = "Ctrl+A",
            ["find"] = "Ctrl+F",
            ["new"] = "Ctrl+N",
            ["open"] = "Ctrl+O",
            ["print"] = "Ctrl+P",
            ["save"] = "Ctrl+S",
        };

    /// <summary>Normalized form of the Web Search command (a VK, not a chord).</summary>
    public const string WebSearchCommand = "websearch";

    /// <summary>Unknown-command message: the S1097 text plus the
    /// valid-choices suffix (S1903), so the error guides the user.
    /// The list is the normalized command names, sorted.</summary>
    private static string UnknownCommandMessage(string command)
        => Localizer.Format("S1097", command) + " " + Localizer.Format("S1903",
            string.Join(", ", Chords.Keys.Append(WebSearchCommand).OrderBy(k => k, StringComparer.Ordinal)));
    private const ushort VkBrowserSearch = 0xAA;

    public static string NormalizeCommand(string? raw)
        => (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

    public ValidationResult Validate(JsonObject parameters)
    {
        string command = NormalizeCommand(ParamReader.GetString(parameters, "command", "Command"));
        return Chords.ContainsKey(command) || command == WebSearchCommand
            ? ValidationResult.Valid
            : ValidationResult.Invalid(UnknownCommandMessage(command));
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string command = NormalizeCommand(ParamReader.GetString(parameters, "command", "Command"));
        if (command == WebSearchCommand)
        {
            var (sent, errno) = SendInputNative.SendKeys(new[]
            {
                new SendInputNative.KeyStroke(VkBrowserSearch, KeyUp: false, Extended: false),
                new SendInputNative.KeyStroke(VkBrowserSearch, KeyUp: true, Extended: false),
            });
            return Task.FromResult(sent == 2
                ? ActionResult.Ok()
                : ActionResult.Fail(Localizer.Format("S1096", sent, errno)));
        }
        if (!Chords.TryGetValue(command, out string? chord))
            return Task.FromResult(ActionResult.Fail(UnknownCommandMessage(command)));
        var p = new JsonObject { ["keys"] = chord };
        return _hotkey.ExecuteAsync(p, context);
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
