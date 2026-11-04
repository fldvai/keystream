using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.Actions.Common;
using Keystream.App.Host;
using Keystream.App.Services;
using Keystream.Core.Config;
using Keystream.Core.Devices;
using Keystream.Core.Engine;
using Keystream.Core.Localization;
using Keystream.Core.Icons;
using Keystream.Core.Input;
using Keystream.Windows.Interception;
using Keystream.App.Views;

namespace Keystream.App.ViewModels;

public partial class BindingItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required BindingConfig Model { get; init; }

    /// <summary>Tile flash: lit while the physical deck key is held down
    /// (UI v2.5) - immediate proof of which tile a key belongs to.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isFlashing;

    /// <summary>§124 - true while this tile is selected
    /// (blue frame + order number in Mode Pilih, no size/layout change). Owned by
    /// MainViewModel's TileSelection set; re-applied on every
    /// RefreshBindings because item VMs are rebuilt per refresh.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isSelected;

    /// <summary>§133 M3: 1-based position of this tile in the ordered
    /// TileSelection (urutan masuk); 0 = not selected. Set ONLY by
    /// MainViewModel.SyncSelectionFlags/ApplySelectionFlags - never
    /// directly - so deselect/delete/prune/refresh always renumber
    /// tightly 1..N with no gaps.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private int _selectionIndex;

    /// <summary>§133 M2: true only when selected AND Mode Pilih is
    /// active - the order-number circle in the middle of the tile.
    /// Outside the mode this is false even for selected tiles
    /// (highlight only). Set ONLY by ApplySelectionFlags.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _showSelectionNumber;

    /// <summary>§132: passthrough of the card's IsActive flag
    /// (BindingConfig.IsActive), refreshed on every RefreshBindings
    /// like IsSelected. Drives the dimmed rendering + "Nonaktif"
    /// label in both templates; the resolver/flash paths read
    /// Model.IsActive directly.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isActive = true;

    /// <summary>True when the card is inactive (dimmed + labeled).</summary>
    public bool IsInactive => !IsActive;
    partial void OnIsActiveChanged(bool value) => OnPropertyChanged(nameof(IsInactive));

    /// <summary>True when this tile's first step is the MicMute media
    /// command - only such tiles carry the live mic-state indicator.</summary>
    public bool IsMicMuteTile { get; init; }

    /// <summary>Live microphone state (v3.2.0): true while the default
    /// capture endpoint is muted. Polled and pushed by MainViewModel;
    /// the tile draws a red slash over its icon while this is set.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isMicMuted;

    /// <summary>⚠ badge (UI v2.8, the user's simple warning): this tile's
    /// key is also used by the other scope - hovering explains which one
    /// wins. Set by MainViewModel when the grid is built.</summary>
    public bool HasConflict { get; init; }
    public string ConflictText { get; init; } = "";
    public string DisplayLabel => string.IsNullOrWhiteSpace(Model.Label) ? Model.KeyId : Model.Label;

    /// <summary>§124: field teks yang dicari kotak pencarian dashboard
    /// (label, nama tombol fisik, DetailText, nama aksi, target
    /// langkah pertama). Diisi MainViewModel saat grid dibangun;
    /// tidak ditampilkan, hanya dipakai pencocokan.</summary>
    internal string[] SearchFields { get; set; } = Array.Empty<string>();
    /// <summary>The stored icon value, defaulting to the gamepad key.</summary>
    public string RawIcon => string.IsNullOrWhiteSpace(Model.Icon) ? "gamepad" : Model.Icon;
    /// <summary>The AppIcons key for this tile, or null when the stored
    /// value is a free-form glyph (rendered as text instead).</summary>
    public string? IconKey => AppIcons.ResolveKey(RawIcon);

    /// <summary>Folder the uploaded icons live in, supplied by
    /// MainViewModel from the host's IconStore (v3.4.0).</summary>
    public string IconsFolder { get; init; } = "";

    /// <summary>Full path of the uploaded icon file, or null when this
    /// tile has no file icon or its file is gone (missing files fall
    /// back to the built-in vector icon instead of a broken image).
    /// A file name smuggling a path separator is rejected outright.</summary>
    public string? FileIconPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconsFolder)) return null;
            // Render priority (v3.6.0): upload file first, auto file
            // second; the first file that actually exists wins, so a
            // deleted upload falls through to the auto icon rather
            // than a blank tile.
            foreach (string candidate in new[] { Model.Icon, Model.AutoIcon })
            {
                string? name = IconReference.FileName(candidate);
                if (name is null) continue;
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                string path = Path.Combine(IconsFolder, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }

    public bool HasFileIcon => FileIconPath is not null;

    /// <summary>True when the tile should render the free-form glyph
    /// text (not a vector icon, not a file icon).</summary>
    /// <summary>The vector icon actually drawn: the catalog key, or
    /// the default gamepad when a file icon's file is gone (v3.4.0
    /// fallback - a tile never renders blank for a missing upload).</summary>
    public string? TileIconKey => FileIconPath is null
        ? (IconKey ?? (IconReference.IsFileIcon(Model.Icon) ? "gamepad" : null))
        : null;

    public bool ShowGlyph => TileIconKey is null && !HasFileIcon;
    /// <summary>The glyph to render when there is no vector icon.</summary>
    public string IconGlyph => IconKey is null ? RawIcon : "";
    public string BackgroundColor => string.IsNullOrWhiteSpace(Model.BackgroundColor) ? "#2A2F3A" : Model.BackgroundColor;
    public string KeyBadge => Model.ChordMask == ChordMask.None
        ? KeyNames.Normalize(
            string.IsNullOrWhiteSpace(Model.KeyName) ? Model.KeyId : Model.KeyName)
        // §168: a chord-mapped card displays the chord AS its key.
        : ChordMask.Format(Model.ChordMask,
            string.IsNullOrWhiteSpace(Model.KeyName) ? Model.KeyId : Model.KeyName);

    public string KeyCapBadge
    {
        get
        {
            string t = (KeyBadge ?? "").Trim();
            // §168: a chord form ("Ctrl+S") uppercases whole - before
            // the single-modifier rules below, which would strip it
            // to a bare "CTRL".
            if (t.Contains('+')) return t.ToUpperInvariant();
            if (t.StartsWith("Key ", StringComparison.OrdinalIgnoreCase))
                return t.Substring(4).ToUpperInvariant();
            if (t.Equals("Space", StringComparison.OrdinalIgnoreCase)) return "SPACE";
            if (t.Equals("Return", StringComparison.OrdinalIgnoreCase) || t.Equals("Enter", StringComparison.OrdinalIgnoreCase)) return "ENTER";
            if (t.Equals("Escape", StringComparison.OrdinalIgnoreCase)) return "ESC";
            if (t.Equals("Back", StringComparison.OrdinalIgnoreCase) || t.Equals("Backspace", StringComparison.OrdinalIgnoreCase)) return "BKSP";
            if (t.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "WIN";
            if (t.Contains("Control", StringComparison.OrdinalIgnoreCase) || t.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) return "CTRL";
            if (t.Contains("Alt", StringComparison.OrdinalIgnoreCase)) return "ALT";
            if (t.Contains("Shift", StringComparison.OrdinalIgnoreCase)) return "SHFT";
            if (t.Equals("Delete", StringComparison.OrdinalIgnoreCase) || t.Equals("Del", StringComparison.OrdinalIgnoreCase)) return "DEL";
            if (t.Equals("Insert", StringComparison.OrdinalIgnoreCase) || t.Equals("Ins", StringComparison.OrdinalIgnoreCase)) return "INS";
            if (t.Contains("Page Up", StringComparison.OrdinalIgnoreCase)) return "PGUP";
            if (t.Contains("Page Down", StringComparison.OrdinalIgnoreCase)) return "PGDN";
            return t.ToUpperInvariant();
        }
    }
    /// <summary>The tile's second line (v2.9.17, the user's redesign):
    /// the action's actual CONTENT - the hotkey chord, the media command,
    /// the OBS command + scene, the file or app name - never just the
    /// action-type name.</summary>
    public string DetailText
    {
        get
        {
            var first = Model.Steps.FirstOrDefault();
            if (first is null) return Localizer.Get("S0328");
            string P(string key) => ParamReader.GetString(first.Params, key) ?? "";
            string detail = first.ActionId switch
            {
                "hotkey" => P("keys"),
                "media" => DisplayOf(ActionCatalog.MediaCommands, P("command")),
                "basic" => DisplayOf(ActionCatalog.BasicCommands, P("command")),
                "profile" => ProfileDetail(first),
                "launcher" => LauncherDetail(first),
                "link" => ShortTarget(P("url").Length > 0 ? P("url") : P("target")),
                "obs" => ObsDetail(first),
                "sound" => FileName(P("file")),
                "deck" => DeckDetail(first),
                _ => "",
            };
            if (string.IsNullOrWhiteSpace(detail)) detail = ActionCatalog.FriendlyName(first.ActionId);
            return Model.Steps.Count > 1 ? $"{detail} +{Model.Steps.Count - 1}" : detail;
        }
    }

    public bool HasDetail => Model.Steps.Count > 0 && !string.IsNullOrWhiteSpace(DetailText);

    private static string DisplayOf(System.Collections.Generic.IReadOnlyList<ActionCatalog.ValueOption> options, string value)
        => options.FirstOrDefault(o => o.Value == value)?.Display ?? value;

    private static string LauncherDetail(ActionStep step)
        => ShortTarget(ParamReader.GetString(step.Params, "target") ?? "");

    private static string ObsDetail(ActionStep step)
    {
        string cmd = ParamReader.GetString(step.Params, "command") ?? "";
        string display = DisplayOf(ActionCatalog.ObsCommands, cmd);
        var spec = ActionCatalog.ObsSpec(cmd);
        string? name = spec.NameParam.Length > 0 ? ParamReader.GetString(step.Params, spec.NameParam) : null;
        return string.IsNullOrWhiteSpace(name) ? display : $"{display}: {name}";
    }

    private static string DeckDetail(ActionStep step)
    {
        string cmd = ParamReader.GetString(step.Params, "command") ?? "";
        string? page = ParamReader.GetString(step.Params, "pageName");
        if (cmd == "goto" && !string.IsNullOrWhiteSpace(page)) return Localizer.Format("S0331", page);
        if (cmd == "momentary" && !string.IsNullOrWhiteSpace(page)) return Localizer.Format("S0332", page);
        return DisplayOf(ActionCatalog.DeckCommands, cmd);
    }

    private static string ProfileDetail(ActionStep step)
    {
        string? name = ParamReader.GetString(step.Params, "profileName");
        return string.IsNullOrWhiteSpace(name) ? "" : Localizer.Format("S0333", name);
    }

    private static string FileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string name = System.IO.Path.GetFileName(path.Trim());
        return string.IsNullOrWhiteSpace(name) ? path.Trim() : name;
    }

    private static string ShortTarget(string target)
    {
        target = target.Trim();
        int scheme = target.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0) return target[(scheme + 3)..].TrimEnd('/');
        return FileName(target);
    }
}

/// <summary>
/// UI v2 main window model - deliberately slim. The main window shows
/// status and tiles; everything technical (service, OBS, settings) lives
/// in the Settings window, and binding editing lives in the tile editor
/// dialog. What remains here: header pills, profiles/pages, the tile
/// grid, press-to-map capture, and the execution status line.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly System.Windows.Threading.DispatcherTimer _micTimer;
    private bool _captureArmed;

    /// <summary>§168: pending chord state for press-to-map capture,
    /// shared by the new-card and rebind paths (one accumulator, one
    /// rule). Reset on every arm/cancel/completion - a leaked mask
    /// would corrupt the next capture.</summary>
    private readonly PendingChord _captureChord = new();
    private readonly HashSet<KeyIdentity> _soundPressedKeys = new();

    // §123 butir 5 (blokir pintar): selama capture armed, eksekusi deck
    // diblokir lewat _host.SuppressEngineEvents - AppHost meng-gate
    // Engine.ProcessEvent dengan flag itu, sedangkan DeckEventObserved
    // TIDAK ter-gate, jadi capture tetap menerima tombol. Disiplin
    // kepemilikan: arming menyimpan nilai flag SEBELUM arming, jalur
    // keluar memulihkan NILAI ITU (bukan selalu false), agar supresi
    // milik editor yang sedang listening tidak pernah dibersihkan
    // capture; editor memakai disiplin simpan/pulihkan yang sama.
    private bool _suppressBeforeCapture;

    /// <summary>Naikkan supresi saat arming. Hanya pada transisi
    /// tidak-armed → armed, agar nilai tersimpan tidak tertimpa arming
    /// ulang.</summary>
    private void ArmCaptureSuppression()
    {
        if (_captureArmed) return;
        _suppressBeforeCapture = _host.SuppressEngineEvents;
        _host.SuppressEngineEvents = true;
    }

    /// <summary>Pulihkan nilai flag sebelum arming. Idempotent: nilai
    /// yang dipulihkan sama di semua jalur keluar capture.</summary>
    private void RestoreCaptureSuppression()
        => _host.SuppressEngineEvents = _suppressBeforeCapture;

    public event Action? RequestOpenWizard;
    public event Action? RequestOpenSettings;
    public event Action? RequestOpenMacro;
    public event Action? RequestOpenObsConfig;
    public event Action? RequestOpenDeckStatus;
    public event Action<BindingEditorViewModel>? RequestEditBinding;
    /// <summary>§124: rename-one-card dialog (small PageEdit-style
    /// window, never the full editor). Set by MainWindow; null = the
    /// rename is refused silently by staying unapplied.</summary>
    public event Action<BindingRenameViewModel>? RequestRenameBinding;
    public event Action<ProfileEditViewModel>? RequestEditProfile;
    public event Action<PageEditViewModel>? RequestEditPage;

    /// <summary>Yes/no confirmation for destructive actions (delete
    /// profile/page). Set by the window to AppDialog; null = refuse.</summary>
    public Func<string, bool>? ConfirmPrompt { get; set; }

    /// <summary>§171 Task 6: three-choice prompt for an imported
    /// Global section (Replace/Merge/Skip). Set by the window to the
    /// shared GlobalImport.Ask dialog; null = Skip (never overwrite
    /// Global silently).</summary>
    public Func<int, GlobalImportChoice>? RequestGlobalImportChoice { get; set; }

    private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new();
    // Timer sekali-tembak umpan balik langkah lambat (§45): dipersenjatai
    // tiap StepStarted, dilucuti tiap StepExecuted/JobFinished - jadi
    // "Menjalankan…" tidak pernah muncul SETELAH hasil sudah tampil.
    private readonly System.Windows.Threading.DispatcherTimer _slowStepTimer = new();
    private bool _isInitialized;

    public MainViewModel(AppHost host)
    {
        _host = host;
        DashboardViewMode = _host.Config.Settings.DashboardViewMode == "list" ? "list" : "grid";
        _statusTimer.Interval = TimeSpan.FromSeconds(3.5);
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText = Localizer.Get("S0334");
            StatusDotColor = "#10B981";
        };
        AppVersionText = GetAppVersionString();
        IsAlwaysOnTop = _host.Config.Settings.AlwaysOnTop;

        _host.HealthChanged += RefreshHeader;
        _host.Dispatcher.StepExecuted += ex => RunOnUi(() =>
        {
            string status = ex.Result.Success ? Localizer.Get("S0335") : Localizer.Get("S0336") + ex.Result.Error;
            string dot = ex.Result.Success ? "#10B981" : "#EF4444";
            Notify(Localizer.Format("S0337", DateTime.Now, ex.KeyId, ex.ActionId, status), dot, 4.0);
            if (ex.ActionId is "browser" or "browsergroup")
            {
                // Baris menetap: sukses memakai Detail ("'X' dibuka /
                // difokuskan / ditutup", "grup 'G' - N profil dibuka");
                // gagal memakai Error lengkap (tooltip menampung alasan
                // per entri grup yang terpotong di footer satu baris).
                string time = DateTime.Now.ToString(Localizer.Get("S0338"));
                if (ex.Result.Success && ex.Result.Detail is not null)
                {
                    LastBrowserResultText = Localizer.Format("S0339", ex.Result.Detail, time);
                    LastBrowserResultTooltip = LastBrowserResultText;
                    LastBrowserResultDot = ex.Result.Detail.Contains("ditutup") ? "#8A93A5" : "#10B981";
                }
                else if (!ex.Result.Success)
                {
                    LastBrowserResultText = Localizer.Format("S0340", ex.Result.Error, time);
                    LastBrowserResultTooltip = ex.Result.Error ?? LastBrowserResultText;
                    LastBrowserResultDot = "#EF4444";
                }
                HasLastBrowserResult = LastBrowserResultText.Length > 0;
            }
        });
        // Agregat multi-langkah (§45): bila ada langkah gagal di job
        // multi-langkah, tampilkan ringkasan gagal - bukan hasil langkah
        // terakhir (yang bisa "OK" padahal langkah awal gagal).
        _host.Dispatcher.JobFinished += (keyId, failed, total) => RunOnUi(() =>
        {
            if (total > 1 && failed > 0)
                Notify(Localizer.Format("S0341", DateTime.Now, keyId, failed, total), "#EF4444", 5.0);
        });
        _slowStepTimer.Interval = TimeSpan.FromSeconds(1.5);
        _slowStepTimer.Tick += (_, _) =>
        {
            _slowStepTimer.Stop();
            Notify(Localizer.Get("S0342"), "#8A93A5", 30.0);
        };
        _host.Dispatcher.StepStarted += (_, _) => RunOnUi(() =>
        {
            _slowStepTimer.Stop();
            _slowStepTimer.Start();
        });
        _host.Dispatcher.StepExecuted += _ => RunOnUi(() =>
        {
            _slowStepTimer.Stop();
            RefreshMicState();
        });
        _micTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _micTimer.Tick += (_, _) => RefreshMicState();
        _micTimer.Start();
        _host.DeckEventObserved += OnDeckEventObserved;
        _host.DeckEventObserved += e => RunOnUi(() => FlashTile(e));
        _host.DeckEventObserved += e =>
        {
            if (e.Direction == KeyDirection.Up)
            {
                lock (_soundPressedKeys) { _soundPressedKeys.Remove(e.Key); }
                return;
            }

            if (e.Direction == KeyDirection.Down)
            {
                bool isInitialPress;
                lock (_soundPressedKeys)
                {
                    isInitialPress = _soundPressedKeys.Add(e.Key);
                }

                if (isInitialPress && !e.IsRepeat && _host.Config.Settings.PlaySoundOnPress)
                {
                    SoundFeedback.PlayClick();
                }
            }
        };
        _host.PageSwitchedByAction += () => RunOnUi(RefreshProfilesAndPages);
        _host.IconsChanged += () => RunOnUi(RefreshBindings);
        _host.ActiveProfileChangedByAutoSwitch += () => RunOnUi(RefreshProfilesAndPages);
        _host.IdentificationCompleted += _ => RunOnUi(RefreshHeader);
        // §148: pill gabungan WebSocket ikut berubah saat salah satu
        // client berubah status - timer HealthChanged 500ms tetap
        // menjadi backstop, tidak disentuh.
        _host.Obs.ConnectionChanged += () => RunOnUi(RefreshHeader);
        _host.StreamerBot.ConnectionChanged += () => RunOnUi(RefreshHeader);
        _isPassthrough = _host.IsPassthrough;
        _host.PassthroughChanged += isPass => RunOnUi(() =>
        {
            if (_isPassthrough != isPass)
            {
                _isPassthrough = isPass;
                OnPropertyChanged(nameof(IsPassthrough));
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(PauseIconGlyph));
            }
        });
        // §89: log footer untuk SEMUA jalur toggle (rocker header,
        // hotkey, menu tray) - sebelumnya hanya rocker/hotkey yang
        // Notify via ToggleGlobalEnabled, tray tidak. Event ini
        // hanya menyala saat nilai benar-benar berubah
        // (SetGlobalEnabled early-return bila sama).
        _host.GlobalEnabledChanged += value => RunOnUi(() =>
        {
            NotifyGlobalStylingChanged();
            RefreshBindings();
            Notify(value ? Localizer.Get("S0362") : Localizer.Get("S0363"),
                   value ? "#10B981" : "#F59E0B", 3.0);
        });
        // §86: language toggle - re-render computed display strings, header
        // pills and tiles on switch (tile labels themselves are user data
        // and never translated).
        Localizer.LanguageChanged += () => RunOnUi(OnLanguageChanged);

        RefreshHeader();
        RefreshProfilesAndPages();
        if (host.InitialLoad.Note is not null)
            // FreshDefault = seluruh binding hilang: beri waktu baca
            // lebih lama (15 detik) daripada catatan biasa (5 detik).
            Notify(host.InitialLoad.Note, "#38BDF8",
                host.InitialLoad.Source == LoadSource.FreshDefault ? 15.0 : 5.0);
        else if (host.Config.DeckDevice.RequiresReidentification)
            Notify(Localizer.Get("S0343"), "#F59E0B", 6.0);
        else
        {
            StatusText = Localizer.Get("S0334");
            StatusDotColor = "#10B981";
        }
        _isInitialized = true;
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) { action(); return; }
        // MUST stay asynchronous: this runs on the interception worker
        // thread for every deck stroke, and that thread is the only thing
        // forwarding the MAIN keyboard. A synchronous Invoke here once
        // froze every keyboard for as long as a modal (opened from the
        // capture path, inside the Invoke) stayed open.
        dispatcher.BeginInvoke(action);
    }

    // --- header --------------------------------------------------------------

    [ObservableProperty] private string _headerDeckText = "";
    [ObservableProperty] private string _headerObsText = "";
    [ObservableProperty] private string _deckDot = "#8A93A6";
    [ObservableProperty] private string _obsDot = "#8A93A6";

    // --- status bar telemetry HUD --------------------------------------------
    [ObservableProperty] private string _statusText = Localizer.Get("S0334");
    [ObservableProperty] private string _statusDotColor = "#10B981";
    // Baris hasil browser yang MENETAP (temuan riset 3): hanya in-memory,
    // hanya diperbarui oleh aksi browser, tidak pernah oleh timer atau
    // Notify lain - jadi hasil tekan bisa dicek ulang setelah HUD
    // transient kembali ke READY. Kosong saat start (jujur: belum ada
    // hasil sesi ini).
    [ObservableProperty] private string _lastBrowserResultText = "";
    [ObservableProperty] private string _lastBrowserResultTooltip = "";
    [ObservableProperty] private string _lastBrowserResultDot = "#8A93A5";
    [ObservableProperty] private bool _hasLastBrowserResult;
    [ObservableProperty] private string _appVersionText = Localizer.Get("S0344");
    [ObservableProperty] private bool _isAlwaysOnTop;

    public string LastExecutionText
    {
        get => StatusText;
        set => Notify(value);
    }
    public bool HasLastExecution => true;

    public void Notify(string message, string dotColor = "#10B981", double durationSeconds = 3.5)
    {
        RunOnUi(() =>
        {
            _statusTimer.Stop();
            StatusText = message;
            StatusDotColor = dotColor;
            _statusTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
            _statusTimer.Start();
        });
    }

    // §171: internal agar SettingsViewModel memakai string versi
    // yang sama persis (satu sumber, tanpa duplikasi).
    internal static string GetAppVersionString()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (ver != null && (ver.Major > 0 || ver.Minor > 0))
            return Localizer.Format("S0345", ver.Major, ver.Minor, ver.Build);
        return Localizer.Get("S0344");
    }

    private long _lastSeenDroppedJobs;
    private string _lastResolverWarningsShown = "";
    private string? _lastSaveErrorShown;
    private bool? _lastDeckPresent;
    private string? _lastFaultErrorShown;

    private void RefreshHeader()
    {
        // Antrean penuh (§45): DroppedJobs hanya dibaca SelfCheck -
        // kini footer memberitahu sekali per kenaikan.
        long dropped = _host.Dispatcher.DroppedJobs;
        if (dropped > _lastSeenDroppedJobs)
            Notify(Localizer.Get("S0346"), "#F59E0B", 4.0);
        _lastSeenDroppedJobs = dropped;
        // Peringatan resolver: sekali per perubahan (bukan per tick).
        string resolverWarnings = string.Join("; ", _host.Engine.LastResolveWarnings);
        if (resolverWarnings.Length > 0 && resolverWarnings != _lastResolverWarningsShown)
            Notify(Localizer.Get("S0348") + resolverWarnings, "#F59E0B", 6.0);
        _lastResolverWarningsShown = resolverWarnings;
        // Save gagal menetap (§45): LastSaveError dulu hanya dibaca
        // SelfCheck - kini footer memberitahu sekali per pesan berbeda
        // (bukan dialog per tick, bukan hilang tanpa jejak).
        string? saveError = _host.LastSaveError;
        if (saveError is not null && saveError != _lastSaveErrorShown)
            Notify(Localizer.Get("S0349") + saveError, "#EF4444", 8.0);
        _lastSaveErrorShown = saveError;

        var d = _host.Config.DeckDevice;
        var h = _host.Health;
        bool identified = d.Value.Length > 0 && !d.RequiresReidentification;
        // §44/disconnect + D8: putus/sambung/fault dulu HANYA mengubah
        // pill header (dan log internal) - tanpa pengumuman footer,
        // user yang sedang menekan deck tidak tahu kenapa tidak ada
        // yang terjadi. Kini TRANSISI-nya diumumkan sekali lewat
        // footer (string header yang sudah ada, tanpa kunci baru):
        // baseline pertama tidak mengumumkan (status awal sudah
        // ditampilkan pill + catatan load), dan Faulted diumumkan
        // sekali per LastError yang berbeda, dengan detail alasannya.
        if (identified && h.State == InterceptionState.Running)
        {
            if (_lastDeckPresent == true && !h.DeckPresent)
                Notify(Localizer.Get("S0352"), "#F59E0B", 6.0);
            else if (_lastDeckPresent == false && h.DeckPresent)
                Notify(Localizer.Get("S0351"), "#10B981", 3.5);
            _lastDeckPresent = h.DeckPresent;
        }
        else if (identified && h.State == InterceptionState.Faulted)
        {
            if (h.LastError != _lastFaultErrorShown)
                Notify(Localizer.Get("S0353")
                    + (string.IsNullOrEmpty(h.LastError) ? "" : ": " + h.LastError),
                    "#EF4444", 8.0);
            _lastFaultErrorShown = h.LastError;
        }
        HeaderDeckText = !identified
            ? Localizer.Get("S0350")
            : h.State == InterceptionState.Running
                ? (h.DeckPresent ? Localizer.Get("S0351") : Localizer.Get("S0352"))
                : Localizer.Get("S0353");
        DeckDot = !identified ? "#8A93A6" : h.State == InterceptionState.Running ? (h.DeckPresent ? "#3DDC84" : "#FFC65C") : "#FFC65C";
        ObsDot = _host.Obs.IsConnected || _host.StreamerBot.IsConnected ? "#3DDC84" : "#8A93A6";
        // §148: satu pill gabungan WebSocket - 4-state dari
        // (OBS, Streamer.bot): tak ada → WEBSOCKET abu; hanya OBS →
        // OBS; hanya Streamer.bot → STREAMER.BOT; keduanya →
        // WEBSOCKET. Kunci OBS lama tidak lagi menggerakkan pill ini.
        HeaderObsText = _host.Obs.IsConnected && _host.StreamerBot.IsConnected ? Localizer.Get("S1761")
            : _host.Obs.IsConnected ? Localizer.Get("S1762")
            : _host.StreamerBot.IsConnected ? Localizer.Get("S1763")
            : Localizer.Get("S1761");
    }

    [ObservableProperty] private bool _isPassthrough;
    partial void OnIsPassthroughChanged(bool value)
    {
        _host.SetPassthrough(value);
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(PauseIconGlyph));
    }
    public string PauseButtonText => IsPassthrough ? Localizer.Get("S0356") : Localizer.Get("S0357");
    public string PauseIconGlyph => IsPassthrough ? "▷" : "❚❚";

    // §86 language selector (title-bar dropdown, Wave 1: 12 languages):
    // options are projected from the Localizer registry (the single
    // source of truth) - no code list or endonym is duplicated here.
    // A ValueTuple cannot be bound (WPF binds properties, not fields),
    // so each registry entry is projected to a LanguageOption record
    // with Code/Endonym properties. Closed display shows DisplayCode
    // (upper-case 2-letter code); the Settings dropdown shows the
    // endonym from the same registry.
    public sealed record LanguageOption(string Code, string Endonym)
    {
        public string DisplayCode => Code.ToUpperInvariant();
    }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; } =
        Localizer.Languages.Select(l => new LanguageOption(l.Code, l.Endonym)).ToArray();

    // TwoWay selection for the title-bar dropdown: the staged value is
    // the lower-case registry code. The initial value is the live
    // language (startup already applied the saved setting before any
    // window was created); OnLanguageChanged re-syncs it whenever
    // another path (Settings SaveAll) switches the language.
    [ObservableProperty] private string _selectedLanguage = Localizer.Language;

    private bool _normalizingLanguage;

    partial void OnSelectedLanguageChanged(string value)
    {
        var normalized = Localizer.NormalizeLanguage(value);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            // Non-canonical input (defensive only: the dropdown only ever
            // yields registry codes). Re-assign through the generated
            // property so change notification + this handler run normally;
            // the re-entrant call applies the normalized value.
            if (_normalizingLanguage) return;
            _normalizingLanguage = true;
            try { SelectedLanguage = normalized; }
            finally { _normalizingLanguage = false; }
            return;
        }
        ApplyLanguage(normalized);
    }

    // --- dashboard view mode (§90) ----------------------------------------
    // Segmented toggle Grid/List in the title row. Global (not
    // per-profile), persisted in Settings like Language. Anything
    // other than "list" normalizes to "grid".
    [ObservableProperty] private string _dashboardViewMode = "grid";
    public bool IsGridView => DashboardViewMode != "list";
    public bool IsListView => DashboardViewMode == "list";
    partial void OnDashboardViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsGridView));
        OnPropertyChanged(nameof(IsListView));
    }

    [RelayCommand]
    private void SetDashboardViewMode(string mode)
    {
        if (mode is not ("grid" or "list")) return;
        DashboardViewMode = mode;
        _host.Config.Settings.DashboardViewMode = mode;
        try { _host.Save(); }
        catch { /* AppHost.Save already recorded LastSaveError; UI stays switched */ }
    }

    // --- dashboard search (§124 butir 2) --------------------------------
    // Satu kotak cari di baris header; live filter saat mengetik
    // (UpdateSourceTrigger=PropertyChanged di XAML). Bukan filter
    // Bermasalah, tanpa chip/hitungan: kartu yang tidak cocok
    // disembunyikan dari Bindings, urutan dan halaman aktif tidak
    // berubah (Bindings hanya berisi halaman aktif; _allTiles
    // memegang semua kartu halaman itu). Teks TIDAK dibersihkan
    // saat ganti halaman - filter yang sama berlaku untuk halaman
    // aktif berikutnya; dikosongkan lewat tombol × / Esc di kotak.
    [ObservableProperty] private string _dashboardSearchText = "";
    partial void OnDashboardSearchTextChanged(string value) => ApplyDashboardSearchFilter();

    /// <summary>Isi ulang Bindings dari _allTiles sesuai teks cari,
    /// dengan urutan asal dipertahankan. Dipanggil tiap ketikan dan
    /// tiap RefreshBindings; tidak membangun ulang item, jadi status
    /// sesaat item (mis. flash) bertahan selama mengetik.</summary>
    private void ApplyDashboardSearchFilter()
    {
        Bindings.Clear();
        foreach (var item in _allTiles)
            if (DashboardSearchFilter.Matches(DashboardSearchText, item.SearchFields))
                Bindings.Add(item);
        // §134 A1: the cancel-all label depends on the visible set,
        // which changes here without any selection mutation.
        OnPropertyChanged(nameof(SelectAllButtonText));
    }

    // Guard: while ApplyLanguage is applying a selection, the synchronous
    // LanguageChanged refresh must not re-enter the apply path (and the
    // SetLanguage no-change early return covers the already-current case).
    private bool _applyingLanguage;

    /// <summary>Applies a title-bar language selection, in the exact order
    /// the old toggle used: Localizer.SetLanguage, persist the choice in
    /// Settings.Language, then _host.Save() with save failure swallowed
    /// (AppHost.Save records LastSaveError; the UI stays switched).
    /// A selection equal to the live language is a no-op: it arrives
    /// here from the desync refresh in OnLanguageChanged (Settings
    /// SaveAll applied the same language) and must not re-save.</summary>
    public void ApplyLanguage(string? code)
    {
        var normalized = Localizer.NormalizeLanguage(code);
        if (_applyingLanguage || normalized == Localizer.Language) return;
        _applyingLanguage = true;
        try
        {
            Localizer.SetLanguage(normalized);
            _host.Config.Settings.Language = normalized;
            try { _host.Save(); }
            catch { /* AppHost.Save already recorded LastSaveError; UI stays switched */ }
        }
        finally { _applyingLanguage = false; }
    }

    private void OnLanguageChanged()
    {
        // Desync guard: sync the title-bar selection to the live language
        // without re-applying/saving - OnSelectedLanguageChanged no-ops
        // (via ApplyLanguage) when the selection already equals the live
        // language, and the generated setter no-ops when it is unchanged.
        SelectedLanguage = Localizer.Language;
        OnPropertyChanged(string.Empty); // all computed display strings
        RefreshHeader();
        RefreshBindings();
    }

    public void TogglePassthroughFromHotkey() => RunOnUi(() => IsPassthrough = !IsPassthrough);

    // --- §137 master shortcuts: recording / playback / pause (Worker B) ---
    // Called from MainWindow.WndProc on the UI thread, exactly like
    // TogglePassthroughFromHotkey above. Every body runs through
    // RunOnUi (a no-op hop on the UI thread) and is wrapped in
    // try/catch: a hotkey must NEVER throw out of WndProc. All three
    // delegate to the CURRENT panel handler through FloatPanelHost -
    // the macro modal when it is open, the standalone controller
    // otherwise - so no second start/stop implementation exists here:
    // recording starts/stops through the panel ●/■ paths, playback
    // through the panel ▶/CTS-cancel paths, and the pause gate lives
    // in MacroAction (PlaybackPauseGate). The session kind read
    // (PanelSessionKind) is the handlers' own state, not a second
    // ownership model. Each fire is logged at Debug level (Worker C's
    // CrashLog.WriteDebug, Debug-only): never per-keystroke, only
    // per master-shortcut fire, as the CrashLog contract requires.

    /// <summary>§137 Toggle Recording: recording → stop via the exact
    /// ■ path; playback → record request, which the handlers refuse
    /// with the visible S1633 error (a recording must never start
    /// over a running playback); record countdown → cancel it;
    /// playtest countdown → left alone (a ● press is a no-op while
    /// it runs, panel behaviour); idle → start into the target macro,
    /// or footer S1728 when no macro is selected/targetable (never
    /// create an unnamed macro silently).</summary>
    public void ToggleRecordingFromHotkey()
    {
        CrashLog.WriteDebug("Master shortcut: ToggleRecording ditekan.");
        RunOnUi(ToggleRecordingFromHotkeyCore);
    }

    private void ToggleRecordingFromHotkeyCore()
    {
        try
        {
            switch (_host.PanelHost.CurrentSessionKind)
            {
                case PanelSessionKind.Recording:
                    _host.PanelHost.StopCurrentRecording();
                    return;
                case PanelSessionKind.Playback:
                    // Refused inside the handler with S1633 (modal
                    // ShowError / standalone footer) - the exact
                    // panel ● behaviour while a playback runs.
                    _host.PanelHost.StartCurrentRecording();
                    return;
                case PanelSessionKind.RecordCountdown:
                    _host.PanelHost.CancelCurrentCountdown();
                    return;
                case PanelSessionKind.PlaytestCountdown:
                    return;
                default:
                    break;
            }
            if (!HasHotkeyRecordingTarget())
            {
                Notify(Localizer.Get("S1728"), "#F59E0B", 3.5);
                return;
            }
            _host.PanelHost.StartCurrentRecording();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MainViewModel.ToggleRecordingFromHotkey", ex);
        }
    }

    /// <summary>Whether a recording started by the hotkey would have
    /// a target: the shared session's selected macro id. When the
    /// modal is attached its working draft governs (it may be an
    /// unsaved draft whose id is not in the library yet); otherwise
    /// only a saved library entry counts - a stale id left by a
    /// deletion is NOT a target, so the hotkey reports S1728 instead
    /// of starting into nothing.</summary>
    private bool HasHotkeyRecordingTarget()
    {
        string? id = _host.FloatPanel.SelectedMacroId;
        if (id is null) return false;
        if (_host.PanelHost.IsModalAttached) return true;
        return _host.Config.Macros.Any(m => m.Id == id);
    }

    /// <summary>§137 Toggle Playback: playing → stop via the exact
    /// CTS-cancel path (silent, like ESC); playtest countdown →
    /// cancel it (no playback fires); record countdown → left alone
    /// (the recording countdown owns the session); idle/recording →
    /// start via the exact panel ▶ path, whose own guards refuse it
    /// silently while recording. No macro selected → an honest
    /// footer notice from EXISTING strings (S1165 no selection,
    /// S1167 deleted macro, S1168 zero steps - standalone case only;
    /// the modal draft's steps cannot be seen from here, so its
    /// handler's own snapshot validation governs there).</summary>
    public void TogglePlaybackFromHotkey()
    {
        CrashLog.WriteDebug("Master shortcut: TogglePlayback ditekan.");
        RunOnUi(TogglePlaybackFromHotkeyCore);
    }

    private void TogglePlaybackFromHotkeyCore()
    {
        try
        {
            var kind = _host.PanelHost.CurrentSessionKind;
            if (kind == PanelSessionKind.Playback)
            {
                _host.PanelHost.StopCurrentPlayback();
                return;
            }
            if (kind == PanelSessionKind.PlaytestCountdown)
            {
                _host.PanelHost.CancelCurrentCountdown();
                return;
            }
            if (kind == PanelSessionKind.RecordCountdown)
                return;
            string? id = _host.FloatPanel.SelectedMacroId;
            if (id is null)
            {
                Notify(Localizer.Get("S1165"), "#F59E0B", 3.5);
                return;
            }
            if (_host.PanelHost.IsModalAttached)
            {
                // The modal's working draft governs; its snapshot
                // validation (S0935/S0936) and empty-steps guard
                // decide honestly inside the ▶ path.
                _host.PanelHost.StartCurrentPlayback();
                return;
            }
            var macro = _host.Config.Macros.FirstOrDefault(m => m.Id == id);
            if (macro is null)
            {
                Notify(Localizer.Get("S1167"), "#F59E0B", 3.5);
                return;
            }
            if (macro.Steps.Count == 0)
            {
                Notify(Localizer.Format("S1168", macro.Name), "#F59E0B", 3.5);
                return;
            }
            _host.PanelHost.StartCurrentPlayback();
        }
        catch (Exception ex)
        {
            CrashLog.Write("MainViewModel.TogglePlaybackFromHotkey", ex);
        }
    }

    /// <summary>§137 Pause/Resume Playback: toggles the per-run pause
    /// gate of the playback in flight (pause applies only between
    /// steps and inside delays; cancellation always wins over the
    /// pause, so a paused playback is stoppable immediately). No
    /// playback active - including during a countdown, which has not
    /// started playing yet - → no-op + footer S1729, per the
    /// coordinator decision. Never blocks the UI thread: the gate
    /// only suspends the playback task's own awaits.</summary>
    public void PauseResumePlaybackFromHotkey()
    {
        CrashLog.WriteDebug("Master shortcut: PauseResumePlayback ditekan.");
        RunOnUi(PauseResumePlaybackFromHotkeyCore);
    }

    private void PauseResumePlaybackFromHotkeyCore()
    {
        try
        {
            if (!_host.PanelHost.ToggleCurrentPlaybackPause())
                Notify(Localizer.Get("S1729"), "#38BDF8", 3.5);
        }
        catch (Exception ex)
        {
            CrashLog.Write("MainViewModel.PauseResumePlaybackFromHotkey", ex);
        }
    }

    [RelayCommand]
    private void TogglePause() => IsPassthrough = !IsPassthrough;

    [RelayCommand]
    private void OpenWizard() => RequestOpenWizard?.Invoke();

    [RelayCommand]
    private void OpenSettings() => RequestOpenSettings?.Invoke();

    [RelayCommand]
    private void OpenMacro() => RequestOpenMacro?.Invoke();

    [RelayCommand]
    private void OpenObsConfig() => RequestOpenObsConfig?.Invoke();

    [RelayCommand]
    private void OpenDeckStatus() => RequestOpenDeckStatus?.Invoke();

    // --- Global ON/OFF Toggle & Dynamic CyberDeck Styling -------------------
    public bool IsGlobalEnabled => _host.Config.Settings.IsGlobalEnabled;
    public string GlobalToggleText => IsGlobalEnabled ? Localizer.Get("S0360") : Localizer.Get("S0361");
    public string GlobalToggleBackground => IsGlobalEnabled ? "#0C345C" : "#171C26";
    public string GlobalToggleBorder => IsGlobalEnabled ? "#38BDF8" : "#374151";
    public string GlobalToggleForeground => IsGlobalEnabled ? "#38BDF8" : "#7E8B9F";

    // Dynamic keycap colors: Blue when ON, Gray/Slate when OFF
    public string GlobalUnderglowBrush => IsGlobalEnabled ? "#1E86E0" : "#00000000";
    public string GlobalChassisBackground => IsGlobalEnabled ? "#0E325A" : "#141720";
    public string GlobalChassisBorder => IsGlobalEnabled 
        ? (IsEditingGlobal ? "#5BE4FF" : "#2572BD") 
        : (IsEditingGlobal ? "#607494" : "#2B3547");
    public string GlobalFaceBackground => IsGlobalEnabled ? "#1B6DBE" : "#222836";
    public string GlobalFaceBorder => IsGlobalEnabled 
        ? (IsEditingGlobal ? "#BDEBFF" : "#4FA6F7") 
        : (IsEditingGlobal ? "#738BAE" : "#344158");
    public string GlobalTextForeground => IsGlobalEnabled ? "#FFFFFF" : "#7E8B9F";

    private void NotifyGlobalStylingChanged()
    {
        OnPropertyChanged(nameof(IsGlobalEnabled));
        OnPropertyChanged(nameof(GlobalToggleText));
        OnPropertyChanged(nameof(GlobalToggleBackground));
        OnPropertyChanged(nameof(GlobalToggleBorder));
        OnPropertyChanged(nameof(GlobalToggleForeground));
        OnPropertyChanged(nameof(GlobalUnderglowBrush));
        OnPropertyChanged(nameof(GlobalChassisBackground));
        OnPropertyChanged(nameof(GlobalChassisBorder));
        OnPropertyChanged(nameof(GlobalFaceBackground));
        OnPropertyChanged(nameof(GlobalFaceBorder));
        OnPropertyChanged(nameof(GlobalTextForeground));
    }

    // §89: log footer TIDAK lagi di sini - sudah terpusat di handler
    // GlobalEnabledChanged (satu log per eksekusi, semua jalur).
    [RelayCommand]
    public void ToggleGlobalEnabled()
    {
        _host.SetGlobalEnabled(!_host.Config.Settings.IsGlobalEnabled);
        RefreshBindings();
        NotifyGlobalStylingChanged();
    }

    public void NextProfile()
    {
        if (Profiles.Count <= 1) return;
        int idx = SelectedProfile is null ? 0 : Profiles.IndexOf(SelectedProfile);
        int next = (idx + 1) % Profiles.Count;
        SelectedProfile = Profiles[next];
        Notify(Localizer.Format("S0364", SelectedProfile.Name), "#38BDF8", 3.0);
    }

    public void PreviousProfile()
    {
        if (Profiles.Count <= 1) return;
        int idx = SelectedProfile is null ? 0 : Profiles.IndexOf(SelectedProfile);
        int prev = (idx - 1 + Profiles.Count) % Profiles.Count;
        SelectedProfile = Profiles[prev];
        Notify(Localizer.Format("S0364", SelectedProfile.Name), "#38BDF8", 3.0);
    }

    // --- halaman global (edit vs jalan dipisah) --------------------------------

    /// <summary>True while the grid shows the GLOBAL page for editing.
    /// Editing never changes what the engine runs: the active profile
    /// page stays the executing one (see the banner in the main area).</summary>
    [ObservableProperty] private bool _isEditingGlobal;
    [ObservableProperty] private bool _hasNoBindings = true;
    partial void OnIsEditingGlobalChanged(bool value)
    {
        // §132: masuk/keluar edit-Global = ganti konteks halaman -
        // seleksi + snapshot Urungkan milik halaman lama, keduanya
        // dibersihkan (ClearSelectionAndUndo).
        ClearSelectionAndUndo();
        OnPropertyChanged(nameof(DisplayedPageName));
        OnPropertyChanged(nameof(CanRenamePage));
        NotifyGlobalStylingChanged();
        RefreshBindings();
    }

    public string DisplayedPageName => IsEditingGlobal ? Localizer.Get("S0232") : SelectedPage?.Name ?? Localizer.Get("S0365");

    /// <summary>The dashboard title can be renamed (modal, via
    /// EditPageCommand - §89) - except in global editing, whose
    /// title is structural.</summary>
    public bool CanRenamePage => !IsEditingGlobal && SelectedPage is not null;
    public string ActivePageName => SelectedPage?.Name ?? "-";
    public PageConfig? EffectivePage => IsEditingGlobal ? _host.Config.GlobalPage : SelectedPage;

    [RelayCommand]
    private void OpenGlobal()
    {
        IsEditingGlobal = true;
        RefreshBindings();
    }

    // --- profiles / pages -------------------------------------------------------

    public ObservableCollection<ProfileConfig> Profiles { get; } = new();
    public ObservableCollection<PageConfig> Pages { get; } = new();
    public ObservableCollection<BindingItemViewModel> Bindings { get; } = new();

    [ObservableProperty] private ProfileConfig? _selectedProfile;
    [ObservableProperty] private PageConfig? _selectedPage;

    partial void OnSelectedProfileChanged(ProfileConfig? value)
    {
        // §164: pindah profil SELALU keluar dari tampilan Global - invarian
        // eksplisit di sini, bukan lewat rantai emergen RefreshPages/
        // OnSelectedPageChanged (rantai itu tidak menyala bila profil tujuan
        // tanpa halaman - SelectedPage=null lebih dini di-return - atau
        // SelectedPage sudah null sebelumnya). Ditaruh PALING AWAL, sebelum
        // guard null, agar kasus nol-profil ikut tertutup; bila sudah false,
        // assignment ini no-op (setter melewatkan nilai sama) sehingga tidak
        // ada refresh ganda, dan bila true->false, ClearSelectionAndUndo +
        // RefreshBindings dari OnIsEditingGlobalChanged tidak berbahaya
        // (idempotent, langsung ditimpa RefreshPages di bawah sebelum
        // Save/Notify apa pun).
        IsEditingGlobal = false;
        if (value is null) return;
        _host.Config.ActiveProfileId = value.Id;
        RefreshPages();
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            // D9: dulu Save tanpa try/catch - gagal = dialog crash dan
            // pilihan profil di memori menyimpang dari file tanpa
            // pesan. Pilihan TIDAK dipulihkan (itulah tampilan sesi
            // saat ini; Save berikutnya akan menuliskannya), tapi
            // kegagalan selalu diberitahu lewat footer.
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        if (_isInitialized)
            Notify(Localizer.Format("S0367", value.Name), "#38BDF8", 2.5);
    }

    /// <summary>Called for EVERY click on a page row - including the page
    /// that is already selected. Re-selecting the current page fires no
    /// SelectedPage change, which once trapped the user in global-editing
    /// mode: he clicked "Main Page" to go back and nothing happened,
    /// because the only exit was the selection-change handler.</summary>
    public void ActivatePage(PageConfig page)
    {
        if (!ReferenceEquals(SelectedPage, page))
        {
            SelectedPage = page; // full change path; also exits global editing
            return;
        }
        IsEditingGlobal = false; // the mode change alone refreshes the grid
    }

    partial void OnSelectedPageChanged(PageConfig? value)
    {
        if (value is null) return;
        // §132: pindah halaman mengakhiri seleksi + Urungkan. (Jalur
        // ini tidak lewat RefreshPages - klik baris halaman langsung
        // mengubah SelectedPage - jadi pembersihan harus di sini.)
        ClearSelectionAndUndo();
        IsEditingGlobal = false;
        _host.Config.Settings.ActivePageId = value.Id;
        RefreshBindings();
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            // D9: sama seperti OnSelectedProfileChanged - halaman
            // terpilih dipertahankan di sesi, kegagalan diberitahu
            // lewat footer, tanpa Notify sukses yang menyesatkan.
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        if (_isInitialized)
            Notify(Localizer.Format("S0368", value.Name), "#38BDF8", 2.5);
    }

    public void RefreshProfilesAndPages()
    {
        Profiles.Clear();
        foreach (var p in _host.Config.Profiles) Profiles.Add(p);
        SelectedProfile = _host.Config.ActiveProfile ?? Profiles.FirstOrDefault();
        RefreshPages();
    }

    private void RefreshPages()
    {
        // §132: ganti halaman/profil mengakhiri seleksi + Urungkan -
        // keduanya milik halaman lama dan tidak boleh terbawa.
        ClearSelectionAndUndo();
        Pages.Clear();
        if (SelectedProfile is not null)
            foreach (var pg in SelectedProfile.Pages) Pages.Add(pg);
        SelectedPage = Pages.FirstOrDefault(pg => pg.Id == _host.Config.Settings.ActivePageId)
                       ?? Pages.FirstOrDefault();
        if (SelectedPage is null)
        {
            IsEditingGlobal = true;
        }
        RefreshBindings();
    }

    private readonly Dictionary<(ushort Scan, bool E0), BindingItemViewModel> _tileByKey = new();

    /// <summary>§124: SEMUA kartu halaman aktif, urutan asal. Bindings
    /// adalah tampilan terfilternya (lihat ApplyDashboardSearchFilter);
    /// _tileByKey dan RefreshMicState memakai daftar penuh ini agar
    /// kartu yang sedang tersembunyi filter tidak kehilangan status.</summary>
    private readonly List<BindingItemViewModel> _allTiles = new();

    public void RefreshBindings()
    {
        OnPropertyChanged(nameof(DisplayedPageName));
        OnPropertyChanged(nameof(CanRenamePage));
        OnPropertyChanged(nameof(ActivePageName));
        Bindings.Clear();
        _allTiles.Clear();
        _tileByKey.Clear();
        var page = EffectivePage;
        if (page is null) return;

        // ⚠ conflicts (key-level, across scopes) - computed once per grid.
        var conflictTextByKey = new Dictionary<(ushort, bool), string>();
        if (IsEditingGlobal)
        {
            var pagesByKey = new Dictionary<(ushort, bool), List<string>>();
            if (SelectedProfile is not null)
                foreach (var pg in SelectedProfile.Pages)
                    foreach (var b in pg.Bindings)
                    {
                        // §132: kartu nonaktif tidak dihitung konflik.
                        if (!b.IsActive) continue;
                        var k = (b.ScanCode, b.IsE0);
                        if (!pagesByKey.TryGetValue(k, out var list)) pagesByKey[k] = list = new List<string>();
                        if (!list.Contains(pg.Name)) list.Add(pg.Name);
                    }
            foreach (var (k, names) in pagesByKey)
                conflictTextByKey[k] = Localizer.Format("S0369", string.Join(", ", names.Take(3)));
        }
        else
        {
            foreach (var b in _host.Config.GlobalPage.Bindings)
            {
                // §132: kartu Global nonaktif tidak membayangi kartu profil.
                if (!b.IsActive) continue;
                conflictTextByKey[(b.ScanCode, b.IsE0)] =
                    Localizer.Get("S0371");
            }
        }

        // §132: duplikat same-page = kunci yang dipakai >= 2 binding
        // AKTIF di halaman yang sedang ditampilkan, lewat ATURAN
        // TUNGGAL BindingResolver.DuplicateKeys (kartu tanpa tombol
        // tidak pernah dihitung). Teks same-page menang atas teks
        // lintas-scope untuk kunci itu: duplikat lokal lebih
        // spesifik daripada konflik antar-scope.
        var samePageConflictTextByKey = new Dictionary<(ushort, bool, byte), string>();
        foreach (var k in BindingResolver.DuplicateKeys(page.Bindings))
            samePageConflictTextByKey[k] = Localizer.Get("S1634");

        // §124: nama target untuk pencarian - dicari sekali per grid,
        // bukan per kartu. Macro/grup yang sudah dihapus dari pustaka
        // = tanpa nama (kartu tetap bisa dicari lewat label/detail).
        var macroNameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in _host.Config.Macros) macroNameById[m.Id] = m.Name;
        var groupNameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in _host.Config.BrowserProfileGroups) groupNameById[g.Id] = g.Name;

        foreach (var b in page.Bindings)
        {
            // §123: preseden same-page atas lintas-scope (lihat di atas).
            string? ct;
            bool conflict;
            if (samePageConflictTextByKey.TryGetValue((b.ScanCode, b.IsE0, b.ChordMask), out string? sameCt))
            {
                conflict = true;
                ct = sameCt;
            }
            else
            {
                conflict = conflictTextByKey.TryGetValue((b.ScanCode, b.IsE0), out ct);
            }
            var item = new BindingItemViewModel
            {
                Model = b,
                HasConflict = conflict,
                ConflictText = ct ?? "",
                IsMicMuteTile = IsMicMuteBinding(b),
                IconsFolder = _host.IconStore.Folder,
                // §133 M3: IsSelected/SelectionIndex/ShowSelectionNumber
                // TIDAK diisi di sini - SyncSelectionFlags di bawah
                // mengisinya dari _selection yang terurut, sesudah
                // prune, agar angka selalu rapat 1..N.
                // §132: flag aktif/nonaktif kartu, untuk render redup.
                IsActive = b.IsActive,
            };
            // §124: field cari - label + nama tombol + teks kartu +
            // nama aksi + target langkah pertama (nama macro/grup
            // ter-resolusi; isi langkah macro tidak dibaca).
            var first = b.Steps.FirstOrDefault();
            string? macroName = null;
            string? groupName = null;
            if (first is not null)
            {
                if (first.ActionId == "macro")
                    macroNameById.TryGetValue(ParamReader.GetString(first.Params, "macroId", "MacroId") ?? "", out macroName);
                else if (first.ActionId == "browsergroup")
                    groupNameById.TryGetValue(ParamReader.GetString(first.Params, "groupId", "GroupId") ?? "", out groupName);
            }
            item.SearchFields = new[]
            {
                item.DisplayLabel, item.KeyBadge, item.KeyCapBadge,
                b.KeyName, b.KeyId, item.DetailText,
                ActionCatalog.FriendlyName(first?.ActionId ?? ""),
            }.Concat(DashboardSearchFilter.BuildTargetFields(first, macroName, groupName)).ToArray();
            _allTiles.Add(item);
            _tileByKey[(b.ScanCode, b.IsE0)] = item;
        }
        ApplyDashboardSearchFilter();
        // §124 butir 3: buang seleksi kartu yang sudah tidak ada di
        // halaman (dihapus) agar hitungan tidak menyebut kartu hantu.
        // §133 M3/M8a: prune + penerapan ulang seleksi/angka/mode
        // lewat SATU jalur (SyncSelectionFlags) - hapus via ×,
        // editor, rename, dan duplikat semuanya tiba di sini, jadi
        // renumber + keluar-mode-otomatis bila seleksi habis selalu
        // terjadi; seleksi bertahan melewati refresh (ganti bahasa,
        // ganti Grid/List, teks filter) karena kuncinya objek Model
        // yang sama dan tidak ada Clear di jalur-jalur itu.
        _selection.Prune(page.Bindings);
        // §134 A1: prune the select-all snapshot with the same live
        // set (deleted cards drop out of both); the active flag is
        // deliberately KEPT - the cancel-all label check and the
        // restore filter in SelectAllVisible stay correct on the
        // pruned lists, and an emptied snapshot restores to an
        // empty selection (auto-exit, §133 empty edge).
        _selectAllSnapshot?.RemoveAll(m => !page.Bindings.Contains(m));
        SyncSelectionFlags();
        // §124: empty-state "belum ada kartu" (S0239/S0240) hanya bila
        // halaman memang kosong - BUKAN bila filter mengosongkan
        // hasil; hasil kosong tampil kosong apa adanya, tanpa pesan.
        HasNoBindings = _allTiles.Count == 0;
        RefreshMicState();
    }

    private static bool IsMicMuteBinding(BindingConfig b)
    {
        var first = b.Steps.FirstOrDefault();
        if (first is null || first.ActionId != "media") return false;
        string cmd = Keystream.Actions.Media.MediaAction.NormalizeCommand(
            ParamReader.GetString(first.Params, "command", "Command"));
        return cmd == Keystream.Actions.Media.MediaAction.MicMuteCommand;
    }

    /// <summary>Keeps mic-mute tiles honest about the REAL device state:
    /// reads the default capture endpoint and pushes it to every mic
    /// tile on the current page. Cheap no-op when no mic tile is shown;
    /// also catches mutes changed outside the app (Settings, other apps).</summary>
    private void RefreshMicState()
    {
        // §124: iterasi _allTiles (bukan Bindings) agar kartu mic yang
        // sedang tersembunyi filter tetap segar saat filter dibuka.
        bool any = false;
        foreach (var i in _allTiles) if (i.IsMicMuteTile) { any = true; break; }
        if (!any) return;
        if (!Keystream.Actions.Media.MicMute.TryGetMuted(out bool muted)) return;
        foreach (var i in _allTiles)
            if (i.IsMicMuteTile && i.IsMicMuted != muted) i.IsMicMuted = muted;
    }

    private void FlashTile(KeyEvent e)
    {
        // §132 U7: tekan deck pada kartu NONAKTIF = silent no-op, tanpa
        // kilau. _tileByKey last-write-wins per kunci, jadi kembaran
        // nonaktif yang terdaftar sesudah kembaran aktif akan
        // membayangi jalur cepat di bawah; bila target jalur cepat
        // nonaktif, pindai mundur untuk kartu AKTIF terakhir dengan
        // kunci itu (identik dengan _tileByKey bila semua kartu untuk
        // kunci itu aktif - perilaku lama dipertahankan).
        if (_tileByKey.TryGetValue((e.Key.ScanCode, e.Key.IsE0), out var fast)
            && fast.Model.IsActive)
        {
            fast.IsFlashing = e.IsDown;
            return;
        }
        for (int i = _allTiles.Count - 1; i >= 0; i--)
        {
            var item = _allTiles[i];
            if (item.Model.ScanCode == e.Key.ScanCode && item.Model.IsE0 == e.Key.IsE0 && item.Model.IsActive)
            {
                item.IsFlashing = e.IsDown;
                return;
            }
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new ProfileConfig { Name = Localizer.Format("S0372", Profiles.Count + 1), Pages = { new PageConfig() } };
        _host.Config.Profiles.Add(profile);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // D9: dulu Save tanpa try/catch - gagal = dialog crash dan
            // modal edit terbuka untuk profil yang tidak tersimpan.
            // Kini profil dihapus dari memori dan kegagalan diberitahu
            // lewat footer; modal tidak dibuka.
            _host.Config.Profiles.Remove(profile);
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshProfilesAndPages();
        SelectedProfile = profile;
        EditProfileCore(isExistingProfile: false); // a new profile opens its edit modal right away
    }

    // §175 (dikoreksi 30 Okt 2026): the rail pencil (EditProfileCommand)
    // opens the same modal with the "Edit Profil" title; AddProfile
    // uses the core directly so a new profile gets "Tambah Profil".
    // The mode modal is a different window (PageEditViewModel, S0836
    // "Edit Mode") — never the same title as the profile modal.
    // No other behaviour differs.
    [RelayCommand]
    private void EditProfile() => EditProfileCore(isExistingProfile: true);

    private void EditProfileCore(bool isExistingProfile)
    {
        var profile = SelectedProfile;
        if (profile is null) return;
        var vm = new ProfileEditViewModel(_host, profile) { IsExistingProfile = isExistingProfile };
        RequestEditProfile?.Invoke(vm);
        if (vm.WantsDuplicate)
        {
            var copy = DuplicateProfile(profile);
            _host.Config.Profiles.Add(copy);
            try { _host.Save(); }
            catch (Exception ex)
            {
                // D9: sama seperti AddProfile - duplikat dihapus dari
                // memori, kegagalan diberitahu lewat footer.
                _host.Config.Profiles.Remove(copy);
                Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
                return;
            }
            RefreshProfilesAndPages();
            SelectedProfile = copy;
            Notify(Localizer.Format("S0373", copy.Name), "#38BDF8", 3.0);
            return;
        }
        RefreshProfilesAndPages();
        SelectedProfile = profile;
    }

    // --- sidebar profile buttons (UI overhaul, 24 Oct 2026) -------------------
    // Import/Export reuse the same .ksprofile format as Settings; Reset
    // clears every binding of the active profile (pages and names stay),
    // always behind an explicit confirmation dialog.

    [RelayCommand]
    private void ExportProfile()
    {
        var profile = SelectedProfile;
        if (profile is null) return;
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
            // D9/§6: catch dulu hanya IOException/UnauthorizedAccess -
            // Serialize (JsonException) dan File.Move di luar keluarga
            // itu lolos ke dialog crash tanpa pesan footer. Pesan dan
            // perilaku sama, jangkauan diperluas ke semua kegagalan.
            Notify(Localizer.Get("S0376") + ex.Message, "#EF4444", 4.0);
            return;
        }
        Notify(Localizer.Format("S0377", profile.Name), "#38BDF8", 3.0);
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
            if (_host.Config.Profiles.Any(pr => pr.Name == profile.Name))
                profile.Name += Localizer.Get("S0379");
            _host.Config.Profiles.Add(profile);
            added = profile;
            _host.Save();
            // §171a: import harus PINDAH ke profil yang baru diimport -
            // dulu RefreshProfilesAndPages mengembalikan tampilan ke
            // ActiveProfile lama ("profilenya mana?"). Pola yang sama
            // seperti AddProfile/DuplicateProfile: pilih sesudah refresh;
            // OnSelectedProfileChanged (§164) keluar dari edit Global,
            // menulis ActiveProfileId, RefreshPages + Save.
            // §171a-koreksi: refresh + pilih TIDAK BOLEH me-rollback
            // import yang sudah tersimpan — kegagalan refresh (konten
            // impor membuat ReloadActivePage/RefreshBindings melempar)
            // dulu jatuh ke catch luar: profil dihapus dari memori dan
            // sidebar kosong sampai restart. Dijaga sendiri: profil
            // tetap tersimpan, footer jujur (S1833) bila refresh gagal.
            string? refreshError = null;
            try { RefreshProfilesAndPages(); SelectedProfile = profile; }
            catch (Exception ex) { refreshError = Localizer.Format("S1833", ex.Message); }
            // Peringatan action tak dikenal dulu hilang di jalur ini
            // (§45) - kini ikut tampil di footer (lebih lama bila ada).
            string warnings = report.Warnings.Count > 0 ? Localizer.Get("S0380") + string.Join(" ", report.Warnings) : "";
            string globalText = report.Global is not null && globalOutcome is not null
                ? " " + GlobalImport.OutcomeText(globalChoice, globalOutcome, report.GlobalDropped) : "";
            Notify(Localizer.Format("S0382", profile.Name, report.BindingsImported)
                + (report.BindingsDropped > 0 ? Localizer.Format("S0383", report.BindingsDropped) : "") + ")." + globalText + warnings
                + (refreshError is not null ? " " + refreshError : ""),
                refreshError is not null ? "#EF4444" : "#10B981", refreshError is not null || report.Warnings.Count > 0 ? 8.0 : 3.5);
        }
        catch (InvalidDataException ex)
        {
            Notify(Localizer.Get("S0385") + ex.Message, "#EF4444", 4.0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dulu IOException lolos ke dialog crash DAN profil sudah
            // telanjur masuk memori (hilang saat restart, §45). Kini:
            // rollback dari memori + pesan jelas, bukan crash.
            if (added is not null) _host.Config.Profiles.Remove(added);
            if (prevGlobalBindings is not null) _host.Config.GlobalPage.Bindings = prevGlobalBindings;
            Notify(Localizer.Get("S0386") + ex.Message, "#EF4444", 4.0);
        }
        catch (Exception ex)
        {
            // §6: Parse sudah membungkus JsonException jadi
            // InvalidDataException, tapi _host.Save() di dalam try
            // yang sama bisa melempar di luar keluarga IO
            // (JsonException saat serialisasi config, NotSupported-/
            // SecurityException) - dulu lolos tanpa pesan DAN profil
            // telanjur di memori. Rollback + pesan yang sama seperti
            // jalur IO; tidak ada aturan validasi yang berubah.
            if (added is not null) _host.Config.Profiles.Remove(added);
            if (prevGlobalBindings is not null) _host.Config.GlobalPage.Bindings = prevGlobalBindings;
            Notify(Localizer.Get("S0386") + ex.Message, "#EF4444", 4.0);
        }
    }

    [RelayCommand]
    private void ResetProfile()
    {
        var profile = SelectedProfile;
        if (profile is null) return;
        if (ConfirmPrompt?.Invoke(Localizer.Format("S0387", profile.Name)) != true)
            return;
        // Snapshot per mode sebelum clear: bila Save gagal, binding
        // dipulihkan - dulu reset tampak berhasil di sesi ini lalu
        // binding lama "kembali sendiri" saat restart (§45).
        var snapshot = profile.Pages.Select(p =>
            System.Text.Json.JsonSerializer.Serialize(p.Bindings, ConfigStore.JsonOptions)).ToList();
        foreach (var page in profile.Pages)
            page.Bindings.Clear();
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            // D9: catch dulu hanya IOException/UnauthorizedAccess -
            // Save() juga bisa melempar JsonException saat serialisasi
            // config (ConfigStore.Save:54) dan kegagalan itu dulu lolos
            // tanpa rollback: binding hilang di sesi lalu "kembali
            // sendiri" saat restart. Rollback + pesan sama untuk semua
            // jenis kegagalan.
            for (int i = 0; i < profile.Pages.Count; i++)
            {
                var restored = System.Text.Json.JsonSerializer.Deserialize<List<BindingConfig>>(snapshot[i], ConfigStore.JsonOptions);
                if (restored is not null) profile.Pages[i].Bindings.AddRange(restored);
            }
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0388") + ex.Message, "#EF4444", 4.0);
            return;
        }
        // §132: reset menghapus semua kartu halaman - seleksi +
        // snapshot Urungkan milik halaman lama ikut dibersihkan
        // (situs keempat ClearSelectionAndUndo; prune di
        // RefreshBindings di bawah juga membuang seleksi kartu yang
        // sudah tidak ada).
        ClearSelectionAndUndo();
        RefreshBindings();
        Notify(Localizer.Format("S0389", profile.Name), "#F59E0B", 3.5);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        var profile = SelectedProfile;
        if (profile is null) return;
        if (_host.Config.Profiles.Count <= 1)
        {
            Notify(Localizer.Get("S0390"), "#F59E0B", 3.0);
            return;
        }
        if (ConfirmPrompt?.Invoke(Localizer.Format("S0391", profile.Name)) != true)
            return;
        int index = _host.Config.Profiles.IndexOf(profile);
        _host.Config.Profiles.Remove(profile);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Gagal simpan = hapus dibatalkan total (profil kembali),
            // bukan hilang di memori lalu muncul lagi saat restart.
            // D9: catch dulu hanya IOException/UnauthorizedAccess -
            // diperluas ke semua kegagalan Save() (mis. JsonException
            // serialisasi), rollback + pesan sama.
            if (index >= 0) _host.Config.Profiles.Insert(index, profile);
            Notify(Localizer.Get("S0392") + ex.Message, "#EF4444", 4.0);
            return;
        }
        Notify(Localizer.Format("S0393", profile.Name), "#F59E0B", 3.0);
        RefreshProfilesAndPages();
    }

    private static ProfileConfig DuplicateProfile(ProfileConfig source)
    {
        // JSON round-trip copies EVERY persisted field, including the
        // switch-back flag, verbatim (fresh Ids below are the only
        // differences) — no per-field copy list to keep in sync.
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var copy = System.Text.Json.JsonSerializer.Deserialize<ProfileConfig>(json)
                   ?? throw new InvalidOperationException(Localizer.Get("S0394"));
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " " + Localizer.Get("S0912");
        foreach (var page in copy.Pages)
            page.Id = Guid.NewGuid().ToString("N");
        return copy;
    }

    /// <summary>Auto-mode trigger parse, profile semantics verbatim
    /// (ProfileEditViewModel.Save): split on ',' and ';', drop
    /// empties, trim, case-insensitive dedupe. Shared by AddPage and
    /// EditPage so the two paths can never diverge.</summary>
    private static List<string> ParseAutoSwitchExe(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [RelayCommand]
    private void AddPage()
    {
        if (SelectedProfile is null)
        {
            var prof = _host.Config.Profiles.FirstOrDefault();
            if (prof is null)
            {
                prof = new ProfileConfig();
                _host.Config.Profiles.Add(prof);
            }
            _host.Config.ActiveProfileId = prof.Id;
            RefreshProfilesAndPages();
        }
        if (SelectedProfile is null) return;
        var vm = new PageEditViewModel { IsNew = true, Name = Localizer.Format("S0396", Pages.Count + 1), AutoSwitchText = "" };
        RequestEditPage?.Invoke(vm);
        if (!vm.Accepted) return;
        // Pertahanan lapis kedua (modal PageEditWindow sudah menolak nama
        // kosong lewat AppDialog dan tetap terbuka): bila jalur ini
        // tercapai dari pemanggil lain, penolakan tetap ber-info di
        // footer, bukan diam seperti dulu.
        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            Notify(Localizer.Get("S1566"), "#F59E0B", 3.5);
            return;
        }
        var page = new PageConfig { Name = vm.Name.Trim(), AutoSwitchExe = ParseAutoSwitchExe(vm.AutoSwitchText), SwitchBackOnAutoSwitch = vm.SwitchBackOnAutoSwitch };
        SelectedProfile.Pages.Add(page);
        try { _host.Save(); }
        catch (Exception ex)
        {
            SelectedProfile.Pages.Remove(page);
            RefreshPages();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshPages();
        SelectedPage = page;
        Notify(Localizer.Format("S0397", page.Name), "#38BDF8", 3.0);
    }

    [RelayCommand]
    private void EditPage(PageConfig? page)
    {
        page ??= SelectedPage;
        if (page is null) return;
        // Instant tile color, OPT-IN preselect: only when every card
        // already shares one color AND that color is a preset; mixed
        // colors, a custom color, or empty all preselect NOTHING, so
        // Simpan without touching the row repaints nothing. The
        // preset-canonical casing is used so the later apply is a
        // no-op for the preselected case (apply skips equal colors).
        string? initialColor = null;
        if (page.Bindings.Count > 0)
        {
            string first = page.Bindings[0].BackgroundColor;
            if (!string.IsNullOrWhiteSpace(first)
                && ActionCatalog.ColorPresets.Contains(first, StringComparer.OrdinalIgnoreCase)
                && page.Bindings.All(b => string.Equals(b.BackgroundColor, first, StringComparison.OrdinalIgnoreCase)))
                initialColor = ActionCatalog.ColorPresets.First(p => string.Equals(p, first, StringComparison.OrdinalIgnoreCase));
        }
        var vm = new PageEditViewModel { IsNew = false, Name = page.Name, AutoSwitchText = string.Join(", ", page.AutoSwitchExe), BindingCount = page.Bindings.Count, SwitchBackOnAutoSwitch = page.SwitchBackOnAutoSwitch };
        vm.SelectedInstantColor = initialColor;
        RequestEditPage?.Invoke(vm);
        // Duplikat-dari-modal DIPERIKSA DULU, sebelum !Accepted:
        // tombol Duplikat menutup modal TANPA menyimpan, jadi
        // Accepted tetap false di jalur ini — cek sesudahnya tidak
        // akan pernah tercapai. Tombol itu sendiri adalah
        // konfirmasinya, jadi jalur ini memanggil core tanpa prompt.
        if (vm.WantsDuplicate)
        {
            DuplicatePageCore(page, confirm: false);
            return;
        }
        if (!vm.Accepted) return;
        // Pertahanan lapis kedua, sama seperti AddPage: penolakan nama
        // kosong ber-info di footer bila modal tidak menahannya.
        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            Notify(Localizer.Get("S1566"), "#F59E0B", 3.5);
            return;
        }
        string oldName = page.Name;
        var oldAutoSwitch = page.AutoSwitchExe.ToList();
        bool oldSwitchBack = page.SwitchBackOnAutoSwitch;
        List<string>? oldColors = null;
        page.Name = vm.Name.Trim();
        page.AutoSwitchExe = ParseAutoSwitchExe(vm.AutoSwitchText);
        page.SwitchBackOnAutoSwitch = vm.SwitchBackOnAutoSwitch;
        // Instant tile color applies ONLY when a swatch is selected
        // (tap or opt-in preselect); null selection paints nothing.
        // Cards already equal to the color are skipped, so Simpan
        // without touching the row is a true no-op even for casing.
        if (vm.SelectedInstantColor is string color)
        {
            oldColors = page.Bindings.Select(b => b.BackgroundColor).ToList();
            foreach (var b in page.Bindings)
                if (!string.Equals(b.BackgroundColor, color, StringComparison.OrdinalIgnoreCase))
                    b.BackgroundColor = color;
        }
        try { _host.Save(); }
        catch (Exception ex)
        {
            page.Name = oldName;
            page.AutoSwitchExe = oldAutoSwitch;
            page.SwitchBackOnAutoSwitch = oldSwitchBack;
            if (oldColors is not null)
                for (int i = 0; i < oldColors.Count; i++)
                    page.Bindings[i].BackgroundColor = oldColors[i];
            RefreshPages();
            SelectedPage = page;
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshPages();
        SelectedPage = page;
        Notify(Localizer.Format("S0398", page.Name), "#38BDF8", 3.0);
    }

    /// <summary>Quick-delete straight from the tile's trash button
    /// (v2.9.17). The grid only ever shows the page being edited, so the
    /// owner page is unambiguous: EffectivePage.</summary>
    [RelayCommand]
    private void DeleteBinding(BindingItemViewModel? item)
    {
        if (item is null || EffectivePage is not { } page) return;
        if (ConfirmPrompt?.Invoke(Localizer.Format("S0399", item.DisplayLabel, item.KeyBadge, page.Name)) != true)
            return;
        int index = page.Bindings.IndexOf(item.Model);
        page.Bindings.Remove(item.Model);
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            // D9: dulu Save tanpa try/catch - gagal = dialog crash DAN
            // Notify sukses tetap menyala di bawahnya (S0400), padahal
            // kartu "kembali sendiri" saat restart. Kini hapus
            // dibatalkan total (kartu kembali di posisi semula) dan
            // kegagalan diberitahu lewat footer.
            if (index >= 0) page.Bindings.Insert(index, item.Model);
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        // §133 M8a: RefreshBindings mem-prune kartu terhapus dari
        // seleksi + me-renumber rapat + keluar-mode-otomatis bila
        // seleksi habis - tanpa menghapus seluruh seleksi. Jalur gagal
        // di bawah juga RefreshBindings, memulihkan flag/angka kartu
        // yang dikembalikan.
        RefreshBindings();
        Notify(Localizer.Format("S0400", item.DisplayLabel), "#F59E0B", 3.0);
    }

    [RelayCommand]
    private void DeletePage(PageConfig? page)
    {
        if (SelectedProfile is null) return;
        page ??= SelectedPage;
        if (page is null) return;
        if (SelectedProfile.Pages.Count == 0)
        {
            return;
        }
        if (ConfirmPrompt?.Invoke(Localizer.Format("S0402", page.Name, page.Bindings.Count)) != true)
            return;
        int index = SelectedProfile.Pages.IndexOf(page);
        SelectedProfile.Pages.Remove(page);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // D9: dulu Save tanpa try/catch - gagal = dialog crash DAN
            // Notify sukses (S0403) tetap menyala, padahal halaman
            // "kembali sendiri" saat restart. Kini halaman kembali di
            // posisi semula (daftar halaman tidak di-refresh: ia masih
            // menampilkan halaman yang dipulihkan) dan kegagalan
            // diberitahu lewat footer.
            if (index >= 0) SelectedProfile.Pages.Insert(index, page);
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        Notify(Localizer.Format("S0403", page.Name), "#F59E0B", 3.0);
        RefreshPages();
    }

    /// <summary>A ready-made starter page mirroring the sample layout the
    /// user approved: numpad tiles for OBS/media plus a few letter macros.
    /// Everything is a normal binding afterwards - clicking a tile opens
    /// the same simple editor (e.g. to fix OBS scene names to match the
    /// user's real scenes).</summary>
    /// <summary>§75: tombol "+ Template" (dulu "Muat Mode Template") kini membuka pemilih
    /// template (MainWindow menangani event ini), bukan langsung memuat
    /// Stream seperti dulu.</summary>
    public event Action? RequestOpenTemplatePicker;

    [RelayCommand]
    private void OpenTemplatePicker() => RequestOpenTemplatePicker?.Invoke();

    /// <summary>§98: badge versi di footer membuka modal About
    /// (MainWindow menangani event ini).</summary>
    public event Action? RequestOpenAbout;

    [RelayCommand]
    private void OpenAbout() => RequestOpenAbout?.Invoke();

    /// <summary>§75: memuat satu template sebagai halaman baru di profil
    /// aktif. §76: aturan tolak-duplikat QC DICABUT atas perintah user
    /// (02:37 WIB) - memuat template yang sama berkali-kali diizinkan,
    /// karena halaman adalah data profil biasa dan duplikatnya tidak
    /// merusak apa-apa. Agar halaman kembar tetap bisa dibedakan di
    /// daftar halaman, nama halaman baru diberi akhiran otomatis
    /// "(2)", "(3)", dst. bila nama defaultnya sudah dipakai.
    /// Cap TemplateId tetap dipasang (berguna untuk penanda asal).
    /// §76 juga: sesudah halaman tampil, ikon otomatis kartu
    /// launcher/link ikut di-fetch (favicon/ikon exe) seperti yang
    /// dilakukan editor saat save - kartu template sebelumnya TIDAK
    /// PERNAH lewat editor, jadi AutoIcon-nya kosong dan tile selalu
    /// menampilkan ikon katalog (mis. YouTube = "media") sampai user
    /// buka kartu + save ulang.</summary>
    public async Task LoadTemplatePageAsync(string templateId)
    {
        if (SelectedProfile is null) return;
        var page = templateId switch
        {
            "media" => BuildMediaPage(),
            "produktivitas" => BuildProductivityPage(),
            "navigasi" => BuildNavigationPage(),
            "browser" => BuildBrowserPage(),
            "aplikasi" => BuildAplikasiPage(),
            "youtube" => BuildYouTubePage(),
            "stream" => BuildStreamPage(),
            "mix" => BuildMixPage(),
            _ => null,
        };
        if (page is null) return;
        // §170 Task 4: anti-bentrok - sesuaikan kunci template ke tombol
        // kosong (hanya Global + nav yang memblokir; halaman baru selalu
        // kosong, jadi kartu halaman lain tidak ikut dihitung). Kartu yang
        // sudah ada tidak pernah disentuh; kartu template tidak pernah dibuang.
        var blocked = new HashSet<(ushort, bool, byte)>();
        foreach (var nav in SelectedProfile.NavBindings)
            if (nav.IsActive && nav.ScanCode != 0) blocked.Add((nav.ScanCode, nav.IsE0, nav.ChordMask));
        if (_host.Config.Settings.IsGlobalEnabled)
            foreach (var g in _host.Config.GlobalPage.Bindings)
                if (g.IsActive && g.ScanCode != 0) blocked.Add((g.ScanCode, g.IsE0, g.ChordMask));
        var remaps = TemplateKeyFit.FitKeys(page, blocked);
        if (SelectedProfile.Pages.Any(p => p.Name == page.Name))
        {
            int n = 2;
            while (SelectedProfile.Pages.Any(p => p.Name == $"{page.Name} ({n})")) n++;
            page.Name = $"{page.Name} ({n})";
        }
        SelectedProfile.Pages.Add(page);
        try { _host.Save(); }
        catch (Exception ex)
        {
            // Silent-save fix (jalur template): dulu Save tanpa try/catch
            // - gagal simpan melempar keluar Task async tanpa pesan dan
            // halaman sudah telanjur masuk memori. Kini: rollback +
            // pesan footer, tanpa Notify sukses.
            SelectedProfile.Pages.Remove(page);
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshPages();
        SelectedPage = page;
        if (remaps.Count == 0)
            Notify(Localizer.Format("S0405", page.Name), "#10B981", 3.5);
        else
            Notify(Localizer.Format("S1811", page.Name, string.Join(", ", remaps.Select(r => $"{r.Label}: {r.FromKey}→{r.ToKey}"))), "#10B981", 4.0);
        try { await RefreshTemplateIconsAsync(page); }
        catch { /* ikon latar tidak boleh merusak pemuatan halaman */ }
    }

    /// <summary>§76: fetch ikon otomatis untuk kartu launcher/link di
    /// halaman template - cerminan logika editor (BindingEditorViewModel
    /// FetchLinkIconAsync/FetchAppIconAsync): URL → favicon langsung;
    /// exe → ekstrak ikon, gagal → favicon domain tebakan. Dihormati:
    /// setting AutoFetchIcons (mati = tidak fetch) dan upload user
    /// (ikon file memblokir fetch). Halaman sudah tampil dulu dengan
    /// ikon katalog; begitu ada favicon yang berhasil diimpor,
    /// AutoIcon ditulis + Save + RefreshBindings agar tile berubah
    /// tanpa buka kartu + save ulang. Gagal fetch = ikon katalog
    /// bertahan diam-diam (bukan error).</summary>
    private async Task RefreshTemplateIconsAsync(PageConfig page)
    {
        if (!_host.Config.Settings.AutoFetchIcons) return;
        long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
        bool changed = false;
        foreach (var b in page.Bindings)
        {
            var step = b.Steps.FirstOrDefault();
            if (step is null) continue;
            string? target = step.ActionId switch
            {
                "link" => ParamReader.GetString(step.Params, "url", "Url", "target", "Target"),
                "launcher" => ParamReader.GetString(step.Params, "target", "Target"),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(target) || !string.IsNullOrEmpty(b.AutoIcon)) continue;
            if (IconReference.IsFileIcon(b.Icon)) continue;
            try
            {
                string? iconValue = null;
                if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // §171: cache-first - pakai ikon tersimpan bila ada,
                    // unduh hanya bila belum ada di disk.
                    iconValue = _host.IconStore.TryGetCachedFavicon(target);
                    if (iconValue is null)
                    {
                        byte[]? data = await Services.FaviconFetcher.FetchAsync(target);
                        if (data is not null)
                        {
                            iconValue = _host.IconStore.ImportBytes(data, maxBytes).IconValue;
                            _host.IconStore.SetCachedFavicon(target, iconValue);
                        }
                    }
                }
                else
                {
                    var bitmap = Services.AppIconExtractor.Extract(target);
                    if (bitmap is not null)
                    {
                        iconValue = _host.IconStore.ImportBitmap(bitmap, 0, maxBytes).IconValue;
                    }
                    else if (IconReference.GuessDomain(target) is string domain)
                    {
                        // §171: cache-first juga untuk fallback favicon
                        // dari nama domain.
                        iconValue = _host.IconStore.TryGetCachedFavicon("https://" + domain);
                        if (iconValue is null
                            && await Services.FaviconFetcher.FetchAsync("https://" + domain) is byte[] web)
                        {
                            iconValue = _host.IconStore.ImportBytes(web, maxBytes).IconValue;
                            _host.IconStore.SetCachedFavicon("https://" + domain, iconValue);
                        }
                    }
                }
                if (iconValue is not null)
                {
                    b.AutoIcon = iconValue;
                    changed = true;
                }
            }
            catch { /* satu kartu gagal fetch tidak menggagalkan yang lain */ }
        }
        if (changed)
        {
            _host.Save();
            // RefreshBindings saja (bukan RefreshPages): tile dibangun
            // ulang dari halaman terpilih tanpa memindahkan SelectedPage
            // (RefreshPages mereset SelectedPage ke ActivePageId).
            RefreshBindings();
        }
    }

    // Pembangun kartu template: B = tap, B2 = trigger eksplisit. Dulu
    // fungsi lokal di LoadSamplePage; diangkat ke metode statis agar
    // keempat builder template memakainya tanpa duplikasi.
    private static BindingConfig B(string keyName, ushort scan, string label, string icon, string color,
        string actionId, params (string Key, string Value)[] ps) => B2(keyName, scan, label, icon, color, actionId, TriggerKind.Tap, ps);

    private static BindingConfig B2(string keyName, ushort scan, string label, string icon, string color,
        string actionId, TriggerKind trigger, params (string Key, string Value)[] ps) => new()
        {
            KeyId = $"K-{scan:X2}",
            KeyName = keyName,
            ScanCode = scan,
            Label = label,
            Icon = icon,
            BackgroundColor = color,
            Trigger = trigger,
            Steps =
            {
                new ActionStep
                {
                    ActionId = actionId,
                    Params = new System.Text.Json.Nodes.JsonObject(
                        ps.Select(p => new KeyValuePair<string, System.Text.Json.Nodes.JsonNode?>(
                            p.Key, System.Text.Json.Nodes.JsonValue.Create(p.Value)))),
                },
            },
        };

    // §75 Gelombang 1: Template Media (8 kartu, media key sistem global).
    private static PageConfig BuildMediaPage() => new()
    {
        Name = Localizer.Get("S0406"),
        TemplateId = "media",
        Bindings =
        {
            B("P", 0x19, Localizer.Get("S0408"), "play-pause", "#16324A", "media", ("command", "PlayPause")),
            B("N", 0x31, Localizer.Get("S0410"), "next", "#16324A", "media", ("command", "Next")),
            B("V", 0x2F, Localizer.Get("S0412"), "prev", "#16324A", "media", ("command", "Prev")),
            B("S", 0x1F, Localizer.Get("S0414"), "stop", "#16324A", "media", ("command", "Stop")),
            B2("U", 0x16, Localizer.Get("S0416"), "volume", "#16324A", "media", TriggerKind.Repeat, ("command", "VolumeUp")),
            B2("D", 0x20, Localizer.Get("S0418"), "volume-low", "#16324A", "media", TriggerKind.Repeat, ("command", "VolumeDown")),
            B("X", 0x2D, Localizer.Get("S0420"), "volume-x", "#16324A", "media", ("command", "Mute")),
            B("M", 0x32, Localizer.Get("S0422"), "mic", "#16324A", "media", ("command", "MicMute")),
        },
    };

    // §75 Gelombang 1: Template Produktivitas (11 kartu, aksi basic -
    // perintah semantik, bukan chord mentah).
    private static PageConfig BuildProductivityPage() => new()
    {
        Name = Localizer.Get("S0423"),
        TemplateId = "produktivitas",
        Bindings =
        {
            B("X", 0x2D, Localizer.Get("S0424"), "scissors", "#1E3B2A", "basic", ("command", "Cut")),
            B("C", 0x2E, Localizer.Get("S0425"), "copy", "#1E3B2A", "basic", ("command", "Copy")),
            B("V", 0x2F, Localizer.Get("S0427"), "paste", "#1E3B2A", "basic", ("command", "Paste")),
            B("U", 0x16, Localizer.Get("S0428"), "undo", "#1E3B2A", "basic", ("command", "Undo")),
            B("R", 0x13, Localizer.Get("S0429"), "redo", "#1E3B2A", "basic", ("command", "Redo")),
            B("A", 0x1E, Localizer.Get("S0430"), "grid", "#1E3B2A", "basic", ("command", "All")),
            B("F", 0x21, Localizer.Get("S0431"), "search", "#1E3B2A", "basic", ("command", "Find")),
            B("S", 0x1F, Localizer.Get("S0123"), "save", "#1E3B2A", "basic", ("command", "Save")),
            B("N", 0x31, Localizer.Get("S0182"), "plus", "#1E3B2A", "basic", ("command", "New")),
            B("P", 0x19, Localizer.Get("S0432"), "printer", "#1E3B2A", "basic", ("command", "Print")),
            B("W", 0x11, Localizer.Get("S0434"), "globe", "#1E3B2A", "basic", ("command", "WebSearch")),
        },
    };

    // §75 Gelombang 1: Template Navigasi & Jendela (12 kartu, perintah
    // level OS - nama command dinormalisasi case-insensitif oleh
    // WindowsAction, jadi "LockPC" cocok dengan LockCommand "lockpc").
    private static PageConfig BuildNavigationPage() => new()
    {
        Name = Localizer.Get("S0435"),
        TemplateId = "navigasi",
        Bindings =
        {
            B("W", 0x11, Localizer.Get("S0436"), "layers", "#3B2A17", "windows", ("command", "SwitchWindows")),
            B("B", 0x30, Localizer.Get("S0437"), "layers", "#3B2A17", "windows", ("command", "SwitchWindowsBack")),
            B("T", 0x14, Localizer.Get("S0438"), "apps", "#3B2A17", "windows", ("command", "TaskView")),
            B("X", 0x2D, Localizer.Get("S0439"), "x", "#3B2A17", "windows", ("command", "CloseWindow")),
            B("M", 0x32, Localizer.Get("S0440"), "monitor", "#3B2A17", "windows", ("command", "Maximize")),
            B("I", 0x17, Localizer.Get("S0441"), "device", "#3B2A17", "windows", ("command", "Minimize")),
            B("L", 0x26, Localizer.Get("S0442"), "prev", "#3B2A17", "windows", ("command", "SnapLeft")),
            B("R", 0x13, Localizer.Get("S0443"), "next", "#3B2A17", "windows", ("command", "SnapRight")),
            B("D", 0x20, Localizer.Get("S0444"), "eye", "#3B2A17", "windows", ("command", "ShowDesktop")),
            B("K", 0x25, Localizer.Get("S0445"), "lock", "#3B2A17", "windows", ("command", "LockPC")),
            B("N", 0x31, Localizer.Get("S0446"), "next", "#3B2A17", "windows", ("command", "NextDesktop")),
            B("Enter", 0x1C, Localizer.Get("S0447"), "grid", "#3B2A17", "deck", ("command", "home")),
        },
    };

    // Template Stream: ISI TIDAK DIUBAH di Gelombang 1 (revisi isinya -
    // hapus Macro F13/14/15, perbaiki label/ikon - adalah Gelombang 2).
    // Yang berubah hanya: halaman kini dicap TemplateId "stream".
    // §169: Template Browser DIBANGUN ULANG persis Gambar 1 (29 Okt 2026,
    // 15 kartu situs, menggantikan isi lama 12 kartu): SEMUA kartu
    // "launcher" + target https://domain TANPA path (DetailText =
    // ShortTarget = domain persis seperti subtitle gambar); kunci =
    // huruf keycap gambar (scancode set-1); label = string S1785-S1799
    // (teks gambar persis di kedua bahasa, sengaja); warna = hasil
    // sampling Gambar 1 (inversi gradien HexGradient: atas = warna
    // dasar, bawah = 60% - fit linear per tile); ikon = kunci katalog
    // terdekat (youtube/whatsapp/telegram/spotify asli; Discord =
    // "message" sesuai preseden §79; sisanya generik) + favicon asli
    // via §76 bila online. Template lain TIDAK disentuh. Halaman
    // Browser lama yang sudah dimuat TIDAK ikut berubah (tidak ada
    // migrasi) - hapus + muat ulang untuk isi baru.
    private static PageConfig BuildBrowserPage() => new()
    {
        Name = Localizer.Get("S0448"),
        TemplateId = "browser",
        Bindings =
        {
            B("Y", 0x15, Localizer.Get("S1785"), "youtube", "#D92128", "launcher", ("target", "https://youtube.com")),
            B("D", 0x20, Localizer.Get("S1786"), "message", "#2556C8", "launcher", ("target", "https://discord.com")),
            B("W", 0x11, Localizer.Get("S1787"), "whatsapp", "#20A84B", "launcher", ("target", "https://web.whatsapp.com")),
            B("N", 0x31, Localizer.Get("S1788"), "tv", "#FF8001", "launcher", ("target", "https://www.netflix.com")),
            B("X", 0x2D, Localizer.Get("S1789"), "x", "#2B303A", "launcher", ("target", "https://x.com")),
            B("G", 0x22, Localizer.Get("S1790"), "code", "#808080", "launcher", ("target", "https://github.com")),
            B("F", 0x21, Localizer.Get("S1791"), "globe", "#3090E0", "launcher", ("target", "https://www.facebook.com")),
            B("T", 0x14, Localizer.Get("S1792"), "telegram", "#070779", "launcher", ("target", "https://web.telegram.org")),
            B("I", 0x17, Localizer.Get("S1793"), "camera", "#FF80FF", "launcher", ("target", "https://www.instagram.com")),
            B("S", 0x1F, Localizer.Get("S1794"), "spotify", "#018041", "launcher", ("target", "https://open.spotify.com")),
            B("P", 0x19, Localizer.Get("S1795"), "pin", "#804101", "launcher", ("target", "https://id.pinterest.com")),
            B("E", 0x12, Localizer.Get("S1796"), "mail", "#80FFFF", "launcher", ("target", "https://mail.google.com")),
            B("L", 0x26, Localizer.Get("S1797"), "user", "#FF8041", "launcher", ("target", "https://id.linkedin.com")),
            B("R", 0x13, Localizer.Get("S1798"), "message", "#FDCC07", "launcher", ("target", "https://www.reddit.com")),
            B("A", 0x1E, Localizer.Get("S1799"), "grid", "#018080", "launcher", ("target", "https://www.amazon.com")),
        },
    };

    // §79 Gelombang 2: Template Aplikasi (11 kartu, proposal §74).
    // §170: Template Aplikasi menjadi "Windows Tools" (15 kartu
    // perkakas bawaan Windows, kunci huruf + warna biru bersiklus).
    // Kartu web lama (WhatsApp/Telegram) sudah ada di template Browser.
    // Halaman Aplikasi lama yang sudah dimuat TIDAK ikut berubah.
    private static PageConfig BuildAplikasiPage() => new()
    {
        Name = Localizer.Get("S0467"),
        TemplateId = "aplikasi",
        Bindings =
        {
            B("E", 0x12, Localizer.Get("S0468"), "folder", "#1B5FAA", "windows", ("command", "Explorer")),
            B("N", 0x31, Localizer.Get("S0470"), "edit", "#2E75C8", "launcher", ("target", "notepad.exe")),
            B("C", 0x2E, Localizer.Get("S1800"), "grid", "#14467E", "launcher", ("target", "calc.exe")),
            B("P", 0x19, Localizer.Get("S0472"), "image", "#1B5FAA", "launcher", ("target", "mspaint.exe")),
            B("S", 0x1F, Localizer.Get("S1801"), "camera", "#2E75C8", "launcher", ("target", "snippingtool.exe")),
            B("T", 0x14, Localizer.Get("S0473"), "sliders", "#14467E", "windows", ("command", "TaskManager")),
            B("G", 0x22, Localizer.Get("S0230"), "gear", "#1B5FAA", "windows", ("command", "Settings")),
            B("R", 0x13, Localizer.Get("S1802"), "code", "#2E75C8", "launcher", ("target", "regedit.exe")),
            B("D", 0x20, Localizer.Get("S1803"), "device", "#14467E", "launcher", ("target", "devmgmt.msc")),
            B("O", 0x18, Localizer.Get("S1804"), "code", "#1B5FAA", "launcher", ("target", "cmd.exe")),
            B("W", 0x11, Localizer.Get("S1805"), "code", "#2E75C8", "launcher", ("target", "powershell.exe")),
            B("M", 0x32, Localizer.Get("S1806"), "search", "#14467E", "launcher", ("target", "magnify.exe")),
            B("A", 0x1E, Localizer.Get("S1807"), "grid", "#1B5FAA", "launcher", ("target", "charmap.exe")),
            B("K", 0x25, Localizer.Get("S1808"), "keyboard", "#2E75C8", "launcher", ("target", "osk.exe")),
            B("L", 0x26, Localizer.Get("S1809"), "sliders", "#14467E", "launcher", ("target", "control.exe")),
        },
    };

    // §79 Gelombang 2: Template YouTube (11 kartu, proposal §74).
    private static PageConfig BuildYouTubePage() => new()
    {
        Name = Localizer.Get("S0477"),
        TemplateId = "youtube",
        Bindings =
        {
            B("Y", 0x15, Localizer.Get("S0449"), "youtube", "#471512", "launcher", ("target", "https://youtube.com")),
            B("K", 0x25, Localizer.Get("S0408"), "play-pause", "#471512", "hotkey", ("keys", "K")),
            B("J", 0x24, Localizer.Get("S0478"), "prev", "#471512", "hotkey", ("keys", "J")),
            B("L", 0x26, Localizer.Get("S0479"), "next", "#471512", "hotkey", ("keys", "L")),
            B("F", 0x21, Localizer.Get("S0480"), "monitor", "#471512", "hotkey", ("keys", "F")),
            B("M", 0x32, Localizer.Get("S0481"), "volume-x", "#471512", "hotkey", ("keys", "M")),
            B("U", 0x16, Localizer.Get("S0416"), "volume", "#471512", "hotkey", ("keys", Localizer.Get("S0482"))),
            B("D", 0x20, Localizer.Get("S0418"), "volume-low", "#471512", "hotkey", ("keys", Localizer.Get("S0483"))),
            B("C", 0x2E, Localizer.Get("S0484"), "message", "#471512", "hotkey", ("keys", "C")),
            B("0", 0x0B, Localizer.Get("S0485"), "refresh", "#471512", "hotkey", ("keys", "0")),
            B("T", 0x14, Localizer.Get("S0487"), "video", "#471512", "hotkey", ("keys", "T")),
        },
    };

    // §79 Gelombang 2: Template Stream REVISI (12 kartu, menggantikan
    // isi lama 15 kartu). Perbaikan QC: Macro F13/F14/F15 DIHAPUS
    // (label bohong + tombol mati); "Mute All" → "Bisukan Speaker";
    // label Inggris → Indonesia; Scene 2 "layers" → "scene";
    // "Scene 1/2" adalah PLACEHOLDER - deskripsi pemilih mewajibkan
    // user menyesuaikan dulu (scene default OBS adalah "Scene").
    // Halaman Stream lama yang sudah dimuat TIDAK ikut berubah
    // (tidak ada migrasi) - hapus + muat ulang untuk isi revisi.
    private static PageConfig BuildStreamPage() => new()
    {
        Name = Localizer.Get("S0489"),
        TemplateId = "stream",
        Bindings =
        {
            B("1", 0x02, Localizer.Get("S0490"), "obs", "#1A1E28", "obs", ("command", "SwitchScene"), ("sceneName", Localizer.Get("S0491"))),
            B("2", 0x03, Localizer.Get("S0492"), "scene", "#1A1E28", "obs", ("command", "SwitchScene"), ("sceneName", Localizer.Get("S0493"))),
            B("R", 0x13, Localizer.Get("S0494"), "record", "#1A1E28", "obs", ("command", "ToggleRecord")),
            B("M", 0x32, Localizer.Get("S0422"), "mic", "#1A1E28", "media", ("command", "MicMute")),
            B("P", 0x19, Localizer.Get("S0408"), "play-pause", "#1A1E28", "media", ("command", "PlayPause")),
            B("N", 0x31, Localizer.Get("S0410"), "next", "#1A1E28", "media", ("command", "Next")),
            B2("D", 0x20, Localizer.Get("S0418"), "volume-low", "#1A1E28", "media", TriggerKind.Repeat, ("command", "VolumeDown")),
            B2("U", 0x16, Localizer.Get("S0416"), "volume", "#1A1E28", "media", TriggerKind.Repeat, ("command", "VolumeUp")),
            B("X", 0x2D, Localizer.Get("S0420"), "volume-x", "#1A1E28", "media", ("command", "Mute")),
            B("S", 0x1F, Localizer.Get("S0495"), "spotify", "#1A1E28", "launcher", ("target", "https://open.spotify.com")),
            B("Y", 0x15, Localizer.Get("S0449"), "youtube", "#1A1E28", "launcher", ("target", "https://youtube.com")),
            B("Enter", 0x1C, Localizer.Get("S0450"), "message", "#1A1E28", "launcher", ("target", "https://discord.com/app")),
        },
    };

    // §80: Template Mix (12 kartu LINTAS kategori - bukan 1 kategori
    // spesifik). Isi = kartu harian yang paling sering dipakai dari
    // tiap kategori, pola sama seperti template sebelumnya (builder +
    // TemplateId + kunci numpad + verifikasi command/ikon). Warna
    // §170: #35322C batu hangat gelap (bukan ungu) + kunci huruf.
    private static PageConfig BuildMixPage() => new()
    {
        Name = Localizer.Get("S0496"),
        TemplateId = "mix",
        Bindings =
        {
            B("P", 0x19, Localizer.Get("S0408"), "play-pause", "#35322C", "media", ("command", "PlayPause")),
            B("Y", 0x15, Localizer.Get("S0449"), "youtube", "#35322C", "launcher", ("target", "https://youtube.com")),
            B("M", 0x32, Localizer.Get("S0422"), "mic", "#35322C", "media", ("command", "MicMute")),
            B("C", 0x2E, Localizer.Get("S0425"), "copy", "#35322C", "basic", ("command", "Copy")),
            B("V", 0x2F, Localizer.Get("S0427"), "paste", "#35322C", "basic", ("command", "Paste")),
            B("W", 0x11, Localizer.Get("S0436"), "layers", "#35322C", "windows", ("command", "SwitchWindows")),
            B2("U", 0x16, Localizer.Get("S0416"), "volume", "#35322C", "media", TriggerKind.Repeat, ("command", "VolumeUp")),
            B2("D", 0x20, Localizer.Get("S0418"), "volume-low", "#35322C", "media", TriggerKind.Repeat, ("command", "VolumeDown")),
            B("X", 0x2D, Localizer.Get("S0420"), "volume-x", "#35322C", "media", ("command", "Mute")),
            B("K", 0x25, Localizer.Get("S0445"), "lock", "#35322C", "windows", ("command", "LockPC")),
            B("N", 0x31, Localizer.Get("S0470"), "edit", "#35322C", "launcher", ("target", "notepad.exe")),
            B("Enter", 0x1C, Localizer.Get("S0447"), "grid", "#35322C", "deck", ("command", "home")),
        },
    };

    // --- tiles -------------------------------------------------------------------

    /// <summary>§132: opens the editor for one card: single click
    /// (outside Mode Pilih) and menu Buka (in or out of the mode).
    /// The command itself never touches the selection - clearing
    /// after a left-click editor open is the code-behind's job
    /// (Tile_MouseLeftButtonDown), and RefreshBindings after the
    /// editor closes re-applies selection+numbers+mode.</summary>
    [RelayCommand]
    private void OpenEditor(BindingItemViewModel? item)
    {
        if (item is null || EffectivePage is null) return;
        RequestEditBinding?.Invoke(new BindingEditorViewModel(_host, EffectivePage, item.Model));
    }

    // --- §132: selection + card menu (§133 M: Mode Pilih kembali) ----------

    private readonly TileSelection _selection = new();
    private readonly BulkDeleteUndoStore _undoStore = new();

    // --- §134 A1: snapshot "Pilih semua" -------------------------------
    // _selectAllSnapshot = ordered selection as it was BEFORE the
    // last SelectAllVisible press; _selectAllActive = a snapshot is
    // live. Restoring replaces the selection with the snapshot
    // (filtered to cards still on the page) and stays in Mode
    // Pilih. Clearing rule (simplest correct set): cleared by
    // ClearSelection (which covers ExitSelectMode, context
    // switches via ClearSelectionAndUndo, and the §134 A2
    // menu-close clear) and by every manual single-card mutation
    // (SelectCard / ToggleSelection - a manual toggle after
    // select-all voids the snapshot, label back to "Pilih
    // semua"). RefreshBindings PRUNES the snapshot list alongside
    // _selection instead of clearing the flag: a deleted card
    // drops out of both, the label check (SelectionCoversVisible)
    // and the restore filter stay correct, and a snapshot taken
    // on another page is invalid because every context switch
    // goes through ClearSelectionAndUndo. EnterSelectMode never
    // touches the snapshot (the right-click prelude already
    // cleared it when the selection actually changed; the
    // already-selected no-op prelude leaves both unchanged).
    private List<BindingConfig>? _selectAllSnapshot;
    private bool _selectAllActive;

    private void ClearSelectAllSnapshot()
    {
        _selectAllSnapshot = null;
        _selectAllActive = false;
    }

    /// <summary>§134 A1: true while the bar's select-all button is
    /// in its cancel-all state - a snapshot is live AND the current
    /// selection still covers every visible card. The label follows
    /// this, not the bare flag, so a filter change that exposes an
    /// unselected card flips the button back to "Pilih semua"
    /// (pressing it then takes a FRESH snapshot) without voiding
    /// the stored snapshot.</summary>
    private bool IsSelectAllCancelState => _selectAllActive
        && Selection134Logic.SelectionCoversVisible(
            _selection.Snapshot(), Bindings.Select(i => i.Model));

    /// <summary>§134 A1: bar button label - S1713 "Batalkan semua"
    /// in the cancel-all state, else S1710 "Pilih semua". S1696
    /// ("Pilih semua di halaman ini") is NOT used: it is an
    /// orphan string with no call site in §133, and the bar's
    /// established short label is S1710. Localizer.Get reads the
    /// active language at get time; refresh rides
    /// RaiseSelectionDerivedProperties (every selection change),
    /// OnLanguageChanged (string.Empty), and
    /// ApplyDashboardSearchFilter (visible set changes).</summary>
    public string SelectAllButtonText => Localizer.Get(
        IsSelectAllCancelState ? "S1713" : "S1710");

    public bool HasSelection => SelectedCount > 0;
    public string SelectedCountText => Localizer.Format("S1638", SelectedCount);

    /// <summary>§153 Part A (verdict user 21:14 WIB 28 Okt 2026;
    /// menggantikan aturan ANY §132 U4 dan M6 §133): if the MAJORITY
    /// of selected cards is active (active &gt; inactive, or a TIE)
    /// the toggle deactivates ALL; if the majority is inactive it
    /// activates ALL. The label follows the action that will run;
    /// a tie deactivates (safe direction: activating cards could fire
    /// unexpected macros). Empty selection reads MajorityActive()=true
    /// (0&gt;=0, a tie) but is never acted on - ToggleSelectedActive
    /// guards the empty case and MenuToggleText falls back to the
    /// clicked card. §133: label menu Nonaktifkan/Aktifkan untuk
    /// seleksi.</summary>
    public string BulkToggleText => Localizer.Get(
        _selection.MajorityActive() ? "S1691" : "S1692");

    /// <summary>§133 F01: label item toggle di menu, dihitung dari
    /// HIMPUNAN EFEKTIF saat menu dibangun - SATU tempat ini, dipakai
    /// konstruksi menu (MainWindow.xaml.cs). Bila ada seleksi, pakai
    /// BulkToggleText (aturan mayoritas §153 Part A pada seleksi); bila
    /// seleksi kosong (mis. Ctrl+klik-kanan men-toggle-off satu-satunya
    /// kartu terseleksi), label mengikuti keadaan kartu yang diklik:
    /// aktif -&gt; S1691 "Nonaktifkan", nonaktif -&gt; S1692 "Aktifkan".
    /// Tanpa ini, seleksi kosong membaca MajorityActive()=true dan
    /// selalu berlabel "Nonaktifkan" padahal handler fallback bisa
    /// mengaktifkan kartu nonaktif yang diklik. Model.IsActive adalah
    /// sumber kebenaran
    /// (passthrough IsActive tile bisa basi bila tile belum di-refresh).</summary>
    public string MenuToggleText(BindingItemViewModel? clicked)
    {
        if (HasSelection) return BulkToggleText;
        if (clicked is null) return BulkToggleText;
        return Localizer.Get(clicked.Model.IsActive ? "S1691" : "S1692");
    }

    /// <summary>§132: "Urungkan" shows only while a bulk-delete
    /// snapshot is restorable in this session context. §133: tanpa UI
    /// Urungkan - snapshot tetap ada sebagai data dan dibersihkan
    /// hanya oleh ClearSelectionAndUndo.</summary>
    public bool CanRestoreUndo => _undoStore.CanRestore;

    [ObservableProperty] private int _selectedCount;
    partial void OnSelectedCountChanged(int value) => RaiseSelectionDerivedProperties();

    private void RaiseSelectionDerivedProperties()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(BulkToggleText));
        OnPropertyChanged(nameof(CanRestoreUndo));
        OnPropertyChanged(nameof(IsSelectMode));
        OnPropertyChanged(nameof(SelectAllButtonText));
    }

    // --- §133 M1: Mode Pilih -------------------------------------------
    // Flag mode TERPISAH dari HasSelection: mode bisa aktif dengan
    // seleksi yang menyusut, dan keluar otomatis begitu seleksi
    // menjadi kosong (ExitModeIfEmpty, lewat SyncSelectionFlags).
    // Arah pemanggilan TUNGGAL: ExitSelectMode mematikan flag DULU
    // lalu mendelegasikan ke ClearSelection; ClearSelection sendiri
    // TIDAK pernah menyentuh flag secara langsung - keluar-mode
    // darinya hanyalah konsekuensi SyncSelectionFlags bila mode
    // aktif dan seleksi kini kosong.
    private bool _selectModeEntered;

    /// <summary>§133 M1: true while Mode Pilih is active. Backed by
    /// _selectModeEntered; raised in RaiseSelectionDerivedProperties
    /// (plus here on change) so the bar Visibility binding follows.</summary>
    public bool IsSelectMode
    {
        get => _selectModeEntered;
        private set
        {
            if (_selectModeEntered == value) return;
            _selectModeEntered = value;
            OnPropertyChanged(nameof(IsSelectMode));
        }
    }

    /// <summary>§133 M1: masuk Mode Pilih. Sudah dalam mode = no-op
    /// (seleksi tidak diubah). Seleksi dipertahankan apa adanya.
    /// Sengaja TIDAK memanggil SyncSelectionFlags: jalur abnormal
    /// (Enter dengan seleksi kosong) harus memilih kartu DULU di
    /// handler menu "Pilih" - auto-exit di Sync akan langsung
    /// mematikan mode yang baru dinyalakan bila dipanggil di sini.</summary>
    public void EnterSelectMode()
    {
        if (IsSelectMode) return;
        IsSelectMode = true;
        ApplySelectionFlags();
        RaiseSelectionDerivedProperties();
    }

    /// <summary>§133 M1/M7: keluar Mode Pilih = flag off + hapus
    /// seleksi (+ renumber, via ClearSelection). Dipakai Batal (bar),
    /// Esc, klik area kosong, dan jalur sukses Salin/Pindah/Hapus.
    /// Di luar mode pun aman dipanggil: flag sudah off, seleksi
    /// tetap dihapus (semantik Esc/area-kosong lama). [RelayCommand]
    /// menghasilkan ExitSelectModeCommand yang diikat bar (XAML).</summary>
    [RelayCommand]
    public void ExitSelectMode()
    {
        IsSelectMode = false;
        ClearSelection();
    }

    /// <summary>§133 M1: keluar mode otomatis bila mode aktif dan
    /// seleksi menjadi kosong sesudah sebuah mutasi. Hanya dipanggil
    /// dari SyncSelectionFlags - tidak dari EnterSelectMode.</summary>
    private void ExitModeIfEmpty()
    {
        if (!IsSelectMode || _selection.Count > 0) return;
        IsSelectMode = false;
        // ShowSelectionNumber sudah false untuk semua tile: tidak ada
        // tile terseleksi, jadi Apply ulang tidak mengubah apa pun.
        RaiseSelectionDerivedProperties();
    }

    /// <summary>Penjaga hapus massal yang SUDAH ADA: pemetaan (capture
    /// armed) atau sesi perekaman/playback panel (FloatPanelSession).
    /// Tidak ada penjaga baru - TileActionBarLogic hanya meng-OR-kan
    /// keduanya. §132: penjaga yang sama menutup pembukaan menu
    /// klik-kanan kartu.</summary>
    public bool BulkDeleteBlocked =>
        TileActionBarLogic.BulkDeleteBlocked(IsCaptureArmed, _host.FloatPanel.SessionActive);

    /// <summary>§132: single click on a card. Plain click REPLACES the
    /// selection with that card; Ctrl+click toggles membership
    /// (Ctrl+click on the sole selected card leaves an empty
    /// selection). §133: tidak lagi dipanggil dari klik kiri (klik
    // kiri membuka editor di luar mode, toggle di dalam mode lewat
    // ToggleSelection); dipakai sebagai inti oleh
    /// SelectForContextMenu. The modifier is read here, at event
    /// time, on the UI thread.</summary>
    [RelayCommand]
    private void SelectCard(BindingItemViewModel? item)
    {
        if (item is null) return;
        // §134 A1: a manual single-card mutation voids the
        // select-all snapshot (right-click prelude included).
        ClearSelectAllSnapshot();
        bool ctrl = (System.Windows.Input.Keyboard.Modifiers
                     & System.Windows.Input.ModifierKeys.Control) != 0;
        if (ctrl) _selection.Toggle(item.Model);
        else _selection.Replace(item.Model);
        SyncSelectionFlags();
    }

    /// <summary>§133 M4: toggle seleksi satu kartu untuk klik kiri
    /// DALAM Mode Pilih - tanpa membaca Ctrl: klik kiri menambah
    /// kartu ke seleksi (append di akhir urutan) atau membatalkannya.
    /// Keluar-mode-otomatis bila ini mengosongkan seleksi, lewat
    /// SyncSelectionFlags.</summary>
    public void ToggleSelection(BindingItemViewModel? item)
    {
        if (item is null) return;
        // §134 A1: manual toggle voids the select-all snapshot -
        // the label returns to "Pilih semua" (via Sync below) even
        // though the selection may still cover the visible set.
        ClearSelectAllSnapshot();
        _selection.Toggle(item.Model);
        SyncSelectionFlags();
    }

    /// <summary>§133: prelude seleksi klik kanan, dipanggil SEBELUM
    /// menu konteks dibuka. Ctrl = toggle kartu diklik (via
    /// SelectCard); tanpa Ctrl + kartu belum terseleksi = ganti
    /// seleksi dengan kartu itu (via SelectCard); kartu sudah
    /// terseleksi = seleksi dipertahankan (tidak ada panggilan).
    /// Mode dipertahankan: mutasi seleksi lewat SyncSelectionFlags
    /// hanya keluar-mode-otomatis bila seleksi menjadi kosong.</summary>
    public void SelectForContextMenu(BindingItemViewModel? item)
    {
        if (item is null) return;
        bool ctrl = (System.Windows.Input.Keyboard.Modifiers
                     & System.Windows.Input.ModifierKeys.Control) != 0;
        if (!ctrl && _selection.Contains(item.Model)) return;
        SelectCard(item);
    }

    /// <summary>§133 M3: SATU jalur penerapan seleksi ke semua tile
    /// hidup (_allTiles, termasuk yang tersembunyi filter): IsSelected,
    /// SelectionIndex 1-based dari urutan TileSelection (0 bila tidak
    /// terseleksi), dan ShowSelectionNumber = IsSelected &amp;&amp;
    /// IsSelectMode. Dipanggil tanpa auto-exit (EnterSelectMode);
    /// semua mutasi lain lewat SyncSelectionFlags.</summary>
    private void ApplySelectionFlags()
    {
        foreach (var tile in _allTiles)
        {
            tile.IsSelected = _selection.Contains(tile.Model);
            int index = _selection.IndexOf(tile.Model);
            tile.SelectionIndex = index >= 0 ? index + 1 : 0;
            tile.ShowSelectionNumber = tile.IsSelected && IsSelectMode;
        }
        SelectedCount = _selection.Count;
        RaiseSelectionDerivedProperties();
    }

    /// <summary>Applies the _selection set to every live item VM and
    /// refreshes every derived selection property - including when
    /// the count did not change (replacing one card with another) -
    /// then auto-exits the mode when the selection became empty
    /// (ExitModeIfEmpty). THE central selection-mutation exit point:
    /// every selection change in this file goes through here (or
    /// through RefreshBindings, which calls it after pruning).</summary>
    private void SyncSelectionFlags()
    {
        ApplySelectionFlags();
        ExitModeIfEmpty();
    }

    /// <summary>§132 U3: "Pilih semua di halaman ini" selects VISIBLE
    /// cards only (the filtered Bindings list), never filter-hidden
    /// ones. No-op when nothing is visible, so a filter matching
    /// nothing can never clear an existing selection.
    /// §134 A1: the command is now a TOGGLE. First press snapshots
    /// the current ordered selection, then replaces it with the
    /// visible set (label flips to S1713 "Batalkan semua"). While
    /// in the cancel-all state, pressing restores the snapshot
    /// EXACTLY (same cards, same order/numbers via
    /// SyncSelectionFlags), consumes the snapshot, and STAYS in
    /// Mode Pilih - ExitSelectMode is never called here; only an
    /// EMPTY restored selection auto-exits via ExitModeIfEmpty
    /// (the tolerated §133 edge: mode entered with an empty
    /// selection, select-all, cancel-all). Restore filters to
    /// cards still on the current page, so a card deleted since
    /// the snapshot is pruned, never resurrected. A snapshot that
    /// is live but whose label state lapsed (filter change) is
    /// overwritten by a fresh snapshot on the next press, never
    /// restored stale.</summary>
    [RelayCommand]
    private void SelectAllVisible()
    {
        if (Bindings.Count == 0) return;
        if (IsSelectAllCancelState && _selectAllSnapshot is not null)
        {
            var page = EffectivePage;
            var restored = page is null
                ? _selectAllSnapshot.ToList()
                : _selectAllSnapshot.Where(m => page.Bindings.Contains(m)).ToList();
            ClearSelectAllSnapshot();
            _selection.ReplaceAll(restored);
            SyncSelectionFlags();
            return;
        }
        _selectAllSnapshot = _selection.Snapshot();
        _selectAllActive = true;
        _selection.ReplaceAll(Bindings.Select(i => i.Model));
        SyncSelectionFlags();
    }

    /// <summary>§134 A2: ordered snapshot of the current selection
    /// (a copy) + order-sensitive equality, for the card menu's
    /// Closed handler in MainWindow.xaml.cs - the handler compares
    /// the selection at menu-open time with the selection at
    /// close time to decide whether the temporary right-click
    /// selection may be cleared. Reference equality: BindingConfig
    /// has no Equals override, matching TileSelection semantics.</summary>
    public List<BindingConfig> SelectionSnapshot() => _selection.Snapshot();

    public bool SelectionMatches(IReadOnlyList<BindingConfig> snapshot)
        => _selection.Snapshot().SequenceEqual(snapshot);

    /// <summary>RENAME from the card menu (clicked card only):
    /// dialog kecil, bukan editor penuh. Pola PageEdit: VM membawa
    /// Name + Accepted, MainWindow menampilkan modalnya, penolakan
    /// nama kosong berlapis dua (dialog + pertahanan di sini).
    /// Selection untouched - §133 M7: dalam Mode Pilih, Rename TIDAK
    /// keluar mode dan TIDAK mengubah seleksi/urutan; RefreshBindings
    /// sesudah save menerapkan ulang seleksi+angka+mode.</summary>
    public void RenameCard(BindingItemViewModel item)
    {
        if (EffectivePage is null) return;
        var vm = new BindingRenameViewModel { Name = item.DisplayLabel };
        RequestRenameBinding?.Invoke(vm);
        if (!vm.Accepted) return;
        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            Notify(Localizer.Get("S1644"), "#F59E0B", 3.5);
            return;
        }
        string oldLabel = item.Model.Label;
        string newLabel = vm.Name.Trim();
        if (oldLabel == newLabel) return;
        item.Model.Label = newLabel;
        try { _host.Save(); }
        catch (Exception ex)
        {
            item.Model.Label = oldLabel;
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshBindings();
        Notify(Localizer.Format("S1645", newLabel), "#38BDF8", 3.0);
    }

    /// <summary>HAPUS from the card menu uses the same single-delete
    /// path as the tile's ✕ button (confirm S0399 + D9 rollback in
    /// DeleteBinding) - the menu calls DeleteBindingCommand directly,
    /// so there is no separate bar-target delete method.</summary>

    /// <summary>DUPLIKAT from the card menu (clicked card only):
    /// salinan dalam (aksi sama), label baru tak-bentrok, tombol
    /// fisik SAMA - salinan memakai tombol yang sama sampai user
    /// menetapkan tombol lain lewat Ganti Tombol; peringatan duplikat
    /// same-page §123 menandainya sementara itu. Disisipkan tepat
    /// sesudah kartu asal. Selection untouched - §133 M7: dalam Mode
    /// Pilih, Duplikat TIDAK keluar mode dan TIDAK mengubah
    /// seleksi/urutan; kartu baru yang disisipkan TIDAK terseleksi
    /// (bukan anggota _selection), RefreshBindings memberinya angka 0.</summary>
    public void DuplicateCard(BindingItemViewModel item)
    {
        if (EffectivePage is not { } page) return;
        string newLabel = TileActionBarLogic.UniquifyDuplicateLabel(
            item.DisplayLabel,
            page.Bindings.Select(b => string.IsNullOrWhiteSpace(b.Label) ? b.KeyId : b.Label));
        var copy = TileActionBarLogic.DuplicateBinding(item.Model, newLabel);
        int index = page.Bindings.IndexOf(item.Model);
        page.Bindings.Insert(index >= 0 ? index + 1 : page.Bindings.Count, copy);
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            page.Bindings.Remove(copy);
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshBindings();
        Notify(Localizer.Format("S1646", newLabel), "#38BDF8", 3.0);
    }

    /// <summary>§133: clears the selection (Esc, empty-area click, or
    /// after a successful bulk delete): the _selection set is emptied
    /// and every live card flag/number is re-applied through the
    /// central SyncSelectionFlags. The flag mode is NOT touched
    /// directly here - but when the mode is active, emptying the
    /// selection auto-exits it via SyncSelectionFlags
    /// (ExitModeIfEmpty); ExitSelectMode is the explicit exit and
    /// delegates here after turning the flag off. The bulk-delete
    /// undo snapshot is NOT cleared here (DeleteSelected records it;
    /// only context switches clear both - see ClearSelectionAndUndo).
    /// Plain method, no generated command: callers are code-behind
    /// and ClearSelectionAndUndo.</summary>
    public void ClearSelection()
    {
        // §134 A1: clearing the selection voids the select-all
        // snapshot - covers ExitSelectMode, context switches
        // (ClearSelectionAndUndo), and the A2 menu-close clear.
        ClearSelectAllSnapshot();
        _selection.Clear();
        SyncSelectionFlags();
    }

    /// <summary>§132 F13: context switches clear BOTH the selection and
    /// the bulk-delete undo snapshot - the snapshot belongs to the
    /// page it was taken on. §133 M7: this also exits Mode Pilih -
    /// ClearSelection auto-exits when it empties the selection while
    /// the mode is active. Call sites: OnIsEditingGlobalChanged,
    /// OnSelectedPageChanged, RefreshPages, ResetProfile. Nothing
    /// else clears the selection (filter-text and view-mode changes
    /// must not).</summary>
    private void ClearSelectionAndUndo()
    {
        ClearSelection();
        _undoStore.Clear();
        RaiseSelectionDerivedProperties();
    }

    /// <summary>Hapus massal dari menu kartu (seleksi): SATU konfirmasi
    /// AppDialog yang menyebut jumlah; DITOLAK selama perekaman/
    /// pemetaan berjalan (penjaga lama, lihat BulkDeleteBlocked);
    /// gagal simpan = semua kartu kembali (rollback D9).
    /// §133 M7b: SUKSES -&gt; ExitSelectMode (hapus seleksi + flag
    /// off); GAGAL simpan / ditolak BulkDeleteBlocked / Batal dialog
    /// -&gt; mode + seleksi DIPERTAHANKAN (jalur gagal return sebelum
    /// ExitSelectMode dan snapshot undo yang belum tercatat tidak
    /// dihapus).</summary>
    [RelayCommand]
    public void DeleteSelected()
    {
        if (EffectivePage is not { } page) return;
        if (BulkDeleteBlocked)
        {
            Notify(Localizer.Get("S1642"), "#F59E0B", 4.0);
            return;
        }
        var targets = _selection.Snapshot().Where(m => page.Bindings.Contains(m)).ToList();
        if (targets.Count == 0) return;
        if (ConfirmPrompt?.Invoke(Localizer.Format("S1640", targets.Count, page.Name)) != true)
            return;
        var removed = targets.Select(m => (Index: page.Bindings.IndexOf(m), Model: m)).ToList();
        foreach (var m in targets) page.Bindings.Remove(m);
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            foreach (var (index, model) in removed.OrderBy(r => r.Index))
                if (index >= 0 && index <= page.Bindings.Count) page.Bindings.Insert(index, model);
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        // §132: record the session undo snapshot AFTER the Save
        // succeeds; ExitSelectMode below must NOT clear it (hanya
        // ClearSelectionAndUndo yang membersihkan snapshot).
        _undoStore.Record(page, removed);
        ExitSelectMode();
        RefreshBindings();
        Notify(Localizer.Format("S1641", targets.Count), "#F59E0B", 3.0);
    }

    // --- §132: active/inactive toggle, copy/move, undo, duplicate page ----------

    /// <summary>§132 F44: the ONE writer of the live IsActive flag for
    /// the menu single-toggle and the bulk toggle. Mutates the flags,
    /// reloads, saves once; on Save failure every flag is restored
    /// (D9). No session guard: toggling only affects future dispatch,
    /// which the §121 session gate already blocks. No footer notice:
    /// the dimmed rendering is the feedback.</summary>
    public void SetActive(IEnumerable<BindingConfig> models, bool active)
    {
        var targets = models.ToList();
        if (targets.Count == 0) return;
        var oldValues = targets.Select(m => m.IsActive).ToList();
        foreach (var m in targets) m.IsActive = active;
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            for (int i = 0; i < targets.Count; i++) targets[i].IsActive = oldValues[i];
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshBindings();
    }

    /// <summary>Nonaktifkan/Aktifkan for one card (card menu): flips
    /// that card's current state. The menu label already showed the
    /// action that will run. §133 M7b: dalam Mode Pilih, toggle
    /// per-kartu ini TIDAK keluar mode (bukan op bulk) - RefreshBindings
    /// di SetActive menerapkan ulang seleksi+angka+mode.</summary>
    public void ToggleCardActive(BindingItemViewModel item)
        => SetActive(new[] { item.Model }, !item.Model.IsActive);

    /// <summary>§153 Part A selection toggle (card menu + bar):
    /// aturan MAYORITAS (verdict user 21:14 WIB 28 Okt 2026;
    /// menggantikan aturan ANY §132 U4 dan M6 §133): bila mayoritas
    /// target aktif (aktif &gt; nonaktif, atau SERI) nonaktifkan
    /// SEMUA; bila mayoritas nonaktif aktifkan SEMUA. Seri -&gt;
    /// nonaktifkan (arah aman). One Save via SetActive (reload+save+
    /// rollback bila gagal, tanpa footer sukses - pola SetActive).
    /// The menu/bar label (BulkToggleText) already showed the action
    /// that will run. §133 M7: TIDAK keluar mode - berbeda dengan
    /// Salin/Pindah/Hapus yang keluar mode saat sukses.</summary>
    [RelayCommand]
    public void ToggleSelectedActive()
    {
        if (EffectivePage is not { } page) return;
        var targets = _selection.Snapshot().Where(m => page.Bindings.Contains(m)).ToList();
        if (targets.Count == 0) return;
        SetActive(targets, !TileSelection.MajorityActive(targets));
    }

    /// <summary>§132: one copy/move destination - a page in some
    /// profile, or the single Global page. DisplayName is what the
    /// submenu/dropdown shows and what the footer notice names.</summary>
    public sealed record TransferDestination(string DisplayName, PageConfig Page, bool IsGlobalPage);

    /// <summary>All copy/move destinations, live: every profile's
    /// pages in order, then Halaman Global (S1697) last. The current
    /// page is included: copying onto it behaves like Duplikat, and
    /// moving onto it copies without removing (CardTransferService).
    /// §134 B1: DisplayName is the page name alone. When the same
    /// page name occurs more than once across ALL profiles' pages
    /// (counted by exact name, Ordinal - the same comparison the
    /// name-uniqueness checks use), EVERY entry sharing that name
    /// gets "Name (Profile)"; non-colliding entries stay bare. The
    /// count spans profiles, so two same-named pages inside ONE
    /// profile (possible: PageEditWindow never rejects a duplicate
    /// name) are also suffixed - but with the profile name only,
    /// the two twins still render identically, exactly as their
    /// sidebar rows do; no further disambiguation exists without
    /// inventing a new display format. Halaman Global (S1697) is
    /// never counted and never suffixed.</summary>
    public IReadOnlyList<TransferDestination> TransferDestinations()
    {
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var profile in _host.Config.Profiles)
            foreach (var page in profile.Pages)
                nameCounts[page.Name] = nameCounts.TryGetValue(page.Name, out int n) ? n + 1 : 1;
        var list = new List<TransferDestination>();
        foreach (var profile in _host.Config.Profiles)
            foreach (var page in profile.Pages)
                list.Add(new TransferDestination(
                    nameCounts[page.Name] > 1 ? $"{page.Name} ({profile.Name})" : page.Name,
                    page, false));
        list.Add(new TransferDestination(Localizer.Get("S1697"), _host.Config.GlobalPage, true));
        return list;
    }

    /// <summary>Copy/move for the card menu (clicked card only).</summary>
    public void CopyCardsTo(IReadOnlyList<BindingConfig> models, TransferDestination destination)
        => TransferCards(models, destination, isMove: false, exitSelectModeOnSuccess: false);

    /// <summary>Copy/move for the card menu (clicked card only).</summary>
    public void MoveCardsTo(IReadOnlyList<BindingConfig> models, TransferDestination destination)
        => TransferCards(models, destination, isMove: true, exitSelectModeOnSuccess: false);

    /// <summary>Copy/move for the card menu (whole selection).
    /// §133 M7b: sukses -&gt; keluar mode (lihat TransferCards).</summary>
    public void CopySelectedTo(TransferDestination destination)
        => TransferCards(SelectedModels(), destination, isMove: false, exitSelectModeOnSuccess: true);

    /// <summary>Copy/move for the card menu (whole selection).
    /// §133 M7b: sukses -&gt; keluar mode (lihat TransferCards).</summary>
    public void MoveSelectedTo(TransferDestination destination)
        => TransferCards(SelectedModels(), destination, isMove: true, exitSelectModeOnSuccess: true);

    private List<BindingConfig> SelectedModels()
        => EffectivePage is { } page
            ? _selection.Snapshot().Where(m => page.Bindings.Contains(m)).ToList()
            : new List<BindingConfig>();

    /// <summary>§132 D1 wiring (see CardTransferService): the service
    /// prepares the in-memory changes; this method owns
    /// ReloadActivePage + ONE Save + D9 rollback + RefreshBindings +
    /// footer notice. NEVER switches SelectedProfile/SelectedPage and
    /// NEVER calls RefreshPages (it resets SelectedPage). Moving TO
    /// Global removes the card from profile export by design - no
    /// extra warning beyond the footer notice.
    /// §133 M7b: bila exitSelectModeOnSuccess (jalur seleksi
    /// CopySelectedTo/MoveSelectedTo), SUKSES -&gt; ExitSelectMode
    /// sesudah footer; GAGAL simpan -&gt; return di catch sebelum
    /// titik itu, mode+seleksi dipertahankan (RefreshBindings di
    /// catch menerapkan ulang keduanya; kartu dipulihkan). Jalur
    /// kartu-tunggal (CopyCardsTo/MoveCardsTo) tidak pernah keluar
    /// mode - ia adalah fallback menu saat seleksi kosong.</summary>
    private void TransferCards(IReadOnlyList<BindingConfig> models, TransferDestination destination, bool isMove, bool exitSelectModeOnSuccess)
    {
        if (EffectivePage is not { } page) return;
        var cards = models.Where(m => page.Bindings.Contains(m)).ToList();
        if (cards.Count == 0) return;
        IReadOnlyList<BindingConfig> copies;
        IReadOnlyList<(int Index, BindingConfig Model)> removed = Array.Empty<(int, BindingConfig)>();
        if (isMove)
        {
            var result = CardTransferService.MoveCards(page, destination.Page, cards);
            copies = result.Copies;
            removed = result.Removed;
        }
        else
        {
            copies = CardTransferService.CopyCards(page, destination.Page, cards);
        }
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            foreach (var copy in copies) destination.Page.Bindings.Remove(copy);
            foreach (var (index, model) in removed.OrderBy(r => r.Index))
                if (index >= 0 && index <= page.Bindings.Count) page.Bindings.Insert(index, model);
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshBindings();
        string displayLabel = cards.Count == 1
            ? CardTransferService.DisplayLabelOf(cards[0])
            : Localizer.Format("S1638", cards.Count);
        string notice = Localizer.Format(isMove ? "S1699" : "S1698", displayLabel, destination.DisplayName);
        if (destination.IsGlobalPage && !_host.Config.Settings.IsGlobalEnabled)
            notice += " " + Localizer.Get("S1700");
        Notify(notice, "#38BDF8", 3.5);
        if (exitSelectModeOnSuccess) ExitSelectMode();
    }

    /// <summary>§132: restores the bulk-delete snapshot (one level,
    /// session-only). Gone-page guard: the snapshot's page must still
    /// exist in some profile or be the Global page, else a no-op and
    /// the snapshot is consumed. Restored cards are NOT re-selected;
    /// the current selection (if any was made after the delete) is
    /// preserved. Save-failure rollback removes every restored model
    /// from every page list by reference; the snapshot is already
    /// consumed and is NOT revived.</summary>
    [RelayCommand]
    private void UndoBulkDelete()
    {
        if (!_undoStore.TryRestore(
                p => ReferenceEquals(p, _host.Config.GlobalPage)
                     || _host.Config.Profiles.Any(pr => pr.Pages.Contains(p)),
                out var restored))
        {
            RaiseSelectionDerivedProperties();
            return;
        }
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            foreach (var model in restored)
            {
                foreach (var pr in _host.Config.Profiles)
                    foreach (var pg in pr.Pages) pg.Bindings.Remove(model);
                _host.Config.GlobalPage.Bindings.Remove(model);
            }
            _host.ReloadActivePage();
            RefreshBindings();
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshBindings();
        Notify(Localizer.Format("S1702", restored.Count), "#38BDF8", 3.5);
    }

    /// <summary>§132 C6: duplicates the SIDEBAR ROW's page (not
    /// necessarily the selected page), inserted directly after the
    /// source. Never touches the Global page (rows never represent
    /// it). Follows the AddPage pattern: RefreshPages, then
    /// SelectedPage = copy (RefreshPages resets SelectedPage, so the
    /// assignment MUST come after it). §134 B2: confirm (S1714) via
    /// the existing ConfirmPrompt handshake BEFORE any mutation -
    /// a "no" answer, or a null ConfirmPrompt, changes nothing: no
    /// duplicate, no page switch, no footer, no Save. DuplicateCard
    /// deliberately keeps NO confirm (unchanged). Full Edit Mode
    /// modal: the modal's Duplikat button IS the confirmation, so
    /// EditPage calls DuplicatePageCore(page, confirm: false) and
    /// skips the prompt; every guard and post-mutation step is
    /// shared, so the two paths cannot diverge.</summary>
    [RelayCommand]
    private void DuplicatePage(PageConfig? page) => DuplicatePageCore(page, confirm: true);

    private void DuplicatePageCore(PageConfig? page, bool confirm)
    {
        if (SelectedProfile is null) return;
        page ??= SelectedPage;
        if (page is null) return;
        if (ReferenceEquals(page, _host.Config.GlobalPage)) return;
        if (!SelectedProfile.Pages.Contains(page)) return;
        if (confirm && ConfirmPrompt?.Invoke(Localizer.Format("S1714", page.Name)) != true)
            return;
        var copy = CardTransferService.DuplicatePage(
            page, SelectedProfile.Pages.Select(p => p.Name));
        int index = SelectedProfile.Pages.IndexOf(page);
        SelectedProfile.Pages.Insert(index + 1, copy);
        _host.ReloadActivePage();
        try { _host.Save(); }
        catch (Exception ex)
        {
            SelectedProfile.Pages.Remove(copy);
            Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
            return;
        }
        RefreshPages();
        SelectedPage = copy;
        Notify(Localizer.Format("S1701", copy.Name), "#38BDF8", 3.5);
    }

    // --- press-to-map ---------------------------------------------------------------

    [ObservableProperty] private string _captureHint = "";

    /// <summary>§168a: live pending-chord text for the "Petakan
    /// Tombol" modal cap — the modifiers held so far ("CTRL",
    /// "CTRL+SHIFT"), "…" while nothing is held. Updated on the UI
    /// thread from the capture path; the modal binds its keycap to
    /// this. The completed chord itself is never shown here: the
    /// modal closes on the main key's DOWN, and the chord appears in
    /// the editor.</summary>
    [ObservableProperty] private string _capturePendingCap = "…";

    private BindingItemViewModel? _rebindTarget;

    /// <summary>Raised when capture arms: the view shows the waiting popup.</summary>
    public event Action? RequestWaitingCapture;
    /// <summary>Raised when custom rebind arms: specifies title, promptTitle, promptDesc.</summary>
    public event Action<string, string, string>? RequestWaitingCustomCapture;
    /// <summary>Raised on the UI thread right before the editor opens for
    /// a captured key: the view closes the waiting popup.</summary>
    public event Action? CaptureCompleted;
    public bool IsCaptureArmed => _captureArmed;

    /// <summary>§168a: recompute <see cref="CapturePendingCap"/> from
    /// the pending chord. UI thread only (binding notification);
    /// driver-thread callers post through <see cref="RunOnUi"/>.
    /// Reads the live accumulator, so a refresh posted before a
    /// completion can never show a stale mask.</summary>
    private void RefreshCapturePendingCap()
    {
        string text = ChordMask.FormatModifiers(_captureChord.Mask);
        CapturePendingCap = string.IsNullOrEmpty(text) ? "…" : text;
    }

    public void CancelCapture()
    {
        // Hanya pulihkan bila capture memang sedang armed: CancelCapture
        // yang nyasar saat tidak armed tidak boleh menyentuh flag milik
        // listening editor. (Penutup popup tunggu di MainWindow sudah
        // memeriksa IsCaptureArmed sebelum memanggil ini.)
        if (_captureArmed) RestoreCaptureSuppression();
        _captureArmed = false;
        _rebindTarget = null;
        _captureChord.Reset();
        RefreshCapturePendingCap();
    }

    [RelayCommand]
    private void ChangeBindingKey(BindingItemViewModel? item)
    {
        if (item is null || EffectivePage is null) return;
        if (_host.Health.State != InterceptionState.Running)
        {
            CaptureHint = Localizer.Get("S0497");
            return;
        }
        ArmCaptureSuppression();
        _rebindTarget = item;
        _captureArmed = true;
        _captureChord.Reset();
        RefreshCapturePendingCap();
        CaptureHint = "";
        RequestWaitingCustomCapture?.Invoke(
            Localizer.Get("S0007"),
            Localizer.Get("S0300"),
            Localizer.Get("S0498"));
    }

    [RelayCommand]
    private void ArmCapture()
    {
        if (_host.Health.State != InterceptionState.Running)
        {
            CaptureHint = Localizer.Get("S0497");
            return;
        }
        ArmCaptureSuppression();
        _rebindTarget = null;
        _captureArmed = true;
        _captureChord.Reset();
        RefreshCapturePendingCap();
        CaptureHint = "";
        RequestWaitingCapture?.Invoke();
    }

    /// <summary>§103: nilai AutoIcon default kartu baru = ikon aplikasi.
    /// Asset Assets/keystream.png (sudah Resource, dipakai About) diimpor
    /// sekali ke IconStore (idempotent, transparansi dipertahankan,
    /// disimpan ≤128px); nilai di-cache dan file diverifikasi ada - bila
    /// file hilang, impor ulang. Gagal impor → null (pemanggil memakai
    /// fallback gamepad lama agar pembuatan kartu tak pernah gagal).</summary>
    private string? _appDefaultIconValue;

    private string? GetAppDefaultIconValue()
    {
        if (_appDefaultIconValue is string cached)
        {
            string? name = IconReference.FileName(cached);
            if (name is not null && File.Exists(Path.Combine(_host.IconStore.Folder, name)))
                return cached;
        }
        try
        {
            using var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Keystream;component/Assets/keystream.png", UriKind.Absolute))?.Stream;
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
            var result = _host.IconStore.ImportBytes(ms.ToArray(), maxBytes);
            _appDefaultIconValue = result.IconValue;
            return _appDefaultIconValue;
        }
        catch
        {
            return null;
        }
    }

    private void OnDeckEventObserved(KeyEvent e)
    {
        // §168: UP events reach capture only for the pending-chord
        // release rule (a modifier release emptying the mask
        // completes the capture with the first-pressed modifier as a
        // single key). All other releases stay armed, ignored.
        if (!_captureArmed || e.IsRepeat) return;
        if (!e.IsDown)
        {
            byte maskBefore = _captureChord.Mask;
            var single = _captureChord.ReleaseUp(e.Key);
            if (single is null)
            {
                // §168a: a modifier release that shrank the pending
                // mask updates the modal cap (CTRL+SHIFT -> CTRL).
                // Releases that change nothing post nothing.
                if (_captureChord.Mask != maskBefore) RunOnUi(RefreshCapturePendingCap);
                return;
            }
            e = new KeyEvent(e.Device, single.Value.Key, single.Value.VirtualKey, KeyDirection.Down, false, e.TimestampQpc);
            // Fall through: the accumulator already reset itself, so
            // the mask snapshot below is 0 (plain single-key capture).
        }
        else if (_captureChord.AbsorbDown(e.Key, e.VirtualKey))
        {
            // §168: a modifier DOWN is absorbed into the pending
            // chord - stay armed, do not bind, do not disarm.
            // §168a: the held modifier updates the modal cap.
            RunOnUi(RefreshCapturePendingCap);
            return;
        }
        _captureArmed = false;
        // Snapshot on the driver thread: the accumulator resets here,
        // so the UI body below cannot observe a moved mask.
        byte chordMask = _captureChord.Mask;
        _captureChord.Reset();
        RunOnUi(() =>
        {
            // §130 (verifikasi): seluruh badan dibungkus try/finally
            // agar supresi capture SELALU dipulihkan - termasuk bila
            // RefreshBindings/ReloadActivePage/editor melempar di
            // tengah jalan. Tanpa ini, satu exception membuat
            // SuppressEngineEvents macet true: deck mati + Reset Data
            // ditolak selamanya (S1688) sampai restart. Restore di
            // sini idempotent (menulis nilai tersimpan yang sama) dan
            // ShowDialog editor di bawah memblokir sampai editor
            // tutup, jadi finally tidak pernah membersihkan supresi
            // listening editor yang masih hidup.
            try
            {
            CaptureHint = "";
            // §123 butir 5: capture berakhir di sini (tanpa
            // CaptureCompleted - popup tunggu tetap terbuka sampai user
            // menutupnya, dan penutupnya tidak memanggil CancelCapture
            // karena _captureArmed sudah false), jadi supresi WAJIB
            // dipulihkan di return ini, bukan dititipkan ke penutup.
            if (EffectivePage is null) { RestoreCaptureSuppression(); return; }
            var page = EffectivePage;

            if (_rebindTarget != null)
            {
                var target = _rebindTarget.Model;
                _rebindTarget = null;
                string newKeyName = KeyNames.GetDisplayName(e.Key.ScanCode, e.Key.IsE0);
                // §168: the mapped key is (key, mask) - the duplicate
                // check and the prompt both use the chord form.
                string displayKeyName = ChordMask.Format(chordMask, newKeyName);
                var duplicate = page.Bindings.FirstOrDefault(b =>
                    !ReferenceEquals(b, target) && b.ScanCode == e.Key.ScanCode && b.IsE0 == e.Key.IsE0 && b.IsE1 == e.Key.IsE1 && b.ChordMask == chordMask);
                int duplicateIndex = duplicate is null ? -1 : page.Bindings.IndexOf(duplicate);
                if (duplicate != null)
                {
                    // Batch 1 fix: never silently delete another binding.
                    // ConfirmPrompt follows this file's own pattern (null
                    // = refuse, same as delete profile/page); on refusal
                    // nothing is changed and nothing is saved.
                    if (ConfirmPrompt?.Invoke(Localizer.Format("S0499", displayKeyName, duplicate.Label, page.Name)) != true)
                    {
                        CaptureHint = Localizer.Get("S0500");
                        // §123 butir 5: penolakan = capture berakhir,
                        // popup tetap terbuka - pulihkan di sini.
                        RestoreCaptureSuppression();
                        return;
                    }
                    page.Bindings.Remove(duplicate);
                }
                ushort oldScan = target.ScanCode;
                bool oldE0 = target.IsE0;
                bool oldE1 = target.IsE1;
                uint oldVirtualKey = target.VirtualKey;
                string oldKeyName = target.KeyName;
                string oldKeyId = target.KeyId;
                byte oldChordMask = target.ChordMask;
                target.ScanCode = e.Key.ScanCode;
                target.IsE0 = e.Key.IsE0;
                target.IsE1 = e.Key.IsE1;
                target.VirtualKey = e.VirtualKey;
                target.KeyName = newKeyName;
                target.ChordMask = chordMask;
                target.KeyId = $"K-{e.Key.ScanCode:X2}{(e.Key.IsE0 ? "E" : "")}";
                try { _host.Save(); }
                catch (Exception ex)
                {
                    // D9: dulu Save tanpa try/catch - gagal = dialog
                    // crash, tombol sudah pindah di memori, dan popup
                    // tunggu tidak pernah ditutup (CaptureCompleted
                    // tidak tercapai). Kini: tombol + duplikat
                    // dipulihkan, popup ditutup, kegagalan diberitahu
                    // lewat footer.
                    target.ScanCode = oldScan;
                    target.IsE0 = oldE0;
                    target.IsE1 = oldE1;
                    target.VirtualKey = oldVirtualKey;
                    target.KeyName = oldKeyName;
                    target.KeyId = oldKeyId;
                    target.ChordMask = oldChordMask;
                    if (duplicate is not null && duplicateIndex >= 0)
                        page.Bindings.Insert(duplicateIndex, duplicate);
                    _host.ReloadActivePage();
                    RefreshBindings();
                    // §123 butir 5: pulihkan SEBELUM CaptureCompleted
                    // menutup popup (dan sebelum editor dibuka di jalur
                    // sukses) agar supresi capture tidak bocor ke sesi
                    // editor.
                    RestoreCaptureSuppression();
                    CaptureCompleted?.Invoke();
                    Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
                    return;
                }
                _host.ReloadActivePage();
                RefreshBindings();
                RestoreCaptureSuppression();
                CaptureCompleted?.Invoke();
                return;
            }

            // §123: pemetaan baru SELALU membuat kartu baru, termasuk bila
            // tombol sudah terikat - kartu lama tidak disentuh sama sekali;
            // duplikatnya ditandai peringatan same-page di RefreshBindings.
            string keyName = KeyNames.GetDisplayName(e.Key.ScanCode, e.Key.IsE0);
            var created = new BindingConfig
            {
                KeyId = $"K-{e.Key.ScanCode:X2}{(e.Key.IsE0 ? "E" : "")}",
                KeyName = keyName,
                ScanCode = e.Key.ScanCode,
                IsE0 = e.Key.IsE0,
                IsE1 = e.Key.IsE1,
                VirtualKey = e.VirtualKey,
                // §168: the captured chord mask is part of the new
                // card's key (0 = plain single key, unchanged).
                ChordMask = chordMask,
                // §168a: the default display name is the chord text,
                // not the bare main-key name ("Alt+V", not "Key V").
                // Mask 0 falls through unchanged via Format.
                Label = ChordMask.Format(chordMask, keyName),
                // §103: default kartu baru = ikon aplikasi (AutoIcon),
                // bukan lagi "gamepad". Icon sengaja kosong: pilihan
                // eksplisit user (katalog/upload) tetap menang; gagal
                // impor → AutoIcon kosong → tile jatuh ke gamepad lama
                // (pembuatan kartu tak pernah gagal karenanya).
                AutoIcon = GetAppDefaultIconValue() ?? "",
                BackgroundColor = "#2A2F3A",
            };
            page.Bindings.Add(created);
            try { _host.Save(); }
            catch (Exception ex)
            {
                // D9: dulu Save tanpa try/catch - gagal = dialog
                // crash, lalu editor tetap terbuka untuk kartu yang
                // tidak tersimpan. Kini kartu dihapus dari memori,
                // popup tunggu ditutup, editor TIDAK dibuka, dan
                // kegagalan diberitahu lewat footer.
                page.Bindings.Remove(created);
                RefreshBindings();
                RestoreCaptureSuppression();
                CaptureCompleted?.Invoke();
                Notify(Localizer.Get("S0937") + " " + ex.Message, "#EF4444", 4.0);
                return;
            }
            _host.ReloadActivePage();
            RefreshBindings();
            RestoreCaptureSuppression();
            CaptureCompleted?.Invoke();
            RequestEditBinding?.Invoke(new BindingEditorViewModel(_host, page, created));
            }
            finally { RestoreCaptureSuppression(); }
        });
    }
}
