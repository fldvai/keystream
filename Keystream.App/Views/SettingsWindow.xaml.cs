using System;
using System.ComponentModel;
using Keystream.Core.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Keystream.Actions.Hotkey;
using Keystream.App.Host;
using Keystream.App.Services;
using Keystream.App.ViewModels;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Border = System.Windows.Controls.Border;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Keystream.App.Views;

public partial class SettingsWindow : Window
{
    private enum HotkeySlot { Panic, Global, Next, Prev, ToggleRecording, TogglePlayback, PauseResumePlayback }

    private readonly AppHost _host;
    private readonly SettingsViewModel _viewModel;
    private HotkeySlot? _recordingSlot;
    private string? _recordPreviousText;

    /// <summary>§137 A6: nilai <see cref="AppHost.SuppressEngineEvents"/>
    /// sebelum rekaman chord jendela ini dimulai. Rekaman chord
    /// menaikkan flag host itu (proksi capture bersama, disiplin
    /// simpan/pulihkan seperti ArmCaptureSuppression di MainViewModel)
    /// agar WndProc MainWindow menelan WM_HOTKEY selama capture -
    /// chord yang sedang direkam tidak boleh memicu aksinya. Hanya
    /// StartRecording yang menulis flag ini, hanya StopRecording yang
    /// memulihkannya, dan hanya saat rekaman memang sedang berjalan.</summary>
    private bool _suppressBeforeHotkeyCapture;

    /// <summary>§130: true while a confirmed data reset is running.
    /// Window_Closing skips the discard confirm then - the reset
    /// itself was confirmed with DELETE and the config is being
    /// deleted anyway, so prompting "discard changes?" during
    /// shutdown would only block the restart.</summary>
    private bool _resetInProgress;

    public SettingsWindow(AppHost host, SettingsViewModel viewModel)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.ApplyDarkTitleBar(this);
        DataContext = viewModel;
        _host = host;
        _viewModel = viewModel;
        viewModel.ConfirmPrompt = message =>
            AppDialog.Show(this, message, "Keystream",
                AppDialogButtons.YesNo, AppDialogIcon.Warning)
            == AppDialogResult.Yes;
        // §171 Task 6: prompt impor Global (Ganti/Gabung/Lewati) -
        // hook yang sama polanya dengan ConfirmPrompt di atas.
        viewModel.RequestGlobalImportChoice = count => GlobalImport.Ask(this, count);
        viewModel.RequestOpenWizard += () =>
        {
            var wizard = new WizardWindow(host) { Owner = this };
            wizard.ShowDialog();
            viewModel.OnIdentificationCompleted();
        };
        viewModel.RequestClose += () => Close();
        Closed += (_, _) => viewModel.Dispose();
        viewModel.RequestShowMessage += msg =>
            AppDialog.Show(this, msg, "Keystream",
                AppDialogButtons.Ok, AppDialogIcon.Warning);

        Deactivated += (_, _) => StopRecording();
        // §89: selaraskan toggle dengan tray - tray dapat mengubah
        // StartMinimized/StartWithWindows/AutoFetchIcons selagi
        // jendela ini terbuka; snapshot ctor akan basi tanpanya.
        Activated += (_, _) => _viewModel.RefreshLiveToggles();
        // Klik di dalam area hotkey (tombol/chip slot mana pun) tidak
        // boleh mematikan rekaman lewat handler ini - kontrol itu sendiri
        // yang menangani kliknya (tombol Merekam = batal, tombol Rekam /
        // chip slot lain = pindah rekam). Klik di luar area hotkey = batal.
        MouseDown += (_, e) =>
        {
            if (_recordingSlot is not null && !IsInsideHotkeyGrid(e.OriginalSource))
                StopRecording();
        };
        RefreshHotkeySlots();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ShellChrome.HeaderMouseDown(sender, e, this, false);

    /// <summary>§130 - Reset Data: penjaga dulu, modal DELETE, lalu
    /// restart lewat App. Penjaga memakai yang SUDAH ADA: sesi
    /// perekaman/playback panel (FloatPanel.SessionActive, penjaga
    /// §120/§121 yang sama dengan BulkDeleteBlocked), supresi eksekusi
    /// deck (SuppressEngineEvents - naik setiap kali capture pemetaan
    /// armed, di MainViewModel.ArmCaptureSuppression maupun saat
    /// editor listening; Settings tidak memegang MainViewModel, jadi
    /// flag host ini adalah proksi capture-armed yang terbukti dari
    /// kode), dan rekaman hotkey jendela ini sendiri (_recordingSlot).
    /// Penolakan ditulis ke SaveMessageText (pesan seksi Save, kini di
    /// footer) dan modal tidak dibuka. Gagal hapus / gagal restart =
    /// pesan S1689/S1690 di tempat yang sama, jendela tetap terbuka.
    /// Perubahan SaveAll yang belum disimpan TIDAK ditanyakan di
    /// sini: konfirmasi DELETE sudah mencakupnya, dan reset menghapus
    /// config itu apa pun jawabannya.</summary>
    private void ResetData_Click(object sender, RoutedEventArgs e)
    {
        if (_host.FloatPanel.SessionActive || _host.SuppressEngineEvents || _recordingSlot is not null)
        {
            _viewModel.SaveMessageText = Localizer.Get("S1688");
            return;
        }
        if (System.Windows.Application.Current is not App app
            || string.IsNullOrEmpty(Environment.ProcessPath)
            || !System.IO.File.Exists(Environment.ProcessPath))
        {
            _viewModel.SaveMessageText = Localizer.Get("S1690");
            return;
        }
        var confirm = new ResetConfirmWindow { Owner = this };
        if (confirm.ShowDialog() != true || !confirm.Confirmed) return;

        _resetInProgress = true;
        bool restarted = false;
        try { restarted = app.ResetDataAndRestart(); }
        finally { if (!restarted) _resetInProgress = false; }
        if (!restarted)
        {
            // §151 P1a: tampilkan failureDetail VERBATIM di samping
            // S1689, dan buat TETAP terlihat: SaveMessageText
            // kedaluwarsa 3,5 detik (§128, OnSaveMessageTextChanged
            // meng-Arm MessageExpirer), jadi teks kegagalan ini
            // ditulis lewat SetPersistentSaveMessage (satu penugasan
            // tanpa Arm; pesan biasa berikutnya meng-Arm normal dan
            // menggantikannya). Tanpa string baru: S1689 yang sudah
            // ada + baris baru + detail apa adanya (ResetFailureMessage).
            string? detail = app.LastResetFailureDetail;
            if (string.IsNullOrWhiteSpace(detail))
                _viewModel.SaveMessageText = Localizer.Get("S1689");
            else
                _viewModel.SetPersistentSaveMessage(
                    ResetFailureMessage.Compose(Localizer.Get("S1689"), detail));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>§171: pemeriksaan pembaruan manual lewat jalur
    /// bersama (UpdateChecker) - dialog yang sama persis dengan
    /// §166, owner jendela ini. Tombol dinonaktifkan selama
    /// pemeriksaan agar tidak ada dua pemeriksaan bersamaan.
    /// B-03: ditawarkan hanya bila tag berubah (bukan versi).
    /// B-02: DownloadAndInstall hanya sesudah konfirmasi eksplisit
    /// (jawaban Ya pada dialog di bawah) - sudah begitu sebelum
    /// perbaikan ini, dipertahankan apa adanya.</summary>
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckUpdateButton.IsEnabled) return;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            // B-03: tangkap tag terakhir SEBELUM CheckUpdatesAsync
            // (RecordResult di dalamnya menimpa LastSeenReleaseTag
            // dengan tag remote) agar gate tag dapat dibandingkan.
            string lastSeenTag = _host.Config.Settings.LastSeenReleaseTag;
            var result = await _viewModel.CheckUpdatesAsync();
            if (result.FailureReason is not null)
            {
                AppDialog.Show(this, Localizer.Format("S1781", result.FailureReason),
                    Localizer.Get("S1554"), AppDialogButtons.Ok, AppDialogIcon.Error);
                return;
            }
            var release = result.Release!;
            if (!UpdateChecker.IsNewReleaseTag(release.Tag, lastSeenTag))
            {
                AppDialog.Show(this, Localizer.Format("S1780", MainViewModel.GetAppVersionString()),
                    Localizer.Get("S1554"), AppDialogButtons.Ok, AppDialogIcon.Info);
                return;
            }
            var answer = AppDialog.ShowTwoChoices(this, UpdateChecker.BuildDialogText(release),
                Localizer.Get("S1554"), Localizer.Get("S1842"), Localizer.Get("S1843"), AppDialogIcon.Question);
            if (answer == AppDialogResult.Yes)
                UpdateChecker.DownloadAndInstall(this, release);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>Audit Cancel: jendela ini menutup lewat × / tombol
    /// Batal di footer / Alt+F4 / RequestClose (SaveAll), semuanya
    /// lewat Closing ini - Batal memanggil Close() yang sama dengan
    /// ×, bukan jalur tutup baru. Tanpa handler, field SaveAll yang
    /// diubah lalu ditutup tanpa Simpan dibuang diam-diam. Kini: bila
    /// ada perubahan SaveAll yang belum disimpan, konfirmasi S0906
    /// (pola MacroManager §97, judul "Keystream" seperti dialog lain
    /// di jendela ini); menjawab Tidak = tetap terbuka. Tanpa
    /// perubahan = tutup diam, benar. StopRecording dulu (pola
    /// MacroManager) dan ClearFocus dulu agar TextBox LostFocus
    /// (ObsHost/IconMaxKbText) sempat menulis ke VM sebelum dirty
    /// check - tanpa ini ketikan terakhir yang belum pindah fokus
    /// tidak terdeteksi. Reset Data yang sudah dikonfirmasi (§130)
    /// melewati konfirmasi buang ini (lihat _resetInProgress).</summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        StopRecording();
        Keyboard.ClearFocus();
        if (!_resetInProgress && _viewModel.HasUnsavedSaveAllChanges() && !ConfirmDiscard())
            e.Cancel = true;
    }

    private bool ConfirmDiscard() => AppDialog.Show(this,
        Localizer.Get("S0906"), "Keystream",
        AppDialogButtons.YesNo, AppDialogIcon.Question) == AppDialogResult.Yes;

    /// <summary>Klik nama keyboard deck: buka modal pilih keyboard.
    /// Modal tidak mengubah apa pun sampai Hubungkan ditekan; bila user
    /// memilih jalur wizard, wizard dibuka lewat command yang sama dengan
    /// tombol "Pilih / Identifikasi Keyboard" agar refresh pasca-wizard
    /// (OnIdentificationCompleted) tetap jalan.</summary>
    private void DeckName_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var picker = new DeckDevicePickerWindow(_host) { Owner = this };
        picker.ShowDialog();
        if (picker.Applied)
            _viewModel.OnIdentificationCompleted();
        else if (picker.OpenWizardRequested)
            _viewModel.IdentifyDeckCommand.Execute(null);
    }

    private string SlotChord(HotkeySlot slot) => slot switch
    {
        HotkeySlot.Panic => _viewModel.PauseHotkeyText,
        HotkeySlot.Global => _viewModel.ToggleGlobalHotkeyText,
        HotkeySlot.Next => _viewModel.NextProfileHotkeyText,
        HotkeySlot.Prev => _viewModel.PrevProfileHotkeyText,
        HotkeySlot.ToggleRecording => _viewModel.ToggleRecordingHotkeyText,
        HotkeySlot.TogglePlayback => _viewModel.TogglePlaybackHotkeyText,
        HotkeySlot.PauseResumePlayback => _viewModel.PauseResumePlaybackHotkeyText,
        _ => "",
    };

    private void SetSlotChord(HotkeySlot slot, string value)
    {
        switch (slot)
        {
            case HotkeySlot.Panic: _viewModel.PauseHotkeyText = value; break;
            case HotkeySlot.Global: _viewModel.ToggleGlobalHotkeyText = value; break;
            case HotkeySlot.Next: _viewModel.NextProfileHotkeyText = value; break;
            case HotkeySlot.Prev: _viewModel.PrevProfileHotkeyText = value; break;
            case HotkeySlot.ToggleRecording: _viewModel.ToggleRecordingHotkeyText = value; break;
            case HotkeySlot.TogglePlayback: _viewModel.TogglePlaybackHotkeyText = value; break;
            case HotkeySlot.PauseResumePlayback: _viewModel.PauseResumePlaybackHotkeyText = value; break;
        }
    }

    private static string SlotLabel(HotkeySlot slot) => slot switch
    {
        HotkeySlot.Panic => Localizer.Get("S0963"),
        HotkeySlot.Global => Localizer.Get("S0209"),
        HotkeySlot.Next => Localizer.Get("S0965"),
        HotkeySlot.Prev => Localizer.Get("S0966"),
        HotkeySlot.ToggleRecording => Localizer.Get("S1719"),
        HotkeySlot.TogglePlayback => Localizer.Get("S1720"),
        HotkeySlot.PauseResumePlayback => Localizer.Get("S1721"),
        _ => Localizer.Get("S0987"),
    };

    private (Button Rekam, Button Merekam, StackPanel Filled, StackPanel Keys) SlotViews(HotkeySlot slot) => slot switch
    {
        HotkeySlot.Panic => (RekamPanic, MerekamPanic, FilledPanic, KeysPanic),
        HotkeySlot.Global => (RekamGlobal, MerekamGlobal, FilledGlobal, KeysGlobal),
        HotkeySlot.Next => (RekamNext, MerekamNext, FilledNext, KeysNext),
        HotkeySlot.Prev => (RekamPrev, MerekamPrev, FilledPrev, KeysPrev),
        HotkeySlot.ToggleRecording => (RekamToggleRecording, MerekamToggleRecording, FilledToggleRecording, KeysToggleRecording),
        HotkeySlot.TogglePlayback => (RekamTogglePlayback, MerekamTogglePlayback, FilledTogglePlayback, KeysTogglePlayback),
        HotkeySlot.PauseResumePlayback => (RekamPauseResumePlayback, MerekamPauseResumePlayback, FilledPauseResumePlayback, KeysPauseResumePlayback),
        _ => (RekamPanic, MerekamPanic, FilledPanic, KeysPanic),
    };

    private HotkeySlot? SlotFromView(object? view) => view switch
    {
        var b when b == RekamPanic || b == MerekamPanic || b == KeysPanic || b == ClearPanic => HotkeySlot.Panic,
        var b when b == RekamGlobal || b == MerekamGlobal || b == KeysGlobal || b == ClearGlobal => HotkeySlot.Global,
        var b when b == RekamNext || b == MerekamNext || b == KeysNext || b == ClearNext => HotkeySlot.Next,
        var b when b == RekamPrev || b == MerekamPrev || b == KeysPrev || b == ClearPrev => HotkeySlot.Prev,
        var b when b == RekamToggleRecording || b == MerekamToggleRecording || b == KeysToggleRecording || b == ClearToggleRecording => HotkeySlot.ToggleRecording,
        var b when b == RekamTogglePlayback || b == MerekamTogglePlayback || b == KeysTogglePlayback || b == ClearTogglePlayback => HotkeySlot.TogglePlayback,
        var b when b == RekamPauseResumePlayback || b == MerekamPauseResumePlayback || b == KeysPauseResumePlayback || b == ClearPauseResumePlayback => HotkeySlot.PauseResumePlayback,
        _ => null,
    };

    private bool IsInsideHotkeyGrid(object? source)
    {
        var current = source as System.Windows.DependencyObject;
        while (current != null)
        {
            if (ReferenceEquals(current, HotkeyGrid) || ReferenceEquals(current, MacroHotkeyGrid)) return true;
            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : System.Windows.LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void HotkeyRekam_Click(object sender, RoutedEventArgs e)
    {
        if (SlotFromView(sender) is { } slot) StartRecording(slot);
    }

    private void HotkeyMerekam_Click(object sender, RoutedEventArgs e) => StopRecording();

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (SlotFromView(sender) is { } slot)
        {
            SetSlotChord(slot, "");
            RefreshSlot(slot);
        }
    }

    private void HotkeyKeys_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (SlotFromView(sender) is { } slot) StartRecording(slot);
    }

    private void HotkeyKeys_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SlotFromView(sender) is not { } slot) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Return or Key.Space)
        {
            StartRecording(slot);
            e.Handled = true;
            return;
        }
        if ((key is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetSlotChord(slot, "");
            RefreshSlot(slot);
            e.Handled = true;
        }
    }

    private void StartRecording(HotkeySlot slot)
    {
        if (_recordingSlot is not null) StopRecording();
        _recordPreviousText = SlotChord(slot);
        _recordingSlot = slot;
        // §137 A6: naikkan proksi capture host selama rekaman chord;
        // WndProc membaca flag ini dan menelan WM_HOTKEY, jadi chord
        // yang ditekan untuk direkam tidak memicu aksi master apa pun.
        // Disimpan SEBELUM dinaikkan agar pemilik sebelumnya (capture
        // deck / listening editor) tidak pernah dibersihkan.
        _suppressBeforeHotkeyCapture = _host.SuppressEngineEvents;
        _host.SuppressEngineEvents = true;
        RefreshSlot(slot);
        StartRecBlink();
    }

    private void StopRecording(bool restorePreviousText = true)
    {
        if (_recordingSlot is not { } slot) return;
        _recordingSlot = null;
        // §137 A6: pulihkan nilai flag sebelum rekaman chord - bukan
        // selalu false - agar supresi pemilik lain tetap hidup.
        _host.SuppressEngineEvents = _suppressBeforeHotkeyCapture;
        if (restorePreviousText && _recordPreviousText is not null)
            SetSlotChord(slot, _recordPreviousText);
        _recordPreviousText = null;
        RefreshSlot(slot);
        StopRecBlink();
    }

    private void RefreshSlot(HotkeySlot slot)
    {
        var (rekam, merekam, filled, keys) = SlotViews(slot);
        if (_recordingSlot == slot)
        {
            rekam.Visibility = Visibility.Collapsed;
            merekam.Visibility = Visibility.Visible;
            filled.Visibility = Visibility.Collapsed;
            return;
        }
        merekam.Visibility = Visibility.Collapsed;
        string chord = SlotChord(slot);
        if (string.IsNullOrWhiteSpace(chord))
        {
            rekam.Visibility = Visibility.Visible;
            filled.Visibility = Visibility.Collapsed;
            return;
        }
        rekam.Visibility = Visibility.Collapsed;
        filled.Visibility = Visibility.Visible;
        RebuildKeysPanel(keys, chord);
    }

    private void RefreshHotkeySlots()
    {
        RefreshSlot(HotkeySlot.Panic);
        RefreshSlot(HotkeySlot.Global);
        RefreshSlot(HotkeySlot.Next);
        RefreshSlot(HotkeySlot.Prev);
        RefreshSlot(HotkeySlot.ToggleRecording);
        RefreshSlot(HotkeySlot.TogglePlayback);
        RefreshSlot(HotkeySlot.PauseResumePlayback);
    }

    /// <summary>Chip terisi: satu keycap per tombol, dipisah "+".
    /// "Ctrl+Shift+G" -&gt; [CTRL] + [SHIFT] + [G]. Chord tak ter-parse
    /// (sisa input manual dulu) tampil sebagai satu keycap merah dengan
    /// teks mentahnya - jangan dihapus diam-diam, klik untuk rekam ulang.</summary>
    private void RebuildKeysPanel(StackPanel panel, string chordText)
    {
        panel.Children.Clear();
        bool valid = KeyChord.TryParse(chordText, out _, out _);
        string[] parts = valid
            ? chordText.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : new[] { chordText };
        System.Windows.Media.Brush borderBrush, textBrush;
        if (valid)
        {
            borderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x4A, 0x63));
            textBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF1, 0xF5, 0xF9));
        }
        else
        {
            borderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            textBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFC, 0xA5, 0xA5));
        }
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "+",
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B)),
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                });
            }
            var cap = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x29, 0x3B)),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(6),
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            cap.Child = new TextBlock
            {
                Text = parts[i].ToUpperInvariant(),
                Foreground = textBrush,
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 12.5,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            panel.Children.Add(cap);
        }
    }

    private void StartRecBlink()
    {
        if (FindResource("RecBlink") is System.Windows.Media.Animation.Storyboard sb)
            sb.Begin(this);
    }

    private void StopRecBlink()
    {
        if (FindResource("RecBlink") is System.Windows.Media.Animation.Storyboard sb)
            sb.Stop(this);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (_recordingSlot is { } slot)
        {
            // Standalone modifier press - don't end recording yet, let user press the combo
            if (IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            // Escape cancels recording, restoring the previous value
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                StopRecording();
                e.Handled = true;
                return;
            }

            // Backspace / Delete clears the hotkey
            if ((key is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
            {
                SetSlotChord(slot, "");
                StopRecording(restorePreviousText: false);
                e.Handled = true;
                return;
            }

            string? chord = FormatKeyChord(e);
            if (!string.IsNullOrEmpty(chord))
            {
                // Duplikat antar 7 hotkey ditolak selagi merekam - user
                // tetap dalam mode rekam dan bisa menekan kombinasi lain.
                // Enum.GetValues mencakup ketujuh slot (§137 menambah 3)
                // tanpa daftar manual; penolakan memakai S0988 yang
                // menamai slot pemilik chord, tidak pernah menimpa diam-diam.
                HotkeySlot? duplicate = null;
                foreach (var s in Enum.GetValues<HotkeySlot>())
                {
                    if (s != slot && string.Equals(SlotChord(s), chord, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = s;
                        break;
                    }
                }
                if (duplicate is { } dup)
                {
                    AppDialog.Show(this, Localizer.Format("S0988", chord, SlotLabel(dup)),
                        "Keystream", AppDialogButtons.Ok, AppDialogIcon.Warning);
                    e.Handled = true;
                    return;
                }
                SetSlotChord(slot, chord);
            }

            StopRecording(restorePreviousText: false);
            e.Handled = true;
            return;
        }

        // Keyboard-only: chip terpilih + Enter/Space = mulai merekam;
        // Backspace/Delete = kosongkan slot. Tombol Rekam/Merekam/×
        // menangani Enter/Space sendiri lewat Click - jangan di-Handled
        // di sini agar Click-nya tetap jalan.
        if (Keyboard.FocusedElement is { } focusedElement
            && SlotFromView(focusedElement) is { } focusedSlot)
        {
            if (focusedElement is StackPanel
                && key is Key.Return or Key.Space)
            {
                StartRecording(focusedSlot);
                e.Handled = true;
                return;
            }
            if ((key is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
            {
                SetSlotChord(focusedSlot, "");
                RefreshSlot(focusedSlot);
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static string? FormatKeyChord(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return null;

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");

        string? keyName = ConvertKeyToName(key);
        if (string.IsNullOrEmpty(keyName)) return null;

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static string? ConvertKeyToName(Key key)
    {
        if (key >= Key.F1 && key <= Key.F24)
            return key.ToString();

        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return Localizer.Get("S0947") + (key - Key.NumPad0);

        if (key >= Key.A && key <= Key.Z)
            return key.ToString();

        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.Prior => Localizer.Get("S0951"),
            Key.Next => Localizer.Get("S0952"),
            Key.Up => Localizer.Get("S0482"),
            Key.Down => Localizer.Get("S0483"),
            Key.Left => Localizer.Get("S0953"),
            Key.Right => Localizer.Get("S0954"),
            Key.Capital => Localizer.Get("S0955"),
            Key.Snapshot => Localizer.Get("S0990"),
            Key.Pause => "Pause",
            Key.Scroll => Localizer.Get("S0956"),
            Key.NumLock => Localizer.Get("S0957"),
            Key.Apps => "Menu",
            Key.Multiply => Localizer.Get("S0991"),
            Key.Add => Localizer.Get("S0992"),
            Key.Subtract => Localizer.Get("S0993"),
            Key.Divide => Localizer.Get("S0994"),
            Key.Decimal => Localizer.Get("S0995"),
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe or Key.OemBackslash => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => null
        };
    }
}
