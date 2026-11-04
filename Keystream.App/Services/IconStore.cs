using System.IO;
using Keystream.Core.Localization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Keystream.Actions.Launcher;
using Keystream.Core.Config;
using Keystream.Core.Icons;

namespace Keystream.App.Services;

/// <summary>Thrown when an icon import is refused; the message is already
/// written for the user (shown in an AppDialog by the caller).</summary>
public sealed class IconImportException : Exception
{
    public IconImportException(string message) : base(message) { }
}

/// <summary>
/// Uploaded tile icons (v3.4.0): every imported image is converted to
/// the smallest useful form and collected in ONE folder -
/// %AppData%\Keystream\Icons. Conversion: decode, downscale to at most
/// 128×128 (aspect preserved), re-encode as PNG (transparency kept,
/// metadata stripped). Storage: content-addressed file names
/// (SHA-256 of the converted bytes), so duplicates collapse to one
/// file and names can never collide. The tile stores only the
/// "file:&lt;name&gt;.png" reference; the Settings cleanup deletes by
/// reference scan, never by guesswork.
/// </summary>
public sealed class IconStore
{
    public sealed record ImportResult(
        string IconValue, string FilePath,
        long OriginalBytes, int StoredWidth, int StoredHeight, long StoredBytes);

    private static readonly string[] AllowedExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    // §171: indeks cache favicon - URL ternormalisasi -> nilai ikon
    // ("file:<nama>.png") yang SUDAH tersimpan di folder ini. Satu
    // file JSON di DALAM folder Icons, sehingga Clean Up (DeleteAll /
    // DeleteOrphans, yang menghapus semua file tanpa filter ekstensi)
    // ikut menghapusnya tanpa kode hapus baru, dan Reset Data ikut
    // menghapusnya bersama seluruh folder data. Bukan store kedua:
    // byte ikonnya tetap file content-addressed yang sama.
    private const string FaviconCacheFileName = "favicon-cache.json";
    private readonly object _faviconCacheLock = new();

    public string Folder { get; }

    public IconStore(string dataDirectory)
    {
        Folder = Path.Combine(dataDirectory, "Icons");
    }

    public ImportResult Import(string sourcePath, long maxBytes)
    {
        if (!File.Exists(sourcePath))
            throw new IconImportException(Localizer.Get("S0312"));
        var info = new FileInfo(sourcePath);
        if (IconReference.ValidateSize(info.Length, maxBytes) is string tooBig)
            throw new IconImportException(tooBig);
        if (!AllowedExtensions.Contains(Path.GetExtension(sourcePath).ToLowerInvariant()))
            throw new IconImportException(
                Localizer.Get("S0313"));

        BitmapSource bitmap;
        try
        {
            using var stream = File.OpenRead(sourcePath);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                throw new IconImportException(Localizer.Get("S0314"));
            bitmap = decoder.Frames[0];
        }
        catch (IconImportException)
        {
            throw;
        }
        catch
        {
            throw new IconImportException(Localizer.Get("S0314"));
        }
        return ImportBitmap(bitmap, info.Length, maxBytes);
    }

    /// <summary>Imports already-downloaded bytes (website favicons,
    /// v3.5.0): the size limit applies to the downloaded file, then
    /// the same conversion + storage core runs.</summary>
    public ImportResult ImportBytes(byte[] data, long maxBytes)
    {
        if (IconReference.ValidateSize(data.Length, maxBytes) is string tooBig)
            throw new IconImportException(tooBig);
        BitmapSource bitmap;
        try
        {
            using var stream = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                throw new IconImportException(Localizer.Get("S0315"));
            bitmap = decoder.Frames[0];
        }
        catch (IconImportException)
        {
            throw;
        }
        catch
        {
            throw new IconImportException(Localizer.Get("S0315"));
        }
        return ImportBitmap(bitmap, data.Length, maxBytes);
    }

    /// <summary>The conversion + storage core every import shares:
    /// downscale to at most 128×128, PNG-encode, store
    /// content-addressed. `originalBytes` is 0 when there is no source
    /// file to report (extracted app icons) - the stored size is what
    /// the info line shows then.</summary>
    public ImportResult ImportBitmap(BitmapSource bitmap, long originalBytes, long maxBytes)
    {
        if (bitmap.PixelWidth > IconReference.MaxDimension
            || bitmap.PixelHeight > IconReference.MaxDimension)
        {
            double scale = Math.Min(
                (double)IconReference.MaxDimension / bitmap.PixelWidth,
                (double)IconReference.MaxDimension / bitmap.PixelHeight);
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            encoder.Save(stream);
            bytes = stream.ToArray();
        }

        string hex = Convert.ToHexString(SHA256.HashData(bytes));
        string fileName = IconReference.FileNameForHash(hex);
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, fileName);
        if (!File.Exists(path))
        {
            // Tulis atomik via temp: dulu WriteAllBytes langsung - gagal
            // di tengah tulis meninggalkan PNG parsial, lalu impor
            // berikutnya melihat file "ada" dan mengklaim sukses (§45).
            string temp = path + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        return new ImportResult(
            IconReference.IconValue(fileName), path, originalBytes,
            bitmap.PixelWidth, bitmap.PixelHeight, bytes.Length);
    }

    public (int Count, long Bytes) Stats()
    {
        if (!Directory.Exists(Folder)) return (0, 0);
        var files = Directory.GetFiles(Folder);
        int count = 0;
        long bytes = 0;
        foreach (var f in files)
        {
            // §171: indeks cache bukan ikon - jangan dihitung.
            if (string.Equals(Path.GetFileName(f), FaviconCacheFileName,
                StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                // File hilang/terkunci antara GetFiles dan Length dilewati:
                // statistik hanya dari file yang tersisa.
                long size = new FileInfo(f).Length;
                count++;
                bytes += size;
            }
            catch { } // per-file: satu file hilang/terkunci tidak boleh menghentikan sisanya
        }
        return (count, bytes);
    }

    // §171 - cache favicon: cache-first. Kunci = URL ternormalisasi
    // (aturan yang sama dengan LinkAction: tanpa skema -> https://),
    // tanpa garis miring akhir, huruf kecil - jadi "youtube.com" dan
    // "https://youtube.com/" memetakan ke satu entri. Lookup TIDAK
    // pernah mengunduh: mengembalikan nilai ikon hanya bila file
    // PNG-nya masih ada di disk; entri basi dihapus diam-diam.
    // Semua metode cache best-effort dan tidak pernah melempar:
    // indeks rusak/hilang berarti unduh ulang, bukan error.
    private static string FaviconCacheKey(string rawUrl)
        => LinkAction.NormalizeUrl(rawUrl).TrimEnd('/').ToLowerInvariant();

    private Dictionary<string, string> LoadFaviconCache()
    {
        try
        {
            string path = Path.Combine(Folder, FaviconCacheFileName);
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void SaveFaviconCache(Dictionary<string, string> cache)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, FaviconCacheFileName);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // Cache best-effort: kegagalan tulis tidak boleh
            // menggagalkan pemuatan ikon.
        }
    }

    public string? TryGetCachedFavicon(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;
        lock (_faviconCacheLock)
        {
            var cache = LoadFaviconCache();
            if (!cache.TryGetValue(FaviconCacheKey(rawUrl), out string? iconValue)) return null;
            if (IconReference.FileName(iconValue) is string name
                && File.Exists(Path.Combine(Folder, name))) return iconValue;
            cache.Remove(FaviconCacheKey(rawUrl));
            SaveFaviconCache(cache);
            return null;
        }
    }

    public void SetCachedFavicon(string rawUrl, string iconValue)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)
            || IconReference.FileName(iconValue) is null) return;
        lock (_faviconCacheLock)
        {
            var cache = LoadFaviconCache();
            cache[FaviconCacheKey(rawUrl)] = iconValue;
            SaveFaviconCache(cache);
        }
    }

    /// <summary>§171: true bila indeks cache ada - dipakai Clean Up
    /// untuk melaporkan bahwa cache ikut dibersihkan.</summary>
    public bool HasFaviconCacheIndex()
        => File.Exists(Path.Combine(Folder, FaviconCacheFileName));

    /// <summary>§171: ukuran file PNG tersimpan untuk satu nilai ikon,
    /// atau null bila file-nya tidak ada - dipakai status editor saat
    /// ikon diambil dari cache (bukan dari unduhan baru).</summary>
    public long? StoredBytes(string iconValue)
    {
        if (IconReference.FileName(iconValue) is not string name) return null;
        string path = Path.Combine(Folder, name);
        return File.Exists(path) ? new FileInfo(path).Length : null;
    }

    /// <summary>File names actually referenced by some binding's icon,
    /// across all profiles and the Global page.</summary>
    public static HashSet<string> ReferencedNames(DeckConfig config)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in config.Profiles)
        foreach (var page in profile.Pages)
        foreach (var binding in page.Bindings)
        {
            if (IconReference.FileName(binding.Icon) is string name)
                used.Add(name);
            if (IconReference.FileName(binding.AutoIcon) is string autoName)
                used.Add(autoName);
        }
        foreach (var binding in config.GlobalPage.Bindings)
        {
            if (IconReference.FileName(binding.Icon) is string name)
                used.Add(name);
            if (IconReference.FileName(binding.AutoIcon) is string autoName)
                used.Add(autoName);
        }
        return used;
    }

    public int DeleteOrphans(DeckConfig config, out long freedBytes, out int failed)
    {
        freedBytes = 0;
        failed = 0;
        if (!Directory.Exists(Folder)) return 0;
        var used = ReferencedNames(config);
        int deleted = 0;
        foreach (var path in Directory.GetFiles(Folder))
        {
            if (used.Contains(Path.GetFileName(path))) continue;
            try
            {
                // freedBytes hanya sesudah File.Delete sukses: hapus
                // gagal (catch) tidak boleh terhitung bebas.
                long size = new FileInfo(path).Length;
                File.Delete(path);
                freedBytes += size;
                deleted++;
            }
            catch { failed++; } // per-file: satu file terkunci tidak boleh menghentikan sisanya (§45)
        }
        return deleted;
    }

    public int DeleteAll(out long freedBytes, out int failed)
    {
        freedBytes = 0;
        failed = 0;
        if (!Directory.Exists(Folder)) return 0;
        int deleted = 0;
        foreach (var path in Directory.GetFiles(Folder))
        {
            try
            {
                // freedBytes hanya sesudah File.Delete sukses: hapus
                // gagal (catch) tidak boleh terhitung bebas.
                long size = new FileInfo(path).Length;
                File.Delete(path);
                freedBytes += size;
                deleted++;
            }
            catch { failed++; }
        }
        return deleted;
    }
}
