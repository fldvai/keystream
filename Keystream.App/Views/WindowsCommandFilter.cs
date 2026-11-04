using System;
using System.Collections.Generic;

namespace Keystream.App.Views;

/// <summary>
/// §126 - logika murni filter pemilih Windows Commands, sengaja bebas
/// dari tipe UI agar bisa ditautkan langsung ke wb-harness net8.0
/// polos (disiplin yang sama seperti DashboardSearchFilter.cs §124):
/// hanya System + koleksi di sini; bagian UI (SearchablePanelPicker)
/// hanya memanggil ke sini.
///
/// Dua semantik, dipilih pemanggil lewat flag di kontrol (§126 pekerja C,
/// pola yang sama dengan EscapeClearsFilterFirst):
///  - Filter (AND-per-kata + keyword): HANYA pemilih Windows Commands.
///    Kueri kosong / hanya spasi = semua cocok; kueri dipecah per kata;
///    SETIAP kata harus muncul sebagai substring (OrdinalIgnoreCase) di
///    nama tampilan, kunci entri, ATAU keyword entri. Kunci entri adalah
///    Value id (mis. "TaskManager", "SettingsDisplay"). Keyword adalah
///    nama file target peluncuran (mis. "gpedit.msc") + batangnya
///    ("gpedit") + alias umum ("defrag", "ipconfig") - tanpa keyword,
///    kueri target seperti "gpedit"/"msconfig" tidak cocok di field
///    mana pun. Keyword HANYA untuk pencarian: bukan Value katalog,
///    bukan command yang divalidasi WindowsAction.
///  - FilterContainsPhrase (frasa literal): perilaku pemilih profil
///    Browser sebelum §126, dikembalikan bit-identik untuk pemakai
///    kontrol selain Windows. Seluruh kueri (apa adanya, tanpa pecah
///    kata) harus muncul sebagai substring di nama tampilan ATAU kunci
///    entri; tanpa keyword, tanpa AND. Kueri multi-kata yang urutan
///    katanya terbalik TIDAK cocok di sini - itulah perilaku lama.
///
/// Filter TIDAK mengurutkan ulang: baris yang cocok keluar dalam
/// urutan katalog yang masuk; yang tak cocok disembunyikan saja,
/// bukan dihapus - pilihan yang sudah terpilih tidak disentuh filter
/// karena penyaringan hanya mengganti ItemsSource daftar.</summary>
public static class WindowsCommandFilter
{
    /// <summary>Keyword pencarian per Value baru §126 + §127, dikunci oleh
    /// kunci ternormalisasi (spasi/_/- dihapus, huruf kecil - sama
    /// seperti WindowsAction.NormalizeCommand). Entri lama tidak punya
    /// keyword: perilakunya tetap Display + Value saja.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Keywords =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["backuprestore"] = new[] { "sdclt.exe", "sdclt" },
            ["commandprompt"] = new[] { "cmd.exe", "cmd" },
            ["computermanagement"] = new[] { "compmgmt.msc", "compmgmt" },
            ["controlpanel"] = new[] { "control.exe", "control" },
            ["diskdefragmenter"] = new[] { "dfrgui.exe", "dfrgui", "defrag" },
            ["devicemanager"] = new[] { "devmgmt.msc", "devmgmt" },
            ["directxdiagnostic"] = new[] { "dxdiag.exe", "dxdiag" },
            ["diskcleanup"] = new[] { "cleanmgr.exe", "cleanmgr" },
            ["diskmanagement"] = new[] { "diskmgmt.msc", "diskmgmt" },
            ["eventviewer"] = new[] { "eventvwr.msc", "eventvwr" },
            ["grouppolicyeditor"] = new[] { "gpedit.msc", "gpedit" },
            ["netstat"] = new[] { "cmd.exe", "cmd", "netstat" },
            ["networkconnections"] = new[] { "ncpa.cpl", "ncpa" },
            ["networkinformation"] = new[] { "cmd.exe", "cmd", "ipconfig" },
            ["performancemonitor"] = new[] { "perfmon.msc", "perfmon" },
            ["poweroptions"] = new[] { "powercfg.cpl", "powercfg" },
            ["programsandfeatures"] = new[] { "appwiz.cpl", "appwiz" },
            ["resourcemonitor"] = new[] { "resmon.exe", "resmon" },
            ["securitymaintenance"] = new[] { "wscui.cpl", "wscui" },
            ["services"] = new[] { "services.msc", "services" },
            ["sharedfolders"] = new[] { "fsmgmt.msc", "fsmgmt" },
            ["systemconfiguration"] = new[] { "msconfig.exe", "msconfig" },
            ["systeminformation"] = new[] { "msinfo32.exe", "msinfo32", "msinfo" },
            ["systemproperties"] = new[] { "sysdm.cpl", "sysdm" },
            ["systemrestore"] = new[] { "rstrui.exe", "rstrui" },
            ["taskscheduler"] = new[] { "taskschd.msc", "taskschd" },
            ["windowsfeatures"] = new[] { "optionalfeatures.exe", "optionalfeatures" },
            ["windowspowershell"] = new[] { "powershell.exe", "powershell" },
            // §127 admin tools (keyword = nama file target + batangnya;
            // "cmd" kini cocok di 7 entri cmd.exe: 3 §126 + 4 §127).
            ["checkdisk"] = new[] { "cmd.exe", "cmd", "chkdsk.exe", "chkdsk" },
            ["dism"] = new[] { "cmd.exe", "cmd", "dism.exe", "dism" },
            ["flushdns"] = new[] { "cmd.exe", "cmd", "ipconfig.exe", "ipconfig", "flushdns" },
            ["msrt"] = new[] { "mrt.exe", "mrt" },
            ["registryeditor"] = new[] { "regedit.exe", "regedit" },
            ["systemfilechecker"] = new[] { "cmd.exe", "cmd", "sfc.exe", "sfc" },
        };

    /// <summary>Keyword pencarian untuk satu kunci entri (Value id,
    /// "GroupPolicyEditor" atau bentuk ternormalisasinya); kosong bila
    /// entri tidak punya keyword. Dipakai harness untuk membuktikan
    /// peta keyword selaras dengan target WindowsAction.Launches.</summary>
    public static IReadOnlyList<string> KeywordsFor(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Array.Empty<string>();
        string normalized = key.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        return Keywords.TryGetValue(normalized, out string[]? words) ? words : Array.Empty<string>();
    }

    /// <summary>True bila setiap kata kueri muncul di nama tampilan,
    /// kunci entri, atau salah satu keyword entri.</summary>
    public static bool Matches(string? query, string? display, string? key)
    {
        var fields = new List<string?>();
        fields.Add(display);
        fields.Add(key);
        fields.AddRange(KeywordsFor(key));
        return DashboardSearchFilter.Matches(query, fields);
    }

    /// <summary>True bila seluruh kueri (frasa literal, tanpa pecah
    /// kata) muncul di nama tampilan atau kunci entri. Kueri kosong /
    /// hanya spasi = semua cocok, seperti Matches.</summary>
    public static bool MatchesPhrase(string? query, string? display, string? key)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return (display is not null && display.Contains(query, StringComparison.OrdinalIgnoreCase))
            || (key is not null && key.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Baris yang cocok (AND-per-kata + keyword), dalam urutan
    /// masuk yang utuh; kueri tak-cocok menghasilkan daftar kosong
    /// (tanpa pesan).</summary>
    public static List<T> Filter<T>(string? query, IReadOnlyList<T> rows,
        Func<T, string?> display, Func<T, string?> key)
    {
        var shown = new List<T>();
        foreach (T row in rows)
        {
            if (Matches(query, display(row), key(row))) shown.Add(row);
        }
        return shown;
    }

    /// <summary>Baris yang cocok (frasa literal Contains, tanpa keyword),
    /// dalam urutan masuk yang utuh - semantik lama pemilih profil
    /// Browser sebelum §126.</summary>
    public static List<T> FilterContainsPhrase<T>(string? query, IReadOnlyList<T> rows,
        Func<T, string?> display, Func<T, string?> key)
    {
        var shown = new List<T>();
        foreach (T row in rows)
        {
            if (MatchesPhrase(query, display(row), key(row))) shown.Add(row);
        }
        return shown;
    }
}
