using System.Text.Json;
using System.Text.Json.Nodes;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Keystream.Setup.Localization;

namespace Keystream.Setup;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private string _installDir;
    private string _exePath;
    private bool _isUpdate;
    private bool _applyingLanguage;

    public MainWindow()
    {
        InitializeComponent();

        _installDir = ShortcutHelper.TryGetInstallLocation() ?? ShortcutHelper.DefaultInstallDir;
        _exePath = Path.Combine(_installDir, "Keystream.exe");
        _isUpdate = File.Exists(_exePath);

        txtInstallPath.Text = _installDir;

        BuildLanguageDropdown();
        ApplyTexts();
    }

    /// <summary>Re-applies every wizard text from the Setup string
    /// table (W#### keys) for the current language. XAML keeps the
    /// English defaults as design-time fallback; this method is the
    /// runtime source of truth, called in the ctor and on every
    /// language-dropdown change. Variant (install/update) texts go
    /// through <see cref="ApplyVariantTexts"/>.</summary>
    private void ApplyTexts()
    {
        Title = SetupLocalizer.Get("W0001");
        txtInstallLocationLabel.Text = SetupLocalizer.Get("W0003");
        btnBrowse.Content = SetupLocalizer.Get("W0004");
        chkDesktop.Content = SetupLocalizer.Get("W0005");
        btnCancel.Content = SetupLocalizer.Get("W0006");
        txtInstallingSubtitle.Text = SetupLocalizer.Get("W0009");
        txtStatus.Text = SetupLocalizer.Get("W0010");
        txtDetail.Text = SetupLocalizer.Get("W0011");
        txtDriverNoticeTitle.Text = SetupLocalizer.Get("W0014");
        txtDriverNoticeBody.Text = SetupLocalizer.Get("W0015");
        chkLaunch.Content = SetupLocalizer.Get("W0016");
        chkRunAtStartup.Content = SetupLocalizer.Get("W0017");
        btnRestartLater.Content = SetupLocalizer.Get("W0018");
        btnRestartNow.Content = SetupLocalizer.Get("W0019");
        btnFinish.Content = SetupLocalizer.Get("W0020");
        ApplyVariantTexts();
        SyncLanguageDropdown();
    }

    /// <summary>Install/update variant texts, re-applied wherever the
    /// variant is (re-)decided: ctor, and btnInstall_Click after the
    /// install folder is validated (an existing exe flips install to
    /// update).</summary>
    private void ApplyVariantTexts()
    {
        txtWelcomeTitle.Text = SetupLocalizer.Get(_isUpdate ? "W0035" : "W0002");
        btnInstall.Content = SetupLocalizer.Get(_isUpdate ? "W0036" : "W0007");
        txtInstallingTitle.Text = SetupLocalizer.Get(_isUpdate ? "W0037" : "W0008");
        txtFinishedTitle.Text = SetupLocalizer.Get(_isUpdate ? "W0038" : "W0012");
        txtFinishedSubtitle.Text = SetupLocalizer.Get(_isUpdate ? "W0039" : "W0013");
    }

    /// <summary>Language dropdown (welcome panel only): endonyms only
    /// - each language's own name is self-identifying in every UI
    /// language, so no label or code prefix is needed and the control
    /// fits the panel's empty top-right corner without moving any
    /// other element. Built once; selection is synced, never rebuilt,
    /// on re-apply.</summary>
    private void BuildLanguageDropdown()
    {
        _applyingLanguage = true;
        try
        {
            cmbLanguage.Items.Clear();
            foreach (var (code, endonym) in SetupLocalizer.Languages)
                cmbLanguage.Items.Add(new ComboBoxItem { Content = endonym, Tag = code });
            SyncLanguageDropdown();
        }
        finally
        {
            _applyingLanguage = false;
        }
    }

    private void SyncLanguageDropdown()
    {
        _applyingLanguage = true;
        try
        {
            foreach (ComboBoxItem item in cmbLanguage.Items)
            {
                if (item.Tag is string code && code == SetupLocalizer.Language)
                {
                    cmbLanguage.SelectedItem = item;
                    return;
                }
            }
            foreach (ComboBoxItem item in cmbLanguage.Items)
            {
                if (item.Tag is string code && code == "en")
                {
                    cmbLanguage.SelectedItem = item;
                    return;
                }
            }
        }
        finally
        {
            _applyingLanguage = false;
        }
    }

    private void cmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguage) return;
        if (cmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string code)
        {
            SetupLocalizer.SetLanguage(code);
            // SetLanguage fires LanguageChanged only on a real change
            // (FlowDirection refresh rides that event); re-render
            // unconditionally so re-selecting the same language still
            // re-applies every text.
            ApplyTexts();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 19041+ / Windows 11)
            DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        }
        catch { }
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void btnBrowse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string current = (txtInstallPath.Text ?? "").Trim();
            string initial = Directory.Exists(current) ? Path.GetFullPath(current) : _installDir;
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = SetupLocalizer.Get("W0040"),
                InitialDirectory = initial
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                txtInstallPath.Text = dlg.FolderName;
            }
        }
        catch { }
    }

    // Validation messages: {0} carries the OS/.NET message (English by nature - see the accepted limit in tools/gen_setup_strings.py).
    private static bool TryValidateInstallDir(string? input, out string dir, out string? error)
    {
        dir = "";
        error = null;
        string chosen = (input ?? "").Trim();
        if (chosen.Length == 0)
        {
            error = SetupLocalizer.Get("W0041");
            return false;
        }
        if (chosen.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = SetupLocalizer.Get("W0042");
            return false;
        }
        string full;
        try
        {
            full = Path.GetFullPath(chosen);
        }
        catch (Exception ex)
        {
            error = SetupLocalizer.Format("W0043", ex.Message);
            return false;
        }
        if (!Path.IsPathRooted(full))
        {
            error = SetupLocalizer.Get("W0044");
            return false;
        }
        if (string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
        {
            error = SetupLocalizer.Get("W0045");
            return false;
        }
        if (File.Exists(full))
        {
            error = SetupLocalizer.Get("W0046");
            return false;
        }
        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex)
        {
            error = SetupLocalizer.Format("W0047", ex.Message);
            return false;
        }
        dir = full;
        return true;
    }

    private async void btnInstall_Click(object sender, RoutedEventArgs e)
    {
        bool createDesktop = chkDesktop.IsChecked == true;

        if (!TryValidateInstallDir(txtInstallPath.Text, out string chosenDir, out string? validationError))
        {
            MessageDialog.Show(this, SetupLocalizer.Get("W0048"), validationError);
            return;
        }

        _installDir = chosenDir;
        _exePath = Path.Combine(_installDir, "Keystream.exe");
        _isUpdate = File.Exists(_exePath);
        ApplyVariantTexts();

        pnlWelcome.Visibility = Visibility.Collapsed;
        pnlInstalling.Visibility = Visibility.Visible;

        bool driverJustInstalled = false;
        string? errorMessage = null;

        await Task.Run(() =>
        {
            try
            {
                // 1. Matikan Keystream bila sedang berjalan
                UpdateProgress(10, SetupLocalizer.Get("W0049"), SetupLocalizer.Get("W0050"));
                foreach (var p in Process.GetProcessesByName("Keystream"))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                }

                // 2. Ekstrak payload
                UpdateProgress(20, SetupLocalizer.Get("W0051"), SetupLocalizer.Get("W0052"));
                Directory.CreateDirectory(_installDir);

                // Bersihkan file runtime lama (dari versi self-contained sebelumnya) agar tidak bentrok dengan .NET sistem
                string[] staleRuntimeFiles =
                [
                    "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "clrgc.dll",
                    "mscordaccore.dll", "mscordbi.dll", "mscorrc.dll", "mscorlib.dll"
                ];
                foreach (var file in staleRuntimeFiles)
                {
                    try
                    {
                        string p = Path.Combine(_installDir, file);
                        if (File.Exists(p)) File.Delete(p);
                    }
                    catch { }
                }

                using (var stream = typeof(App).Assembly.GetManifestResourceStream("payload.zip")
                    ?? throw new InvalidOperationException(SetupLocalizer.Get("W0065")))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string root = Path.GetFullPath(_installDir) + Path.DirectorySeparatorChar;
                    int total = archive.Entries.Count;
                    int count = 0;

                    foreach (var entry in archive.Entries)
                    {
                        count++;
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                        string fullPath = Path.GetFullPath(Path.Combine(_installDir, entry.FullName));
                        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(SetupLocalizer.Format("W0066", entry.FullName));

                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                        entry.ExtractToFile(fullPath, overwrite: true);

                        int pct = 20 + (int)(50.0 * count / Math.Max(1, total));
                        if (count % 10 == 0 || count == total)
                        {
                            UpdateProgress(pct, SetupLocalizer.Get("W0051"), Path.GetFileName(fullPath));
                        }
                    }
                }

                // Hapus salinan KeystreamSetup.exe lama (pra-pemisahan):
                // uninstaller sekarang Uninstall.exe dari payload. Salinan
                // lama hanya membuang ruang; jangan hapus bila Setup
                // justru dijalankan dari salinan itu (terkunci, gagal diam-diam).
                try
                {
                    string self = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    string staleSetup = Path.Combine(_installDir, "KeystreamSetup.exe");
                    if (File.Exists(staleSetup) && !string.Equals(Path.GetFullPath(self), Path.GetFullPath(staleSetup), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(staleSetup);
                    }
                }
                catch { }

                // 3. Buat Shortcut
                UpdateProgress(75, SetupLocalizer.Get("W0053"), SetupLocalizer.Get("W0054"));
                string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                ShortcutHelper.CreateShortcut(_exePath, _installDir, Path.Combine(programsDir, "Keystream.lnk"), SetupLocalizer.Get("W0063"));
                ShortcutHelper.CreateShortcut(_exePath, _installDir, Path.Combine(programsDir, "Keystream", "Keystream.lnk"), SetupLocalizer.Get("W0063"));

                if (createDesktop)
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Keystream.lnk");
                    ShortcutHelper.CreateShortcut(_exePath, _installDir, desktopLnk, SetupLocalizer.Get("W0063"));
                }

                // 4. Daftarkan ke Windows (App Paths & Uninstall)
                UpdateProgress(85, SetupLocalizer.Get("W0055"), SetupLocalizer.Get("W0056"));
                long installedBytes = 0;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(_installDir, "*", SearchOption.AllDirectories))
                    {
                        installedBytes += new FileInfo(file).Length;
                    }
                }
                catch { }

                ShortcutHelper.RegisterAppPaths(_exePath, _installDir);
                ShortcutHelper.RegisterUninstall(_exePath, _installDir, installedBytes > 0 ? installedBytes : 9 * 1024 * 1024);
                ShortcutHelper.NotifyShell();

                // 5. Cek Driver Interception
                UpdateProgress(90, SetupLocalizer.Get("W0057"), SetupLocalizer.Get("W0058"));
                bool driverInstallFailed = false;
                int driverHelperCode = 0;
                if (!DriverInstaller.IsDriverInstalled())
                {
                    UpdateProgress(95, SetupLocalizer.Get("W0059"), SetupLocalizer.Get("W0060"));
                    driverHelperCode = DriverInstaller.RunElevatedDriverInstall();
                    if (driverHelperCode == 0 && DriverInstaller.IsDriverInstalled())
                    {
                        driverJustInstalled = true;
                    }
                    else
                    {
                        driverInstallFailed = true;
                    }
                }

                if (driverInstallFailed)
                {
                    errorMessage = SetupLocalizer.Format("W0061", driverHelperCode);
                }
                else
                {
                    // "Installation Complete!" at 100% is unconditional in the original code (even on update); preserved as W0012.
                    UpdateProgress(100, SetupLocalizer.Get("W0012"), SetupLocalizer.Get("W0062"));
                    Task.Delay(350).Wait();
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
        });

        if (!string.IsNullOrEmpty(errorMessage))
        {
            MessageDialog.Show(
                this,
                SetupLocalizer.Get("W0048"),
                SetupLocalizer.Format("W0064", errorMessage));
            pnlInstalling.Visibility = Visibility.Collapsed;
            pnlWelcome.Visibility = Visibility.Visible;
            return;
        }

        // Tampilkan panel selesai
        pnlInstalling.Visibility = Visibility.Collapsed;
        pnlFinished.Visibility = Visibility.Visible;

        if (driverJustInstalled)
        {
            pnlDriverNotice.Visibility = Visibility.Visible;
            btnRestartNow.Visibility = Visibility.Visible;
            btnRestartLater.Visibility = Visibility.Visible;
            btnFinish.Visibility = Visibility.Collapsed;
        }
        else
        {
            pnlDriverNotice.Visibility = Visibility.Collapsed;
            btnRestartNow.Visibility = Visibility.Collapsed;
            btnRestartLater.Visibility = Visibility.Collapsed;
            btnFinish.Visibility = Visibility.Visible;
        }
    }

    private void UpdateProgress(int percent, string status, string detail)
    {
        Dispatcher.Invoke(() =>
        {
            pbProgress.Value = percent;
            txtStatus.Text = status;
            txtDetail.Text = detail;
        });
    }

    private void btnFinish_Click(object sender, RoutedEventArgs e)
    {
        ApplyStartupSettingIfNeeded();
        ConfigLanguage.ApplyLanguage(SetupLocalizer.Language);
        LaunchAppIfNeeded();
        Close();
    }

    private void btnRestartLater_Click(object sender, RoutedEventArgs e)
    {
        ApplyStartupSettingIfNeeded();
        ConfigLanguage.ApplyLanguage(SetupLocalizer.Language);
        LaunchAppIfNeeded();
        Close();
    }

    private void btnRestartNow_Click(object sender, RoutedEventArgs e)
    {
        ApplyStartupSettingIfNeeded();
        ConfigLanguage.ApplyLanguage(SetupLocalizer.Language);
        try
        {
            Process.Start("shutdown", "/r /t 0");
        }
        catch { }
        Close();
    }

    private void ApplyStartupSettingIfNeeded()
    {
        bool runAtStartup = chkRunAtStartup.IsChecked == true;
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appData, "Keystream");
            Directory.CreateDirectory(configDir);
            string configPath = Path.Combine(configDir, "config.json");

            if (File.Exists(configPath))
            {
                var doc = JsonNode.Parse(File.ReadAllText(configPath));
                if (doc != null)
                {
                    if (doc["settings"] == null)
                    {
                        doc["settings"] = new JsonObject();
                    }
                    doc["settings"]!["startWithWindows"] = runAtStartup;
                    File.WriteAllText(configPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }

            const string taskName = "KeystreamStartup";
            if (runAtStartup && File.Exists(_exePath))
            {
                var psi = new ProcessStartInfo("schtasks.exe",
                    $"/create /tn \"{taskName}\" /tr \"\\\"{_exePath}\\\"\" /sc onlogon /rl highest /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.SetValue("Keystream", $"\"{_exePath}\"");
            }
            else
            {
                var psi = new ProcessStartInfo("schtasks.exe",
                    $"/delete /tn \"{taskName}\" /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.DeleteValue("Keystream", throwOnMissingValue: false);
            }
        }
        catch { }
    }

    private void LaunchAppIfNeeded()
    {
        if (chkLaunch.IsChecked == true && File.Exists(_exePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = _installDir
                });
            }
            catch { }
        }
    }
}
