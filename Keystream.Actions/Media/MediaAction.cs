using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Media;

/// <summary>
/// Media & volume control through ONE mechanism only: SendInput with the
/// dedicated virtual-key codes (via the shared SendInputNative helper).
/// This is the direct fix for the audited MediaAction (finding B2): its
/// "fallback" was unconditional, so every press fired SendInput AND
/// keybd_event AND an APPCOMMAND - volume moved 3 steps per press and
/// mute/play-pause toggled twice for a net no-op. Here a command produces
/// exactly one key-down + one key-up, and a failed SendInput is reported
/// as a failure with its errno.
/// Params: "command" (v2) or legacy "Command": PlayPause, Stop, Next,
/// Prev/Previous, VolumeUp, VolumeDown, Mute, MediaPlayer
/// (case/space-insensitive),
/// plus MicMute - the one command that does NOT go through SendInput:
/// it toggles the default microphone endpoint's mute via Core Audio
/// (see MicMute), because Windows has no mic-mute virtual key.
/// §156 adds VolumeDial: NOT a fixed key - it consumes
/// ActionContext.WheelDelta (signed WHEEL_DELTA units) and sends one
/// VK_VOLUME_UP/DOWN pair per whole notch via the same SendInput path;
/// zero/sub-notch delta is a no-op.
/// </summary>
public sealed class MediaAction : IAction
{
    public ActionDescriptor Descriptor => new("media", Localizer.Get("S0583"), Localizer.Get("S1071"),
        Localizer.Get("S1087"));

    /// <summary>§156: the SendInput sender, injectable for tests. The
    /// default is the real <see cref="SendInputNative.SendKeys"/> - the
    /// SAME helper every other media command uses; no duplicated
    /// P/Invoke, no new send mechanism. A harness passes a fake and
    /// the VM never touches real system volume. AppHost uses the
    /// parameterless constructor, i.e. the real sender.</summary>
    private readonly Func<IReadOnlyList<SendInputNative.KeyStroke>, (uint Sent, int Errno)> _sendKeys;

    public MediaAction()
        : this(null)
    {
    }

    public MediaAction(Func<IReadOnlyList<SendInputNative.KeyStroke>, (uint Sent, int Errno)>? sendKeys)
    {
        _sendKeys = sendKeys ?? SendInputNative.SendKeys;
    }

    public static readonly IReadOnlyDictionary<string, ushort> Commands = new Dictionary<string, ushort>
    {
        ["playpause"] = 0xB3, // VK_MEDIA_PLAY_PAUSE
        ["stop"] = 0xB2,      // VK_MEDIA_STOP
        ["next"] = 0xB0,      // VK_MEDIA_NEXT_TRACK
        ["prev"] = 0xB1,      // VK_MEDIA_PREV_TRACK
        ["previous"] = 0xB1,
        ["volumeup"] = 0xAF,  // VK_VOLUME_UP
        ["volumedown"] = 0xAE,
        ["mute"] = 0xAD,      // VK_VOLUME_MUTE
        ["mediaplayer"] = 0xB5, // VK_LAUNCH_MEDIA_SELECT - the keyboard "Media"
                                // key; Windows opens its default media player
    };

    /// <summary>Normalized form of the MicMute command (no VK exists for it).</summary>
    public const string MicMuteCommand = "micmute";

    /// <summary>§156: normalized form of the VolumeDial command. It has
    /// no fixed VK: the direction comes from the sign of the wheel
    /// delta (see <see cref="DialVolumePlan"/>), so it is NOT an entry
    /// in <see cref="Commands"/> - a Commands entry would imply one
    /// key per execution, and the dial sends one pair per notch.</summary>
    public const string VolumeDialCommand = "volumedial";

    /// <summary>Unknown-command message: the S1180 text plus the
    /// valid-choices suffix (S1903), so the error guides the user.
    /// The list is the normalized command names, sorted.</summary>
    private static string UnknownCommandMessage(string command)
        => Localizer.Format("S1180", command) + " " + Localizer.Format("S1903",
            string.Join(", ", Commands.Keys.Append(MicMuteCommand).Append(VolumeDialCommand).OrderBy(k => k, StringComparer.Ordinal)));

    public static string NormalizeCommand(string? raw)
        => (raw ?? "").Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

    public ValidationResult Validate(JsonObject parameters)
    {
        string command = NormalizeCommand(ParamReader.GetString(parameters, "command", "Command"));
        return Commands.ContainsKey(command) || command == MicMuteCommand || command == VolumeDialCommand
            ? ValidationResult.Valid
            : ValidationResult.Invalid(UnknownCommandMessage(command));
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string command = NormalizeCommand(ParamReader.GetString(parameters, "command", "Command"));
        if (command == MicMuteCommand)
        {
            var (ok, _, error) = MicMute.Toggle();
            return Task.FromResult(ok
                ? ActionResult.Ok()
                : ActionResult.Fail(Localizer.Format("S1179", error)));
        }
        if (command == VolumeDialCommand)
        {
            // §156 dial volume: a pure consumer of the wheel delta the
            // engine coalesced for this job. Zero / sub-notch delta is
            // a no-op success; otherwise one VK_VOLUME_UP/DOWN pair
            // per notch, in ONE SendInput batch via the shared helper
            // (same path as VolumeUp/VolumeDown above). No state.
            var plan = DialVolumePlan.Plan(context.WheelDelta);
            if (plan is null) return Task.FromResult(ActionResult.Ok());
            var strokes = DialVolumePlan.BuildStrokes(plan.Value);
            var (sent, errno) = _sendKeys(strokes);
            return Task.FromResult(sent == (uint)strokes.Count
                ? ActionResult.Ok()
                : ActionResult.Fail(Localizer.Format("S1139", sent, strokes.Count, errno)));
        }
        if (!Commands.TryGetValue(command, out ushort vk))
            return Task.FromResult(ActionResult.Fail(UnknownCommandMessage(command)));

        var (sent2, errno2) = _sendKeys(new[]
        {
            new SendInputNative.KeyStroke(vk, KeyUp: false, Extended: false),
            new SendInputNative.KeyStroke(vk, KeyUp: true, Extended: false),
        });
        return Task.FromResult(sent2 == 2
            ? ActionResult.Ok()
            : ActionResult.Fail(Localizer.Format("S1181", sent2, errno2)));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
