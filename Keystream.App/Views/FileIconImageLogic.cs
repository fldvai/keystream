using System.IO;

namespace Keystream.App.Views;

/// <summary>
/// §151 (P2) - logika murni (WPF-FREE) untuk converter gambar ikon-file,
/// sengaja tanpa referensi namespace WPF apa pun agar berkas ini dapat
/// di-link langsung ke wb-harness net8.0 polos, disiplin yang sama
/// seperti ResetConfirmLogic.cs / TextBoxRevertLogic.cs. Converter WPF-nya
/// (FileIconImageConverter di UiHelpers.cs) mendelegasikan SEMUA keputusan
/// fallback ke sini; jangan duplikasi logika ini di converter maupun di
/// harness - harness menguji berkas ASLI ini lewat Compile Include.
///
/// Keputusan: hanya string path non-kosong yang filenya benar-benar ada
/// yang boleh dimuat. Segala bentuk lain (null, bukan string, string
/// kosong/whitespace, file hilang, path direktori, path tak-valid)
/// mengembalikan null, TANPA PERNAH melempar - persis jalur fallback
/// hari ini: FileIconPath (MainViewModel.cs) mengembalikan null bila
/// tidak ada file, HasFileIcon false, dan Image disembunyikan BoolVis
/// sehingga jalur glyph katalog mengambil alih.
/// </summary>
public static class FileIconImageLogic
{
    /// <summary>Path yang boleh dimuat converter, atau null bila converter
    /// harus mengembalikan fallback (Source null) tanpa menyentuh file.</summary>
    public static string? ResolveLoadablePath(object? value)
    {
        if (value is not string path) return null;
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return File.Exists(path) ? path : null;
        }
        catch
        {
            // Path tak-valid (mis. mengandung NUL) atau I/O lain saat
            // pemeriksaan: fallback, bukan exception ke binding.
            return null;
        }
    }
}
