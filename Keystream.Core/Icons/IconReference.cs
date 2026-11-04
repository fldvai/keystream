using System.IO;
using Keystream.Core.Localization;

namespace Keystream.Core.Icons;

/// <summary>
/// Uploaded tile icons (v3.4.0): a tile's stored Icon value is either a
/// built-in vector key ("mic") or a file reference "file:&lt;name&gt;.png"
/// pointing into the single Icons folder under the app data directory.
/// These helpers are pure (no WPF, no I/O) so the self-check can pin
/// them; the WPF conversion itself lives in the app's IconStore.
/// </summary>
public static class IconReference
{
    public const string FilePrefix = "file:";

    /// <summary>Uploaded icons are always stored at most this large on
    /// either side - the tile draws them at 48 px, so 128 px stays crisp
    /// on HiDPI while keeping every stored file tiny.</summary>
    public const int MaxDimension = 128;

    public static bool IsFileIcon(string? icon)
        => icon is not null && icon.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file name inside the Icons folder, or null when the
    /// stored value is not a file icon.</summary>
    public static string? FileName(string? icon)
    {
        if (!IsFileIcon(icon)) return null;
        string name = icon![FilePrefix.Length..].Trim();
        return name.Length == 0 ? null : name;
    }

    public static string IconValue(string fileName) => FilePrefix + fileName;

    /// <summary>Content-addressed file name: identical image bytes always
    /// map to the same file, so re-uploading an icon never duplicates
    /// storage and names never collide or carry user-supplied mess.</summary>
    public static string FileNameForHash(string hexHash)
        => hexHash[..Math.Min(16, hexHash.Length)].ToLowerInvariant() + ".png";

    /// <summary>Indonesian rejection reason when a file exceeds the
    /// configured upload limit, or null when it fits.</summary>
    public static string? ValidateSize(long bytes, long maxBytes)
        => bytes > maxBytes
            ? Localizer.Format("S1065", FormatSize(bytes), FormatSize(maxBytes))
            : null;

    /// <summary>Best-effort website for an application's executable
    /// (v3.5.1 web fallback): the exe name, letters/digits only,
    /// lowercased, + ".com" - "WhatsApp.exe" → "whatsapp.com".
    /// Null when no usable name remains.</summary>
    public static string? GuessDomain(string targetPath)
    {
        // Split manually on BOTH separators: Path.GetFileNameWithoutExtension
        // is platform-specific, and the self-check runs on Linux where a
        // backslash is not a separator - a Windows path would otherwise
        // be treated as one giant file name.
        string file = (targetPath ?? "")
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "";
        int dot = file.LastIndexOf('.');
        string name = dot > 0 ? file[..dot] : file;
        string clean = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return clean.Length >= 2 ? clean + ".com" : null;
    }

    public static string FormatSize(long bytes)
        => bytes < 1024 ? Localizer.Format("S1066", bytes)
            : bytes < 1024 * 1024 ? Localizer.Format("S1067", bytes / 1024.0)
            : Localizer.Format("S1068", bytes / 1048576.0);
}
