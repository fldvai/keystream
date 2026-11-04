using System.Diagnostics;
using System.IO;
using System.Windows;
using Keystream.App.Host;
using Keystream.App.Services;
using Keystream.App.ViewModels;
using Keystream.App.Views;
using Keystream.Core.Config;
using Keystream.Core.Localization;
using Forms = System.Windows.Forms;

namespace Keystream.App;

/// <summary>
/// Application entry: single-instance mutex (FR-A2 - the audited App.xaml.cs
/// was an empty template), composition via AppHost, tray icon with
/// show/panic/exit, and an orderly shutdown that stops the interception
/// service BEFORE the process dies so no stroke is ever stranded inside a
/// dead context.
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppHost? _host;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private bool _mainWindowClosed;
    private Forms.NotifyIcon? _tray;
    private ModernTrayMenu? _trayMenu;

    /// <summary>§151 P1a - detail of the most recent failed
    /// <see cref="ResetDataAndRestart"/> attempt (the
    /// <c>failureDetail</c> from TryDeleteAppData), verbatim, or
    /// null when the last attempt succeeded, never ran, or failed
    /// without a detail. Cleared at the start of every attempt so
    /// a stale detail can never be shown for a different failure.
    /// Read by SettingsWindow.ResetData_Click to compose the
    /// persistent footer text (S1689 + detail); the bool-only
    /// return value of ResetDataAndRestart is unchanged.</summary>
    public string? LastResetFailureDetail { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // §86: apply the saved language BEFORE any window (or the
        // single-instance dialog) is created, so the first frame already
        // uses it. ConfigStore.Load is a pure read (no writes), so this
        // pre-read is safe; AppHost loads the same file right after.
        Localizer.SetLanguage(ConfigStore.ForCurrentUser().Load().Config.Settings.Language);

        // Wave-2 RTL: every Window (including dialogs opened later)
        // inherits FlowDirection from the current language via the
        // Loaded class handler; a language switch refreshes all open
        // windows. BrowserIconComposer rasterisation is untouched (its
        // FlowDirection.LeftToRight is a FormattedText argument, not a
        // Window). The language pre-read line above is unchanged.
        FlowDirectionHelper.InstallClassHandler(() => Localizer.Language);
        Localizer.LanguageChanged += () => FlowDirectionHelper.RefreshAllOpenWindows(Localizer.Language);

        // Mouse wheel over a CLOSED ComboBox scrolls the page, never the
        // combo's selection (v2.9.26, the user's complaint: while scrolling
        // a modal, passing over a dropdown silently changed "Jenis aksi").
        // An OPEN dropdown keeps its own wheel scrolling.
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ComboBox),
            UIElement.PreviewMouseWheelEvent,
            new System.Windows.Input.MouseWheelEventHandler(ComboBox_PreviewMouseWheel));

        // §110: Esc revert untuk semua TextBox (TextBoxRevert; satu
        // registrasi class handler, di samping handler ComboBox di
        // atas). Revert berjalan di KeyDown bubbling dengan
        // handledEventsToo=false, jadi PreviewKeyDown capture yang
        // menelan Esc selalu menang.
        TextBoxRevert.Register();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("DispatcherUnhandledException", args.Exception);
            // Dialog menampilkan pesan + penyebab dalam (inner exception):
            // untuk error XAML, nama resource yang hilang hanya ada di
            // inner exception - tanpa ini pengguna hanya melihat pesan
            // generik. Handler sendiri tidak boleh melempar.
            string detail = args.Exception.Message;
            if (args.Exception.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
                detail += Localizer.Get("S0305") + inner.Message;
            try
            {
                AppDialog.Show(null,
                    Localizer.Get("S0306") + detail +
                    Localizer.Get("S0307") + CrashLog.LogPath,
                    "Keystream", AppDialogButtons.Ok, AppDialogIcon.Warning);
            }
            catch { /* handler error tidak boleh menjadi error kedua */ }
            args.Handled = true;
        };

        try
        {
            StartupCore();
        }
        catch (Exception ex)
        {
            CrashLog.Write("STARTUP GAGAL", ex);
            AppDialog.Show(null,
                Localizer.Get("S0309") + CrashLog.LogPath +
                "\n\n" + ex.Message,
                "Keystream", AppDialogButtons.Ok, AppDialogIcon.Error);
            Shutdown();
        }
    }

    private void StartupCore()
    {

        // Mutex abandoned (pemilik sebelumnya mati tanpa melepas) melempar
        // AbandonedMutexException PADAHAL mutex justru berhasil diambil
        // alih - tanpa tangkapan ini, kill/crash sebelumnya membuat start
        // berikutnya selalu gagal sekali dengan dialog yang menakutkan.
        bool isFirst;
        try
        {
            _singleInstance = new Mutex(initiallyOwned: true, "Keystream.SingleInstance", out isFirst);
        }
        catch (AbandonedMutexException)
        {
            isFirst = true;
            try { _singleInstance = Mutex.OpenExisting("Keystream.SingleInstance"); } catch { _singleInstance = null; }
        }
        if (!isFirst)
        {
            AppDialog.Show(null, Localizer.Get("S0311"), "Keystream",
                AppDialogButtons.Ok, AppDialogIcon.Info);
            Shutdown();
            return;
        }

        _host = new AppHost();
        _viewModel = new MainViewModel(_host);
        _mainWindow = CreateMainWindow();

        _tray = new Forms.NotifyIcon
        {
            Icon = GetAppIcon(),
            Text = "Keystream",
            Visible = true,
        };

        _trayMenu = new ModernTrayMenu(_host,
            onExitApp: () => ExitApp());

        // Right-click = native context menu (standard tray behavior)
        _tray.ContextMenuStrip = _trayMenu;
        _trayMenu.Opening += (_, _) => _trayMenu.UpdateState();

        // Left-click = open the main app window
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };

        _host.Start();
        CrashLog.WriteInfo($"Startup OK — StartMinimized={_host.Config.Settings.StartMinimized}");

        if (!_host.Config.Settings.StartMinimized)
            ShowMainWindow();

        CheckDriverStateAndPromptIfNeeded();

        // §171: satu pemeriksaan pembaruan otomatis (hanya bila
        // checkbox di Pengaturan aktif) - fire-and-forget, tidak
        // pernah memblokir startup; semua kegagalan diam.
        _ = RunAutoUpdateCheckAsync();
    }

    /// <summary>§171: pemeriksaan otomatis saat startup lewat jalur
    /// bersama (UpdateChecker). DIAM bila gagal, bila tag sama dengan
    /// tag terakhir yang dilihat, atau bila checkbox mati. Dialog +
    /// tawaran halaman rilis HANYA bila tag terbaru berbeda dari
    /// LastSeenReleaseTag (B-03: gate tag, bukan versi). B-02: jalur
    /// otomatis TIDAK PERNAH memanggil DownloadAndInstall - jawaban
    /// Ya hanya membuka halaman rilis di browser, unduh/instal
    /// manual di sana; unduh otomatis hanya dari jalur manual di
    /// Pengaturan sesudah konfirmasi eksplisit. Seluruh badan
    /// dibungkus try/catch: pemeriksaan tidak boleh merusak
    /// startup apa pun yang terjadi.</summary>
    private async Task RunAutoUpdateCheckAsync()
    {
        try
        {
            if (_host is null || !_host.Config.Settings.AutoCheckUpdates) return;
            // B-03: tangkap tag terakhir SEBELUM RecordResult
            // menimpanya dengan tag remote (perbandingan sesudah
            // RecordResult selalu sama dan gate tidak pernah lolos).
            string lastSeenTag = _host.Config.Settings.LastSeenReleaseTag;
            var result = await UpdateChecker.CheckLatestAsync();
            if (result.Release is null) return;
            bool isNewTag = UpdateChecker.IsNewReleaseTag(result.Release.Tag, lastSeenTag);
            UpdateChecker.RecordResult(_host.Config.Settings, result.Release, DateTimeOffset.UtcNow);
            try { _host.Save(); } catch { /* diam: pemeriksaan startup tidak boleh memunculkan error simpan */ }

            // B-03: tawarkan hanya bila tag berubah; versi tidak
            // memveto (selalu 1.0.0, tanpa penomoran formal).
            if (!isNewTag) return;

            var answer = AppDialog.ShowTwoChoices(_mainWindow, UpdateChecker.BuildOpenPageDialogText(result.Release),
                Localizer.Get("S1554"), Localizer.Get("S1842"), Localizer.Get("S1843"), AppDialogIcon.Question);
            // B-02: jalur otomatis hanya beritahu - Ya membuka
            // halaman rilis di browser, TIDAK PERNAH mengunduh/
            // menginstal (DownloadAndInstall hanya jalur manual).
            if (answer == AppDialogResult.Yes)
                UpdateChecker.OpenReleasePage(_mainWindow, result.Release.PageUrl);
        }
        catch
        {
            // Diam: pemeriksaan pembaruan tidak boleh merusak startup.
        }
    }

    private static void ComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo || combo.IsDropDownOpen) return;
        var scroll = FindAncestor<System.Windows.Controls.ScrollViewer>(combo);
        if (scroll is null) return;
        e.Handled = true;
        double step = SystemParameters.WheelScrollLines * 16.0;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - Math.Sign(e.Delta) * step);
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>The ✕ inside every TextBox (template in App.xaml):
    /// clears the field that owns the button; the TwoWay binding carries
    /// the empty value back to the view model.</summary>
    private void ClearTextBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn
            && btn.TemplatedParent is System.Windows.Controls.TextBox tb)
        {
            tb.Clear();
            tb.Focus();
        }
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_host!, _viewModel!);
        window.Closed += (_, _) => _mainWindowClosed = true;
        _mainWindowClosed = false;
        return window;
    }

    private void ShowMainWindow()
    {
        if (_host is null || _viewModel is null) return;
        // A window that was truly closed (not hidden to tray) can never be
        // re-shown - recreate it instead of dying on Show().
        if (_mainWindow is null || _mainWindowClosed)
            _mainWindow = CreateMainWindow();
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private static System.Drawing.Icon GetAppIcon()
    {
        try
        {
            string appIco = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(appIco))
                return new System.Drawing.Icon(appIco);

            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && System.IO.File.Exists(procPath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(procPath);
                if (icon is not null) return icon;
            }

            var uri = new Uri("pack://application:,,,/Keystream;component/app.ico", UriKind.RelativeOrAbsolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo is not null)
                return new System.Drawing.Icon(streamInfo.Stream);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Gagal memuat icon aplikasi untuk tray", ex);
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void ExitApp()
    {
        if (_mainWindow is not null) _mainWindow.ExitRequested = true;
        _trayMenu?.Dispose();
        if (_tray is not null) _tray.Visible = false;
        _host?.StopService();
        // Save gagal saat keluar TIDAK BOLEH membatalkan keluar: dulu
        // exception di sini menghentikan ExitApp di tengah jalan -
        // proses zombie tanpa tray sambil memegang mutex (§45).
        // §130: SuppressSaveOnExit (data reset) melewati save ini -
        // menulis sesudah penghapusan akan membangkitkan config lama.
        try { if (_host is not null && !_host.SuppressSaveOnExit) _host.Save(); } catch (Exception ex) { CrashLog.Write("Save saat keluar gagal", ex); }
        try { _host?.Dispose(); } catch (Exception ex) { CrashLog.Write("Dispose saat keluar gagal", ex); }
        _tray?.Dispose();
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Ignore if current thread does not own mutex
        }
        _singleInstance?.Dispose();
        Shutdown();
    }

    /// <summary>§130 - data reset, dipanggil dari Settings sesudah
    /// modal konfirmasi DELETE. Urutan: (1) penekan save DINAIKKAN
    /// sebelum penghapusan, agar tidak ada penulis config yang
    /// bangkit sesudahnya; (1b) §151 P3 quiesce - timer dihentikan,
    /// service dihentikan, Save latar dikeringkan lewat _saveGate -
    /// sebelum penghapusan; gagal = quiesce DIPULIHKAN (timer/
    /// service kembali seperti sebelum reset, config utuh) dan
    /// flag diturunkan; (2) himpunan file dihapus + diverifikasi,
    /// dengan tulisan CrashLog DITEKAN selama penghapusan (§151 P5)
    /// dan baris hasil ditulis sesudahnya tanpa gating (§151 P1b);
    /// gagal = flag DITURUNKAN lagi dan false, aplikasi tetap jalan
    /// dengan config lamanya; (3) teardown lewat jalur keluar yang
    /// sudah ada (StopService + Dispose host + tray, tanpa Save);
    /// (4) mutex single-instance DILEPAS SEBELUM proses baru
    /// diluncurkan - proses baru yang start selagi mutex dipegang
    /// akan ditolak StartupCore sebagai "sudah berjalan" (dialog
    /// S0311 lalu Shutdown); (5) proses baru dari
    /// Environment.ProcessPath TANPA argumen, via shell execute
    /// sehingga manifest requireAdministrator (§127) memunculkan
    /// prompt UAC seperti start normal; (6) Shutdown. Proses baru
    /// membaca root yang kosong → ConfigStore.Load mengembalikan
    /// FreshDefault (CreateDefault) - kondisi pertama kali diinstal.
    /// Bila UAC ditolak, Process.Start melempar dan aplikasi tetap
    /// keluar (data sudah terhapus); user me-restart manual ke
    /// kondisi first-install yang sama.</summary>
    public bool ResetDataAndRestart()
    {
        if (_host is null) return false;
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath)) return false;

        LastResetFailureDetail = null;
        _host.SuppressSaveOnExit = true;
        // §151 P3: quiesce BEFORE deletion (timers + service +
        // drained Save gate). On the success path below the
        // quiesce is deliberately NOT restored - the process
        // exits and the teardown re-stops the same (idempotent)
        // service/timers.
        // Snapshot quiesce disimpan di host sampai restore/keluar.
        _host.BeginResetQuiesce();
        bool deleted;
        try
        {
            // §151 P5 option (b): suppress CrashLog writes for the
            // synchronous duration of the delete, so no background
            // writer recreates crash.log (and the root) mid-delete.
            // The scope restores the previous state on every path,
            // including an unexpected throw below.
            using (CrashLog.SuppressWrites())
            {
                deleted = _host.TryDeleteAppData(out string? failureDetail);
                LastResetFailureDetail = failureDetail;
            }
        }
        catch
        {
            // An unexpected throw from the delete path must not
            // leave the host quiesced with the save gate held:
            // restore, lower the flag, and let the throw surface
            // to the dispatcher's crash handling (with its log
            // line - suppression has already ended).
            _host.RestoreAfterFailedReset();
            _host.SuppressSaveOnExit = false;
            throw;
        }
        if (!deleted)
        {
            _host.RestoreAfterFailedReset();
            _host.SuppressSaveOnExit = false;
            if (!string.IsNullOrEmpty(LastResetFailureDetail))
                // §151 P1b: the failure detail is written to
                // crash.log UNGATED (every Log Level, including
                // Error) via WriteResetFailure, in the exact
                // "[Warning] Reset Data gagal: <detail>" line
                // format. The old comment claiming the footer
                // showed the detail was false (§150 F6): the
                // footer showed only static S1689, and the detail
                // never reached the user - §151 P1a now shows it
                // persistently in the footer as well, so the log
                // line and the footer carry the same words.
                CrashLog.WriteResetFailure(LastResetFailureDetail);
            return false;
        }

        if (_mainWindow is not null) _mainWindow.ExitRequested = true;
        _trayMenu?.Dispose();
        if (_tray is not null) _tray.Visible = false;
        // §130 (verifikasi): StopService tanpa pelindung di sini
        // berarti satu lemparan sesudah penghapusan berhasil membuat
        // aplikasi setengah-mati tanpa restart (tray sudah disembunyikan,
        // data sudah terhapus). Samakan dengan Dispose di bawah: catat
        // ke crash.log lalu lanjut ke restart.
        try { _host.StopService(); } catch (Exception ex) { CrashLog.Write("StopService saat reset gagal", ex); }
        try { _host.Dispose(); } catch (Exception ex) { CrashLog.Write("Dispose saat reset gagal", ex); }
        _tray?.Dispose();
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Ignore if current thread does not own mutex
        }
        _singleInstance?.Dispose();
        _singleInstance = null;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = processPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Restart sesudah Reset Data gagal", ex);
        }
        Shutdown();
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayMenu?.Dispose();
        if (_host is not null)
        {
            try { _host.StopService(); } catch { }
            try { _host.Dispose(); } catch (Exception ex) { CrashLog.Write("Dispose saat OnExit gagal", ex); }
        }
        base.OnExit(e);
    }

    private void CheckDriverStateAndPromptIfNeeded()
    {
        try
        {
            var driverState = Keystream.Windows.Interception.DriverStatus.ProbeKernelState();
            if (driverState == Keystream.Windows.Interception.DriverState.PendingRestart)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string title = Keystream.Core.Localization.Localizer.Get("S1905");
                    string message = Keystream.Core.Localization.Localizer.Get("S1906");

                    var choice = Views.AppDialog.Show(
                        _mainWindow,
                        message,
                        title,
                        Views.AppDialogButtons.YesNo,
                        Views.AppDialogIcon.Warning);

                    if (choice == Views.AppDialogResult.Yes)
                    {
                        try { Process.Start("shutdown", "/r /t 0"); } catch { }
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
        catch { }
    }
}
