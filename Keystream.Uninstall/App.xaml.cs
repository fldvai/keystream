using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Keystream.Core.Localization;
using Keystream.Setup.Localization;

namespace Keystream.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Wave-2 (Phase B): resolve the saved language BEFORE any window
        // is created, so even the 64-bit dialog below is localized; "en"
        // when no valid config exists. Setup cannot reference
        // Keystream.Core, so the Setup-local table/localizer are used.
        SetupLocalizer.SetLanguage(ConfigLanguage.ReadSavedLanguage());
        FlowDirectionHelper.InstallClassHandler(() => SetupLocalizer.Language);
        SetupLocalizer.LanguageChanged += () => FlowDirectionHelper.RefreshAllOpenWindows(SetupLocalizer.Language);

        if (e.Args.Contains("--install-driver", StringComparer.OrdinalIgnoreCase))
        {
            int code = DriverInstaller.InstallDriverOnly();
            Shutdown(code);
            return;
        }

        if (e.Args.Contains("--uninstall-driver", StringComparer.OrdinalIgnoreCase))
        {
            int code = DriverInstaller.UninstallDriverOnly();
            Shutdown(code);
            return;
        }

        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            // Staged-uninstaller gate (A-14): the uninstaller is normally
            // the copy inside the install folder, whose exe stays
            // locked while it runs, so it can never delete its own folder.
            // Run the confirm/progress UI from a copy in %TEMP% instead;
            // the staged copy can delete the install folder and verify it
            // before showing "Complete". If staging fails, fall through
            // to the in-place window (its retry deleter is best-effort).
            if (!e.Args.Contains("--uninstall-staged", StringComparer.OrdinalIgnoreCase)
                && TryLaunchStagedUninstall())
            {
                Shutdown(0);
                return;
            }

            var uninstallWin = new UninstallWindow();
            uninstallWin.Show();
            return;
        }

        if (!Environment.Is64BitOperatingSystem)
        {
            MessageDialog.Show(
                null,
                SetupLocalizer.Get("W0079"),
                SetupLocalizer.Get("W0078"));
            Shutdown(1);
            return;
        }

        // No arguments: behave like a regular uninstaller.
        var win = new UninstallWindow();
        win.Show();
    }

    private static bool TryLaunchStagedUninstall()
    {
        string stagingDir = "";
        try
        {
            string self = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(self) || !File.Exists(self)) return false;

            string installDir = ShortcutHelper.TryGetInstallLocation() ?? ShortcutHelper.DefaultInstallDir;
            string installRoot = Path.GetFullPath(installDir) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(self).StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)) return false;

            stagingDir = Path.Combine(Path.GetTempPath(), "KeystreamUninstall-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);
            string stagedExe = Path.Combine(stagingDir, "Uninstall.exe");
            File.Copy(self, stagedExe, overwrite: false);

            using var p = Process.Start(new ProcessStartInfo(stagedExe, "--uninstall --uninstall-staged")
            {
                UseShellExecute = true
            });
            if (p == null)
            {
                try { Directory.Delete(stagingDir, true); } catch { }
                return false;
            }
            return true;
        }
        catch
        {
            try
            {
                if (!string.IsNullOrEmpty(stagingDir) && Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
            }
            catch { }
            return false;
        }
    }
}
