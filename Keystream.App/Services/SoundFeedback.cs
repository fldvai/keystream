using System.IO;
using System.Media;

namespace Keystream.App.Services;

/// <summary>
/// Low-latency tactile audio feedback for deck keypresses.
/// Generates an in-memory 18ms mechanical switch click sample without external assets.
/// </summary>
public static class SoundFeedback
{
    private static readonly SoundPlayer? Player;

    static SoundFeedback()
    {
        try
        {
            var stream = GenerateClickWavStream();
            Player = new SoundPlayer(stream);
            Player.LoadAsync();
        }
        catch
        {
            // Fallback: silent if audio device is unavailable
        }
    }

    private static long _lastClickTimestamp;
    private static readonly long MinIntervalTicks = (long)(System.Diagnostics.Stopwatch.Frequency * 0.08); // 80ms debounce

    public static void PlayClick()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now - _lastClickTimestamp < MinIntervalTicks)
            return;
        _lastClickTimestamp = now;

        try
        {
            Player?.Play();
        }
        catch
        {
            // Silent best-effort
        }
    }

    private static Stream GenerateClickWavStream()
    {
        const int sampleRate = 44100;
        const int samples = 750; // ~17ms
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + samples * 2);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // PCM chunk size
            bw.Write((short)1); // PCM
            bw.Write((short)1); // Mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // Byte rate
            bw.Write((short)2); // Block align
            bw.Write((short)16); // Bits per sample
            // data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(samples * 2);

            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / sampleRate;
                // Dual-tone click with fast exponential decay
                double decay = Math.Exp(-i / 120.0);
                double wave = (0.65 * Math.Sin(2.0 * Math.PI * 1300.0 * t) + 0.35 * Math.Sin(2.0 * Math.PI * 750.0 * t)) * decay;
                short val = (short)(wave * 10000);
                bw.Write(val);
            }
        }
        ms.Position = 0;
        return ms;
    }
}

