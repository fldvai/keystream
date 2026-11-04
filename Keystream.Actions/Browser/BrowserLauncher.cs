using System.Diagnostics;
using Keystream.Core.Localization;

namespace Keystream.Actions.Browser;

/// <summary>Launches one browser profile. Chromium: --profile-directory
/// opens a window for that profile even when the browser already runs.
/// Firefox: -P plus -no-remote, because a stock Firefox instance refuses
/// a second profile while it runs; the side effect is that the launched
/// instance does not receive links forwarded from other apps.</summary>
public static class BrowserLauncher
{
    /// <summary>Launches a profile. With profilePath (manual path),
    /// Chromium is launched with --user-data-dir pointing at the
    /// folder's parent plus --profile-directory for the folder name,
    /// and Firefox with -profile plus -no-remote - so a portable or
    /// relocated profile runs even though it is registered nowhere.
    /// Without profilePath, the registered-profile launch is used.</summary>
    public static (bool Ok, string? Error) Launch(
        string? exePath, string? engine, string? profileKey, string? profilePath = null, string? url = null,
        string? userDataDir = null)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return (false, Localizer.Get("S1101"));
        if (!File.Exists(exePath))
            return (false, Localizer.Format("S1122", exePath));
        string args;
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            if (!Directory.Exists(profilePath))
                return (false, Localizer.Format("S1123", profilePath));
            string trimmed = profilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (BrowserDiscovery.NormalizeEngine(engine) == "Firefox")
            {
                args = $"-profile \"{profilePath.Replace("\"", "")}\" -no-remote";
            }
            else
            {
                string? parent = Path.GetDirectoryName(trimmed);
                string name = Path.GetFileName(trimmed);
                if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                    return (false, Localizer.Get("S1127"));
                args = $"--user-data-dir=\"{parent}\" --profile-directory=\"{name}\"";
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(profileKey))
                return (false, Localizer.Get("S1103"));
            string key = profileKey.Replace("\"", "");
            // Temuan riset 4 (blocker pindai): profil terdaftar dari browser
            // kustom (mis. Thorium portabel hasil pindai) HARUS dibuka dari
            // folder User Data-nya sendiri, bukan dari lokasi default exe -
            // tanpa flag ini tile akan membuka profil yang salah secara
            // diam-diam. Cakupan aman: hanya Chromium; Firefox -P selalu
            // merujuk profiles.ini lokasi default (keterbatasan yang
            // didokumentasikan, bukan regresi - perilaku Firefox tidak
            // berubah). Cabang manual mengabaikan userDataDir: parent
            // folder profilnya sendiri sudah otoritatif di sana.
            if (BrowserDiscovery.NormalizeEngine(engine) == "Firefox")
            {
                // Tidak dicek: lokasi profiles.ini default tidak otoritatif
                // untuk Firefox portabel - cek di sana bisa menolak instalasi
                // portabel yang sah. Keterbatasan yang didokumentasikan.
                args = $"-P \"{key}\" -no-remote";
            }
            else if (!string.IsNullOrWhiteSpace(userDataDir))
            {
                // §45 (SIM-08): tanpa cek ini, folder profil yang sudah
                // dihapus tetap "berhasil" dibuka - Chromium membuat profil
                // kosong baru diam-diam dan tile melaporkan sukses.
                string profileFolder = Path.Combine(userDataDir, key);
                if (!Directory.Exists(profileFolder))
                    return (false, Localizer.Format("S1128", key, userDataDir));
                args = $"--user-data-dir=\"{userDataDir.Replace("\"", "")}\" --profile-directory=\"{key}\"";
            }
            else
            {
                args = $"--profile-directory=\"{key}\"";
            }
        }
        // URL posisional terakhir, dikutip, quote di dalam dihapus
        // (preseden yang sama dengan nilai lain). Berlaku untuk kedua
        // engine dan kedua cabang profil; kosong = halaman awal profil.
        if (!string.IsNullOrWhiteSpace(url))
            args += $" \"{url.Replace("\"", "")}\"";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
