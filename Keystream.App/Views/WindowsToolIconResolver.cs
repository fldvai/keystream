using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keystream.Actions.Windows;

namespace Keystream.App.Views;

/// <summary>
/// §129 (pekerja B) - logika murni resolusi sumber ikon kartu Windows
/// Tools, sengaja bebas dari tipe UI agar bisa ditautkan langsung ke
/// wb-harness net8.0 polos (disiplin yang sama seperti
/// WindowsCommandFilter.cs §126): hanya System + koleksi di sini;
/// ekstraksi bitmap-nya tetap lewat AppIconExtractor di editor.
///
/// Satu-satunya sumber kebenaran adalah WindowsAction.Launches:
/// command yang punya entri Launches-lah yang berhak atas ikon
/// (34 peluncuran §126+§127); chord/sequence/virtual-key/URI
/// ms-settings/lock BUKAN peluncuran dan tidak mendapat ikon -
/// resolver mengembalikan null untuk semuanya, dan kartu jatuh ke
/// rantai ikon biasa (upload &gt; AutoIcon &gt; katalog &gt; default)
/// tanpa ditebak-tebak. Editor mengaitkan resolver ini lewat
/// IsWindows (mencakup "windows" lama, "windows-command", dan
/// "windows-tools"); pembedaan Tools-vs-Command yang sesungguhnya
/// dilakukan keanggotaan Launches di sini, bukan id aksi - kartu
/// "windows-tools" berisi halaman ms-settings pun benar-benar
/// mendapat null, dan kartu "windows" lama berisi peluncuran pun
/// benar-benar mendapat path.
///
/// Aturan per jenis FileName (Arguments SELALU diabaikan - inilah
/// yang membuat tool cmd.exe berikon cmd.exe, bukan berikon program
/// di dalam argumen "/k ..."):
///  - .exe / .cpl: diekstrak langsung dari file target itu.
///  - target cmd.exe ("cmd.exe /k ..."): ikonnya dari cmd.exe.
///  - .msc: diekstrak dari FILE .msc-nya sendiri
///    (%SystemRoot%\System32\&lt;nama&gt;.msc) lewat rantai shell yang
///    sama (SHGetFileInfo / ExtractAssociatedIcon di AppIconExtractor).
///    Sumber ini terverifikasi: ProgID MSCFile mendaftarkan
///    DefaultIcon "%1" - ikon berasal dari file-nya sendiri, per
///    file - BUKAN dari mmc.exe (ikon generik host MMC, akan
///    menyamakan semua kartu snap-in) dan BUKAN dari DLL snap-in
///    yang ditebak per nama. Apakah shell mengembalikan ikon
///    berbeda per snap-in (services vs devmgmt) atau satu ikon MMC
///    generik hanya bisa dibuktikan di PC: bila ekstraksi
///    mengembalikan null, pemanggil JANGAN menebak - biarkan
///    default/katalog (status "tidak ditemukan"), seperti §95.
///  - URI ms-settings: tidak ada di Launches, jadi null - tidak ada
///    file target, jangan dipaksa.
///
/// Pencarian file: nama telanjang diselesaikan ke path absolut
/// (System32 dulu - di sanalah .msc/.exe/.cpl sistem tinggal -
/// lalu SysWOW64, lalu %SystemRoot%, lalu direktori PATH). Bila
/// tidak ditemukan di mana pun, null: kartu tetap tersimpan, ikon
/// tidak boleh menggagalkan save. FileName yang sudah berupa path
/// absolut dipakai apa adanya bila ada; bentuk ber-separator lain
/// ditolak (jangan menebak lokasi).</summary>
public static class WindowsToolIconResolver
{
    /// <summary>True bila command (Value katalog, mis. "Services")
    /// adalah salah satu peluncuran Launches - satu-satunya syarat
    /// kartu berhak atas ikon Tools.</summary>
    public static bool IsLaunchCommand(string? commandValue)
        => !string.IsNullOrWhiteSpace(commandValue)
           && WindowsAction.Launches.ContainsKey(WindowsAction.NormalizeCommand(commandValue));

    /// <summary>Kunci dedupe fetch editor untuk satu command + sumber
    /// ikonnya - sama seperti kunci "browser:..." §95: command yang
    /// sama dari sumber yang sama tidak di-fetch ulang.</summary>
    public static string FetchKey(string? commandValue, string? sourcePath)
        => "windowstool:" + WindowsAction.NormalizeCommand(commandValue) + "|" + (sourcePath ?? "");

    /// <summary>Path absolut file sumber ikon untuk satu command
    /// Tools, atau null bila command bukan peluncuran / file target
    /// tidak ditemukan. Overload produksi: memakai SystemRoot dan
    /// PATH proses saat ini.</summary>
    public static string? IconSourcePath(string? commandValue)
        => IconSourcePath(commandValue,
            Environment.GetEnvironmentVariable("SystemRoot"),
            (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries),
            File.Exists);

    /// <summary>Inti yang dapat diuji: pencarian file diinjeksikan
    /// lewat <paramref name="fileExists"/> agar harness Linux dapat
    /// membuktikan resolusi tanpa file Windows sungguhan.</summary>
    public static string? IconSourcePath(string? commandValue, string? systemRoot,
        IEnumerable<string>? pathDirectories, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(commandValue) || fileExists is null) return null;
        if (!WindowsAction.Launches.TryGetValue(
                WindowsAction.NormalizeCommand(commandValue), out var launch))
            return null;
        // Arguments diabaikan dengan sengaja: "cmd.exe /k dism.exe ..."
        // berikon cmd.exe, dan "/k ..." tidak pernah diurai sebagai
        // nama program lain.
        string fileName = (launch.FileName ?? "").Trim();
        if (fileName.Length == 0) return null;

        // Path absolut: pakai apa adanya bila ada.
        if (Path.IsPathRooted(fileName))
            return fileExists(fileName) ? fileName : null;
        // Separator tanpa root (mis. "sub\dir\tool.exe") bukan bentuk
        // Launches - tolak daripada menebak lokasi relatif.
        if (fileName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            return null;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            candidates.Add(Path.Combine(systemRoot, "System32", fileName));
            candidates.Add(Path.Combine(systemRoot, "SysWOW64", fileName));
            candidates.Add(Path.Combine(systemRoot, fileName));
        }
        if (pathDirectories is not null)
        {
            foreach (string dir in pathDirectories)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                candidates.Add(Path.Combine(dir.Trim(), fileName));
            }
        }
        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (fileExists(candidate)) return candidate;
        }
        return null;
    }
}
