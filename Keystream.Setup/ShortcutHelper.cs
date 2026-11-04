using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Keystream.Setup;

internal static class ShortcutHelper
{
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void NotifyShell()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    public static void CreateShortcut(string targetPath, string workingDir, string lnkPath, string description)
    {
        try
        {
            string? dir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDir;
                shortcut.Description = description;
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
                return;
            }
        }
        catch
        {
            // Fallback ke PowerShell bila COM WScript.Shell gagal
        }

        try
        {
            string escapedLnk = lnkPath.Replace("'", "''");
            string escapedTarget = targetPath.Replace("'", "''");
            string escapedDir = workingDir.Replace("'", "''");
            string escapedDesc = description.Replace("'", "''");
            string cmd = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{escapedLnk}');" +
                         $"$s.TargetPath='{escapedTarget}';" +
                         $"$s.WorkingDirectory='{escapedDir}';" +
                         $"$s.Description='{escapedDesc}';" +
                         $"$s.IconLocation='{escapedTarget},0';$s.Save()";

            var psi = new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }

    public static void RegisterAppPaths(string exePath, string installDir)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Keystream.exe");
            if (key != null)
            {
                key.SetValue(null, exePath);
                key.SetValue("Path", installDir);
            }
        }
        catch { }
    }

    public static void RegisterUninstall(string exePath, string installDir, long estimatedSizeBytes)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            if (key != null)
            {
                key.SetValue("DisplayName", "Keystream");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "Keystream");
                key.SetValue("DisplayIcon", exePath + ",0");
                key.SetValue("InstallLocation", installDir);
                string uninstallExe = Path.Combine(installDir, "Uninstall.exe");
                key.SetValue("UninstallString", $"\"{uninstallExe}\" --uninstall");
                key.SetValue("EstimatedSize", (int)(estimatedSizeBytes / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Keystream";

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keystream");

    /// <summary>Reads the install location recorded by RegisterUninstall.
    /// Returns null when Keystream was never installed by this setup or the
    /// value is missing/invalid, so callers fall back to DefaultInstallDir.</summary>
    public static string? TryGetInstallLocation()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            string? value = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(value)) return null;
            string full = Path.GetFullPath(value);
            if (!Path.IsPathRooted(full)) return null;
            return full;
        }
        catch
        {
            return null;
        }
    }

    public static void UnregisterApp()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Keystream.exe", false);
        }
        catch { }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
        }
        catch { }
    }

    /// <summary>Removes the startup entries created by the installer
    /// (MainWindow.ApplyStartupSettingIfNeeded) and by the app itself
    /// (AppHost.ApplyStartWithWindows): the "KeystreamStartup" scheduled
    /// task and the HKCU Run value "Keystream". Both are attempted even
    /// if the other fails. Returns true only when both are verified gone
    /// afterwards; a missing entry counts as removed. Deletion is
    /// best-effort, verification is honest: a leftover is reported by
    /// the caller instead of silently ignored.</summary>
    public static bool RemoveStartupEntries()
    {
        bool taskRemoved = RemoveScheduledTask();
        bool runRemoved = RemoveRunValue();
        return taskRemoved && runRemoved;
    }

    private static bool RemoveScheduledTask()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe",
                "/delete /tn \"KeystreamStartup\" /f")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(3000);
        }
        catch
        {
            return false;
        }

        return !ScheduledTaskExists();
    }

    private static bool ScheduledTaskExists()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe",
                "/query /tn \"KeystreamStartup\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return true;
            if (!p.WaitForExit(3000)) return true;
            return p.ExitCode == 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool RemoveRunValue()
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            key?.DeleteValue("Keystream", throwOnMissingValue: false);

            using var check = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false);
            return check?.GetValue("Keystream") == null;
        }
        catch
        {
            return false;
        }
    }
}

