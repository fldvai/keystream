using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;
using Keystream.Actions.Basic;
using Keystream.Actions.Browser;
using Keystream.Actions.Windows;
using Keystream.Actions.Deck;
using Keystream.Actions.Hotkey;
using Keystream.Actions.Launcher;
using Keystream.Actions.Macro;
using Keystream.Actions.Mouse;
using Keystream.Actions.Media;
using Keystream.Actions.Obs;
using Keystream.Actions.StreamerBot;
using Keystream.Actions.Steam;
using Keystream.Actions.Profile;
using Keystream.App.Services;
using Keystream.App.Views;
using Keystream.Actions.Sound;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Core.Localization;
using Keystream.Core.Devices;
using Keystream.Core.Engine;
using Keystream.Core.Input;
using Keystream.Windows.Interception;
using Keystream.Windows.Input;

namespace Keystream.App.Host;

/// <summary>
/// Composition root + lifecycle. Owns every long-lived service, the three
/// UI-thread timers (engine tick 25 ms, health 500 ms, foreground watcher
/// 700 ms), config persistence, the identification orchestration, and the
/// Start-with-Windows registry value. The App class and view-models talk
/// only to this host - no service is ever reached for statically
/// (the audited app's lifecycle was an empty App.xaml.cs template with
/// services created ad hoc in view-models; finding B3-6).
/// </summary>
public sealed class AppHost : IDisposable
{
    public ConfigStore Store { get; }

    /// <summary>Uploaded tile icons (v3.4.0): the single Icons folder
    /// under the app data directory.</summary>
    public IconStore IconStore { get; }

    /// <summary>Raised after an icon cleanup changed which files exist,
    /// so the main window re-resolves tile icons (missing file icons
    /// fall back to the built-in vector icon).</summary>
    public event Action? IconsChanged;

    public void NotifyIconsChanged() => IconsChanged?.Invoke();

    /// <summary>§113 - shared run-lifetime state between the macro
    /// modal and the floating panel (selection + record settings).
    /// The panel has no settings of its own; it reads everything here.</summary>
    public FloatPanelSession FloatPanel { get; } = new();

    /// <summary>§113 - owns the floating panel window (lifetime,
    /// position, event routing). Created at the end of the ctor.</summary>
    public FloatPanelHost PanelHost { get; private set; } = null!;

    /// <summary>§113 - footer notices raised from panel surfaces
    /// (standalone controller, via NotifyFooter). The main window
    /// subscribes and forwards them to its footer.</summary>
    public event Action<string, string, double>? FooterNotice;

    /// <summary>§113 - raises <see cref="FooterNotice"/> (green dot,
    /// 3.5s by default, matching the other footer notices).</summary>
    public void NotifyFooter(string message, string dotColor = "#10B981", double seconds = 3.5)
        => FooterNotice?.Invoke(message, dotColor, seconds);
    public DeckConfig Config { get; private set; }
    public LoadResult InitialLoad { get; }

    public ObsClient Obs { get; }
    public StreamerBotClient StreamerBot { get; }
    public IReadOnlyDictionary<string, IAction> ActionsById { get; }
    public ActionDispatcher Dispatcher { get; }
    public DeckEngine Engine { get; }

    /// <summary>Hotkey master (Panic dkk.) yang gagal didaftarkan pada
    /// pemanggilan RegisterMasterHotkeys terakhir - di-reset tiap
    /// registrasi ulang. Dibaca Settings agar kegagalan yang dulu
    /// tak terlihat (§45) kini tampil.</summary>
    public List<string> MasterHotkeyFailures { get; } = new();
    public ModeAInterceptionService Service { get; }

    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _healthTimer;
    private InterceptionState _lastLoggedHealthState;
    private readonly DispatcherTimer _foregroundTimer;
    private string? _lastForegroundProcess;
    private IntPtr _lastForegroundHwnd;

    /// <summary>Switch-back armed state: in-memory ONLY, never
    /// serialized — a restart clears it (this instance lives and dies
    /// with the host). Armed only by WatchForeground after a
    /// successful flag-ON auto-switch; see SwitchBackState.</summary>
    private readonly SwitchBackState _switchBack = new();

    /// <summary>Manual-switch lock: in-memory ONLY, never
    /// serialized. After a manual (profile, page) move made while
    /// the foreground was inside a trigger set, auto-switch stays
    /// suppressed while the foreground remains inside that same
    /// set; leaving the set unlocks. See ManualSwitchLock.</summary>
    private readonly ManualSwitchLock _manualLock = new();

    public event Action? HealthChanged;
    public event Action<bool>? PassthroughChanged;
    public event Action<bool>? GlobalEnabledChanged;
    public event Action<KeyEvent>? DeckEventObserved;
    public event Action? ActiveProfileChangedByAutoSwitch;
    public event Action? PageSwitchedByAction;
    public event Action<DeckIdentification>? IdentificationProgress;
    public event Action<DeviceId>? IdentificationCompleted;

    private IdentificationSession? _identifySession;
    private DeckIdentification? _identifyMachine;

    // --- §156 ScrollDial wheel capture ------------------------------------
    // The hook is NEVER installed at startup/idle: the only Start() call
    // site is ApplyDialHoldChanged's hold-started branch. All lifecycle
    // state below is touched only on the UI thread (see the generation
    // mechanism on OnDialHoldChanged/ApplyDialHoldChanged), except
    // _wheelGeneration, which the driver thread bumps with Interlocked.
    private readonly WheelCaptureHook _wheelHook = new();
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher;
    private long _wheelGeneration;
    private KeyIdentity? _wheelActiveKey;
    private bool _wheelDisposed;

    // A13: Setup writes the wizard language choice to this SAME key
    // (ShortcutHelper.UninstallKeyPath in Keystream.Setup) under the
    // LanguageHandoff.RegistryValueName value; the app consumes it
    // once, on FreshDefault only, so the default cards survive a
    // Setup language choice. No second key or value name is invented.
    private const string PendingLanguageKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Keystream";

    // A13: best-effort only, never throws - startup must never fail
    // on the pending-language registry value. Read-only open; a
    // missing key or a non-string value reads as no pending value.
    private static string? ReadPendingLanguage()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PendingLanguageKeyPath);
            return key?.GetValue(LanguageHandoff.RegistryValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    // A13: best-effort only, never throws. Opens writable only when
    // the key exists; a missing value is not an error. A leftover
    // value is harmless - it is only ever read on FreshDefault.
    private static void DeletePendingLanguage()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PendingLanguageKeyPath, true);
            key?.DeleteValue(LanguageHandoff.RegistryValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort only: startup must never fail on this.
        }
    }

    public AppHost()
    {
        Store = ConfigStore.ForCurrentUser();
        IconStore = new IconStore(Path.GetDirectoryName(Store.MainPath) ?? ".");
        InitialLoad = Store.Load();
        Config = InitialLoad.Config;
        // §137: apply the persisted log level BEFORE any gated write
        // below (the load-note WriteWarning and the health-timer
        // WriteWarning read it) and before any window exists. Writes
        // before this point (early-startup unhandled exceptions) use
        // the Information default in CrashLog.
        CrashLog.SetLevel(Config.Settings.LogLevel);
        CrashLog.WriteDebug("Tingkat log dimuat: " + LogLevel.NormalizeLevel(Config.Settings.LogLevel));
        // Catatan pemulihan tidak boleh hanya hidup 5 detik di footer
        // (§45): tulis juga ke crash.log agar selalu bisa ditelusuri,
        // termasuk bila aplikasi start-minimized dan footer tak terlihat.
        if (InitialLoad.Note is not null)
            // §137: a Note exists ONLY on a recovery load (backup
            // fallback / legacy migration / fresh default - see
            // ConfigStore.Load), so this is a warning, not mere info.
            CrashLog.WriteWarning("Load config: " + InitialLoad.Note);
        try
        {
            if (GlobalMigration.MigrateNavBindingsToGlobalPage(Config))
                Save(); // v2.7 nav slots retired into the Global page (one-time)
        }
        catch (Exception ex)
        {
            // A failed migration must NEVER stop the app from opening.
            CrashLog.Write("Migrasi navigasi global gagal (diabaikan, aplikasi tetap jalan)", ex);
        }
        try
        {
            bool migrated = false;
            foreach (var profile in Config.Profiles)
            foreach (var page in profile.Pages)
            foreach (var binding in page.Bindings)
                migrated |= binding.MigrateLegacyAutoIcon();
            foreach (var binding in Config.GlobalPage.Bindings)
                migrated |= binding.MigrateLegacyAutoIcon();
            if (migrated) Save(); // v3.6.0: auto icons move to AutoIcon (one-time)
        }
        catch (Exception ex)
        {
            // A failed migration must NEVER stop the app from opening.
            CrashLog.Write("Migrasi ikon otomatis gagal (diabaikan, aplikasi tetap jalan)", ex);
        }
        try
        {
            // Default Global icons (first run only): CreateDefault seeded
            // the 7 Global cards with Icon empty; here the 5
            // user-supplied icons embedded in this assembly are imported
            // into the Icons folder (content-addressed, ≤128px, via the
            // single IconStore path) and assigned to their cards. Runs
            // ONLY on FreshDefault, alongside the one-time migrations
            // above - an existing config never gets icons or cards.
            // A failure here must NEVER stop the app: cards keep Icon
            // empty (default tile icon) and still work.
            if (InitialLoad.Source == LoadSource.FreshDefault)
            {
                // A13: consume the Setup wizard language choice (if
                // any) exactly once, before seeding/saving below.
                // DecidePendingLanguage returns the raw value only
                // on FreshDefault; normalization is done here via
                // Localizer.NormalizeLanguage. The seeding block's
                // existing Save() below persists the language - no
                // second Save is added. Startup must never fail on
                // this, so every failure is swallowed.
                try
                {
                    string? pending = LanguageHandoff.DecidePendingLanguage(InitialLoad.Source == LoadSource.FreshDefault, ReadPendingLanguage());
                    if (pending is not null)
                    {
                        var settings = Config.Settings;
                        if (settings is not null) settings.Language = Localizer.NormalizeLanguage(pending);
                        Localizer.SetLanguage(settings?.Language);
                        DeletePendingLanguage();
                    }
                }
                catch { }

                try
                {
                    using var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (regKey?.GetValue("Keystream") is not null)
                    {
                        Config.Settings.StartWithWindows = true;
                    }
                }
                catch { }

                SeedDefaultGlobalIcons();
                Save();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Ikon default Global gagal (diabaikan, kartu tetap jalan)", ex);
        }

        Obs = new ObsClient();
        StreamerBot = new StreamerBotClient();
        var actions = new List<IAction>
        {
            new HotkeyAction(),
            // §65: hold/release steps, produced ONLY by down/up
            // recording (never from the card editor) - but they must
            // resolve here, or MacroAction refuses any macro that
            // contains them.
            new KeyDownAction(),
            new KeyUpAction(),
            // §107: mouse steps, produced ONLY by mouse recording
            // (never from the card editor) - but they must resolve
            // here, or MacroAction refuses any macro that contains
            // them. Registered before MacroAction (added below).
            new MouseAction(),
            new MediaAction(),
            new LauncherAction(),
            new LinkAction(),
            new SoundAction(),
            new ObsAction(Obs, () => (Config.Settings.ObsHost, Config.Settings.ObsPassword)),
            new StreamerBotAction(StreamerBot, () => (Config.Settings.StreamerBotHost, Config.Settings.StreamerBotPort, Config.Settings.StreamerBotEndpoint, Config.Settings.StreamerBotPassword)),
            new SteamAction(),
            new PageSwitchAction(ActiveProfilePages, () => Config.Settings.ActivePageId, SwitchToPage),
            new ProfileSwitchAction(AllProfiles, () => Config.ActiveProfileId, SwitchToProfile),
            new BasicAction(),
            // §129: ONE WindowsAction class registered under three ids -
            // legacy "windows" (all 94, forever) plus the two split ids
            // sharing the identical Validate/Execute path. No new
            // execution branch; the dictionaries are untouched.
            new WindowsAction(),
            new WindowsAction(WindowsAction.CommandId),
            new WindowsAction(WindowsAction.ToolsId),
            new BrowserAction(() => Config.BrowserWindowTracking, () => Save()),
            new BrowserGroupAction(() => Config.BrowserProfileGroups, () => Config.BrowserWindowTracking, () => Save()),
        };
        // MacroAction resolves its inner steps against the actions above.
        // The lookup reads the live list, so it COULD resolve "macro"
        // itself - but that path is unreachable: MacroAction.Validate
        // refuses "macro" steps before execution, and ExecuteAsync
        // re-validates, so a macro can never call a macro.
        actions.Add(new MacroAction(() => Config.Macros,
            id => actions.FirstOrDefault(a => string.Equals(a.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase))));
        ActionsById = actions.ToDictionary(a => a.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        Dispatcher = new ActionDispatcher(actions);
        // §121 F-C5: deck keys never execute while a panel/modal
        // session is active (mutually exclusive). §172: the same
        // predicate ALSO covers capture suppression
        // (SuppressEngineEvents) - the engine itself must refuse
        // execution while ANY capture is active, not only the AppHost
        // gate above: Tick has no AppHost gate, so a Tap pending on
        // the double-tap window or a LongPress maturing while a
        // capture is armed would otherwise fire mid-capture. The
        // predicate keeps Core free of App references; modal/playtest
        // execution bypasses the engine (Task.Run ->
        // MacroAction.ExecuteAsync) and is unaffected, as is
        // DeckEventObserved below (editor capture / tile flash keep
        // observing every event).
        Engine = new DeckEngine(Dispatcher, () => FloatPanel.SessionActive || SuppressEngineEvents);
        Service = new ModeAInterceptionService();
        Service.DeckKeyEvent += e =>
        {
            if (!SuppressEngineEvents) Engine.ProcessEvent(e);
            DeckEventObserved?.Invoke(e);
        };
        Engine.LoadPage(Config, Config.Settings.ActivePageId);

        // §156: this ctor runs on the UI thread (App.OnStartup), so
        // CurrentDispatcher IS the UI dispatcher - the only thread the
        // wheel hook may be installed from. Capture it once; the
        // driver-thread hold event marshals through it (see
        // OnDialHoldChanged). No hook is installed here.
        _uiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Engine.DialHoldChanged += OnDialHoldChanged;

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _tickTimer.Tick += (_, _) =>
        {
            DrainWheelDelta();
            Engine.Tick(Stopwatch.GetTimestamp());
        };
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _healthTimer.Tick += (_, _) =>
        {
            // Transisi ke Faulted dulu tidak tercatat di mana pun
            // (bukan di UI, bukan di crash.log, §45) - catat sekali.
            var state = Health.State;
            if (state == InterceptionState.Faulted && _lastLoggedHealthState != InterceptionState.Faulted)
            {
                // §137: a Faulted transition is degraded operation
                // (service down, LastError carried) - a warning. The
                // same state stays visible in UI status at Error level.
                CrashLog.WriteWarning("Service deck Faulted: " + (Health.LastError ?? "-"));
                // §156 service-fault stop path: a faulted service
                // delivers no more key-UPs, so a dial hold active at
                // the fault would otherwise leave the wheel swallowed
                // forever. Idempotent.
                StopWheelCapture();
            }
            _lastLoggedHealthState = state;
            HealthChanged?.Invoke();
        };
        _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _foregroundTimer.Tick += (_, _) => WatchForeground();

        // §113 LAST: the panel host needs Config/Dispatcher to exist
        // (the standalone controller must not touch either - its
        // contract forbids it; it only stores refs here). The host
        // ctor constructs no window; the panel is created lazily.
        var standalone = new StandalonePanelController(this, FloatPanel, (m, c, s) => NotifyFooter(m, c, s));
        PanelHost = new FloatPanelHost(this, FloatPanel, standalone, (m, c, s) => NotifyFooter(m, c, s));
    }

    // §172: volatile backing - the flag is read on the interception
    // worker thread (AppHost gate + DeckEngine predicate) and written
    // on the UI thread by the capture owners; same pattern as the
    // service's own volatile flags.
    private volatile bool _suppressEngineEvents;

    /// <summary>While true, deck events are observed (for assignment
    /// captures) but NOT executed - assigning a navigation key must not
    /// also fire whatever that key currently does. §172: the
    /// false→true edge ALSO unwinds engine state (see
    /// <see cref="ResetEngineForCaptureEngaged"/>) - a press that began
    /// before suppression engaged can never deliver its UP (gated
    /// above), so without the unwind its held/trigger state would
    /// stick and corrupt the next press of the same key.</summary>
    public bool SuppressEngineEvents
    {
        get => _suppressEngineEvents;
        set
        {
            bool was = _suppressEngineEvents;
            _suppressEngineEvents = value;
            if (value && !was) ResetEngineForCaptureEngaged();
        }
    }

    /// <summary>§172: suppression engaged (capture armed/started) -
    /// unwind exactly like Panic engaging (<see cref="SetPassthrough"/>):
    /// a held key's UP will never arrive while the gate is up, so the
    /// engine's held/combo/trigger records for it are stale, the wheel
    /// capture must end, and a held Momentary layer must unwind, or
    /// the layer sticks and the next press of the same key misfires.
    /// Runs only on the false→true edge; re-entrant-safe (the
    /// engine lock is re-entrant on the UI thread, briefly held on
    /// the driver thread) and idempotent (the same clears Panic
    /// already performs). Owners are UI-thread code only; the flag
    /// is never written before Engine/ActionsById exist (both are
    /// built in the ctor, before any view-model can set it).</summary>
    private void ResetEngineForCaptureEngaged()
    {
        StopWheelCapture();
        Engine.ResetForPassthrough();
        if (ActionsById.TryGetValue("deck", out var deckAction) && deckAction is PageSwitchAction pageSwitch)
            pageSwitch.ResetMomentary();
    }

    /// <summary>§130 - while true, <see cref="Save"/> is a no-op and
    /// <see cref="App"/> skips its exit-time save as well. Set BEFORE
    /// the app-data files are deleted by a data reset, so no writer
    /// that fires during teardown (foreground watcher, browser
    /// tracking callbacks, dispatcher) can resurrect the deleted
    /// config. Never persisted; the process exits right after.</summary>
    public bool SuppressSaveOnExit { get; set; }

    public ServiceHealth Health => Service.Health;
    public bool IsPassthrough => Service.Passthrough;

    public void Start()
    {
        _tickTimer.Start();
        _healthTimer.Start();
        _foregroundTimer.Start();

        // §161: backfill nama deck kosong - SETELAH timer jalan, SEBELUM
        // TryStartService (service belum jalan: tidak ada restart; Value/
        // Kind/RequiresReidentification tidak disentuh, hanya FriendlyName).
        // Tidak pernah melempar ke App.OnStartup (lihat BackfillDeckDisplayName).
        BackfillDeckDisplayName();

        var deck = Config.DeckDevice.ToDeviceId();
        if (deck.Kind == DeviceIdKind.InterceptionHardwareId && deck.IsValid
            && !Config.DeckDevice.RequiresReidentification)
        {
            TryStartService(deck);
        }
    }

    /// <summary>§161: isi <c>FriendlyName</c> yang kosong saat startup
    /// dari enumerasi perangkat (satu <c>Enumerate()</c> + satu
    /// <c>Resolve()</c>, sama seperti picker). Guard persis audit §4:
    /// hanya identitas InterceptionHardwareId yang valid, tanpa
    /// tuntutan re-identifikasi, dan nama kosong - lihat
    /// <see cref="DeckDisplayName.ShouldBackfill"/>. Hanya
    /// <c>FriendlyName</c> yang dimutasi; <c>Value</c>/<c>Kind</c>/
    /// <c>RequiresReidentification</c> tidak disentuh; service tidak
    /// dimulai ulang (di titik ini service belum jalan). Perangkat
    /// tidak ditemukan / enumerasi gagal → diam, tanpa save, tanpa
    /// log. Gagal simpan → penelan total (audit §4): nama lama
    /// dipulihkan di memori, <c>LastSaveError</c> dikembalikan ke null
    /// (jangan biarkan nilai basi dari penulisan kosmetik),
    /// <c>CrashLog.WriteDebug</c> (bukan error), startup + service
    /// lanjut normal. Seluruh blok tidak pernah melempar:
    /// <c>App.xaml.cs</c> memanggil <c>Start()</c> tanpa try/catch
    /// lokal, jadi kegagalan backfill apa pun tidak boleh membunuh
    /// startup.</summary>
    private void BackfillDeckDisplayName()
    {
        try
        {
            var deck = Config.DeckDevice.ToDeviceId();
            if (!DeckDisplayName.ShouldBackfill(
                    Config.DeckDevice.FriendlyName, deck, Config.DeckDevice.RequiresReidentification))
                return;
            var (devices, error) = InterceptionDeviceEnumerator.Enumerate();
            if (error is not null) return;
            var device = devices.FirstOrDefault(d =>
                string.Equals(d.HardwareId, deck.Value, StringComparison.OrdinalIgnoreCase));
            if (device is null) return;
            string name = DeckDisplayName.For(deck.Value, device.DeviceNumber, HidDeviceNameResolver.Resolve(devices));
            if (string.IsNullOrWhiteSpace(name)) return;
            string oldName = Config.DeckDevice.FriendlyName;
            Config.DeckDevice.FriendlyName = name;
            try { Save(); }
            catch (Exception ex)
            {
                Config.DeckDevice.FriendlyName = oldName;
                LastSaveError = null;
                CrashLog.WriteDebug("Backfill nama deck gagal disimpan (nama lama dipulihkan): " + ex.Message);
            }
        }
        catch
        {
            // Backfill tidak boleh menggagalkan startup apa pun yang terjadi.
        }
    }

    public bool TryStartService(DeviceId deck)
    {
        try
        {
            Service.Start(deck);
            return true;
        }
        catch (Exception)
        {
            // D8: Start() hanya melempar InvalidOperationException untuk
            // identitas tak valid / sudah jalan, tapi Thread.Start dan
            // jalur lain bisa melempar jenis lain - semuanya berarti
            // "service tidak jalan", bukan crash pemanggil. Health
            // membawa Faulted + LastError bila service sempat
            // mengaturnya; bila tidak, pemanggil wajib memperlakukan
            // false sebagai Service Off (header/tray menampilkannya).
            return false;
        }
    }

    public void StopService() => Service.Stop();

    public void SetPassthrough(bool value)
    {
        if (Service.Passthrough == value) return;
        Service.Passthrough = value;
        if (value)
        {
            // §156 passthrough stop path: a DIRECT idempotent stop,
            // not relying solely on the hold-ended event (which
            // ResetForPassthrough also raises). Held keys never
            // deliver their UP while passthrough is on.
            StopWheelCapture();
            // Panic mid-hold (§45): held keys never deliver their UP
            // while passthrough is on - reset engine state AND unwind
            // any held momentary layer, or keys/layers stick on return.
            Engine.ResetForPassthrough();
            if (ActionsById.TryGetValue("deck", out var deckAction) && deckAction is PageSwitchAction pageSwitch)
                pageSwitch.ResetMomentary();
        }
        PassthroughChanged?.Invoke(value);
    }

    public void SetGlobalEnabled(bool value)
    {
        if (Config.Settings.IsGlobalEnabled == value) return;
        bool oldValue = Config.Settings.IsGlobalEnabled;
        Config.Settings.IsGlobalEnabled = value;
        try { Save(); }
        catch (Exception ex)
        {
            // D8/D9: Save gagal = toggle DIBATALKAN total (nilai lama
            // dipulihkan, event tidak menyala) - dulu Save melempar ke
            // pemanggil (tray: hanya CrashLog; tombol: dialog crash)
            // sementara nilai baru sudah terlanjur di memori. User
            // diberitahu lewat LastSaveError → footer (RefreshHeader),
            // dan UI membaca ulang nilai lama sehingga tidak berbohong.
            Config.Settings.IsGlobalEnabled = oldValue;
            CrashLog.Write("Save toggle global gagal (nilai dipulihkan)", ex);
            return;
        }
        ReloadActivePage();
        GlobalEnabledChanged?.Invoke(value);
    }

    public void ReloadActivePage()
    {
        Engine.LoadPage(Config, Config.Settings.ActivePageId);
    }

    /// <summary>Pages of the active profile - shared by PageSwitchAction
    /// (runtime) and the binding editor (target dropdown).</summary>
    public IReadOnlyList<(string Id, string Name)> ActiveProfilePages()
        => Config.ActiveProfile?.Pages.Select(p => (p.Id, p.Name)).ToList()
           ?? new List<(string, string)>();

    /// <summary>All profiles - shared by ProfileSwitchAction (runtime)
    /// and the binding editor (target dropdown).</summary>
    public IReadOnlyList<(string Id, string Name)> AllProfiles()
        => Config.Profiles.Select(p => (p.Id, p.Name)).ToList();

    /// <summary>Switches the MAIN profile (used by deck keys via
    /// ProfileSwitchAction). Same landing as an auto-profile switch:
    /// the profile's first page becomes active and the UI refreshes
    /// through the existing event.</summary>
    public bool SwitchToProfile(string profileId)
    {
        var profile = Config.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return false;
        if (Config.ActiveProfileId == profileId) return true;
        string oldProfileId = Config.ActiveProfileId;
        string oldPageId = Config.Settings.ActivePageId;
        Config.ActiveProfileId = profileId;
        Config.Settings.ActivePageId = profile.Pages.FirstOrDefault()?.Id ?? "";
        ReloadActivePage();
        try { Save(); }
        catch (Exception)
        {
            // D9: gagal simpan = pindah profil DIBATALKAN total (id
            // lama dipulihkan + halaman aktif dimuat ulang, event
            // tidak menyala). false → aksi deck melaporkan Fail
            // (S1203) ke footer; jalur tray mengandalkan LastSaveError
            // → footer. Dulu Save melempar: aksi deck memang
            // menangkapnya jadi Fail, tapi tray hanya menulis CrashLog
            // sementara memori sudah pindah profil.
            Config.ActiveProfileId = oldProfileId;
            Config.Settings.ActivePageId = oldPageId;
            ReloadActivePage();
            return false;
        }
        ActiveProfileChangedByAutoSwitch?.Invoke();
        return true;
    }

    /// <summary>Switches the active page (used by deck keys via
    /// PageSwitchAction). Returns false when the id is not a page of the
    /// active profile.</summary>
    public bool SwitchToPage(string pageId)
    {
        var profile = Config.ActiveProfile;
        if (profile is null || profile.Pages.All(p => p.Id != pageId)) return false;
        string oldPageId = Config.Settings.ActivePageId;
        Config.Settings.ActivePageId = pageId;
        try { Save(); }
        catch (Exception)
        {
            // D9: sama seperti SwitchToProfile - gagal simpan =
            // pindah halaman dibatalkan total, false → aksi deck Fail,
            // tray mengandalkan LastSaveError → footer.
            Config.Settings.ActivePageId = oldPageId;
            ReloadActivePage();
            return false;
        }
        ReloadActivePage();
        PageSwitchedByAction?.Invoke();
        return true;
    }

    // --- Identification orchestration -------------------------------------
    // The main service is stopped first: one active interception consumer
    // at a time. On completion the new identity is stored and the service
    // restarts with it.

    public event Action<string>? IdentificationFailed;

    public void BeginIdentification()
    {
        StopService();
        _identifyMachine = new DeckIdentification();
        _identifySession = new IdentificationSession();
        _identifySession.KeyDownObserved += OnIdentificationPress;
        _identifySession.IdentificationFailed += reason => IdentificationFailed?.Invoke(reason);
        _identifySession.Start();
        IdentificationProgress?.Invoke(_identifyMachine);
    }

    public void CancelIdentification()
    {
        _identifySession?.Stop();
        _identifySession?.Dispose();
        _identifySession = null;
        _identifyMachine = null;
        // Restore the service with the previously stored identity, if any.
        var deck = Config.DeckDevice.ToDeviceId();
        if (deck.Kind == DeviceIdKind.InterceptionHardwareId && deck.IsValid)
            TryStartService(deck);
    }

    private void OnIdentificationPress(DeviceId device)
    {
        var machine = _identifyMachine;
        if (machine is null) return;
        var stage = machine.Observe(device);
        IdentificationProgress?.Invoke(machine);
        if (stage != IdentificationStage.Completed) return;

        var deck = machine.DeckCandidate!.Value;
        _identifySession?.Stop();
        _identifySession?.Dispose();
        _identifySession = null;
        _identifyMachine = null;

        var oldDeckDevice = Config.DeckDevice;
        // §161: resolve nama segar untuk perangkat yang BARU diidentifikasi.
        // Resolusi sukses SELALU menimpa nama (hasil resolve tidak pernah
        // kosong - fallback VID/PID/Keyboard #N), menutup cacat kedua
        // audit: nama lama ("DELL") menempel di perangkat baru. Gagal
        // enumerasi total → nama lama dipertahankan; identifikasi tetap
        // sukses. Kegagalan resolusi tidak pernah menggagalkan
        // identifikasi (lihat ResolveDeckDisplayName).
        string friendlyName = ResolveDeckDisplayName(deck, oldDeckDevice.FriendlyName);
        Config.DeckDevice = new DeviceBinding
        {
            Kind = nameof(DeviceIdKind.InterceptionHardwareId),
            Value = deck.Value,
            FriendlyName = friendlyName,
            RequiresReidentification = false,
        };
        try { Save(); }
        catch (Exception ex)
        {
            // D8: Save gagal dulu = wizard MENGGANTUNG - Completed
            // tidak pernah menyala, Failed tidak pernah menyala, dan
            // teks terakhir yang tampil adalah langkah terakhir,
            // sampai timer 120 detik wizard menampilkan pesan timeout
            // yang menyesatkan. Kini: identitas lama dipulihkan
            // (memori = file), wizard langsung diberi alasan lewat
            // IdentificationFailed (sudah ditangani WizardWindow),
            // dan Completed TIDAK menyala.
            Config.DeckDevice = oldDeckDevice;
            CrashLog.Write("Save identifikasi deck gagal (identitas lama dipulihkan)", ex);
            IdentificationFailed?.Invoke(Localizer.Get("S0937") + " " + ex.Message);
            return;
        }
        if (!TryStartService(deck))
        {
            // D8: deck SUDAH tersimpan, tapi service tidak jalan -
            // Completed dengan pesan "service dimulai ulang" akan
            // berbohong. IdentificationFailed membawa alasan eksplisit
            // (status Service Off + LastError service, keduanya string
            // yang sudah ada); wizard tetap terbuka, dan menutupnya
            // memulihkan service lewat CancelIdentification.
            string? detail = Health.LastError;
            IdentificationFailed?.Invoke(Localizer.Get("S0353")
                + (string.IsNullOrEmpty(detail) ? "" : ": " + detail));
            return;
        }
        IdentificationCompleted?.Invoke(deck);
    }

    /// <summary>§161: nama tampilan untuk hardware ID yang baru
    /// diidentifikasi, lewat enumerasi + resolver yang sama dengan
    /// picker (<see cref="InterceptionDeviceEnumerator.Enumerate"/> +
    /// <see cref="HidDeviceNameResolver.Resolve"/>), dirender lewat
    /// helper bersama <see cref="DeckDisplayName.For"/> (nama asli →
    /// S0882 VID/PID → S0883). Aturan gagal (audit §2):
    /// enumerasi gagal total (error string / exception) ATAU perangkat
    /// tidak ditemukan dalam enumerasi → <paramref name="oldFriendlyName"/>
    /// dipertahankan, identifikasi tetap sukses; enumerasi berhasil
    /// tetapi nama tak ditemukan → fallback helper (tidak pernah
    /// kosong). Tidak pernah melempar: semua kegagalan ditelan di sini,
    /// supaya resolusi nama tidak pernah menggagalkan identifikasi.
    /// Dipanggil setelah session berhenti dan sebelum Save, di thread
    /// yang sama dengan perilaku existing.</summary>
    private static string ResolveDeckDisplayName(DeviceId deck, string oldFriendlyName)
    {
        try
        {
            var (devices, error) = InterceptionDeviceEnumerator.Enumerate();
            if (error is not null) return oldFriendlyName;
            var device = devices.FirstOrDefault(d =>
                string.Equals(d.HardwareId, deck.Value, StringComparison.OrdinalIgnoreCase));
            if (device is null) return oldFriendlyName;
            return DeckDisplayName.For(deck.Value, device.DeviceNumber, HidDeviceNameResolver.Resolve(devices));
        }
        catch
        {
            return oldFriendlyName;
        }
    }

    // --- Auto-profile ------------------------------------------------------

    private void WatchForeground()
    {
        string? process = NativeMethods.ForegroundProcessName();
        if (process is null) return;
        // Re-evaluate when the foreground WINDOW changes even if the
        // process is the same (e.g. focusing another Chrome profile
        // window): exe triggers must react on focus, not only on a
        // process change. Same process + same window = nothing new.
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (process == _lastForegroundProcess && hwnd == _lastForegroundHwnd) return;
        _lastForegroundProcess = process;
        _lastForegroundHwnd = hwnd;

        // B-01: pelindung luar timer. Badan di bawah ini (sesudah
        // _last ditulis) dibungkus try/catch: lemparan yang lolos
        // di luar try/catch Save di dalam (Evaluate/matcher/lock/
        // Arm/Disarm/event) dicatat ke CrashLog, tanpa dialog -
        // timer 700ms memanggil WatchForeground tanpa try/catch
        // (:318), jadi lemparan di sini dulu naik ke
        // DispatcherUnhandledException. _last SUDAH ditulis di atas,
        // jadi tick berikutnya untuk foreground yang sama tetap
        // no-op; catch ini TIDAK me-rollback (rollback milik jalur
        // Save di dalam, yang tidak diubah) dan hanya menangkap
        // yang lolos dari mereka.
        try
        {
            // Switch-back armed checks FIRST, before any normal matching
            // (the _lastForegroundProcess early-return above stays first,
            // unchanged): (a) divergence from the armed target disarms
            // silently and normal matching proceeds; (b) leaving the
            // trigger set fires the return and falls through to
            // normal matching in the same tick; (c) still on target
            // inside the set stays armed, observes the tick for the
            // manual-switch lock, and consumes the tick. Evaluate
            // already disarmed on (a)/(b); the return point for (b)
            // travels in the evaluation result.
            var armed = _switchBack.Evaluate(Config.ActiveProfileId, Config.Settings.ActivePageId, process);
            if (armed.Decision == SwitchBackDecision.StayArmed)
            {
                // Observe the tick for the manual-switch lock with the
                // armed target's trigger set, so the lock's prev-record
                // stays current while armed instead of going stale: the
                // decision is ignored (this tick is consumed either
                // way) and cannot engage the lock here - StayArmed
                // means the current (profile, page) is the armed
                // target, which is also the lock's auto baseline, so
                // there is no divergence. TriggerBareSet is still
                // armed here (Evaluate disarms only on the other
                // decisions).
                _manualLock.Evaluate(Config.ActiveProfileId, Config.Settings.ActivePageId, process, _switchBack.TriggerBareSet);
                return;
            }
            if (armed.Decision == SwitchBackDecision.FireReturn)
            {
                // No return: fall through to normal matching in this
                // same tick, so a trigger claimed by the new foreground
                // is not lost to the _lastForeground early-return on the
                // next tick. At most one return + one matching switch
                // per tick; FireSwitchBackReturn already ran NoteAutoSet
                // (on a successful return), so the fall-through lock
                // observation below sees no false divergence, and every
                // fall-through path observes the tick for the lock
                // (mode match, profile match, or no match), leaving a
                // prev-record outside the returned-from set.
                FireSwitchBackReturn(armed.ReturnProfileId, armed.ReturnPageId);
            }

            // Mode triggers FIRST: a mode claim beats a profile claim for the
            // same process, and first-match-wins (profile order, then page
            // order) deterministically resolves double claims (see
            // AutoProfileMatcher.FindPageForProcess). Guard differs from the
            // profile path below on purpose: the profile early-return on
            // "same profile already active" would block same-profile mode
            // switches, so a mode match proceeds whenever its target page
            // differs from the active page, and no-ops only when the owning
            // profile AND the mode are already active. A no-op mode match
            // still consumes the tick (return, no fall-through): mode
            // precedence means the profile claim for this process must not
            // fire while the mode is already showing. A no-op guard hit
            // changes NOTHING about the arm: arming/disarming below run
            // only after a successful switch, and a guard return never
            // reaches them.
            var modeMatch = AutoProfileMatcher.FindPageForProcess(Config, process);
            if (modeMatch is not null)
            {
                var (modeProfile, modePage) = modeMatch.Value;
                if (_manualLock.Evaluate(Config.ActiveProfileId, Config.Settings.ActivePageId, process, modePage.AutoSwitchExe) == ManualLockDecision.Suppress) return;
                if (modeProfile.Id == Config.ActiveProfileId
                    && modePage.Id == Config.Settings.ActivePageId) return;

                string oldModeProfileId = Config.ActiveProfileId;
                string oldModePageId = Config.Settings.ActivePageId;
                Config.ActiveProfileId = modeProfile.Id;
                Config.Settings.ActivePageId = modePage.Id;
                ReloadActivePage();
                // Save dari timer TIDAK BOLEH menjadi dialog (lihat §45 di
                // jalur profil di bawah). Gagal = rollback kedua id +
                // ReloadActivePage + CrashLog, tanpa event — sama seperti
                // jalur profil.
                try { Save(); }
                catch (Exception ex)
                {
                    Config.ActiveProfileId = oldModeProfileId;
                    Config.Settings.ActivePageId = oldModePageId;
                    ReloadActivePage();
                    CrashLog.Write("Save auto-switch gagal", ex);
                    return;
                }
                // Arm/disarm runs ONLY after the Save succeeded: a
                // rolled-back switch never touches the arm. The flag is
                // read from the SWITCHED-TO page (the trigger owner);
                // flag OFF replaces any previous arm with no arm.
                if (modePage.SwitchBackOnAutoSwitch)
                    _switchBack.Arm(oldModeProfileId, oldModePageId, modeProfile.Id, modePage.Id, modePage.AutoSwitchExe);
                else
                    _switchBack.Disarm();
                _manualLock.NoteAutoSet(modeProfile.Id, modePage.Id);
                // Same event as the profile path: MainViewModel maps it to
                // RefreshProfilesAndPages, which re-reads ActiveProfile AND
                // ActivePageId and rebuilds Pages/SelectedPage/grid, so a
                // page-only (same-profile) switch refreshes the UI fully
                // with no MainViewModel change. Tray subscribes too.
                ActiveProfileChangedByAutoSwitch?.Invoke();
                return;
            }

            var match = AutoProfileMatcher.FindProfileForProcess(Config, process);
            if (match is null)
            {
                // No claim: still observe the tick so the lock sees
                // leaving its set and the prev-record stays current.
                _manualLock.Evaluate(Config.ActiveProfileId, Config.Settings.ActivePageId, process, null);
                return;
            }
            if (_manualLock.Evaluate(Config.ActiveProfileId, Config.Settings.ActivePageId, process, match.AutoSwitchExe) == ManualLockDecision.Suppress) return;
            if (match.Id == Config.ActiveProfileId) return;

            string oldProfileId = Config.ActiveProfileId;
            string oldPageId = Config.Settings.ActivePageId;
            Config.ActiveProfileId = match.Id;
            Config.Settings.ActivePageId = match.Pages.FirstOrDefault()?.Id ?? "";
            ReloadActivePage();
            // Save dari timer TIDAK BOLEH menjadi dialog: dulu exception di
            // sini naik ke DispatcherUnhandledException -> dialog modal,
            // dan karena timer terus tick, dialog yang sama muncul berulang
            // (§45). Gagal dicatat di LastSaveError untuk status menetap.
            try { Save(); }
            catch (Exception ex)
            {
                Config.ActiveProfileId = oldProfileId;
                Config.Settings.ActivePageId = oldPageId;
                ReloadActivePage();
                CrashLog.Write("Save auto-switch gagal", ex);
                return;
            }
            // Arm/disarm, same rule as the mode path: flag read from the
            // SWITCHED-TO profile, trigger set = the profile's full list,
            // only after a successful Save.
            if (match.SwitchBackOnAutoSwitch)
                _switchBack.Arm(oldProfileId, oldPageId, match.Id, Config.Settings.ActivePageId, match.AutoSwitchExe);
            else
                _switchBack.Disarm();
            _manualLock.NoteAutoSet(match.Id, Config.Settings.ActivePageId);
            ActiveProfileChangedByAutoSwitch?.Invoke();
        }
        catch (Exception ex)
        {
            CrashLog.Write("WatchForeground gagal", ex);
        }
    }

    /// <summary>Fires an armed switch-back return: switches to the
    /// remembered (profile, page), with the fallback that a
    /// remembered page which no longer exists resolves to the
    /// remembered profile's first page. A remembered profile that no
    /// longer exists (or has no pages) disarms silently with no
    /// switch — the state is already disarmed by Evaluate, so there
    /// is nothing left to clear. The return uses the SAME event +
    /// refresh path as an auto-switch (ActiveProfileChangedByAutoSwitch
    /// + ReloadActivePage + Save); a Save failure rolls both ids
    /// back + ReloadActivePage + CrashLog, with no dialog, exactly
    /// like the auto-switch paths. A resolved return equal to the
    /// current state is a silent no-op (no Save, no event).</summary>
    private void FireSwitchBackReturn(string returnProfileId, string returnPageId)
    {
        var profile = Config.Profiles.FirstOrDefault(p => p.Id == returnProfileId);
        if (profile is null || profile.Pages is null || profile.Pages.Count == 0) return;
        string targetPageId = profile.Pages.Any(p => p.Id == returnPageId)
            ? returnPageId
            : profile.Pages[0].Id;
        if (profile.Id == Config.ActiveProfileId && targetPageId == Config.Settings.ActivePageId) return;

        string oldProfileId = Config.ActiveProfileId;
        string oldPageId = Config.Settings.ActivePageId;
        Config.ActiveProfileId = profile.Id;
        Config.Settings.ActivePageId = targetPageId;
        ReloadActivePage();
        try { Save(); }
        catch (Exception ex)
        {
            Config.ActiveProfileId = oldProfileId;
            Config.Settings.ActivePageId = oldPageId;
            ReloadActivePage();
            CrashLog.Write("Save auto-switch gagal", ex);
            return;
        }
        _manualLock.NoteAutoSet(profile.Id, targetPageId);
        ActiveProfileChangedByAutoSwitch?.Invoke();
    }

    // --- Persistence -------------------------------------------------------

    private readonly object _saveGate = new();

    /// <summary>Save terakhir yang gagal (pesan error), atau null bila save
    /// terakhir berhasil. Dipakai UI untuk status menetap - Save yang
    /// gagal tidak boleh hilang begitu saja, tapi juga tidak boleh
    /// menjadi dialog setiap tick timer (lihat WatchForeground).</summary>
    public string? LastSaveError { get; private set; }

    /// <summary>First-run default Global icons: imports the 5 embedded
    /// user-supplied PNGs (Assets/DefaultGlobal) through IconStore -
    /// the same content-addressed, ≤128px, atomic path every upload
    /// uses - and assigns the returned IconValues to the matching
    /// seeded Global cards (matched by label, not by position, so a
    /// future reorder cannot mis-assign an icon). Mapping is DECIDED,
    /// not guessed: window graphics go to window actions - Change
    /// Windows &lt;- Next Tab (forward arrow over a window),
    /// Change Windows Back &lt;- Previous Tab (back arrow over a
    /// window); monitor graphics go to desktop actions. The 2 Mode
    /// cards get NO icon here (they keep the default tile icon); the
    /// rejected alternative (Tab icons -&gt; Mode cards) would put
    /// window graphics on deck-layer actions. Returns true when at
    /// least one icon was assigned (caller Saves). A missing resource
    /// or a failed import for ONE card skips that card only; the
    /// caller catches anything escaping here, so startup never dies
    /// on icons. Resource lookup is by filename SUFFIX over the
    /// assembly manifest (robust to the RootNamespace prefix).</summary>
    private bool SeedDefaultGlobalIcons()
    {
        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Change Windows"] = "ChangeWindows.png",
            ["Change Windows Back"] = "ChangeWindowsBack.png",
            ["New Desktop"] = "NewDesktop.png",
            ["Previous Desktop"] = "PreviousDesktop.png",
            ["Next Desktop"] = "NextDesktop.png",
            // Pill graphics (user-supplied composites, chevron-disc end
            // cropped square + green keyed to transparent): the disc
            // fills the 46x46 tile box; the full wide pill would render
            // ~12px tall and illegible.
            ["Previous Page"] = "PrevPage.png",
            ["Next Page"] = "NextPage.png",
        };
        var assembly = typeof(AppHost).Assembly;
        var names = assembly.GetManifestResourceNames();
        long maxBytes = (long)Config.Settings.IconUploadMaxKb * 1024;
        bool changed = false;
        foreach (var card in Config.GlobalPage.Bindings)
        {
            if (!byLabel.TryGetValue(card.Label, out string? file)) continue;
            if (!string.IsNullOrWhiteSpace(card.Icon)) continue;
            string? resource = names.FirstOrDefault(n =>
                n.EndsWith("." + file, StringComparison.OrdinalIgnoreCase));
            if (resource is null) continue;
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is null) continue;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                card.Icon = IconStore.ImportBytes(ms.ToArray(), maxBytes).IconValue;
                changed = true;
            }
            catch
            {
                // One card's icon failing must not take the others down.
            }
        }
        return changed;
    }

    public void Save()
    {
        // §130: a data reset sets SuppressSaveOnExit BEFORE deleting
        // the app-data files; every Save after that (exit path,
        // foreground timer, browser tracking callbacks, in-flight
        // dispatcher work) must write nothing, or the deleted config
        // comes back. The flag is set only moments before the process
        // exits, so no caller can observe the no-op in normal use.
        if (SuppressSaveOnExit) return;
        // Semua Save (UI timer, editor, tray, worker wizard) lewat satu
        // lock: ConfigStore memakai satu path temp bersama, jadi dua
        // Save bersamaan = sharing-violation pada salah satunya (§45).
        // Lock TIDAK menelan error: pemanggil yang harus memutuskan
        // (dialog / status / restore) - Save tetap melempar.
        lock (_saveGate)
        {
            try
            {
                Store.Save(Config);
                ApplyStartWithWindows();
                // §137: the level applies IMMEDIATELY on every
                // successful Save - no restart. Save() is the single
                // persistence choke point (Settings SaveAll, the
                // immediate-save toggles, tray, profile/page switches),
                // so applying here covers every writer; the call is
                // idempotent when LogLevel did not change. A failed
                // Save throws before this line, so the old level stays
                // active - matching the rollback discipline of the
                // callers. Never throws (CrashLog.SetLevel guards).
                CrashLog.SetLevel(Config.Settings.LogLevel);
                LastSaveError = null;
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                throw;
            }
        }
    }

    private void ApplyStartWithWindows()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        const string taskName = "KeystreamStartup";

        try
        {
            if (Config.Settings.StartWithWindows)
            {
                // Daftarkan via Task Scheduler dengan hak akses Administrator tertinggi (/rl highest)
                // agar menyala otomatis saat login Windows tanpa hambatan pop-up UAC.
                var psi = new ProcessStartInfo("schtasks.exe",
                    $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);

                // Cadangan di Registry Run
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.SetValue("Keystream", $"\"{exePath}\"");
            }
            else
            {
                // Hapus task dari Task Scheduler
                var psi = new ProcessStartInfo("schtasks.exe",
                    $"/delete /tn \"{taskName}\" /f")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);

                // Hapus dari Registry Run
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.DeleteValue("Keystream", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            CrashLog.WriteWarning($"ApplyStartWithWindows failed: {ex.Message}");
        }
    }

    /// <summary>§151 P3 - the active reset quiesce, if any. Non-null
    /// only between <see cref="BeginResetQuiesce"/> and either
    /// <see cref="RestoreAfterFailedReset"/> (delete failed) or
    /// process exit (delete succeeded). Guards both double-quiesce
    /// (the save gate is a re-entrant Monitor - entering it twice
    /// on the UI thread would need two releases) and double-restore.</summary>
    private ResetQuiesceState? _resetQuiesce;

    /// <summary>§130 - deletes the app's own data directory,
    /// %AppData%\Keystream, and everything under it. The set is
    /// enumerated from the code that writes there, and nothing else:
    /// ConfigStore writes config.json + config.json.bak1–3 (+ a
    /// .tmp during save), IconStore writes the Icons folder, and
    /// CrashLog writes crash.log; no other writer targets this root
    /// (browser User Data folders live elsewhere, the Interception
    /// driver is never touched, OBS screenshots go to
    /// Pictures\Keystream). The deletion itself lives in the
    /// harness-linkable AppDataReset helper; this method only
    /// supplies the store paths and the verified root.</summary>
    public bool TryDeleteAppData(out string? failureDetail)
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keystream");
        return AppDataReset.TryDelete(Store.MainPath, IconStore.Folder, expected, out failureDetail);
    }

    /// <summary>§151 P3 - quiesce before a data reset deletes
    /// anything: stops the three UI-thread timers, stops the
    /// interception service, and drains background Saves. MUST be
    /// called on the UI thread AFTER the caller has raised
    /// <see cref="SuppressSaveOnExit"/> (App.ResetDataAndRestart
    /// does): the flag makes every Save that STARTS after it a
    /// no-op, and acquiring <see cref="_saveGate"/> here waits
    /// out the one Save that may already be in flight (its
    /// FileShare.None .tmp write + File.Move are the F3 race) -
    /// after this returns, no Save can hold a file under the root
    /// or recreate config.json mid-deletion. The gate is held
    /// until <see cref="RestoreAfterFailedReset"/>; on the
    /// success path the process exits, so it is never released.
    /// Safe to call when the service/timers are already stopped
    /// (Stop is idempotent; only enabled timers are recorded for
    /// restore). A second call without a restore returns the
    /// existing snapshot WITHOUT re-entering the gate. Browser
    /// tracking callbacks and dispatcher work reach the file
    /// system only through Save(), so the flag + gate cover them;
    /// a deck action already running when quiesce begins is NOT
    /// waited on - stopping the service only prevents NEW deck
    /// input - and a direct IconStore write from such an action
    /// remains the one ungated writer (PC CARA; see §151).</summary>
    public ResetQuiesceState BeginResetQuiesce()
    {
        if (_resetQuiesce is { } existing) return existing;
        var snapshot = new ResetQuiesceState
        {
            TickTimerWasEnabled = _tickTimer.IsEnabled,
            HealthTimerWasEnabled = _healthTimer.IsEnabled,
            ForegroundTimerWasEnabled = _foregroundTimer.IsEnabled,
            ServiceWasRunning = Service.Health.State == InterceptionState.Running,
        };
        _tickTimer.Stop();
        _healthTimer.Stop();
        _foregroundTimer.Stop();
        // Idempotent: ModeAInterceptionService.Stop with a null
        // worker returns immediately, so the §130 teardown's
        // second StopService (and Dispose) on the success path is
        // harmless.
        StopService();
        // Monitor, not lock: acquisition and release live in two
        // methods on the same (UI) thread. A background Save holds
        // the gate only for one Store.Save + registry write; it
        // never waits on the UI thread, so this cannot deadlock.
        Monitor.Enter(_saveGate);
        _resetQuiesce = snapshot;
        return snapshot;
    }

    /// <summary>§151 P3 - restores a quiesced host after a FAILED
    /// delete, so the app keeps running with its config intact:
    /// releases the save gate, restarts exactly the timers that
    /// were enabled, and restarts the service ONLY if it was
    /// running before (same deck-validity conditions as startup;
    /// a failed restart leaves the service off, honestly shown
    /// by the health surface). No-op without a prior quiesce, and
    /// no-op on a second call (the snapshot is single-use).
    /// NEVER called on the success path: there the process exits
    /// and the §130 teardown (StopService/Dispose, mutex release,
    /// restart) runs with the gate still held.</summary>
    public void RestoreAfterFailedReset()
    {
        var snapshot = _resetQuiesce;
        if (snapshot is null || snapshot.Restored) return;
        snapshot.MarkRestored();
        _resetQuiesce = null;
        Monitor.Exit(_saveGate);
        if (snapshot.TickTimerWasEnabled) _tickTimer.Start();
        if (snapshot.HealthTimerWasEnabled) _healthTimer.Start();
        if (snapshot.ForegroundTimerWasEnabled) _foregroundTimer.Start();
        var deck = Config.DeckDevice.ToDeviceId();
        if (snapshot.ShouldRestartService(
                deck.Kind == DeviceIdKind.InterceptionHardwareId && deck.IsValid,
                Config.DeckDevice.RequiresReidentification))
            TryStartService(deck);
    }

    // --- §156 ScrollDial wheel-capture lifecycle ------------------------
    //
    // Generation-counter mechanism (the #1 correctness risk):
    //
    // The hold event fires on the interception/driver thread, which
    // has no message pump - installing the hook there stalls delivery.
    // So OnDialHoldChanged does exactly two things on the driver
    // thread: (1) Interlocked-increment _wheelGeneration, stamping
    // THIS event with a strictly increasing generation number, in
    // event order; (2) BeginInvoke the apply step onto the UI
    // dispatcher. BeginInvoke preserves queue order, and Apply
    // discards any closure whose generation is no longer the latest
    // (a newer event has superseded it). A fast press-release can
    // therefore never leave the hook installed: the DOWN closure is
    // either applied-then-stopped (in queue order) or discarded as
    // stale by the UP's newer generation before it runs.
    //
    // Every synchronous stop path (passthrough, service fault, app
    // exit) calls StopWheelCapture, which ALSO bumps the generation:
    // any hold-started closure still queued on the dispatcher becomes
    // stale and can never reinstall the hook after the stop. All
    // Apply/Stop state (_wheelActiveKey, hook install/uninstall)
    // runs only on the UI thread, so no lock is needed for it.
    //
    // ONE active dial: _wheelActiveKey records the key whose hold
    // installed the hook. A second dial DOWN while one is held is
    // ignored for capture, and that second key's release is ignored
    // too (key-identity check in Apply) - it must NOT stop the
    // first dial's capture.

    private void OnDialHoldChanged(KeyIdentity key, BindingConfig? binding)
    {
        long generation = Interlocked.Increment(ref _wheelGeneration);
        try
        {
            _uiDispatcher.BeginInvoke(new Action(() => ApplyDialHoldChanged(key, binding, generation)));
        }
        catch
        {
            // Dispatcher shutting down (app exit): the queued apply
            // can never run, and the process - hook included - dies
            // with it. Never throw into the driver thread.
        }
    }

    private void ApplyDialHoldChanged(KeyIdentity key, BindingConfig? binding, long generation)
    {
        if (_wheelDisposed) return;
        // Stale closure: a newer event (or a synchronous stop) has
        // superseded this one. Discarding it is what makes a fast
        // press-release and a stop-during-queue both safe.
        if (generation != Volatile.Read(ref _wheelGeneration)) return;
        if (binding is not null)
        {
            // Hold started. A second dial DOWN while one is held is
            // ignored for capture (first dial keeps owning the wheel).
            if (_wheelActiveKey.HasValue) return;
            if (!_wheelHook.Start())
            {
                CrashLog.WriteWarning("WheelCaptureHook gagal dipasang untuk ScrollDial (roda tidak ditangkap).");
                return;
            }
            _wheelActiveKey = key;
        }
        else
        {
            // Hold ended. Only the key that owns the capture may stop
            // it - another dial key's release leaves capture running.
            if (_wheelActiveKey != key) return;
            StopWheelCaptureCore();
        }
    }

    /// <summary>Drains the hook's lock-free pending delta into the
    /// engine - the ONLY path by which wheel input reaches
    /// DeckEngine.AddDialDelta. Called from the UI tick before
    /// Engine.Tick, so the engine's coalescing sees this tick's
    /// wheel movement in the same pass.</summary>
    private void DrainWheelDelta()
    {
        int delta = _wheelHook.DrainPendingDelta();
        if (delta != 0) Engine.AddDialDelta(delta);
    }

    /// <summary>Stops capture and invalidates any queued hold-started
    /// closure (generation bump). Idempotent. Used by every
    /// synchronous stop path: passthrough, service fault, exit.</summary>
    private void StopWheelCapture()
    {
        Interlocked.Increment(ref _wheelGeneration);
        StopWheelCaptureCore();
    }

    private void StopWheelCaptureCore()
    {
        _wheelActiveKey = null;
        try { _wheelHook.Stop(); }
        catch (Exception ex) { CrashLog.Write("Stop WheelCaptureHook gagal", ex); }
    }

    public void Dispose()
    {
        // §156 exit stop path: unsubscribe first so no new hold event
        // can queue an apply after teardown, then stop capture
        // (generation bump invalidates anything already queued).
        // Dispose runs from ExitApp AND OnExit; the second call is a
        // no-op here (unsubscribe + idempotent Stop).
        _wheelDisposed = true;
        try { Engine.DialHoldChanged -= OnDialHoldChanged; }
        catch (Exception ex) { CrashLog.Write("Lepas DialHoldChanged saat keluar gagal", ex); }
        try { StopWheelCapture(); }
        catch (Exception ex) { CrashLog.Write("Stop WheelCaptureHook saat keluar gagal", ex); }
        // §113: close the floating panel FIRST (exit path only -
        // Dispose runs only from ExitApp/OnExit, before Shutdown).
        // Cancels any running countdown via OnPanelHidden before the
        // service/dispatcher teardown below. Idempotent (a second
        // Dispose, from OnExit after ExitApp, is a no-op here).
        try { PanelHost?.ClosePanel(); }
        catch (Exception ex) { CrashLog.Write("Tutup panel mengambang saat keluar gagal", ex); }
        _tickTimer.Stop();
        _healthTimer.Stop();
        _foregroundTimer.Stop();
        _identifySession?.Dispose();
        Service.Dispose();
        Dispatcher.Dispose();
        Obs.Dispose();
        StreamerBot.Dispose();
    }
}
