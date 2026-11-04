using System.Text.Json.Nodes;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Actions.Common;

namespace Keystream.Actions.Browser;

/// <summary>
/// "browser" - open one browser profile with one tile press.
/// Params: "browserExe" (full path), "engine" ("Chromium"/"Firefox"),
/// "profileKey" (Chromium directory name or Firefox profile name),
/// "profileName" (display only, used in error text), "openMode":
///   - "New" (default): always open a new window, as before.
///   - "Focus": if the tracked window for this profile still exists,
///     bring it forward instead of opening a duplicate; otherwise open.
///   - "Toggle": if the tracked window still exists, close it
///     gracefully (WM_CLOSE, never a kill); otherwise open.
/// Validation checks format only, never the exe's existence: a browser
/// installed after the tile was saved must not make the tile
/// unsaveable. A missing exe fails honestly at execution time.
/// Tracking is persisted (DeckConfig.BrowserWindowTracking) so it
/// survives a Keystream restart; the parameterless constructor used by
/// tests keeps tracking in memory only.</summary>
public sealed class BrowserAction : IAction
{
    private readonly Func<List<BrowserWindowTracking>>? _getTracking;
    private readonly Action? _saveTracking;
    private readonly List<BrowserWindowTracking> _memoryTracking = new();

    public BrowserAction() : this(null, null) { }

    public BrowserAction(
        Func<List<BrowserWindowTracking>>? getTracking,
        Action? saveTracking)
    {
        _getTracking = getTracking;
        _saveTracking = saveTracking;
    }

    public ActionDescriptor Descriptor => new("browser", Localizer.Get("S0579"), Localizer.Get("S1071"),
        Localizer.Get("S1072"));

    public static string NormalizeOpenMode(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "focus" => "Focus",
        "toggle" => "Toggle",
        _ => "New",
    };

    /// <summary>Normalizes a tile URL. Empty = no URL (old behavior).
    /// A bare domain (www. prefix, or containing a dot, or localhost)
    /// without a scheme gets https:// prepended - otherwise Chromium
    /// would treat it as a search query, a silent wrong result.
    /// Only http/https survive; anything else (javascript:, data:,
    /// file:, ftp:, interior spaces) is an error, never degraded to
    /// the profile's start page.</summary>
    public static (string? Normalized, string? Error) NormalizeUrl(string? raw)
    {
        string value = (raw ?? "").Trim();
        if (value.Length == 0)
            return (null, null);
        if (value.Contains(' '))
            return (null, Localizer.Get("S1098"));
        if (!value.Contains("://"))
        {
            bool bareDomain = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                || value.Contains('.')
                || value.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
            if (!bareDomain)
                return (null, Localizer.Get("S1099"));
            value = "https://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
            return (null, Localizer.Format("S1100", raw));
        return (uri.ToString(), null);
    }

    private List<BrowserWindowTracking> Tracking => _getTracking?.Invoke() ?? _memoryTracking;
    private void SaveTracking() => _saveTracking?.Invoke();

    public ValidationResult Validate(JsonObject parameters)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ParamReader.GetString(parameters, "browserExe", "BrowserExe")))
            errors.Add(Localizer.Get("S1101"));
        bool manual = parameters.TryGetPropertyValue("profilePath", out _);
        if (manual)
        {
            string path = ParamReader.GetString(parameters, "profilePath", "ProfilePath") ?? "";
            if (string.IsNullOrWhiteSpace(path))
                errors.Add(Localizer.Get("S1102"));
            else if (!Directory.Exists(path))
                // §45: folder manual yang tidak ada dulu LOLOS validasi
                // (cek lama hanya menolak bila folder ADA tapi bukan
                // profil) - tile tersimpan lalu gagal tiap ditekan.
                errors.Add(Localizer.Get("S0799"));
            else if (!BrowserDiscovery.TryDetectProfileFolder(path, out _, out _))
                errors.Add(Localizer.Get("S0800"));
        }
        else if (parameters.TryGetPropertyValue("profileKeys", out var multiNode)
            && multiNode is JsonArray multiArr)
        {
            // Tile multi-profil (25 Okt 2026): daftar dari param tile
            // sendiri - grup tidak dilibatkan sama sekali.
            bool any = multiArr.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s));
            if (!any) errors.Add(Localizer.Get("S1103"));
        }
        else if (string.IsNullOrWhiteSpace(ParamReader.GetString(parameters, "profileKey", "ProfileKey")))
        {
            errors.Add(Localizer.Get("S1103"));
        }
        string mode = (ParamReader.GetString(parameters, "openMode", "OpenMode") ?? "").Trim();
        if (mode.Length > 0 && mode.ToLowerInvariant() is not ("new" or "focus" or "toggle"))
            errors.Add(Localizer.Format("S1104", mode));
        (_, string? urlError) = NormalizeUrl(ParamReader.GetString(parameters, "url", "Url"));
        if (urlError is not null)
            errors.Add(urlError);
        return errors.Count == 0 ? ValidationResult.Valid : ValidationResult.Invalid(string.Join(" ", errors));
    }

    public async Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        string exe = ParamReader.GetString(parameters, "browserExe", "BrowserExe") ?? "";
        string engine = ParamReader.GetString(parameters, "engine", "Engine") ?? "Chromium"; // Literal engine value — never localized (S0813 is the display label only).
        string mode = NormalizeOpenMode(ParamReader.GetString(parameters, "openMode", "OpenMode"));
        // Execution guard: a hand-edited config with an invalid URL must
        // fail here, before launching - never silently open the start
        // page while reporting success.
        (string? url, string? urlError) = NormalizeUrl(ParamReader.GetString(parameters, "url", "Url"));
        if (urlError is not null)
            return ActionResult.Fail(urlError);
        string userDataDir = ParamReader.GetString(parameters, "userDataDir", "UserDataDir") ?? "";

        // Multi-profil (25 Okt 2026): daftar centang dieksekusi di
        // sini, di aksi browser - BUKAN lewat aksi grup dan BUKAN
        // membaca BrowserProfileGroups. Default tanpa konfigurasi:
        // semua profil dibuka berurutan dengan jeda 150ms antar
        // peluncuran (default mode Simultaneous grup); Focus/Toggle
        // mengenai jendela yang sudah terlacak tanpa jeda.
        if (parameters.TryGetPropertyValue("profileKeys", out var keysNode) && keysNode is JsonArray keysArr)
        {
            var keys = keysArr.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").Where(k => k.Length > 0).ToList();
            if (keys.Count == 0)
                return ActionResult.Fail(Localizer.Get("S1103"));
            var names = parameters.TryGetPropertyValue("profileNames", out var namesNode) && namesNode is JsonArray namesArr
                ? namesArr.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").ToList()
                : new List<string>();
            string displayAt(int i) => i < names.Count && names[i].Length > 0 ? names[i] : keys[i];
            var tracking = Tracking;
            int opened = 0, focused = 0, closed = 0;
            var failures = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) await Task.Delay(150);
                var (outcome, error, _) = await ExecuteOneAsync(exe, engine, keys[i], "", displayAt(i), mode, url, userDataDir, tracking, context.Cancellation);
                if (error is not null) { failures.Add(error); continue; }
                switch (outcome)
                {
                    case SingleOutcome.Opened: opened++; break;
                    case SingleOutcome.Focused: focused++; break;
                    case SingleOutcome.Closed: closed++; break;
                }
            }
            if (failures.Count == keys.Count)
                return ActionResult.Fail(string.Join("; ", failures));
            var parts = new List<string>();
            if (opened > 0) parts.Add(Localizer.Format("S1105", opened));
            if (focused > 0) parts.Add(Localizer.Format("S1106", focused));
            if (closed > 0) parts.Add(Localizer.Format("S1107", closed));
            string detail = string.Join(", ", parts);
            // Parsial = Fail, konsisten dengan aksi grup (§45): sukses
            // parsial dengan titik hijau menyesatkan - sebagian profil
            // tidak terbuka tapi footer tampak hijau.
            if (failures.Count > 0)
                return ActionResult.Fail(Localizer.Format("S1108", detail, failures.Count, string.Join("; ", failures)));
            return ActionResult.Ok(detail: detail);
        }

        string key = ParamReader.GetString(parameters, "profileKey", "ProfileKey") ?? "";
        string profilePath = ParamReader.GetString(parameters, "profilePath", "ProfilePath") ?? "";
        string display = ParamReader.GetString(parameters, "profileName", "ProfileName") ?? key;
        var (singleOutcome, singleError, trackingSaveFailed) = await ExecuteOneAsync(exe, engine, key, profilePath, display, mode, url, userDataDir, Tracking, context.Cancellation);
        if (singleError is not null)
            return ActionResult.Fail(singleError);
        return singleOutcome switch
        {
            SingleOutcome.Focused => ActionResult.Ok(detail: Localizer.Format("S1109", display)),
            SingleOutcome.Closed => ActionResult.Ok(detail: Localizer.Format("S1547", display)
                + (trackingSaveFailed ? Localizer.Get("S1548") : "")),
            _ => ActionResult.Ok(detail: Localizer.Format("S1111", display)),
        };
    }

    private enum SingleOutcome { Opened, Focused, Closed }

    /// <summary>Satu profil: fokus/tutup jendela terlacak, atau luncurkan
    /// baru + lacak di latar. Dipakai jalur tunggal maupun tiap entri
    /// jalur multi - pesannya identik dengan perilaku lama.</summary>
    private async Task<(SingleOutcome Outcome, string? Error, bool TrackingSaveFailed)> ExecuteOneAsync(
        string exe, string engine, string key, string profilePath, string display,
        string mode, string? url, string userDataDir,
        List<BrowserWindowTracking> tracking, CancellationToken cancellation)
    {
        // Manual tiles track by full folder path (unique); dropdown tiles
        // track by profile key. Either way the tracking key is stable.
        string trackingKey = profilePath.Length > 0 ? profilePath : key;
        var existing = BrowserWindowTracker.Find(tracking, exe, trackingKey);
        if (mode != "New" && existing is not null && BrowserWindowTracker.IsValid(existing))
        {
            if (mode == "Focus")
            {
                // Sama seperti grup (§45): Focus() yang gagal bukan
                // "difokuskan" - laporkan gagal, jangan sukses palsu.
                if (BrowserWindowTracker.Focus(existing))
                    return (SingleOutcome.Focused, null, false);
                return (SingleOutcome.Focused, Localizer.Format("S1112", display), false);
            }
            BrowserWindowTracker.Close(existing);
            // Re-validasi (§45): dulu Close langsung dianggap berhasil
            // dan record dihapus - bila jendela menolak tutup (dialog,
            // unduhan), tekan berikutnya membuka duplikat. Tunggu 500ms
            // lalu cek ulang; bila masih hidup, PERTAHANKAN record.
            await Task.Delay(500);
            var stillThere = BrowserWindowTracker.Find(tracking, exe, trackingKey);
            if (stillThere is not null && BrowserWindowTracker.IsValid(stillThere))
                return (SingleOutcome.Closed, Localizer.Format("S1113", display), false);
            BrowserWindowTracker.Remove(tracking, exe, trackingKey);
            bool saveFailed = false;
            try { SaveTracking(); }
            catch { saveFailed = true; } // best-effort: tutup tetap dilaporkan, dengan catatan
            return (SingleOutcome.Closed, null, saveFailed);
        }

        var before = BrowserWindowTracker.SnapshotWindows();
        var (ok, error) = BrowserLauncher.Launch(exe, engine, key, profilePath, url, userDataDir);
        if (!ok)
            return (SingleOutcome.Opened, Localizer.Format("S1114", display, error), false);
        _ = TrackInBackgroundAsync(exe, trackingKey, before, cancellation);
        return (SingleOutcome.Opened, null, false);
    }

    private async Task TrackInBackgroundAsync(
        string exe, string key, HashSet<IntPtr> before, CancellationToken cancellation)
    {
        try
        {
            var record = await BrowserWindowTracker.TrackNewWindowAsync(exe, key, before, cancellation);
            if (record is null) return;
            var tracking = Tracking;
            BrowserWindowTracker.Upsert(tracking, record);
            SaveTracking();
        }
        catch
        {
            // Tracking is best-effort: a missed window only means the
            // next press opens a new window instead of focusing.
        }
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
