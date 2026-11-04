using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows.Media.Imaging;
using Keystream.Actions.Launcher;
using Keystream.Core.Icons;

namespace Keystream.App.Services;

/// <summary>
/// Website favicons (v3.5.0): downloads the first decodable favicon
/// for a URL - the page's own icon links first (FaviconDiscovery),
/// /favicon.ico as the fallback. A browser-like User-Agent is sent
/// because some sites refuse bare clients; failures are silent (null),
/// the editor reports them inline instead of popping dialogs.
/// </summary>
public static class FaviconFetcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static FaviconFetcher()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Keystream/3.5");
    }

    public static async Task<byte[]?> FetchAsync(string rawUrl, CancellationToken ct = default)
    {
        string url = LinkAction.NormalizeUrl(rawUrl);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var pageUri)
            || (pageUri.Scheme != "http" && pageUri.Scheme != "https"))
            return null;

        string? html = null;
        try
        {
            using var page = await Http.GetAsync(pageUri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (page.IsSuccessStatusCode
                && page.Content.Headers.ContentType?.MediaType?.Contains("html") == true)
            {
                var bytes = await page.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length > 512 * 1024) Array.Resize(ref bytes, 512 * 1024);
                html = Encoding.UTF8.GetString(bytes);
            }
        }
        catch
        {
            // Page unreadable (offline, blocked, non-HTML) - the
            // /favicon.ico fallback below still gets its chance.
        }

        var candidates = html is null
            ? new[] { new Uri(pageUri, "/favicon.ico") }
            : FaviconDiscovery.DiscoverCandidates(html, pageUri);
        foreach (var candidate in candidates)
        {
            try
            {
                using var resp = await Http.GetAsync(candidate, ct);
                if (!resp.IsSuccessStatusCode) continue;
                var data = await resp.Content.ReadAsByteArrayAsync(ct);
                if (data.Length == 0 || data.Length > 8 * 1024 * 1024) continue;
                if (Decodes(data)) return data;
            }
            catch
            {
                continue;
            }
        }
        return null;
    }

    private static bool Decodes(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
