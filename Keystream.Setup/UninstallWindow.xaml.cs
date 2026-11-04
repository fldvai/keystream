using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Keystream.Setup.Localization;

namespace Keystream.Setup;

public partial class UninstallWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public UninstallWindow()
    {
        InitializeComponent();
        ApplyTexts();
    }

    /// <summary>Re-applies every wizard text from the Setup string
    /// table (W#### keys) for the language resolved in App.OnStartup.
    /// XAML keeps the English defaults as design-time fallback. The
    /// finished title/subtitle defaults here are overwritten by the
    /// warnings branch after uninstall when warnings exist.</summary>
    private void ApplyTexts()
    {
        Title = SetupLocalizer.Get("W0021");
        txtUninstallTitle.Text = SetupLocalizer.Get("W0021");
        chkCleanAppData.Content = SetupLocalizer.Get("W0022");
        txtCleanAppDataHint.Text = SetupLocalizer.Get("W0023");
        chkUninstallDriver.Content = SetupLocalizer.Get("W0024");
        txtUninstallDriverHint.Text = SetupLocalizer.Get("W0025");
        btnCancel.Content = SetupLocalizer.Get("W0006");
        btnUninstall.Content = SetupLocalizer.Get("W0026");
        txtUninstallingTitle.Text = SetupLocalizer.Get("W0027");
        txtStatus.Text = SetupLocalizer.Get("W0028");
        txtFinishedTitle.Text = SetupLocalizer.Get("W0029");
        txtFinishedSubtitle.Text = SetupLocalizer.Get("W0030");
        txtDriverRestartTitle.Text = SetupLocalizer.Get("W0014");
        txtDriverRestartBody.Text = SetupLocalizer.Get("W0031");
        txtCleanupWarningTitle.Text = SetupLocalizer.Get("W0032");
        btnRestartLater.Content = SetupLocalizer.Get("W0018");
        btnRestartNow.Content = SetupLocalizer.Get("W0019");
        btnClose.Content = SetupLocalizer.Get("W0033");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        }
        catch { }
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void btnUninstall_Click(object sender, RoutedEventArgs e)
    {
        bool cleanAppData = chkCleanAppData.IsChecked == true;
        bool uninstallDriver = chkUninstallDriver.IsChecked == true;

        pnlConfirm.Visibility = Visibility.Collapsed;
        pnlProgress.Visibility = Visibility.Visible;

        string installDir = ShortcutHelper.TryGetInstallLocation() ?? ShortcutHelper.DefaultInstallDir;
        string selfPath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        bool selfInsideInstallDir = !string.IsNullOrEmpty(selfPath)
            && Path.GetFullPath(selfPath).StartsWith(
                Path.GetFullPath(installDir) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

        bool driverUninstalled = false;
        bool driverUninstallFailed = false;
        bool startupRemoved = true;
        bool installDirRemoved = true;
        bool installDirRetryLaunched = false;

        await Task.Run(() =>
        {
            try
            {
                // 1. Matikan Keystream bila sedang berjalan
                Dispatcher.Invoke(() => txtStatus.Text = SetupLocalizer.Get("W0067"));
                foreach (var p in Process.GetProcessesByName("Keystream"))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                }

                // 2. Cabut driver Interception jika diminta
                if (uninstallDriver && DriverInstaller.IsDriverInstalled())
                {
                    Dispatcher.Invoke(() => txtStatus.Text = SetupLocalizer.Get("W0068"));
                    int helperCode = DriverInstaller.RunElevatedDriverUninstall();
                    if (helperCode == 0 && !DriverInstaller.IsDriverInstalled())
                    {
                        driverUninstalled = true;
                    }
                    else
                    {
                        driverUninstallFailed = true;
                    }
                }

                // 3. Hapus Shortcut Desktop & Start Menu
                Dispatcher.Invoke(() => txtStatus.Text = SetupLocalizer.Get("W0069"));
                string desktopLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Keystream.lnk");
                if (File.Exists(desktopLnk))
                {
                    try { File.Delete(desktopLnk); } catch { }
                }

                string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string startLnk = Path.Combine(programsDir, "Keystream.lnk");
                if (File.Exists(startLnk))
                {
                    try { File.Delete(startLnk); } catch { }
                }

                string startFolder = Path.Combine(programsDir, "Keystream");
                if (Directory.Exists(startFolder))
                {
                    try { Directory.Delete(startFolder, true); } catch { }
                }

                // 4. Hapus pendaftaran registry + mekanisme startup
                ShortcutHelper.UnregisterApp();
                startupRemoved = ShortcutHelper.RemoveStartupEntries();
                ShortcutHelper.NotifyShell();

                // 5. Hapus %APPDATA%\Keystream jika dipilih
                if (cleanAppData)
                {
                    Dispatcher.Invoke(() => txtStatus.Text = SetupLocalizer.Get("W0070"));
                    string appDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keystream");
                    if (Directory.Exists(appDataDir))
                    {
                        try { Directory.Delete(appDataDir, true); } catch { }
                    }
                }

                // 6. Hapus folder instalasi.
                // Bila proses ini berjalan dari salinan staged di luar
                // folder instalasi, hapus langsung dan verifikasi sebelum
                // panel selesai. Bila berjalan dari dalam folder instalasi
                // (staging gagal), exe terkunci: luncurkan penghapus
                // percobaan-ulang yang menunggu sampai proses ini keluar.
                if (Directory.Exists(installDir))
                {
                    if (selfInsideInstallDir)
                    {
                        installDirRetryLaunched = LaunchRetryDelete(installDir);
                        installDirRemoved = false;
                    }
                    else
                    {
                        installDirRemoved = DeleteDirectoryWithRetry(installDir);
                    }
                }
            }
            catch { }
        });

        pnlProgress.Visibility = Visibility.Collapsed;
        pnlFinished.Visibility = Visibility.Visible;

        var warnings = new List<string>();
        if (driverUninstallFailed)
        {
            warnings.Add(SetupLocalizer.Get("W0071"));
        }
        if (!startupRemoved)
        {
            warnings.Add(SetupLocalizer.Get("W0072"));
        }
        if (!installDirRemoved)
        {
            if (selfInsideInstallDir)
            {
                warnings.Add(installDirRetryLaunched
                    ? SetupLocalizer.Format("W0073", installDir)
                    : SetupLocalizer.Format("W0074", installDir));
            }
            else
            {
                warnings.Add(SetupLocalizer.Format("W0075", installDir));
            }
        }
        if (warnings.Count > 0)
        {
            txtCleanupWarning.Text = string.Join("\n", warnings);
            pnlCleanupWarning.Visibility = Visibility.Visible;
            txtFinishedTitle.Text = SetupLocalizer.Get("W0076");
            txtFinishedSubtitle.Text = SetupLocalizer.Get("W0077");
        }

        if (driverUninstalled)
        {
            pnlDriverRestartNotice.Visibility = Visibility.Visible;
            btnRestartLater.Visibility = Visibility.Visible;
            btnRestartNow.Visibility = Visibility.Visible;
            btnClose.Visibility = Visibility.Collapsed;
        }
        else
        {
            pnlDriverRestartNotice.Visibility = Visibility.Collapsed;
            btnRestartLater.Visibility = Visibility.Collapsed;
            btnRestartNow.Visibility = Visibility.Collapsed;
            btnClose.Visibility = Visibility.Visible;
        }
    }

    private static bool DeleteDirectoryWithRetry(string dir)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
            if (!Directory.Exists(dir)) return true;
            Task.Delay(500).Wait();
        }
        return !Directory.Exists(dir);
    }

    private static bool LaunchRetryDelete(string dir)
    {
        // PowerShell retry loop: polls until the folder is gone, so it
        // keeps retrying after this (locked-exe) process has exited.
        // The Process.Start result is checked by the caller: a null
        // process means no deleter is running.
        try
        {
            string escaped = dir.Replace("'", "''");
            string script = "for ($i=0; $i -lt 60 -and (Test-Path '" + escaped + "'); $i++) { Start-Sleep -Seconds 1; try { Remove-Item '" + escaped + "' -Recurse -Force -ErrorAction Stop } catch { } }";
            var psi = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            using var p = Process.Start(psi);
            return p != null;
        }
        catch
        {
            return false;
        }
    }

    private string? StagingDir
    {
        get
        {
            string self = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(self)) return null;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(self));
            if (string.IsNullOrEmpty(dir)) return null;
            if (!dir.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) return null;
            if (!Path.GetFileName(dir).StartsWith("KeystreamUninstall-", StringComparison.OrdinalIgnoreCase)) return null;
            return dir;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        string? stagingDir = StagingDir;
        if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
        {
            LaunchRetryDelete(stagingDir);
        }
        base.OnClosed(e);
    }

    private void btnRestartLater_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void btnRestartNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("shutdown", "/r /t 0");
        }
        catch { }
        Close();
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
