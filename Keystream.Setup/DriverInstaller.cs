using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;
using Microsoft.Win32;

namespace Keystream.Setup;

internal static class DriverInstaller
{
    private const string DriverServiceKey = @"SYSTEM\CurrentControlSet\Services\keyboard";

    public static bool IsDriverInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DriverServiceKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static string FindInstallerExePath()
    {
        string? storedDir = ShortcutHelper.TryGetInstallLocation();
        if (!string.IsNullOrEmpty(storedDir))
        {
            string inStored = Path.Combine(storedDir, "drivers", "install-interception.exe");
            if (File.Exists(inStored)) return inStored;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string inAppData = Path.Combine(localAppData, "Keystream", "drivers", "install-interception.exe");
        if (File.Exists(inAppData)) return inAppData;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string inBase = Path.Combine(baseDir, "drivers", "install-interception.exe");
        if (File.Exists(inBase)) return inBase;

        string directBase = Path.Combine(baseDir, "install-interception.exe");
        if (File.Exists(directBase)) return directBase;

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string inPf = Path.Combine(pf, "Keystream", "drivers", "install-interception.exe");
        if (File.Exists(inPf)) return inPf;

        return "";
    }

    private static string EnsureInstallerAvailable()
    {
        string found = FindInstallerExePath();
        if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;

        // Fallback: download official driver zip from GitHub release
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string targetDir = Path.Combine(localAppData, "Keystream", "drivers");
            Directory.CreateDirectory(targetDir);
            string targetExe = Path.Combine(targetDir, "install-interception.exe");

            string zipUrl = "https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip";
            string tempZip = Path.Combine(Path.GetTempPath(), "Interception_" + Guid.NewGuid().ToString("N") + ".zip");
            string tempExtract = Path.Combine(Path.GetTempPath(), "Interception_Extract_" + Guid.NewGuid().ToString("N"));

            using (var client = new HttpClient())
            {
                var bytes = client.GetByteArrayAsync(zipUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(tempZip, bytes);
            }

            ZipFile.ExtractToDirectory(tempZip, tempExtract, true);
            string extractedExe = Path.Combine(tempExtract, "Interception", "command line installer", "install-interception.exe");
            if (File.Exists(extractedExe))
            {
                File.Copy(extractedExe, targetExe, true);
            }

            try { File.Delete(tempZip); } catch { }
            try { Directory.Delete(tempExtract, true); } catch { }

            if (File.Exists(targetExe)) return targetExe;
        }
        catch { }

        return "";
    }

    public static int RunElevatedDriverInstall()
    {
        try
        {
            string self = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            using var p = Process.Start(new ProcessStartInfo(self, "--install-driver")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            if (p == null) return 1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    public static int InstallDriverOnly()
    {
        if (!IsAdministrator())
        {
            try
            {
                string self = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                using var p = Process.Start(new ProcessStartInfo(self, "--install-driver")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (p == null) return 1;
                p.WaitForExit();
                return p.ExitCode;
            }
            catch
            {
                return 1;
            }
        }

        string installerExe = EnsureInstallerAvailable();
        if (string.IsNullOrEmpty(installerExe) || !File.Exists(installerExe))
            return 1;

        try
        {
            var psi = new ProcessStartInfo(installerExe, "/install")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    public static int RunElevatedDriverUninstall()
    {
        try
        {
            string self = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            using var p = Process.Start(new ProcessStartInfo(self, "--uninstall-driver")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            if (p == null) return 1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    public static int UninstallDriverOnly()
    {
        if (!IsAdministrator())
        {
            try
            {
                string self = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                using var p = Process.Start(new ProcessStartInfo(self, "--uninstall-driver")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (p == null) return 1;
                p.WaitForExit();
                return p.ExitCode;
            }
            catch
            {
                return 1;
            }
        }

        string installerExe = FindInstallerExePath();
        if (string.IsNullOrEmpty(installerExe) || !File.Exists(installerExe))
            return 1;

        try
        {
            var psi = new ProcessStartInfo(installerExe, "/uninstall")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
