using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Keystream.App.ViewModels;

namespace Keystream.App.Services;

public sealed class AppInfoItem
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public BitmapSource? Icon { get; init; }
    public bool IsRunning { get; init; }
}

public static class AppCatalogService
{
    private static List<AppInfoItem>? _cachedApps;
    private static readonly object _lock = new();

    public static async Task<List<AppInfoItem>> GetAppsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedApps is not null)
            return _cachedApps;

        return await Task.Run(() =>
        {
            var items = new List<AppInfoItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Running applications
            try
            {
                foreach (var running in RunningApps.ListWithPaths())
                {
                    if (string.IsNullOrWhiteSpace(running.Exe)) continue;
                    string path = running.Exe;
                    if (!seenPaths.Add(path)) continue;

                    string name = Path.GetFileNameWithoutExtension(path);
                    seenNames.Add(name);

                    BitmapSource? icon = null;
                    try { icon = AppIconExtractor.Extract(path); } catch { }

                    items.Add(new AppInfoItem
                    {
                        Name = name,
                        Path = path,
                        Icon = icon,
                        IsRunning = true
                    });
                }
            }
            catch { }

            // 2. Start Menu Shortcuts (.lnk and .exe)
            var startFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
            };

            foreach (var root in startFolders)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                string programsFolder = Path.Combine(root, "Programs");
                string searchDir = Directory.Exists(programsFolder) ? programsFolder : root;

                try
                {
                    var files = Directory.GetFiles(searchDir, "*.lnk", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            // Filter out uninstallers or helpers if desired, but keep genuine app shortcuts
                            if (fileName.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                            if (seenPaths.Contains(file)) continue;
                            if (seenNames.Contains(fileName)) continue;

                            seenPaths.Add(file);
                            seenNames.Add(fileName);

                            BitmapSource? icon = null;
                            try { icon = AppIconExtractor.Extract(file); } catch { }

                            items.Add(new AppInfoItem
                            {
                                Name = fileName,
                                Path = file,
                                Icon = icon,
                                IsRunning = false
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            var sorted = items
                .OrderByDescending(i => i.IsRunning)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_lock) { _cachedApps = sorted; }
            return sorted;
        });
    }
}
