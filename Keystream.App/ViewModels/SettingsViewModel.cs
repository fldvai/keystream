using System.IO;
using Keystream.App.Services;
using Keystream.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.Actions.Hotkey;
using Keystream.App.Host;
using Keystream.Core.Config;
// Alias: the VM's generated property is also named LogLevel, so the
// Core policy type needs a distinct name in this file (§137).
using LogLevelPolicy = Keystream.Core.Config.LogLevel;
using Keystream.Core.Icons;
using Keystream.Core.Input;
using Keystream.Windows.Interception;

namespace Keystream.App.ViewModels;

/// <summary>Everything technical lives here (UI v2): deck identity &
/// service, OBS connection, panic hotkey, startup options, profile
/// export/import. The main window shows none of this - only status pills.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;

    public event Action? RequestOpenWizard;
    public event Action? ProfilesChanged;
    public event Action? RequestClose;
    public event Action? Saved;
    public event Action<string>? RequestShowMessage;

    /// <summary>Set by the window to AppDialog; null = refuse.</summary>
    public Func<string, bool>? ConfirmPrompt { get; set; }

    /// <summary>§171 Task 6: three-choice prompt for an imported
    /// Global section (Replace/Merge/Skip). Set by the window to the
    /// shared GlobalImport.Ask dialog; null = Skip.</summary>
    public Func<int, GlobalImportChoice>? RequestGlobalImportChoice { get; set; }

    /// <summary>Melepas langganan HealthChanged (§45, minor): tanpa ini
    /// VM Settings yang sudah ditutup tetap menerima RefreshStatus
    /// tiap 500ms selama aplikasi berjalan.</summary>
    public void Dispose()
    {
        // Timer kedaluwarsa yang tertunda menjadi basi: callback-nya
        // tidak lagi menghapus apa pun sesudah jendela ditutup (§128).
        _messageExpirer.InvalidateAll();
        _host.HealthChanged -= RefreshStatus;
    }

    public SettingsViewModel(AppHost host)
    {
        _host = host;
        _host.HealthChanged += RefreshStatus;
        ObsHost = host.Config.Settings.ObsHost;
        ObsPassword = host.Config.Settings.ObsPassword;
        StreamerBotHost = host.Config.Settings.StreamerBotHost;
        StreamerBotPortText = host.Config.Settings.StreamerBotPort.ToString();
        StreamerBotEndpoint = host.Config.Settings.StreamerBotEndpoint;
        StreamerBotPassword = host.Config.Settings.StreamerBotPassword;
        StartWithWindows = host.Config.Settings.StartWithWindows;
        StartMinimized = host.Config.Settings.StartMinimized;
        AlwaysOnTop = host.Config.Settings.AlwaysOnTop;
        PlaySoundOnPress = host.Config.Settings.PlaySoundOnPress;
        PauseHotkeyText = host.Config.Settings.PauseHotkey;
        ToggleGlobalHotkeyText = host.Config.Settings.ToggleGlobalHotkey ?? "";
        NextProfileHotkeyText = host.Config.Settings.NextProfileHotkey ?? "";
        PrevProfileHotkeyText = host.Config.Settings.PrevProfileHotkey ?? "";
        ToggleRecordingHotkeyText = host.Config.Settings.ToggleRecordingHotkey ?? "";
        TogglePlaybackHotkeyText = host.Config.Settings.TogglePlaybackHotkey ?? "";
        PauseResumePlaybackHotkeyText = host.Config.Settings.PauseResumePlaybackHotkey ?? "";
        // §137: normalized at load so a hand-edited/invalid persisted
        // value shows Information in the dropdown instead of a blank
        // selection; the normalized compare in
        // HasUnsavedSaveAllChanges keeps that from looking dirty.
        LogLevel = LogLevelPolicy.NormalizeLevel(host.Config.Settings.LogLevel);
        // §141: normalized with the 12-code rule
        // (Localizer.NormalizeLanguage: unknown -> "en") so a
        // hand-edited/unknown persisted value shows English in the
        // dropdown instead of a blank selection; the normalized
        // compare in HasUnsavedSaveAllChanges keeps that from
        // looking dirty.
        Language = Localizer.NormalizeLanguage(host.Config.Settings.Language);
        IconMaxKbText = host.Config.Settings.IconUploadMaxKb.ToString();
        IconMaxKbText = host.Config.Settings.IconUploadMaxKb.ToString();
        AutoFetchIcons = host.Config.Settings.AutoFetchIcons;
        AutoCheckUpdates = host.Config.Settings.AutoCheckUpdates;
        RefreshIconStats();
        RefreshUpdateInfo();
        RefreshStatus();
        RefreshDeckText();
        // Auto-profile trigger apps moved to the profile edit modal (v2.9):
        // they belong to the profile, not to global settings.
    }

    [ObservableProperty] private string _driverStatusText = "";

    [ObservableProperty] private string _serviceStatusText = "";
    [ObservableProperty] private System.Windows.Media.Brush _serviceStatusBrush = System.Windows.Media.Brushes.Gray;

    /// <summary>§173: tombol service mengikuti status hidup - true
    /// iff service Running (label "Stop", merah); false = label
    /// "Mulai", hijau. Diperbarui RefreshStatus yang sama dengan
    /// ServiceStatusText (langganan HealthChanged + sesudah
    /// ToggleService), tanpa polling.</summary>
    [ObservableProperty] private bool _isServiceRunning;
    /// <summary>§173: tombol service nonaktif selama Starting
    /// (transisi) - label/warna tidak diubah, label stabil terakhir
    /// selalu "Mulai" karena start hanya terjadi dari berhenti.</summary>
    [ObservableProperty] private bool _isServiceButtonEnabled = true;
    /// <summary>§173b: tombol Hubungkan/Putuskan OBS + Streamer.bot
    /// mengikuti status koneksi hidup - pola yang sama dengan
    /// IsServiceRunning (§173): diisi RefreshStatus (langganan
    /// HealthChanged + sesudah toggle), tanpa polling. Tidak ada
    /// status transisi "menyambung" untuk keduanya, jadi tombol
    /// selalu aktif dan memegang label stabil terakhir.</summary>
    [ObservableProperty] private bool _isObsConnected;
    [ObservableProperty] private bool _isStreamerBotConnected;

    private static readonly System.Windows.Media.Brush GreenBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly System.Windows.Media.Brush RedBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));
    private static readonly System.Windows.Media.Brush AmberBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC6, 0x5C));
    [ObservableProperty] private string _deckText = "";
    [ObservableProperty] private string _obsStatusText = "";
    [ObservableProperty] private string _streamerBotStatusText = "";
    // §128: satu pesan per seksi Settings - dulu satu properti pesan
    // bersama ditulis 13+ situs lintas fitur tetapi hanya satu TextBlock
    // mengikatnya (di seksi Keyboard Deck), sehingga semua umpan balik
    // tampil di bawah indikator driver. Tiap properti mengikuti pola
    // lama: OnXChanged menyalakan flag Has-nya DAN mempersenjatai
    // kedaluwarsa 3,5 detik via MessageExpirer (WPF-free, teruji).
    [ObservableProperty] private string _deckMessageText = "";
    [ObservableProperty] private bool _hasDeckMessage;
    [ObservableProperty] private string _profileMessageText = "";
    [ObservableProperty] private bool _hasProfileMessage;
    [ObservableProperty] private string _iconMessageText = "";
    [ObservableProperty] private bool _hasIconMessage;
    [ObservableProperty] private string _obsMessageText = "";
    [ObservableProperty] private bool _hasObsMessage;
    // §147: pesan seksi Streamer.bot (pola §128) + alasan kegagalan
    // yang MENETAP (pola §139: tanpa expirer, seperti OBS).
    [ObservableProperty] private string _streamerBotMessageText = "";
    [ObservableProperty] private bool _hasStreamerBotMessage;
    [ObservableProperty] private string _streamerBotFailureReasonText = "";
    [ObservableProperty] private bool _hasStreamerBotFailureReason;
    // §139: alasan kegagalan OBS yang MENETAP - tidak seperti
    // ObsMessageText, properti ini TIDAK di-arm oleh MessageExpirer
    // (OnObsFailureReasonTextChanged di bawah hanya menyalakan flag
    // Has-nya). Diisi dari LastError di akhir ToggleObsConnection yang
    // gagal; dikosongkan saat berhasil, di awal percobaan baru, dan
    // saat putus manual.
    [ObservableProperty] private string _obsFailureReasonText = "";
    [ObservableProperty] private bool _hasObsFailureReason;
    // §139: pesan seksi Log Level (paling bawah, sesudah Browser) -
    // pola §128 yang sama: OnLogMessageTextChanged meng-arm expirer.
    [ObservableProperty] private string _logMessageText = "";
    [ObservableProperty] private bool _hasLogMessage;
    // §171: pesan seksi Pembaruan (paling bawah) - pola §128 yang
    // sama: OnUpdateMessageTextChanged meng-arm expirer.
    [ObservableProperty] private string _updateMessageText = "";
    [ObservableProperty] private bool _hasUpdateMessage;
    [ObservableProperty] private string _saveMessageText = "";
    [ObservableProperty] private bool _hasSaveMessage;
    [ObservableProperty] private string _masterHotkeyFailuresText = "";
    [ObservableProperty] private bool _hasMasterHotkeyFailures;

    // Slot kedaluwarsa, satu per properti pesan (§128): token generasi
    // MessageExpirer keyed oleh string ini, jadi timer satu seksi tidak
    // pernah menghapus pesan seksi lain.
    private const string DeckMessageSlot = "deck";
    private const string ProfileMessageSlot = "profile";
    private const string IconMessageSlot = "icon";
    private const string ObsMessageSlot = "obs";
    private const string StreamerBotMessageSlot = "streamerbot";
    private const string LogMessageSlot = "log";
    private const string UpdateMessageSlot = "update";
    private const string SaveMessageSlot = "save";

    // Satu-satunya bagian WPF dari mekanisme kedaluwarsa: DispatcherTimer
    // sekali-tembak di UI thread. Semua Arm() dipanggil dari set
    // properti yang berjalan di UI thread (command/binding), jadi timer
    // selalu terikat ke dispatcher aplikasi. Callback yang fire sesudah
    // jendela ditutup hanya men-set properti VM menjadi "" (tanpa
    // referensi jendela/kontrol), dan InvalidateAll() dari Dispose()
    // membuatnya basi - tidak dapat melempar.
    private readonly MessageExpirer _messageExpirer = new(ScheduleExpiryOnUiThread);

    private static void ScheduleExpiryOnUiThread(TimeSpan delay, Action callback)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); callback(); };
        timer.Start();
    }

    partial void OnDeckMessageTextChanged(string value)
    {
        HasDeckMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(DeckMessageSlot, value, () => { DeckMessageText = ""; });
    }

    partial void OnProfileMessageTextChanged(string value)
    {
        HasProfileMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(ProfileMessageSlot, value, () => { ProfileMessageText = ""; });
    }

    partial void OnIconMessageTextChanged(string value)
    {
        HasIconMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(IconMessageSlot, value, () => { IconMessageText = ""; });
    }

    partial void OnObsMessageTextChanged(string value)
    {
        HasObsMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(ObsMessageSlot, value, () => { ObsMessageText = ""; });
    }

    partial void OnStreamerBotMessageTextChanged(string value)
    {
        HasStreamerBotMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(StreamerBotMessageSlot, value, () => { StreamerBotMessageText = ""; });
    }

    // §147: hanya flag Has-nya - SENGAJA tanpa penjadwalan
    // kedaluwarsa, seperti alasan OBS (§139).
    partial void OnStreamerBotFailureReasonTextChanged(string value)
    {
        HasStreamerBotFailureReason = !string.IsNullOrWhiteSpace(value);
    }

    // §139: hanya flag Has-nya - SENGAJA tanpa penjadwalan kedaluwarsa:
    // alasan harus menetap sampai percobaan berikutnya / putus manual.
    partial void OnObsFailureReasonTextChanged(string value)
    {
        HasObsFailureReason = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnLogMessageTextChanged(string value)
    {
        HasLogMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(LogMessageSlot, value, () => { LogMessageText = ""; });
    }

    partial void OnUpdateMessageTextChanged(string value)
    {
        HasUpdateMessage = !string.IsNullOrWhiteSpace(value);
        _messageExpirer.Arm(UpdateMessageSlot, value, () => { UpdateMessageText = ""; });
    }

    partial void OnSaveMessageTextChanged(string value)
    {
        HasSaveMessage = !string.IsNullOrWhiteSpace(value);
        // §151 P1a: satu penugasan menetap (SetPersistentSaveMessage)
        // melewati Arm - flag dikonsumsi di sini, lalu dibersihkan
        // juga oleh pemanggil bila setter ternyata no-op (nilai
        // sama), sehingga pesan biasa berikutnya tetap kedaluwarsa.
        if (_saveMessagePersistent) { _saveMessagePersistent = false; return; }
        _messageExpirer.Arm(SaveMessageSlot, value, () => { SaveMessageText = ""; });
    }

    /// <summary>§151 P1a - true only while the one persistent
    /// assignment from <see cref="SetPersistentSaveMessage"/> is
    /// being applied; consumed by
    /// <see cref="OnSaveMessageTextChanged"/>.</summary>
    private bool _saveMessagePersistent;

    /// <summary>§151 P1a - writes a save-section message that does
    /// NOT expire after 3.5 s (§128): the pending expiry of any
    /// previous message in this slot is invalidated first, and
    /// this one assignment skips the Arm. Used ONLY for the failed
    /// data-reset text (S1689 + verbatim failureDetail), which the
    /// user must be able to read and copy at leisure - the old
    /// expiring footer is why the §150 failure stayed undiagnosed.
    /// The NEXT ordinary message in this slot re-arms normally
    /// and replaces this one, so the persistent text never sticks
    /// past the next save feedback.</summary>
    public void SetPersistentSaveMessage(string message)
    {
        _messageExpirer.Invalidate(SaveMessageSlot);
        _saveMessagePersistent = true;
        SaveMessageText = message;
        _saveMessagePersistent = false;
    }

    /// <summary>Silent-save audit: kegagalan _host.Save() tidak boleh
    /// diam. Pesan ditulis ke properti pesan seksi pemanggil (target)
    /// sehingga TextBlock seksi itu yang menyala (§128), DAN
    /// RequestShowMessage menjadi AppDialog di SettingsWindow - dialog
    /// menjamin pesan terlihat walau user sedang scroll ke bawah.
    /// S0937 sudah ada ("Gagal menyimpan:"), dipakai seperti §45/§46.</summary>
    private void ShowSaveError(Exception ex, Action<string> setMessage)
    {
        string msg = Localizer.Get("S0937") + " " + ex.Message;
        setMessage(msg);
        RequestShowMessage?.Invoke(msg);
    }
    [ObservableProperty] private string _obsHost = "";
    [ObservableProperty] private string _obsPassword = "";
    [ObservableProperty] private string _streamerBotHost = "";
    [ObservableProperty] private string _streamerBotPortText = "";
    [ObservableProperty] private string _streamerBotEndpoint = "";
    [ObservableProperty] private string _streamerBotPassword = "";
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _playSoundOnPress;
    [ObservableProperty] private string _pauseHotkeyText = "F9";
    [ObservableProperty] private string _toggleGlobalHotkeyText = "";
    [ObservableProperty] private string _nextProfileHotkeyText = "";
    [ObservableProperty] private string _prevProfileHotkeyText = "";
    [ObservableProperty] private string _toggleRecordingHotkeyText = "";
    [ObservableProperty] private string _togglePlaybackHotkeyText = "";
    [ObservableProperty] private string _pauseResumePlaybackHotkeyText = "";

    /// <summary>§137: canonical level name ("Debug"/"Information"/
    /// "Warning"/"Error"), bound to the Log Level dropdown's
    /// SelectedValue (ComboBoxItem Tag). The dropdown only ever
    /// offers the four canonical values, so no validation error path
    /// is needed in SaveAll - NormalizeLevel is the validation.</summary>
    [ObservableProperty] private string _logLevel = LogLevelPolicy.Default;

    /// <summary>§141: staged language code, bound to the Language
    /// dropdown's SelectedValue in the "Umum dan Profil" section.
    /// Staged like LogLevel: choosing a value changes nothing until
    /// SaveAll - no partial change handler exists in this VM on
    /// purpose, so selection never live-applies. The dropdown only
    /// ever offers the 12 registry codes (LanguageOptions), so no
    /// validation error path is needed in SaveAll -
    /// Localizer.NormalizeLanguage is the validation.</summary>
    [ObservableProperty] private string _language = "en";

    /// <summary>§141: the Language dropdown's items, projected from
    /// the Localizer registry (the single source of truth) in
    /// canonical order - no code list or endonym is duplicated here.
    /// Endonyms are invariant proper nouns: they are displayed as
    /// stored and never re-localized when the selection changes.</summary>
    public System.Collections.Generic.IReadOnlyList<LanguageOption> LanguageOptions { get; } = CreateLanguageOptions();

    /// <summary>One Language dropdown item: a registry code plus its
    /// endonym. A ValueTuple cannot be bound (WPF binds properties,
    /// not fields), hence this record.</summary>
    public sealed record LanguageOption(string Code, string Endonym);

    private static System.Collections.Generic.IReadOnlyList<LanguageOption> CreateLanguageOptions()
    {
        var options = new LanguageOption[Localizer.Languages.Count];
        for (var i = 0; i < options.Length; i++)
            options[i] = new LanguageOption(Localizer.Languages[i].Code, Localizer.Languages[i].Endonym);
        return options;
    }

    private void RefreshStatus()
    {
        var h = _host.Health;
        ServiceStatusText = h.State switch
        {
            InterceptionState.Running => h.DeckPresent ? Localizer.Get("S0839") : Localizer.Get("S0840"),
            InterceptionState.Faulted => string.IsNullOrWhiteSpace(_host.Health.LastError)
                ? Localizer.Get("S0841")
                : Localizer.Get("S0842") + _host.Health.LastError,
            InterceptionState.Starting => Localizer.Get("S0843"),
            _ => Localizer.Get("S0414"),
        };
        ServiceStatusBrush = h.State switch
        {
            InterceptionState.Running => h.DeckPresent ? GreenBrush : AmberBrush,
            InterceptionState.Starting => AmberBrush,
            _ => RedBrush,
        };
        // §173: tombol service mengikuti status. Starting = transisi:
        // tombol dinonaktifkan, label/warna tidak diubah (label stabil
        // terakhir selalu "Mulai", karena start hanya dari berhenti).
        if (h.State != InterceptionState.Starting)
        {
            IsServiceRunning = h.State == InterceptionState.Running;
            IsServiceButtonEnabled = true;
        }
        else
        {
            IsServiceButtonEnabled = false;
        }
        DriverStatusText = DriverStatus.Check(h.State == InterceptionState.Running).Text;
        // Hotkey master gagal (§45): dulu RegisterHotKey false ditelan
        // diam-diam - Panic "tidak bekerja" tanpa alasan terlihat.
        MasterHotkeyFailuresText = _host.MasterHotkeyFailures.Count == 0
            ? ""
            : Localizer.Get("S0844") + string.Join(", ", _host.MasterHotkeyFailures);
        HasMasterHotkeyFailures = _host.MasterHotkeyFailures.Count > 0;
        // §139: status biner saja (S0829/S0826). Alasan kegagalan tidak
        // lagi menempel di sini - ia tinggal di ObsFailureReasonText
        // (baris sendiri di bawah Status), agar tidak hilang bersama
        // pesan transient dan tidak menduplikasi baris alasan.
        ObsStatusText = _host.Obs.IsConnected
            ? Localizer.Get("S0829")
            : Localizer.Get("S0826");
        // §173b: tombol Hubungkan/Putuskan mengikuti status hidup.
        IsObsConnected = _host.Obs.IsConnected;
        // §147: status biner yang sama (S0829/S0826); alasan
        // kegagalan tinggal di StreamerBotFailureReasonText.
        StreamerBotStatusText = _host.StreamerBot.IsConnected
            ? Localizer.Get("S0829")
            : Localizer.Get("S0826");
        // §173b: tombol Hubungkan/Putuskan mengikuti status hidup.
        IsStreamerBotConnected = _host.StreamerBot.IsConnected;
    }

    private void RefreshDeckText()
    {
        var d = _host.Config.DeckDevice;
        // The section shows ONLY the keyboard's name (the user's ask);
        // the raw device identity stays one tap away behind the "!"
        // button as DeckDetailText.
        DeckText = d.Value.Length == 0
            ? Localizer.Get("S0845")
            : (string.IsNullOrWhiteSpace(d.FriendlyName) ? Localizer.Get("S0846") : d.FriendlyName);
        DeckDetailText = d.Value.Length == 0
            ? ""
            : Localizer.Format("S0847", DeckText, d.Value, d.Kind) + (d.RequiresReidentification ? Localizer.Get("S0848") : "");
    }

    [ObservableProperty] private string _deckDetailText = "";
    [ObservableProperty] private bool _showDeckDetail;

    [RelayCommand] private void ToggleDeckDetail() => ShowDeckDetail = !ShowDeckDetail;

    [RelayCommand]
    private void ToggleService()
    {
        if (_host.Health.State == InterceptionState.Running)
        {
            _host.StopService();
        }
        else
        {
            var deck = _host.Config.DeckDevice.ToDeviceId();
            if (!_host.TryStartService(deck) && _host.Health.LastError is not null)
                DeckMessageText = _host.Health.LastError;
        }
        RefreshStatus();
    }

    [RelayCommand]
    private void IdentifyDeck() => RequestOpenWizard?.Invoke();

    public void OnIdentificationCompleted()
    {
        RefreshDeckText();
        RefreshStatus();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _host.Config.Settings.StartWithWindows = value;
        try { _host.Save(); }
        catch (Exception ex) { ShowSaveError(ex, v => ProfileMessageText = v); }
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        _host.Config.Settings.StartMinimized = value;
        try { _host.Save(); }
        catch (Exception ex) { ShowSaveError(ex, v => ProfileMessageText = v); }
    }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _host.Config.Settings.AlwaysOnTop = value;
        try { _host.Save(); }
        catch (Exception ex) { ShowSaveError(ex, v => ProfileMessageText = v); }

        if (System.Windows.Application.Current.MainWindow is Views.MainWindow mainWin
            && mainWin.DataContext is MainViewModel mainVm)
        {
            mainVm.IsAlwaysOnTop = value;
        }
    }

    partial void OnPlaySoundOnPressChanged(bool value)
    {
        _host.Config.Settings.PlaySoundOnPress = value;
        try { _host.Save(); }
        catch (Exception ex) { ShowSaveError(ex, v => ProfileMessageText = v); }
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string configDir = Path.Combine(folder, "Keystream");
        try
        {
            Directory.CreateDirectory(configDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = configDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ProfileMessageText = Localizer.Format("S1161", configDir, ex.Message);
        }
    }

    /// <summary>§89: tarik ulang toggle immediate-save dari config
    /// live - tray bisa mengubahnya selagi jendela ini terbuka, dan
    /// snapshot ctor tidak akan tahu. Dipanggil saat jendela aktif
    /// (SettingsWindow.Activated). Penugasan yang nilainya sama
    /// tidak memicu Changed handler (no-op); yang berubah ditulis
    /// balik ke nilai live yang sama + Save (idempotent).</summary>
    public void RefreshLiveToggles()
    {
        StartMinimized = _host.Config.Settings.StartMinimized;
        StartWithWindows = _host.Config.Settings.StartWithWindows;
        AutoFetchIcons = _host.Config.Settings.AutoFetchIcons;
        AlwaysOnTop = _host.Config.Settings.AlwaysOnTop;
        PlaySoundOnPress = _host.Config.Settings.PlaySoundOnPress;
    }

    [ObservableProperty] private string _iconStatsText = "";
    [ObservableProperty] private string _iconMaxKbText = "";
    [ObservableProperty] private bool _autoFetchIcons;

    partial void OnAutoFetchIconsChanged(bool value)
    {
        _host.Config.Settings.AutoFetchIcons = value;
        try { _host.Save(); }
        catch (Exception ex) { ShowSaveError(ex, v => IconMessageText = v); }
    }
    [ObservableProperty] private bool _autoCheckUpdates;

    // §171: pola toggle simpan-langsung - simpan langsung saat
    // checkbox diubah, bukan menunggu Simpan (SaveAll tidak
    // mencakup toggle ini).
    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        _host.Config.Settings.AutoCheckUpdates = value;
        try { _host.Save(); }
        catch (Exception ex) { ShowSaveError(ex, v => UpdateMessageText = v); }
    }

    // §171: baris detail seksi Pembaruan - dirakit di VM agar label
    // terlokalisasi (bukan StringFormat di XAML). §173: hanya dua
    // baris (terpasang + terbaru); baris terbit/catatan/terakhir
    // diperiksa + HasReleasePageUrl dibuang bersama tombolnya.
    [ObservableProperty] private string _updateInstalledText = "";
    [ObservableProperty] private string _updateLatestText = "";

    /// <summary>§171: susun ulang baris detail dari settings yang
    /// tersimpan (dipanggil saat modal dibuka dan sesudah tiap
    /// pemeriksaan berhasil). Tanpa penomoran versi formal, baris
    /// "terbaru" hanya menampilkan tag apa adanya.</summary>
    public void RefreshUpdateInfo()
    {
        var settings = _host.Config.Settings;
        UpdateInstalledText = Localizer.Format("S1814", MainViewModel.GetAppVersionString());
        UpdateLatestText = Localizer.Format("S1815",
            string.IsNullOrWhiteSpace(settings.LastSeenReleaseTag)
                ? Localizer.Get("S1819") : settings.LastSeenReleaseTag);
    }

    /// <summary>§171: satu pemeriksaan lewat jalur bersama
    /// (UpdateChecker). Hasil yang sukses dicatat + disimpan di sini;
    /// dialog ditampilkan pemanggil (SettingsWindow) agar owner-nya
    /// benar. Kegagalan simpan dilaporkan lewat pesan seksi, bukan
    /// diam; kegagalan pemeriksaan dikembalikan sebagai
    /// FailureReason untuk dialog pemanggil.</summary>
    public async Task<UpdateCheckResult> CheckUpdatesAsync()
    {
        var result = await UpdateChecker.CheckLatestAsync();
        if (result.Release is not null)
        {
            UpdateChecker.RecordResult(_host.Config.Settings, result.Release, DateTimeOffset.UtcNow);
            try { _host.Save(); }
            catch (Exception ex) { ShowSaveError(ex, v => UpdateMessageText = v); }
            RefreshUpdateInfo();
        }
        return result;
    }
    [ObservableProperty] private bool _cleanOrphans = true;
    [ObservableProperty] private bool _cleanAllIcons;

    /// <summary>One-line stats for the Icons folder (v3.4.0).</summary>
    private void RefreshIconStats()
    {
        var (count, bytes) = _host.IconStore.Stats();
        IconStatsText = Localizer.Format("S0849", _host.IconStore.Folder, count, IconReference.FormatSize(bytes));
    }

    [RelayCommand]
    private void CleanIcons()
    {
        if (!CleanOrphans && !CleanAllIcons)
        {
            IconMessageText = Localizer.Get("S0858");
            return;
        }
        if (CleanAllIcons && ConfirmPrompt?.Invoke(
            Localizer.Get("S0859")) != true)
            return;
        // §171: indeks cache favicon tinggal di folder Icons, jadi
        // kedua jalur hapus di bawah ikut menghapusnya (DeleteAll /
        // DeleteOrphans menghapus semua file tanpa filter ekstensi).
        // Catat keberadaannya SEBELUM dihapus agar pesannya jujur.
        bool hadFaviconCache = _host.IconStore.HasFaviconCacheIndex();
        int deleted;
        long freed;
        int failed;
        if (CleanAllIcons)
            deleted = _host.IconStore.DeleteAll(out freed, out failed);
        else
            deleted = _host.IconStore.DeleteOrphans(_host.Config, out freed, out failed);
        _host.NotifyIconsChanged();
        RefreshIconStats();
        IconMessageText = deleted == 0 && failed == 0
            ? Localizer.Get("S0860")
            : Localizer.Format("S0861", deleted, IconReference.FormatSize(freed))
              + (failed > 0 ? Localizer.Format("S0862", failed) : "");
        if (hadFaviconCache)
            IconMessageText += " " + Localizer.Get("S1823");
    }
    /// <summary>§139: buka folder log di Explorer. Folder dibuat dulu
    /// bila belum ada. Bila crash.log sudah ada, Explorer dibuka
    /// dengan /select agar file-nya langsung terpilih (argumen
    /// di-quote karena path %AppData% dapat mengandung spasi); bila
    /// file belum ada, /select TIDAK dipakai - foldernya saja yang
    /// dibuka. Kegagalan apa pun (mis. folder tidak dapat dibuat)
    /// dilaporkan jujur lewat LogMessageText (reuse S1161: nama
    /// folder + alasan), tidak pernah crash dan tidak pernah diam.
    /// Tidak menulis apa pun ke log.</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        string logPath = CrashLog.LogPath;
        string folder = Path.GetDirectoryName(logPath) ?? "";
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(logPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + logPath + "\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            LogMessageText = Localizer.Format("S1161",
                string.IsNullOrWhiteSpace(folder) ? logPath : folder, ex.Message);
        }
    }

    /// <summary>§139: bersihkan crash.log. Konfirmasi DULU lewat
    /// ConfirmPrompt bersama (null = tolak, seperti semua pemanggil
    /// lain); hanya jawaban Ya yang memanggil CrashLog.Clear().
    /// Hasilnya dilaporkan jujur (S1733 berhasil / S1734 gagal) -
    /// tidak ada baris "log dibersihkan" yang ditulis ke log sesudah
    /// clear. VM tidak memanggil AppDialog langsung.</summary>
    [RelayCommand]
    private void ClearLog()
    {
        if (ConfirmPrompt?.Invoke(Localizer.Get("S1732")) != true)
            return;
        LogMessageText = CrashLog.Clear()
            ? Localizer.Get("S1733")
            : Localizer.Get("S1734");
    }

    [RelayCommand]
    private async Task ToggleObsConnection()
    {
        if (_host.Obs.IsConnected)
        {
            _host.Obs.Disconnect();
            // §139: putus manual bukan kegagalan - alasan lama tidak
            // boleh tertinggal, karena RefreshStatus (biner) tidak
            // menyentuhnya dan DisconnectInternal tidak menghapus
            // LastError (ObsClient.cs:354-362).
            ObsFailureReasonText = "";
            RefreshStatus();
            return;
        }
        _host.Config.Settings.ObsHost = ObsHost;
        _host.Config.Settings.ObsPassword = ObsPassword;
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Save gagal = koneksi tidak boleh dilanjutkan; tombol yang
            // sama bisa dipakai ulang untuk retry (nilai masih di VM).
            ShowSaveError(ex, v => ObsMessageText = v);
            return;
        }
        // §139: bersihkan alasan percobaan lama SEBELUM ConnectAsync,
        // agar alasan basi tidak menyamar sebagai hasil percobaan ini.
        // (Sesudah Save berhasil: Save gagal = percobaan tidak jalan,
        // alasan lama tetap menjadi jejak terakhir yang valid.)
        ObsFailureReasonText = "";
        bool ok = await _host.Obs.ConnectAsync(ObsHost, ObsPassword);
        if (ok)
        {
            // §139: berhasil = alasan ditegaskan kosong (clear di atas
            // sudah melakukannya; ditegaskan di sini agar situs sukses
            // membaca mandiri) + hasil ditulis ke crash.log.
            // WriteInfo: Information+Debug (§137). Hanya host; tidak
            // ada password di baris ini.
            ObsFailureReasonText = "";
            ObsMessageText = Localizer.Get("S0863");
            CrashLog.WriteInfo("OBS terhubung: " + ObsHost);
        }
        else
        {
            // §139: alasan menetap dari LastError (fallback S0864 bila
            // null/kosong - fallback yang sama dengan baris pesan).
            // ObsMessageText memakai nilai yang sama; perilaku §128-nya
            // tidak berubah (tetap di-arm MessageExpirer).
            string reason = string.IsNullOrWhiteSpace(_host.Obs.LastError)
                ? Localizer.Get("S0864")
                : _host.Obs.LastError;
            ObsFailureReasonText = reason;
            ObsMessageText = reason;
            // §139: WriteWarning: Warning/Information/Debug (§137).
            // Hanya host + alasan (alasan sudah terlokalisasi di
            // ObsClient); TIDAK ada nilai password di baris ini.
            CrashLog.WriteWarning("OBS gagal terhubung: " + ObsHost + " — " + reason);
        }
        RefreshStatus();
    }

    /// <summary>§147: cermin <see cref="ToggleObsConnection"/> untuk
    /// Streamer.bot. Putus manual menghapus alasan lama; sambung
    /// menyimpan keempat field dulu (gagal simpan = tidak lanjut),
    /// membersihkan alasan lama sebelum mencoba, lalu melaporkan
    /// hasil jujur: sukses → S1751 + WriteInfo (hanya host:port),
    /// gagal → alasan menetap dari LastError (fallback S1752) +
    /// WriteWarning (hanya host:port + alasan). Password TIDAK
    /// PERNAH masuk baris log, pesan, atau status apa pun. Port
    /// yang tidak terparse/diluar 1–65535 ditolak jujur lewat
    /// template generik S1034 (pola §137) sebelum mencoba sambung.
    /// ConnectAsync yang melempar diperlakukan sebagai gagal
    /// sambung, bukan crash.</summary>
    [RelayCommand]
    private async Task ToggleStreamerBotConnection()
    {
        if (_host.StreamerBot.IsConnected)
        {
            _host.StreamerBot.Disconnect();
            StreamerBotFailureReasonText = "";
            RefreshStatus();
            return;
        }
        if (!int.TryParse(StreamerBotPortText, out int port) || port < 1 || port > 65535)
        {
            StreamerBotMessageText = Localizer.Format("S1034", Localizer.Get("S1745"), StreamerBotPortText);
            return;
        }
        _host.Config.Settings.StreamerBotHost = StreamerBotHost;
        _host.Config.Settings.StreamerBotPort = port;
        _host.Config.Settings.StreamerBotEndpoint = StreamerBotEndpoint;
        _host.Config.Settings.StreamerBotPassword = StreamerBotPassword;
        try { _host.Save(); }
        catch (Exception ex)
        {
            ShowSaveError(ex, v => StreamerBotMessageText = v);
            return;
        }
        StreamerBotFailureReasonText = "";
        bool ok;
        try
        {
            ok = await _host.StreamerBot.ConnectAsync(
                StreamerBotHost, port, StreamerBotEndpoint, StreamerBotPassword,
                CancellationToken.None);
        }
        catch (Exception)
        {
            ok = false;
        }
        if (ok && _host.StreamerBot.IsConnected)
        {
            StreamerBotFailureReasonText = "";
            StreamerBotMessageText = Localizer.Get("S1751");
            CrashLog.WriteInfo("Streamer.bot terhubung: " + StreamerBotHost + ":" + port);
        }
        else
        {
            string reason = string.IsNullOrWhiteSpace(_host.StreamerBot.LastError)
                ? Localizer.Get("S1752")
                : _host.StreamerBot.LastError;
            StreamerBotFailureReasonText = reason;
            StreamerBotMessageText = reason;
            CrashLog.WriteWarning("Streamer.bot gagal terhubung: " + StreamerBotHost + ":" + port + " — " + reason);
        }
        RefreshStatus();
    }

    /// <summary>Audit Cancel (§104 lanjutan): field-field SaveAll yang
    /// diubah tapi belum disimpan. Dibandingkan dengan config live
    /// (bukan snapshot ctor): ToggleObsConnection yang menyimpan
    /// ObsHost/ObsPassword, dan SaveAll yang berhasil, membuat VM
    /// sama dengan config sehingga tutup berikutnya benar-diam.
    /// Toggle immediate-save (StartWithWindows/StartMinimized/
    /// AutoFetchIcons) sengaja TIDAK dinilai - sudah tersimpan.
    /// Hotkey: spasi-kosong dinormalisasi ke "" seperti SaveAll.
    /// IconMaxKb: kosong/spasi = tidak diubah oleh SaveAll (bersih);
    /// angka yang sama nilainya bersih walau penulisannya beda;
    /// teks tak-terparse = kotor (SaveAll akan menolaknya, tapi
    /// ketikan user tetap layak dikonfirmasi).</summary>
    public bool HasUnsavedSaveAllChanges()
    {
        var s = _host.Config.Settings;
        if (!string.Equals(ObsHost, s.ObsHost ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(ObsPassword, s.ObsPassword ?? "", StringComparison.Ordinal)) return true;
        // §147: keempat field Streamer.bot. Port mengikuti pola
        // IconMaxKbText: kosong/spasi = bersih (SaveAll tidak
        // mengubahnya); teks tak-terparse atau nilainya beda = kotor.
        if (!string.Equals(StreamerBotHost, s.StreamerBotHost ?? "", StringComparison.Ordinal)) return true;
        if (!string.IsNullOrWhiteSpace(StreamerBotPortText)
            && (!int.TryParse(StreamerBotPortText, out int sbPort) || sbPort != s.StreamerBotPort)) return true;
        if (!string.Equals(StreamerBotEndpoint, s.StreamerBotEndpoint ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(StreamerBotPassword, s.StreamerBotPassword ?? "", StringComparison.Ordinal)) return true;
        if (!string.IsNullOrWhiteSpace(IconMaxKbText)
            && (!int.TryParse(IconMaxKbText, out int maxKb) || maxKb != s.IconUploadMaxKb)) return true;
        if (!string.Equals(NormalizeHotkey(PauseHotkeyText), s.PauseHotkey ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(NormalizeHotkey(ToggleGlobalHotkeyText), s.ToggleGlobalHotkey ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(NormalizeHotkey(NextProfileHotkeyText), s.NextProfileHotkey ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(NormalizeHotkey(PrevProfileHotkeyText), s.PrevProfileHotkey ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(NormalizeHotkey(ToggleRecordingHotkeyText), s.ToggleRecordingHotkey ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(NormalizeHotkey(TogglePlaybackHotkeyText), s.TogglePlaybackHotkey ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(NormalizeHotkey(PauseResumePlaybackHotkeyText), s.PauseResumePlaybackHotkey ?? "", StringComparison.Ordinal)) return true;
        // §137: normalized on both sides - a persisted invalid value
        // (shown as Information) is clean until the user picks
        // another level; LogLevel is a SaveAll field, not an
        // immediate-save toggle.
        if (!string.Equals(LogLevelPolicy.NormalizeLevel(LogLevel), LogLevelPolicy.NormalizeLevel(s.LogLevel), StringComparison.Ordinal)) return true;
        // §141: normalized on both sides with the 12-code rule -
        // a persisted unknown value (shown as English) is clean
        // until the user picks another language; Language is a
        // SaveAll field, not an immediate-save toggle.
        if (!string.Equals(Localizer.NormalizeLanguage(Language), Localizer.NormalizeLanguage(s.Language), StringComparison.Ordinal)) return true;
        return false;
    }

    private static string NormalizeHotkey(string text)
        => string.IsNullOrWhiteSpace(text) ? "" : text;

    [RelayCommand]
    private void SaveAll()
    {
        // Batch 1 fix: invalid input used to be silently ignored (old
        // value kept, window closed, no message anywhere). Validate
        // first; on any error show the errors and stay open unsaved.
        var errors = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(IconMaxKbText)
            && (!int.TryParse(IconMaxKbText, out int maxKbCheck) || maxKbCheck <= 0))
            errors.Add(Localizer.Get("S0865"));
        // §147: port Streamer.bot - validasi jujur seperti IconMaxKb;
        // pesan memakai template generik S1034 (pola §137) + nama
        // slot S1745 + nilai mentahnya.
        if (!string.IsNullOrWhiteSpace(StreamerBotPortText)
            && (!int.TryParse(StreamerBotPortText, out int sbPortCheck) || sbPortCheck < 1 || sbPortCheck > 65535))
            errors.Add(Localizer.Format("S1034", Localizer.Get("S1745"), StreamerBotPortText));
        if (!string.IsNullOrWhiteSpace(PauseHotkeyText)
            && !(KeyChord.TryParse(PauseHotkeyText, out var pauseChord, out _) && pauseChord is not null))
            errors.Add(Localizer.Get("S0866"));
        if (!string.IsNullOrWhiteSpace(ToggleGlobalHotkeyText)
            && !(KeyChord.TryParse(ToggleGlobalHotkeyText, out var globalChord, out _) && globalChord is not null))
            errors.Add(Localizer.Get("S0867"));
        if (!string.IsNullOrWhiteSpace(NextProfileHotkeyText)
            && !(KeyChord.TryParse(NextProfileHotkeyText, out var nextChord, out _) && nextChord is not null))
            errors.Add(Localizer.Get("S0868"));
        if (!string.IsNullOrWhiteSpace(PrevProfileHotkeyText)
            && !(KeyChord.TryParse(PrevProfileHotkeyText, out var prevChord, out _) && prevChord is not null))
            errors.Add(Localizer.Get("S0869"));
        // §137: tidak ada string error per-slot untuk 3 slot baru
        // (S0866–S0869 hanya untuk 4 slot lama; kunci baru dilarang) -
        // pesan memakai template generik S1034 yang sudah ada
        // ("{0}: format tidak valid ({1}).") + nama slot S1719–S1721
        // + chord mentahnya, jadi slot dan nilai yang salah tetap
        // bernama tanpa kunci baru. Pertanyaan terbuka untuk
        // koordinator: apakah 3 kunci khusus lebih disukai.
        if (!string.IsNullOrWhiteSpace(ToggleRecordingHotkeyText)
            && !(KeyChord.TryParse(ToggleRecordingHotkeyText, out var recChord, out _) && recChord is not null))
            errors.Add(Localizer.Format("S1034", Localizer.Get("S1719"), ToggleRecordingHotkeyText));
        if (!string.IsNullOrWhiteSpace(TogglePlaybackHotkeyText)
            && !(KeyChord.TryParse(TogglePlaybackHotkeyText, out var playChord, out _) && playChord is not null))
            errors.Add(Localizer.Format("S1034", Localizer.Get("S1720"), TogglePlaybackHotkeyText));
        if (!string.IsNullOrWhiteSpace(PauseResumePlaybackHotkeyText)
            && !(KeyChord.TryParse(PauseResumePlaybackHotkeyText, out var pauseResumeChord, out _) && pauseResumeChord is not null))
            errors.Add(Localizer.Format("S1034", Localizer.Get("S1721"), PauseResumePlaybackHotkeyText));
        if (errors.Count > 0)
        {
            string joined = string.Join("\n", errors);
            SaveMessageText = joined;
            RequestShowMessage?.Invoke(joined);
            return;
        }
        _host.Config.Settings.ObsHost = ObsHost;
        _host.Config.Settings.ObsPassword = ObsPassword;
        // §147: keempat field Streamer.bot; port hanya ditulis bila
        // terparse dalam 1–65535 (kosong = nilai lama dipertahankan,
        // seperti IconMaxKbText; tak-valid sudah ditolak di atas).
        _host.Config.Settings.StreamerBotHost = StreamerBotHost;
        _host.Config.Settings.StreamerBotEndpoint = StreamerBotEndpoint;
        _host.Config.Settings.StreamerBotPassword = StreamerBotPassword;
        if (int.TryParse(StreamerBotPortText, out int sbPort) && sbPort >= 1 && sbPort <= 65535)
            _host.Config.Settings.StreamerBotPort = sbPort;
        if (int.TryParse(IconMaxKbText, out int maxKb) && maxKb > 0)
            _host.Config.Settings.IconUploadMaxKb = maxKb;
        if (string.IsNullOrWhiteSpace(PauseHotkeyText))
        {
            _host.Config.Settings.PauseHotkey = "";
        }
        else if (KeyChord.TryParse(PauseHotkeyText, out var chord, out _) && chord is not null)
        {
            _host.Config.Settings.PauseHotkey = PauseHotkeyText;
        }

        if (string.IsNullOrWhiteSpace(ToggleGlobalHotkeyText))
        {
            _host.Config.Settings.ToggleGlobalHotkey = "";
        }
        else if (KeyChord.TryParse(ToggleGlobalHotkeyText, out var chordGlobal, out _) && chordGlobal is not null)
        {
            _host.Config.Settings.ToggleGlobalHotkey = ToggleGlobalHotkeyText;
        }

        if (string.IsNullOrWhiteSpace(NextProfileHotkeyText))
        {
            _host.Config.Settings.NextProfileHotkey = "";
        }
        else if (KeyChord.TryParse(NextProfileHotkeyText, out var chordNext, out _) && chordNext is not null)
        {
            _host.Config.Settings.NextProfileHotkey = NextProfileHotkeyText;
        }

        if (string.IsNullOrWhiteSpace(PrevProfileHotkeyText))
        {
            _host.Config.Settings.PrevProfileHotkey = "";
        }
        else if (KeyChord.TryParse(PrevProfileHotkeyText, out var chordPrev, out _) && chordPrev is not null)
        {
            _host.Config.Settings.PrevProfileHotkey = PrevProfileHotkeyText;
        }

        if (string.IsNullOrWhiteSpace(ToggleRecordingHotkeyText))
        {
            _host.Config.Settings.ToggleRecordingHotkey = "";
        }
        else if (KeyChord.TryParse(ToggleRecordingHotkeyText, out var chordRec, out _) && chordRec is not null)
        {
            _host.Config.Settings.ToggleRecordingHotkey = ToggleRecordingHotkeyText;
        }

        if (string.IsNullOrWhiteSpace(TogglePlaybackHotkeyText))
        {
            _host.Config.Settings.TogglePlaybackHotkey = "";
        }
        else if (KeyChord.TryParse(TogglePlaybackHotkeyText, out var chordPlay, out _) && chordPlay is not null)
        {
            _host.Config.Settings.TogglePlaybackHotkey = TogglePlaybackHotkeyText;
        }

        if (string.IsNullOrWhiteSpace(PauseResumePlaybackHotkeyText))
        {
            _host.Config.Settings.PauseResumePlaybackHotkey = "";
        }
        else if (KeyChord.TryParse(PauseResumePlaybackHotkeyText, out var chordPauseResume, out _) && chordPauseResume is not null)
        {
            _host.Config.Settings.PauseResumePlaybackHotkey = PauseResumePlaybackHotkeyText;
        }
        // §137: write-back (normalized - the dropdown only offers
        // canonical values). The level applies immediately via
        // AppHost.Save() below; no restart.
        _host.Config.Settings.LogLevel = LogLevelPolicy.NormalizeLevel(LogLevel);
        // §141: write-back (normalized with the 12-code rule -
        // the dropdown only offers registry codes). Written BEFORE
        // the single _host.Save() below; the language itself is
        // applied only at the apply point after Save succeeds.
        _host.Config.Settings.Language = Localizer.NormalizeLanguage(Language);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Silent-save audit: tidak ada snapshot field di VM ini
            // (ctor hanya init satu arah), jadi tidak ada rollback -
            // sesuai arahan, sistem snapshot baru TIDAK dibuat. Modal
            // tetap terbuka, Saved/RequestClose TIDAK di-invoke, dan
            // semua field masih di VM sehingga klik Simpan Semua
            // berikutnya adalah retry langsung.
            ShowSaveError(ex, v => SaveMessageText = v);
            return;
        }
        // §137: Debug seam owned by C - logged only after a
        // successful Save (the apply point), visible at Debug level.
        CrashLog.WriteDebug("Tingkat log diterapkan: " + _host.Config.Settings.LogLevel + " (Simpan Semua).");
        // §141: the apply point - the SAME Localizer.SetLanguage the
        // title-bar language dropdown calls, so LanguageChanged
        // re-localizes all LocSource-bound UI and refreshes the
        // header with no second sync mechanism. Only after
        // _host.Save() succeeds: a failed Save returns above, so
        // the UI language never switches to a value that was not
        // persisted.
        Localizer.SetLanguage(_host.Config.Settings.Language);
        Saved?.Invoke();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void ExportProfile()
    {
        var profile = _host.Config.ActiveProfile;
        if (profile is null) { ProfileMessageText = Localizer.Get("S0870"); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localizer.Get("S0374"),
            FileName = profile.Name + ProfilePackage.FileExtension,
            Filter = Localizer.Get("S0375"),
            DefaultExt = ProfilePackage.FileExtension,
        };
        if (dialog.ShowDialog() != true) return;
        try { ProfilePackage.SaveToFile(dialog.FileName, ProfilePackage.Serialize(profile, globalPage: _host.Config.GlobalPage)); }
        catch (Exception ex)
        {
            // §105 (D6 pekerja 4): dulu hanya IOException/
            // UnauthorizedAccessException - JsonException dari Serialize
            // lolos ke crash dialog tanpa pesan profil. Catch generik aman
            // di sini: Serialize dievaluasi sebelum SaveToFile dipanggil,
            // jadi tidak ada state yang perlu di-rollback; pesan S0376
            // yang sama tetap dipakai.
            ProfileMessageText = Localizer.Get("S0376") + ex.Message;
            return;
        }
        ProfileMessageText = Localizer.Format("S0377", profile.Name);
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localizer.Get("S0277"),
            Filter = Localizer.Get("S0378"),
        };
        if (dialog.ShowDialog() != true) return;
        ProfileConfig? added = null;
        List<BindingConfig>? prevGlobalBindings = null;
        try
        {
            var known = new HashSet<string>(_host.ActionsById.Keys, StringComparer.OrdinalIgnoreCase);
            var report = ProfilePackage.Parse(File.ReadAllText(dialog.FileName), known);
            // §171 Task 6: prompt SESUDAH validasi, SEBELUM apa pun
            // diterapkan - hanya bila paket membawa Global non-kosong.
            var globalChoice = report.Global is not null
                ? RequestGlobalImportChoice?.Invoke(report.GlobalImported) ?? GlobalImportChoice.Skip
                : GlobalImportChoice.Skip;
            GlobalImportOutcome? globalOutcome = null;
            if (report.Global is not null)
            {
                prevGlobalBindings = _host.Config.GlobalPage.Bindings.ToList();
                globalOutcome = GlobalImport.Apply(_host.Config.GlobalPage, report.Global, globalChoice);
            }
            var profile = report.Profile;
            profile.Id = Guid.NewGuid().ToString("N");
            if (_host.Config.Profiles.Any(p => p.Name == profile.Name))
                profile.Name += Localizer.Get("S0379");
            _host.Config.Profiles.Add(profile);
            added = profile;
            _host.Save();
            // §171a: arahkan ActiveProfileId ke profil yang baru diimport
            // SEBELUM ProfilesChanged - agar RefreshProfilesAndPages di
            // main window mendarat di profil baru, bukan ActiveProfile
            // lama. OnSelectedProfileChanged (§164) di sana keluar dari
            // edit Global, RefreshPages + Save (menulis ActiveProfileId).
            // Ditaruh sesudah Save() yang sukses: import yang gagal tetap
            // me-rollback tanpa mengubah profil aktif.
            _host.Config.ActiveProfileId = profile.Id;
            string globalText = report.Global is not null && globalOutcome is not null
                ? " " + GlobalImport.OutcomeText(globalChoice, globalOutcome, report.GlobalDropped) : "";
            ProfileMessageText = Localizer.Format("S0382", profile.Name, report.BindingsImported) +
                          (report.BindingsDropped > 0 ? Localizer.Format("S0383", report.BindingsDropped) : "") + ")." + globalText +
                          (report.Warnings.Count > 0 ? " " + string.Join(" ", report.Warnings) : "");
            // §171a-koreksi: seperti jalur dashboard — refresh di main
            // window TIDAK BOLEH me-rollback import yang sudah tersimpan.
            // Dijaga sendiri; bila gagal, S1833 menempel di pesan
            // (profil tetap tersimpan, muncul sesudah restart).
            try { ProfilesChanged?.Invoke(); }
            catch (Exception ex) { ProfileMessageText += " " + Localizer.Format("S1833", ex.Message); }
        }
        catch (InvalidDataException ex)
        {
            ProfileMessageText = Localizer.Get("S0385") + ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (added is not null) _host.Config.Profiles.Remove(added);
            if (prevGlobalBindings is not null) _host.Config.GlobalPage.Bindings = prevGlobalBindings;
            ProfileMessageText = Localizer.Get("S0386") + ex.Message;
        }
        catch (Exception ex)
        {
            // §105 (D6 pekerja 4): catch generik ketiga - aman karena
            // ProfilePackage.Parse sudah membungkus JsonException menjadi
            // InvalidDataException, jadi urutan catch mempertahankan
            // pesan S0385 untuk file rusak; yang ditangkap di sini hanya
            // kegagalan non-IO (mis. Save() non-IO) - profil yang telanjur
            // ditambahkan di-rollback seperti catch IO di atas.
            if (added is not null) _host.Config.Profiles.Remove(added);
            if (prevGlobalBindings is not null) _host.Config.GlobalPage.Bindings = prevGlobalBindings;
            ProfileMessageText = Localizer.Get("S0386") + ex.Message;
        }
    }
}
