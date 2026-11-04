using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using Keystream.App.ViewModels;
using Keystream.App.Views;
using Keystream.Core.Config;
using Keystream.Core.Localization;

namespace Keystream.App.Services;

/// <summary>One GitHub release, as the update check reports it.</summary>
public sealed record ReleaseInfo(string Tag, string PageUrl, string? Notes, string? PublishedAt, string? DownloadUrl);

/// <summary>Outcome of one update check: either a release, or a
/// pre-localized failure reason (HTTP code / offline / malformed).</summary>
public sealed record UpdateCheckResult(ReleaseInfo? Release, string? FailureReason);

/// <summary>Update check and automated setup downloader service.</summary>
public static class UpdateChecker
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/fldvai/keystream/releases/latest";
    private const int MaxReleaseNotesLength = 500;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders =
        {
            UserAgent = { new ProductInfoHeaderValue("Keystream", "1.0") },
            Accept = { new MediaTypeWithQualityHeaderValue("application/vnd.github+json") },
        },
    };

    public static Version CurrentAppVersion
    {
        get
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)) : new Version(1, 0, 0);
        }
    }

    public static bool TryParseReleaseVersion(string tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;
        string clean = tag.Trim().TrimStart('v', 'V');
        int dash = clean.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) clean = clean[..dash];
        return Version.TryParse(clean, out version);
    }

    public static bool IsUpdateAvailable(string remoteTag, out Version? remoteVersion)
    {
        if (TryParseReleaseVersion(remoteTag, out remoteVersion) && remoteVersion != null)
        {
            return remoteVersion > CurrentAppVersion;
        }
        return false;
    }

    /// <summary>B-03: "baru" berarti tag berubah saja. Gate utama
    /// tawaran pembaruan: true hanya bila tag remote berbeda dari
    /// tag terakhir yang dilihat/ditawarkan
    /// (<see cref="DeckSettings.LastSeenReleaseTag"/>). Perbandingan
    /// versi (<see cref="IsUpdateAvailable"/>) tetap sebagai sekunder
    /// dan tidak pernah memveto tag baru: aplikasi tidak punya
    /// penomoran versi formal (selalu 1.0.0), jadi tag baru dengan
    /// versi yang sama tetap ditawarkan, dan tag yang sama tidak
    /// ditawarkan ulang walau dirilis ulang. Panggil SEBELUM
    /// <see cref="RecordResult"/>: RecordResult menimpa
    /// LastSeenReleaseTag dengan tag remote, sehingga perbandingan
    /// sesudahnya selalu sama.</summary>
    public static bool IsNewReleaseTag(string remoteTag, string? lastSeenTag)
    {
        if (string.IsNullOrWhiteSpace(remoteTag)) return false;
        return !string.Equals(remoteTag.Trim(), (lastSeenTag ?? "").Trim(), StringComparison.Ordinal);
    }

    public static async Task<UpdateCheckResult> CheckLatestAsync()
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseUrl);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(null, "HTTP " + (int)response.StatusCode);

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagProp)
                || tagProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(tagProp.GetString()))
                return new UpdateCheckResult(null, Localizer.Get("S1783"));

            string tag = tagProp.GetString()!;
            string pageUrl = root.TryGetProperty("html_url", out var urlProp)
                && urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(pageUrl))
                return new UpdateCheckResult(null, Localizer.Get("S1783"));

            string? notes = root.TryGetProperty("body", out var bodyProp)
                && bodyProp.ValueKind == JsonValueKind.String
                ? bodyProp.GetString() : null;

            string? publishedAt = root.TryGetProperty("published_at", out var dateProp)
                && dateProp.ValueKind == JsonValueKind.String
                ? dateProp.GetString() : null;

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp)
                        && nameProp.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        if (asset.TryGetProperty("browser_download_url", out var dlProp)
                            && dlProp.ValueKind == JsonValueKind.String)
                        {
                            downloadUrl = dlProp.GetString();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
                downloadUrl = $"https://github.com/fldvai/keystream/releases/download/{tag}/KeystreamSetup.exe";

            return new UpdateCheckResult(new ReleaseInfo(tag, pageUrl, notes, publishedAt, downloadUrl), null);
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(null, Localizer.Get("S1782"));
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult(null, Localizer.Get("S1782"));
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(null, Localizer.Get("S1783"));
        }
    }

    public static string BuildDialogText(ReleaseInfo release)
    {
        return Localizer.Format("S1778", release.Tag);
    }

    /// <summary>Auto-check dialog text: the automatic path only opens
    /// the release page, it never downloads/installs, so the question
    /// must not promise a download (S1778 stays with the manual path,
    /// where Yes downloads and installs).</summary>
    public static string BuildOpenPageDialogText(ReleaseInfo release)
    {
        return Localizer.Format("S1904", release.Tag);
    }

    public static void DownloadAndInstall(Window? owner, ReleaseInfo release)
    {
        string downloadUrl = release.DownloadUrl ?? $"https://github.com/fldvai/keystream/releases/download/{release.Tag}/KeystreamSetup.exe";
        string tempDir = Path.Combine(Path.GetTempPath(), "KeystreamUpdate");
        string targetExe = Path.Combine(tempDir, "KeystreamSetup.exe");

        var progressWin = new UpdateProgressWindow(downloadUrl, targetExe);
        if (owner is not null) progressWin.Owner = owner;
        else progressWin.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        progressWin.ShowDialog();

        if (progressWin.DownloadCompleted && File.Exists(targetExe))
        {
            try
            {
                Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                AppDialog.Show(owner, Localizer.Format("S1781", ex.Message),
                    Localizer.Get("S1554"), AppDialogButtons.Ok, AppDialogIcon.Error);
            }
            return;
        }

        if (progressWin.DownloadError is not null)
        {
            var answer = AppDialog.Show(owner,
                Localizer.Format("S1779", progressWin.DownloadError.Message),
                Localizer.Get("S1554"),
                AppDialogButtons.YesNo,
                AppDialogIcon.Warning);

            if (answer == AppDialogResult.Yes)
            {
                OpenReleasePage(owner, release.PageUrl);
            }
        }
    }

    public static void RecordResult(DeckSettings settings, ReleaseInfo release, DateTimeOffset checkedUtc)
    {
        settings.LastSeenReleaseTag = release.Tag;
        settings.LastReleasePageUrl = release.PageUrl;
        settings.LastReleasePublishedAt = release.PublishedAt ?? "";
        string notes = release.Notes ?? "";
        settings.LastReleaseNotes = notes.Length > MaxReleaseNotesLength
            ? notes[..MaxReleaseNotesLength]
            : notes;
        settings.LastUpdateCheckUtc = checkedUtc.ToString("o");
    }

    public static string PublishedDate(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso)
            || !DateTimeOffset.TryParse(iso, out var parsed))
            return Localizer.Get("S1819");
        return parsed.ToString("yyyy-MM-dd");
    }

    public static string LastChecked(string isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc)
            || !DateTimeOffset.TryParse(isoUtc, out var parsed))
            return Localizer.Get("S1819");
        return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public static void OpenReleasePage(Window? owner, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.Show(owner, Localizer.Format("S1781", ex.Message),
                Localizer.Get("S1554"), AppDialogButtons.Ok, AppDialogIcon.Error);
        }
    }
}
