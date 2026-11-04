using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Actions.Common;

namespace Keystream.Actions.Deck;

/// <summary>
/// "deck" - page/layer switching from a deck key (UI v2.5, the missing
/// half of layers: previously switching pages needed the mouse). All app
/// knowledge arrives through delegates, so the action itself stays pure
/// and testable: the host supplies the active profile's pages, the
/// current page id, and the switch operation.
/// Params: "command": goto (needs "pageId", "pageName" kept for display),
/// next, prev (wrap-around within the active profile), home, and
/// momentary (v3.3.0, the Layer Modifier: needs "pageId"; on the
/// Momentary trigger's key-down it switches to that page remembering
/// the current one, on MomentaryRelease it switches back - a stack, so
/// nested holds unwind in reverse order).
/// </summary>
public sealed class PageSwitchAction : IAction
{
    private readonly Func<IReadOnlyList<(string Id, string Name)>> _pages;
    private readonly Func<string> _currentPageId;
    private readonly Func<string, bool> _switchTo;

    public PageSwitchAction(
        Func<IReadOnlyList<(string Id, string Name)>> pages,
        Func<string> currentPageId,
        Func<string, bool> switchTo)
    {
        _pages = pages;
        _currentPageId = currentPageId;
        _switchTo = switchTo;
    }

    public ActionDescriptor Descriptor => new("deck", Localizer.Get("S1073"), Localizer.Get("S1074"),
        Localizer.Get("S1075"));

    private readonly List<string> _momentaryStack = new();
    private readonly object _momentaryGate = new();

    /// <summary>Unwinds all held momentary layers (Panic/passthrough,
    /// §45): returns to the page active before the outermost hold and
    /// clears the stack. Without this, a layer held when passthrough
    /// engages stays stuck - its MomentaryRelease can never fire.</summary>
    public void ResetMomentary()
    {
        string? back;
        lock (_momentaryGate)
        {
            if (_momentaryStack.Count == 0) return;
            back = _momentaryStack[0];
            _momentaryStack.Clear();
        }
        if (back is not null && back != _currentPageId())
            _switchTo(back);
    }

    private static string Normalize(string? raw)
        => (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

    public ValidationResult Validate(JsonObject parameters)
    {
        string command = Normalize(ParamReader.GetString(parameters, "command", "Command"));
        switch (command)
        {
            case "next":
            case "prev":
            case "home":
                return ValidationResult.Valid;
            case "goto":
            case "gotopage":
            case "momentary":
                string? pageId = ParamReader.GetString(parameters, "pageId");
                if (string.IsNullOrWhiteSpace(pageId))
                    return ValidationResult.Invalid(Localizer.Get("S1129"));
                var pages = _pages();
                if (pages.Count > 0 && pages.All(p => p.Id != pageId))
                    return ValidationResult.Invalid(Localizer.Get("S1130"));
                return ValidationResult.Valid;
            default:
                return ValidationResult.Invalid(Localizer.Format("S1131", command));
        }
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        var v = Validate(parameters);
        if (!v.IsValid) return Task.FromResult(ActionResult.Fail(string.Join("; ", v.Errors)));

        string command = Normalize(ParamReader.GetString(parameters, "command", "Command"));
        var pages = _pages();
        if (pages.Count == 0)
            return Task.FromResult(ActionResult.Fail(Localizer.Get("S1132")));

        if (command == "momentary")
        {
            string target = ParamReader.GetString(parameters, "pageId")!;
            if (context.FiredKind == TriggerKind.MomentaryRelease)
            {
                string? back = null;
                lock (_momentaryGate)
                {
                    if (_momentaryStack.Count > 0)
                    {
                        back = _momentaryStack[^1];
                        _momentaryStack.RemoveAt(_momentaryStack.Count - 1);
                    }
                }
                if (back is null || back == _currentPageId())
                    return Task.FromResult(ActionResult.Ok()); // nothing held (or already back) - no-op
                return Task.FromResult(_switchTo(back)
                    ? ActionResult.Ok()
                    : ActionResult.Fail(Localizer.Get("S1133")));
            }
            string current = _currentPageId();
            if (current == target)
                return Task.FromResult(ActionResult.Ok()); // already on that layer - don't stack
            lock (_momentaryGate) _momentaryStack.Add(current);
            if (!_switchTo(target))
            {
                lock (_momentaryGate) _momentaryStack.RemoveAt(_momentaryStack.Count - 1);
                return Task.FromResult(ActionResult.Fail(Localizer.Get("S1134")));
            }
            return Task.FromResult(ActionResult.Ok());
        }

        string targetId;
        if (command == "home")
        {
            if (pages.Count == 0)
                return Task.FromResult(ActionResult.Fail(Localizer.Get("S1135")));
            targetId = pages[0].Id; // halaman utama = halaman pertama profil
        }
        else if (command is "goto" or "gotopage")
        {
            targetId = ParamReader.GetString(parameters, "pageId")!;
        }
        else
        {
            if (pages.Count < 2)
                return Task.FromResult(ActionResult.Fail(Localizer.Get("S1135")));
            int idx = pages.ToList().FindIndex(p => p.Id == _currentPageId());
            if (idx < 0) idx = 0;
            int next = command == "next"
                ? (idx + 1) % pages.Count
                : (idx - 1 + pages.Count) % pages.Count;
            targetId = pages[next].Id;
        }

        return Task.FromResult(_switchTo(targetId)
            ? ActionResult.Ok()
            : ActionResult.Fail(Localizer.Get("S1136")));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
