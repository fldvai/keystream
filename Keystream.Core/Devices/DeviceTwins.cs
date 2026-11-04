namespace Keystream.Core.Devices;

/// <summary>§161 - predikat kembar murni: hardware ID mana yang muncul
/// lebih dari sekali dalam satu enumerasi. Dipakai picker untuk
/// menandai baris duplikat dan untuk gerbang konfirmasi; tidak
/// menyentuh enumerasi itu sendiri dan tidak tahu apa-apa tentang
/// WPF/P-Invoke, sehingga dapat di-link langsung ke harness.
///
/// Perbandingan ordinal-ignore-case (sama seperti pengelompokan
/// inline picker sebelumnya). Entri null/kosong/whitespace dilewati:
/// identitas kosong bukan identitas perangkat, dan dua entri kosong
/// tidak boleh dihitung sebagai kembar. Himpunan hasil memakai
/// comparer yang sama; casing entri di dalamnya mengikuti kemunculan
/// yang memicu deteksi (hanya Contains yang dipakai pemanggil).</summary>
public static class DeviceTwins
{
    /// <summary>Mengembalikan himpunan hardware ID yang muncul &gt;1
    /// kali di <paramref name="hardwareIds"/> (ordinal-ignore-case).
    /// Daftar kosong / satu perangkat / semua berbeda → himpunan
    /// kosong. Tidak pernah melempar untuk input null (→ kosong).</summary>
    public static HashSet<string> DuplicateHardwareIds(IEnumerable<string?>? hardwareIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hardwareIds is null) return duplicates;
        foreach (var id in hardwareIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!seen.Add(id)) duplicates.Add(id);
        }
        return duplicates;
    }
}
