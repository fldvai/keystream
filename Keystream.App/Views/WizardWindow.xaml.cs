using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Keystream.Core.Localization;
using Keystream.App.Host;
using Keystream.Core.Devices;
using Keystream.Core.Engine;
using Keystream.Windows.Interception;

namespace Keystream.App.Views;

/// <summary>Thin shell over the host's identification orchestration: the
/// instructions come from the DeckIdentification state machine (via host
/// events), never from window-local logic, so the wizard cannot drift from
/// the protocol the instruments proved.</summary>
public partial class WizardWindow : Window
{
    private enum ActionButtonMode
    {
        None,
        RestartPc,
        LaunchInstaller
    }

    private readonly AppHost _host;
    private bool _completed;
    private bool _identificationStarted;
    private ActionButtonMode _actionMode = ActionButtonMode.None;

    // Timeout tanpa-progress (§45): dulu wizard bisa menggantung di
    // LANGKAH 1 selamanya bila driver mati / tidak ada tombol ditekan.
    // Tiap progress me-reset timer; 120 detik tanpa progress apa pun
    // menghentikan sesi dan menampilkan alasan - Batal tetap jalan
    // (Closed memulihkan service lama).
    private readonly System.Windows.Threading.DispatcherTimer _noProgressTimer = new()
    {
        Interval = TimeSpan.FromSeconds(120),
    };

    // Idle guidance timer: jika dalam 10 detik belum ada tombol yang terdeteksi,
    // berikan petunjuk tambahan agar user tahu status pendengaran input.
    private readonly System.Windows.Threading.DispatcherTimer _idleTipTimer = new()
    {
        Interval = TimeSpan.FromSeconds(10),
    };

    public WizardWindow(AppHost host)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        _host = host;
        _host.IdentificationProgress += OnProgress;
        _host.IdentificationCompleted += OnCompleted;
        _host.IdentificationFailed += OnFailed;

        _noProgressTimer.Tick += (_, _) =>
        {
            _noProgressTimer.Stop();
            _idleTipTimer.Stop();
            _host.CancelIdentification();
            _identificationStarted = false;

            var driverState = DriverStatus.ProbeKernelState();
            if (driverState == DriverState.PendingRestart)
            {
                ApplyDriverPendingRestartState();
            }
            else if (driverState == DriverState.NotDetected)
            {
                ApplyDriverNotDetectedState();
            }
            else
            {
                StepText.Text = Localizer.Get("S1907");
                StepText.Foreground = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!;
                MessageText.Text = Localizer.Get("S1005");
                HintText.Text = Localizer.Get("S1908");
            }
        };

        _idleTipTimer.Tick += (_, _) =>
        {
            _idleTipTimer.Stop();
            // Berikan petunjuk lembut hanya jika masih di langkah awal
            HintText.Text = Localizer.Get("S1909");
        };

        Loaded += (_, _) =>
        {
            CheckDriverAndInitialize();
        };

        Closed += (_, _) =>
        {
            _noProgressTimer.Stop();
            _idleTipTimer.Stop();
            _host.IdentificationProgress -= OnProgress;
            _host.IdentificationCompleted -= OnCompleted;
            _host.IdentificationFailed -= OnFailed;
            if (!_completed && _identificationStarted)
            {
                _host.CancelIdentification();
                _identificationStarted = false;
            }
        };
    }

    private void CheckDriverAndInitialize()
    {
        var driverState = DriverStatus.ProbeKernelState();
        if (driverState == DriverState.PendingRestart)
        {
            ApplyDriverPendingRestartState();
            return;
        }

        if (driverState == DriverState.NotDetected)
        {
            ApplyDriverNotDetectedState();
            return;
        }

        // Driver is Working
        _identificationStarted = true;
        _host.BeginIdentification();
        _noProgressTimer.Start();
        _idleTipTimer.Start();
    }

    private void ApplyDriverPendingRestartState()
    {
        StepText.Text = Localizer.Get("S1910");
        StepText.Foreground = (Brush)new BrushConverter().ConvertFromString("#F59E0B")!; // Amber

        MessageText.Text = Localizer.Get("S1911");

        HintText.Text = Localizer.Get("S1912");

        _actionMode = ActionButtonMode.RestartPc;
        ActionButton.Content = Localizer.Get("S1913");
        ActionButton.Visibility = Visibility.Visible;
    }

    private static bool TryFindDriverInstaller(out string exePath, out string arguments)
    {
        exePath = "";
        arguments = "";
        string baseDir = AppContext.BaseDirectory;
        string setupExe = Path.Combine(baseDir, "KeystreamSetup.exe");
        if (File.Exists(setupExe)) { exePath = setupExe; return true; }
        string uninstallExe = Path.Combine(baseDir, "Uninstall.exe");
        if (File.Exists(uninstallExe)) { exePath = uninstallExe; arguments = "--install-driver"; return true; }
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string legacySetup = Path.Combine(localAppData, "Keystream", "KeystreamSetup.exe");
        if (File.Exists(legacySetup)) { exePath = legacySetup; return true; }
        string legacyUninstall = Path.Combine(localAppData, "Keystream", "Uninstall.exe");
        if (File.Exists(legacyUninstall)) { exePath = legacyUninstall; arguments = "--install-driver"; return true; }
        return false;
    }

    private void ApplyDriverNotDetectedState()
    {
        StepText.Text = Localizer.Get("S1914");
        StepText.Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!; // Red

        MessageText.Text = Localizer.Get("S1915");

        HintText.Text = Localizer.Get("S1916");

        if (TryFindDriverInstaller(out _, out _))
        {
            _actionMode = ActionButtonMode.LaunchInstaller;
            ActionButton.Content = Localizer.Get("S1917");
            ActionButton.Visibility = Visibility.Visible;
        }
        else
        {
            _actionMode = ActionButtonMode.None;
            ActionButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnProgress(DeckIdentification machine)
    {
        Dispatcher.Invoke(() =>
        {
            _noProgressTimer.Stop();
            _noProgressTimer.Start();
            _idleTipTimer.Stop();

            StepText.Foreground = (Brush)new BrushConverter().ConvertFromString("#38BDF8")!; // Cyan
            StepText.Text = machine.Stage switch
            {
                IdentificationStage.AwaitingDeck => Localizer.Get("S0303"),
                IdentificationStage.AwaitingMain => Localizer.Get("S1006"),
                IdentificationStage.AwaitingDeckConfirm => Localizer.Get("S1007"),
                _ => Localizer.Get("S1008"),
            };

            // Reset hint ke teks panduan standar bila input berhasil diterima
            HintText.Text = Localizer.Get("S0304");

            // §161 (b) - tersangka-kembar: setelah ambang penolakan
            // S1061 berturut-turut, S1061 saja menyesatkan (user memang
            // menekan keyboard berbeda, tetapi ID-nya identik), jadi
            // S1775 ditambahkan di MessageText. Stage/penghitung tidak
            // diubah di sini; tidak ada auto-complete/auto-select.
            // MessageText tetap kontrol existing - tidak ada perubahan XAML.
            string message = machine.LastMessage ?? "";
            if (machine.Stage == IdentificationStage.AwaitingMain
                && machine.ConsecutiveSameDeviceRejections >= DeckIdentification.TwinSuspectThreshold)
                message += " " + Localizer.Get("S1775");
            MessageText.Text = message;
        });
    }

    private void OnFailed(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            _noProgressTimer.Stop();
            _idleTipTimer.Stop();
            _identificationStarted = false;

            var driverState = DriverStatus.ProbeKernelState();
            if (driverState == DriverState.PendingRestart)
            {
                ApplyDriverPendingRestartState();
                return;
            }

            if (driverState == DriverState.NotDetected)
            {
                ApplyDriverNotDetectedState();
                return;
            }

            StepText.Text = Localizer.Get("S1918");
            StepText.Foreground = (Brush)new BrushConverter().ConvertFromString("#EF4444")!;
            MessageText.Text = reason + " " + Localizer.Get("S1009");
            HintText.Text = Localizer.Get("S1919");
            _actionMode = ActionButtonMode.None;
            ActionButton.Visibility = Visibility.Collapsed;
        });
    }

    private void OnCompleted(DeviceId deck)
    {
        _completed = true;
        _noProgressTimer.Stop();
        _idleTipTimer.Stop();
        Dispatcher.Invoke(() =>
        {
            StepText.Text = Localizer.Get("S1008");
            StepText.Foreground = (Brush)new BrushConverter().ConvertFromString("#10B981")!; // Emerald green
            HintText.Text = Localizer.Get("S0304");

            string friendlyName = _host.Config.DeckDevice.FriendlyName;
            MessageText.Text = string.IsNullOrWhiteSpace(friendlyName)
                ? Localizer.Format("S1010", deck.Value)
                : Localizer.Format("S1776", friendlyName, deck.Value);

            _actionMode = ActionButtonMode.None;
            ActionButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = Localizer.Get("S0335");
        });
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMode == ActionButtonMode.RestartPc)
        {
            string title = Localizer.Get("S1920");
            string msg = Localizer.Get("S1921");

            var choice = AppDialog.Show(this, msg, title, AppDialogButtons.YesNo, AppDialogIcon.Warning);
            if (choice == AppDialogResult.Yes)
            {
                try
                {
                    Process.Start("shutdown", "/r /t 0");
                }
                catch (Exception ex)
                {
                    AppDialog.Show(this, ex.Message, "Error", AppDialogButtons.Ok, AppDialogIcon.Error);
                }
                Close();
            }
        }
        else if (_actionMode == ActionButtonMode.LaunchInstaller)
        {
            if (TryFindDriverInstaller(out string launcherExe, out string launcherArgs))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(launcherExe, launcherArgs) { UseShellExecute = true });
                    Close();
                }
                catch (Exception ex)
                {
                    AppDialog.Show(this, ex.Message, "Error", AppDialogButtons.Ok, AppDialogIcon.Error);
                }
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
