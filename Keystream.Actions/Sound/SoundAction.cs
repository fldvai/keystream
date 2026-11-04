using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using NAudio.Wave;
using Keystream.Core.Actions;
using Keystream.Actions.Common;

namespace Keystream.Actions.Sound;

/// <summary>
/// Plays a sound file (soundboard) - the action the legacy UI offered
/// ("Sound" in its ActionType list) but the legacy codebase never
/// implemented. Originally WAV-only via System.Media.SoundPlayer;
/// since v2.9.23 it plays through NAudio: WAV/MP3 directly, and
/// M4A/AAC/WMA/FLAC/OGG through Windows Media Foundation codecs.
/// Playback is asynchronous; a new press stops and restarts the sound.
/// Params: "file" (also accepts legacy-style "File"/"Path"/"Target").
/// </summary>
public sealed class SoundAction : IAction
{
    private static readonly string[] SupportedExtensions =
        { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".aif", ".aiff" };

    private static readonly object Gate = new();
    private static IWavePlayer? _output;
    private static AudioFileReader? _reader;

    public ActionDescriptor Descriptor => new("sound", Localizer.Get("S1092"), Localizer.Get("S1093"),
        Localizer.Get("S1094"));

    public ValidationResult Validate(JsonObject parameters)
    {
        string? file = ParamReader.GetString(parameters, "file", "File", "Path", "Target");
        if (string.IsNullOrWhiteSpace(file))
            return ValidationResult.Invalid(Localizer.Get("S1204"));
        string ext = Path.GetExtension(file);
        if (!SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Invalid(
                $"Format '{ext}' tidak didukung. Yang didukung: {string.Join(", ", SupportedExtensions)}.");
        return ValidationResult.Valid;
    }

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string? file = ParamReader.GetString(parameters, "file", "File", "Path", "Target");
        if (string.IsNullOrWhiteSpace(file))
            return Task.FromResult(ActionResult.Fail(Localizer.Get("S1205")));
        if (!File.Exists(file))
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1206", file)));

        try
        {
            lock (Gate)
            {
                StopCurrentLocked();
                var reader = new AudioFileReader(file);
                WaveOutEvent output;
                try
                {
                    output = new WaveOutEvent();
                }
                catch
                {
                    // Konstruktor output gagal: reader sudah dibuat, jangan bocor.
                    reader.Dispose();
                    throw;
                }
                try
                {
                    output.Init(reader);
                }
                catch
                {
                    // Init gagal: reader/output sudah dibuat tapi belum disimpan ke
                    // _reader/_output, jadi catch luar tak bisa Dispose keduanya.
                    output.Dispose();
                    reader.Dispose();
                    throw;
                }
                output.PlaybackStopped += (_, _) =>
                {
                    lock (Gate)
                    {
                        if (ReferenceEquals(_output, output)) StopCurrentLocked();
                    }
                };
                _reader = reader;
                _output = output;
                output.Play();
            }
            return Task.FromResult(ActionResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Fail(Localizer.Format("S1207", file, ex.Message)));
        }
    }

    private static void StopCurrentLocked()
    {
        try { _output?.Stop(); } catch { /* already stopped */ }
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
