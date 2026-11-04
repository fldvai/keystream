using CommunityToolkit.Mvvm.ComponentModel;
using Keystream.Core.Localization;
using CommunityToolkit.Mvvm.Input;
using Keystream.App.Host;

namespace Keystream.App.ViewModels;

public partial class ObsConfigViewModel : ObservableObject
{
    private readonly AppHost _host;

    public event Action? RequestClose;
    public event Action? Saved;

    [ObservableProperty] private string _obsHost = "";
    [ObservableProperty] private string _obsPassword = "";
    [ObservableProperty] private string _obsStatusText = Localizer.Get("S0826");
    [ObservableProperty] private string _obsStatusDot = "#8A93A6";
    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private bool _isBusy;
    // §173c: live connection state for the state-driven Hubungkan/
    // Putuskan pair (same pattern as SettingsViewModel §173b) -
    // set in RefreshStatus() below, never polled by the view.
    [ObservableProperty] private bool _isObsConnected;

    // §148: Streamer.bot tab twin - staged properties mirroring the OBS
    // tab exactly (initialised from live settings in the ctor below).
    [ObservableProperty] private string _streamerBotHost = "";
    [ObservableProperty] private string _streamerBotPortText = "";
    [ObservableProperty] private string _streamerBotEndpoint = "";
    [ObservableProperty] private string _streamerBotPassword = "";
    [ObservableProperty] private string _streamerBotStatusText = Localizer.Get("S0826");
    [ObservableProperty] private string _streamerBotStatusDot = "#8A93A6";
    [ObservableProperty] private string _streamerBotMessageText = "";
    // §173c: twin of IsObsConnected for the Streamer.bot tab pair -
    // set in RefreshStreamerBotStatus() below.
    [ObservableProperty] private bool _isStreamerBotConnected;
    // §148: persistent failure reason, mirroring the §147 Settings
    // pattern (StreamerBotFailureReasonText): no expirer, survives
    // RefreshStreamerBotStatus, cleared only on success, a new
    // attempt, or manual disconnect.
    [ObservableProperty] private string _streamerBotFailureReasonText = "";
    [ObservableProperty] private bool _hasStreamerBotFailureReason;

    partial void OnStreamerBotFailureReasonTextChanged(string value)
        => HasStreamerBotFailureReason = !string.IsNullOrWhiteSpace(value);

    public ObsConfigViewModel(AppHost host)
    {
        _host = host;
        ObsHost = string.IsNullOrWhiteSpace(host.Config.Settings.ObsHost) ? "ws://127.0.0.1:4455" : host.Config.Settings.ObsHost; // Literal default — never localized (S0827 is display text only).
        ObsPassword = host.Config.Settings.ObsPassword;
        // §148: Streamer.bot staged values straight from live settings
        // (no default substitution: DeckConfig already defaults Host to
        // "127.0.0.1", Port to 8080, Endpoint to "/").
        StreamerBotHost = host.Config.Settings.StreamerBotHost;
        StreamerBotPortText = host.Config.Settings.StreamerBotPort.ToString();
        StreamerBotEndpoint = host.Config.Settings.StreamerBotEndpoint;
        StreamerBotPassword = host.Config.Settings.StreamerBotPassword;
        RefreshStatus();
        RefreshStreamerBotStatus();
        // §173c: live connection state while the modal is open
        // (same HealthChanged pattern as SettingsViewModel) - the
        // Hubungkan/Putuskan pairs flip even if the connection
        // drops outside the modal. Dispose() unsubscribes (wired
        // to the window Closed event, SettingsWindow pattern).
        _host.HealthChanged += RefreshStatus;
        _host.HealthChanged += RefreshStreamerBotStatus;
        // Batch 1 fix: initial message only. RefreshStatus itself no
        // longer touches MessageText (below), so ToggleConnection's
        // "diputus" message survives its finally block.
        if (_host.Obs.IsConnected) MessageText = Localizer.Get("S0828");
        else if (!string.IsNullOrWhiteSpace(_host.Obs.LastError)) MessageText = _host.Obs.LastError;
        // §148 twin: connected → S1751 message; otherwise a live
        // LastError becomes the persistent failure reason.
        if (_host.StreamerBot.IsConnected) StreamerBotMessageText = Localizer.Get("S1751");
        else if (!string.IsNullOrWhiteSpace(_host.StreamerBot.LastError)) StreamerBotFailureReasonText = _host.StreamerBot.LastError;
    }

    // Batch 1 fix: status-only. It used to overwrite MessageText with a
    // stale LastError, which erased ToggleConnection's own message
    // ("Koneksi OBS diputus.") in its finally block.
    public void RefreshStatus()
    {
        bool connected = _host.Obs.IsConnected;
        IsObsConnected = connected;
        ObsStatusText = connected ? Localizer.Get("S0829") : Localizer.Get("S0826");
        ObsStatusDot = connected ? "#10B981" : "#EF4444";
    }

    // §148: RefreshStatus twin for the Streamer.bot tab. Status-only:
    // never touches StreamerBotMessageText or
    // StreamerBotFailureReasonText (Batch 1 lesson, same as OBS).
    public void RefreshStreamerBotStatus()
    {
        bool connected = _host.StreamerBot.IsConnected;
        IsStreamerBotConnected = connected;
        StreamerBotStatusText = connected ? Localizer.Get("S0829") : Localizer.Get("S0826");
        StreamerBotStatusDot = connected ? "#10B981" : "#EF4444";
    }

    /// <summary>Melepas langganan HealthChanged (§173c, pola
    /// SettingsViewModel.Dispose): tanpa ini VM modal yang sudah
    /// ditutup tetap menerima RefreshStatus tiap tick selama
    /// aplikasi berjalan. Dipanggil dari Closed jendela.</summary>
    public void Dispose()
    {
        _host.HealthChanged -= RefreshStatus;
        _host.HealthChanged -= RefreshStreamerBotStatus;
    }

    [RelayCommand]
    private async Task ToggleConnection()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (_host.Obs.IsConnected)
            {
                _host.Obs.Disconnect();
                MessageText = Localizer.Get("S0830");
            }
            else
            {
                _host.Config.Settings.ObsHost = ObsHost.Trim();
                _host.Config.Settings.ObsPassword = ObsPassword;
                try { _host.Save(); }
                catch (Exception ex)
                {
                    // Save gagal = koneksi TIDAK dilanjutkan. return di
                    // dalam try tetap menjalankan finally (IsBusy=false,
                    // RefreshStatus) dan RefreshStatus tidak menyentuh
                    // MessageText (§Batch 1), jadi pesan ini bertahan.
                    MessageText = Localizer.Get("S0937") + " " + ex.Message;
                    return;
                }

                MessageText = Localizer.Get("S0831");
                bool ok = await _host.Obs.ConnectAsync(ObsHost.Trim(), ObsPassword);
                MessageText = ok ? Localizer.Get("S0832") : (_host.Obs.LastError ?? Localizer.Get("S0833"));
            }
        }
        finally
        {
            IsBusy = false;
            RefreshStatus();
        }
    }

    /// <summary>§148: ToggleConnection twin for Streamer.bot, mirroring
    /// the §147 Settings pattern (SettingsViewModel.
    /// ToggleStreamerBotConnection): port validation via the generic
    /// S1034 template BEFORE any save or connect; save live settings
    /// + _host.Save() first (save-fail = no connect, S0937 message);
    /// connect via the SHARED _host.StreamerBot instance - no new
    /// client is constructed anywhere here; disconnect path clears
    /// the persistent failure reason; the password is never logged
    /// (log lines carry host:port + reason only, as in Settings).
    /// ConnectAsync throwing is treated as a failed connect, not a
    /// crash (Settings pattern). No "connecting…" string exists for
    /// Streamer.bot, so none is invented: success reports S1751,
    /// failure reports the persistent reason.</summary>
    [RelayCommand]
    private async Task ToggleStreamerBotConnection()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (_host.StreamerBot.IsConnected)
            {
                _host.StreamerBot.Disconnect();
                StreamerBotFailureReasonText = "";
                StreamerBotMessageText = "";
            }
            else
            {
                if (!int.TryParse(StreamerBotPortText, out int port) || port < 1 || port > 65535)
                {
                    StreamerBotMessageText = Localizer.Format("S1034", Localizer.Get("S1745"), StreamerBotPortText);
                    return;
                }

                _host.Config.Settings.StreamerBotHost = StreamerBotHost.Trim();
                _host.Config.Settings.StreamerBotPort = port;
                _host.Config.Settings.StreamerBotEndpoint = StreamerBotEndpoint;
                _host.Config.Settings.StreamerBotPassword = StreamerBotPassword;
                try { _host.Save(); }
                catch (Exception ex)
                {
                    // Save gagal = koneksi TIDAK dilanjutkan (OBS twin
                    // rule); return runs finally, and
                    // RefreshStreamerBotStatus never touches the
                    // message, so this survives.
                    StreamerBotMessageText = Localizer.Get("S0937") + " " + ex.Message;
                    return;
                }

                StreamerBotFailureReasonText = "";
                bool ok;
                try
                {
                    ok = await _host.StreamerBot.ConnectAsync(
                        StreamerBotHost.Trim(), port, StreamerBotEndpoint, StreamerBotPassword,
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
                    CrashLog.WriteInfo("Streamer.bot terhubung: " + StreamerBotHost.Trim() + ":" + port);
                }
                else
                {
                    string reason = string.IsNullOrWhiteSpace(_host.StreamerBot.LastError)
                        ? Localizer.Get("S1752")
                        : _host.StreamerBot.LastError;
                    StreamerBotFailureReasonText = reason;
                    StreamerBotMessageText = reason;
                    CrashLog.WriteWarning("Streamer.bot gagal terhubung: " + StreamerBotHost.Trim() + ":" + port + " — " + reason);
                }
            }
        }
        finally
        {
            IsBusy = false;
            RefreshStreamerBotStatus();
        }
    }

    /// <summary>Audit Cancel: host/password yang diubah tapi belum
    /// disimpan (Save) - dan belum tersimpan lewat Connect, yang
    /// menyimpan dulu sebelum menghubungkan. Dibandingkan dengan
    /// config live, jadi setelah Save/Connect yang berhasil tutup
    /// berikutnya benar-diam. Host dibandingkan ter-Trim (Save dan
    /// Connect menyimpan hasil Trim); substitusi default ctor
    /// (config kosong → literal default) bukan edit user sehingga bersih.
    /// Password dibandingkan persis (tanpa trim).
    /// §148: the four Streamer.bot fields extend the same rule -
    /// host Trim-compared (no literal-default substitution applies: the
    /// ctor stages the live value verbatim), port following the
    /// §147 Settings rule (blank = clean, Save keeps the old port;
    /// unparseable or different = dirty), endpoint/password exact.
    /// </summary>
    public bool HasUnsavedChanges()
    {
        var s = _host.Config.Settings;
        string savedHost = (s.ObsHost ?? "").Trim();
        string currentHost = ObsHost.Trim();
        bool hostDirty = !string.Equals(currentHost, savedHost, StringComparison.Ordinal)
            && !(string.IsNullOrWhiteSpace(savedHost)
                && string.Equals(currentHost, "ws://127.0.0.1:4455", StringComparison.Ordinal));
        if (hostDirty) return true;
        if (!string.Equals(ObsPassword, s.ObsPassword ?? "", StringComparison.Ordinal)) return true;
        // §148: Streamer.bot fields.
        if (!string.Equals(StreamerBotHost.Trim(), (s.StreamerBotHost ?? "").Trim(), StringComparison.Ordinal)) return true;
        if (!string.IsNullOrWhiteSpace(StreamerBotPortText)
            && (!int.TryParse(StreamerBotPortText, out int sbPort) || sbPort != s.StreamerBotPort)) return true;
        if (!string.Equals(StreamerBotEndpoint, s.StreamerBotEndpoint ?? "", StringComparison.Ordinal)) return true;
        return !string.Equals(StreamerBotPassword, s.StreamerBotPassword ?? "", StringComparison.Ordinal);
    }

    [RelayCommand]
    private void Save()
    {
        // §148: ONE Save persists OBS + all four Streamer.bot fields
        // together (no new save semantic). Port follows the §147
        // Settings SaveAll rule: blank = keep the persisted port;
        // unparseable/out-of-range = named S1034 failure, stay open,
        // persist NOTHING (same as SaveAll's validate-first rule).
        // Save failure sets the message on BOTH tabs so it is visible
        // whichever tab is active (the footer sits outside the tabs).
        int sbPort = _host.Config.Settings.StreamerBotPort;
        if (!string.IsNullOrWhiteSpace(StreamerBotPortText)
            && (!int.TryParse(StreamerBotPortText, out sbPort) || sbPort < 1 || sbPort > 65535))
        {
            StreamerBotMessageText = Localizer.Format("S1034", Localizer.Get("S1745"), StreamerBotPortText);
            return;
        }
        _host.Config.Settings.ObsHost = ObsHost.Trim();
        _host.Config.Settings.ObsPassword = ObsPassword;
        _host.Config.Settings.StreamerBotHost = StreamerBotHost.Trim();
        _host.Config.Settings.StreamerBotPort = sbPort;
        _host.Config.Settings.StreamerBotEndpoint = StreamerBotEndpoint;
        _host.Config.Settings.StreamerBotPassword = StreamerBotPassword;
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Silent-save audit: modal tetap terbuka, Saved/
            // RequestClose TIDAK di-invoke; nilai masih di VM sehingga
            // klik Simpan berikutnya adalah retry langsung.
            string saveError = Localizer.Get("S0937") + " " + ex.Message;
            MessageText = saveError;
            StreamerBotMessageText = saveError;
            return;
        }
        Saved?.Invoke();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
