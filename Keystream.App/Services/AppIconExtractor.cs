using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Keystream.App.Services;

/// <summary>
/// Icons of local applications (v3.5.0): the shell's large icon for
/// an .exe, shortcut (.lnk), or document (associated app icon) via
/// SHGetFileInfo, with ExtractAssociatedIcon as the fallback. Both
/// sources are 32×32 - the tile draws at 48 px, so WPF upscales them
/// (soft, not broken). The 256 px shell image list would be crisper
/// but needs fragile COM vtable interop; deliberately not used.
/// Handles are destroyed after conversion (the WPF copy owns its own).
/// </summary>
public static class AppIconExtractor
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex,
        int cxIcon, int cyIcon, [Out] IntPtr[] phicon, [Out] uint[] piconid,
        uint nIcons, uint flags);

    public static BitmapSource? Extract(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        // Largest first (v3.6.0): a 32x32 icon upscaled to the tile's
        // 48x48 renders visibly soft ("pecah"). PrivateExtractIcons
        // asks the exe for its real 256x256 (then 48x48) image;
        // IconStore downscales to 128, so the tile gets true pixels
        // instead of an upscale. Any failure falls through to the
        // old 32x32 chain below.
        foreach (int size in new[] { 256, 48 })
        {
            try
            {
                var icons = new IntPtr[1];
                var ids = new uint[1];
                uint got = PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0);
                if (got > 0 && icons[0] != IntPtr.Zero)
                {
                    try
                    {
                        var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                            icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bitmap.Freeze();
                        return bitmap;
                    }
                    finally
                    {
                        DestroyIcon(icons[0]);
                    }
                }
            }
            catch
            {
                // Fall through to the next size / old chain.
            }
        }
        try
        {
            var info = new SHFILEINFO();
            IntPtr ok = SHGetFileInfo(path, 0, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (ok != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    DestroyIcon(info.hIcon);
                }
            }
        }
        catch
        {
            // Fall through to the associated-icon path.
        }
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
