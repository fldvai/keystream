using System.Text.RegularExpressions;

namespace Keystream.Core.Icons;

/// <summary>
/// Favicon candidates for a page (v3.5.0, pure string/Uri logic so the
/// self-check can pin it): the page's own apple-touch-icon links
/// first (usually the largest artwork), then its icon/shortcut-icon
/// links, then the conventional /favicon.ico fallback. Relative hrefs
/// resolve against the page URL; duplicates collapse.
/// </summary>
public static class FaviconDiscovery
{
    public static IReadOnlyList<Uri> DiscoverCandidates(string html, Uri pageUri)
    {
        var touch = new List<Uri>();
        var icons = new List<Uri>();
        foreach (Match tag in Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            string? rel = Attribute(tag.Value, "rel");
            if (rel is null || !rel.Contains("icon", StringComparison.OrdinalIgnoreCase)) continue;
            string? href = Attribute(tag.Value, "href");
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (!Uri.TryCreate(pageUri, href.Trim(), out var uri)) continue;
            if (rel.Contains("apple-touch-icon", StringComparison.OrdinalIgnoreCase))
            {
                if (!touch.Contains(uri)) touch.Add(uri);
            }
            else if (!icons.Contains(uri))
            {
                icons.Add(uri);
            }
        }
        var result = new List<Uri>(touch);
        result.AddRange(icons);
        var fallback = new Uri(pageUri, "/favicon.ico");
        if (!result.Contains(fallback)) result.Add(fallback);
        return result;
    }

    private static string? Attribute(string tag, string name)
    {
        var m = Regex.Match(tag,
            name + "\\s*=\\s*\"([^\"]*)\"|" + name + "\\s*=\\s*'([^']*)'",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
    }
}
