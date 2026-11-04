using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.App.Services;
using Keystream.Core.Config;
using Keystream.Core.Localization;

namespace Keystream.App.ViewModels;

/// <summary>§158 (Worker A) - one step of a combination ("Kombinasi
/// Tombol"), edited with the MAIN editor's action pattern: the same
/// action catalog, the same ~25 per-action fields, the same pure
/// mapping through <see cref="ActionStepCodec"/>. Host-free: no
/// <c>AppHost</c> anywhere in this class. The owning
/// <c>BindingEditorViewModel</c> pushes every live list (browser
/// detection, profiles, groups, macros, pages, OBS/Streamer.bot
/// discovery) into this step and subscribes to its
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> to run
/// the per-step refreshes; this class only raises the computed
/// visibility/label properties when its own fields change.
/// <para>Contract surface (Worker B binds to these names): all
/// <c>Is*</c> flags, all fields, all option lists, <c>DelayAfterMs</c>,
/// <c>DeleteStepCommand</c>, the
/// <c>RequestManageBrowserGroups</c>/<c>RequestManageMacros</c> events,
/// <c>LoadFromStep</c>/<c>ToStep</c>. Option-list names follow the MAIN
/// view-model's member names where they differ from the §158 task
/// list - <c>MediaOptions</c> (not MediaCommands), <c>BasicOptions</c>,
/// <c>DeckOptions</c>, <c>ObsOptions</c>, <c>BrowserProfiles</c> (not
/// BrowserProfileOptions) - and <c>BrowserOptions</c> is included for
/// the browser dropdown although the task list does not name it.
/// <c>CurrentWindowsOptions</c> mirrors the main VM's session-aware
/// picker (legacy "windows" steps see the full union).
/// <see cref="DelayAfterMs"/> is persisted per step (A5: no delay UI -
/// the macro step pattern is code-behind, not a reusable control -
/// so a fresh step always carries 0 and nothing in the UI edits it;
/// a delay already stored on a step round-trips untouched).</para>
    /// <para>§159: parity surface added on top of the §158
    /// contract (Worker B, from Worker A's gap table G-01–G-14).
    /// Every new command is STEP-OWNED: it mutates only this step's
    /// properties, or raises a <c>Request*</c> event the owner
    /// answers for THIS step (the owner closure captures the step).
    /// No step command ever writes a main-editor property - the trap
    /// §159 exists to close. Dialogs are never opened here (host-free):
    /// browse commands raise <c>RequestBrowse*</c> / the Pindai command
    /// raises <c>RequestPindaiBrowsers</c>, and the owner forwards the
    /// shared <see cref="EditorDialogSpecs"/> spec to the window.
    /// <c>IconFetchStatus</c> is deliberately never filled (G-14:
    /// icon auto-fetch is a main-card concern; the property exists so
    /// the four status rows mirror the main sections and stay
    /// collapsed). <c>HasTrackedBrowserWindow</c> is owner-maintained.
    /// The app-drawer lists are owner-filled from the shared cache;
    /// picking an item writes only this step's
    /// <c>LauncherTarget</c> - never the combination Label, which the
    /// main handler fills but no step owns.</para></summary>
public sealed partial class ComboStepViewModel : ObservableObject
{
    private bool _openedAsLegacyWindows;
    private IReadOnlyList<ActionCatalog.ActionOption> _actionOptionsBase = ActionCatalog.Actions;

    /// <summary>The type dropdown for this step: the owner's session
    /// list, plus the legacy "windows" entry only when THIS step
    /// opened as a legacy "windows" step (mirrors the main VM's
    /// session rule, per step rather than per card).</summary>
    public IReadOnlyList<ActionCatalog.ActionOption> ActionOptions
    {
        get
        {
            if (!_openedAsLegacyWindows) return _actionOptionsBase;
            if (_actionOptionsBase.Any(a => a.ActionId == "windows")) return _actionOptionsBase;
            var options = _actionOptionsBase.ToList();
            int at = options.FindIndex(a => a.ActionId == "windows-command");
            options.Insert(at < 0 ? 0 : at, new ActionCatalog.ActionOption("windows", "S0577", "S0578"));
            return options;
        }
    }

    /// <summary>Owner push: the editor session's action-option list
    /// (the main VM's session list, which already carries the legacy
    /// entry when the MAIN card opened as legacy "windows").</summary>
    public void SetActionOptions(IReadOnlyList<ActionCatalog.ActionOption> options)
    {
        _actionOptionsBase = options;
        OnPropertyChanged(nameof(ActionOptions));
    }

    [ObservableProperty] private string _selectedActionId = "hotkey";
    [ObservableProperty] private string _hotkeyText = "";
    [ObservableProperty] private string _mediaValue = "PlayPause";
    [ObservableProperty] private string _basicValue = "Copy";
    [ObservableProperty] private string _windowsValue = "SwitchWindows";
    [ObservableProperty] private string _browserExe = "";
    [ObservableProperty] private string _browserEngine = "Chromium";
    [ObservableProperty] private string _browserProfileKey = "";
    [ObservableProperty] private bool _browserProfileMulti;
    public ObservableCollection<string> BrowserProfileKeys { get; } = new();
    [ObservableProperty] private string _browserGroupId = "";
    [ObservableProperty] private string _macroId = "";
    [ObservableProperty] private string _browserOpenMode = "New";
    [ObservableProperty] private string _browserUrl = "";
    [ObservableProperty] private string _browserUserDataDir = "";
    [ObservableProperty] private string _manualBrowserExe = "";
    [ObservableProperty] private string _manualProfilePath = "";
    [ObservableProperty] private string _manualProfileStatus = "";
    [ObservableProperty] private string _profileId = "";
    [ObservableProperty] private string _launcherTarget = "";
    [ObservableProperty] private string _linkUrl = "";
    [ObservableProperty] private string _obsCommandValue = "SwitchScene";
    [ObservableProperty] private string _obsNameText = "";
    [ObservableProperty] private string _obsName2Text = "";
    [ObservableProperty] private string _obsValueText = "";
    [ObservableProperty] private string _streamerBotActionText = "";
    [ObservableProperty] private string _streamerBotArgsText = "";
    [ObservableProperty] private string _steamCommandValue = "launch";
    [ObservableProperty] private string _steamBigPictureMode = "toggle";
    [ObservableProperty] private string _steamAppId = "";
    [ObservableProperty] private string _steamGameName = "";
    [ObservableProperty] private string _steamStatusValue = "online";
    [ObservableProperty] private string _steamStatusSecondValue = "invisible";
    [ObservableProperty] private bool _steamIsToggle;
    [ObservableProperty] private string _steamRunningGameAction = "focus";
    [ObservableProperty] private bool _isSteamInstalled = true;
    public bool IsSteamNotInstalled => !IsSteamInstalled;
    partial void OnIsSteamInstalledChanged(bool value) => OnPropertyChanged(nameof(IsSteamNotInstalled));
    public event Action? RequestRefreshSteamGames;
    [RelayCommand] private void RefreshSteamGames() => RequestRefreshSteamGames?.Invoke();
    public ObservableCollection<BindingEditorViewModel.SteamGameChoice> SteamGameOptions { get; } = new();
    public IReadOnlyList<ActionCatalog.ValueOption> SteamCommands => ActionCatalog.SteamCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> SteamBigPictureOptions => ActionCatalog.SteamBigPictureOptions;
    public IReadOnlyList<ActionCatalog.ValueOption> SteamStatuses => ActionCatalog.SteamStatuses;
    public IReadOnlyList<ActionCatalog.ValueOption> SteamRunningGameActions => ActionCatalog.SteamRunningGameActions;
    [ObservableProperty] private string _soundFile = "";
    [ObservableProperty] private string _deckCommandValue = "goto";
    [ObservableProperty] private string _deckPageId = "";

    /// <summary>§158 A5: per-step delay, persisted, never edited by
    /// the UI (no reusable delay control exists - see the coordinator
    /// report). Fresh steps carry 0; <see cref="LoadFromStep"/> reads
    /// a stored delay and <see cref="ToStep"/> writes it back, so a
    /// hand-edited delay is never silently zeroed.</summary>
    [ObservableProperty] private int _delayAfterMs;

    /// <summary>Engine/display detected for the manual profile folder.
    /// Owner-maintained (the owner recomputes both when
    /// <see cref="ManualProfilePath"/> changes); read by
    /// <see cref="ToStep"/> via the codec bag, mirroring the main VM's
    /// private <c>_manualEngine</c>/<c>_manualProfileDisplay</c>.</summary>
    public string ManualEngine { get; set; } = "Chromium";
    public string ManualProfileDisplay { get; set; } = "";

    /// <summary>Full live profile list for the selected browser,
    /// owner-maintained (mirrors the main VM's
    /// <c>_allBrowserProfiles</c>); the display source for
    /// <see cref="ToStep"/>, including a missing-key sentinel when
    /// the stored key vanished from the live list.</summary>
    public List<BindingEditorViewModel.BrowserProfileChoice> AllBrowserProfiles { get; set; } = new();

    [ObservableProperty] private string _browserProfilePlaceholder = Localizer.Get("S0075");
    [ObservableProperty] private string _obsListHint = "";
    [ObservableProperty] private string _obsList2Hint = "";
    [ObservableProperty] private string _streamerBotDiscoveryHint = "";

    public const string CustomBrowserSentinel = ActionStepCodec.CustomBrowserSentinel;
    public bool IsBrowserCustom => BrowserExe == CustomBrowserSentinel;
    public bool IsBrowserDropdown => !IsBrowserCustom;
    public string PindaiButtonText => IsBrowserCustom ? Localizer.Get("S0516") : Localizer.Get("S0517");

    public ObservableCollection<BindingEditorViewModel.BrowserChoice> BrowserOptions { get; } = new();
    public ObservableCollection<BindingEditorViewModel.BrowserProfileChoice> BrowserProfiles { get; } = new();
    public ObservableCollection<ActionCatalog.ValueOption> BrowserGroupOptions { get; } = new();
    public ObservableCollection<ActionCatalog.ValueOption> MacroOptions { get; } = new();
    public ObservableCollection<ActionCatalog.ValueOption> DeckPageOptions { get; } = new();
    public ObservableCollection<ActionCatalog.ValueOption> ProfileOptions { get; } = new();
    public ObservableCollection<string> ObsNameOptions { get; } = new();
    public ObservableCollection<string> ObsName2Options { get; } = new();
    public ObservableCollection<string> StreamerBotActionOptions { get; } = new();

    public IReadOnlyList<ActionCatalog.ValueOption> MediaOptions => ActionCatalog.MediaCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> BasicOptions => ActionCatalog.BasicCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> WindowsCommandOptions => ActionCatalog.WindowsCommandCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> WindowsToolsOptions => ActionCatalog.WindowsToolsCommands;
    /// <summary>The picker source for the selected type - the matching
    /// partition for a split id, the full legacy union for a legacy
    /// "windows" step. Mirrors the main VM.</summary>
    public IReadOnlyList<ActionCatalog.ValueOption> CurrentWindowsOptions => SelectedActionId switch
    {
        "windows-command" => ActionCatalog.WindowsCommandCommands,
        "windows-tools" => ActionCatalog.WindowsToolsCommands,
        _ => ActionCatalog.WindowsCommands,
    };
    public IReadOnlyList<ActionCatalog.ValueOption> BrowserOpenModeOptions => new[]
    {
        new ActionCatalog.ValueOption("New", Localizer.Get("S0522")),
        new ActionCatalog.ValueOption("Focus", Localizer.Get("S0523")),
        new ActionCatalog.ValueOption("Toggle", Localizer.Get("S0524")),
    };
    public IReadOnlyList<ActionCatalog.ValueOption> DeckOptions => ActionCatalog.DeckCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> ObsOptions => ActionCatalog.ObsCommands;

    /// <summary>§158 contract aliases: the task-list names for the
    /// same lists as the main-VM-named properties above
    /// (<c>MediaOptions</c>/<c>BasicOptions</c>/<c>DeckOptions</c>/
    /// <c>ObsOptions</c>/<c>BrowserProfiles</c>). The combo section
    /// binds these literal names; both spellings return the same
    /// data, so either works and neither can drift.</summary>
    public IReadOnlyList<ActionCatalog.ValueOption> MediaCommands => ActionCatalog.MediaCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> BasicCommands => ActionCatalog.BasicCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> DeckCommands => ActionCatalog.DeckCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> ObsCommands => ActionCatalog.ObsCommands;
    public ObservableCollection<BindingEditorViewModel.BrowserProfileChoice> BrowserProfileOptions => BrowserProfiles;

    public bool IsHotkey => SelectedActionId == "hotkey";
    public bool IsMedia => SelectedActionId == "media";
    public bool IsBasic => SelectedActionId == "basic";
    public bool IsWindows => SelectedActionId is "windows" or "windows-command" or "windows-tools";
    public bool IsBrowser => SelectedActionId == "browser";
    public bool IsBrowserGroup => SelectedActionId == "browsergroup";
    public bool IsMacro => SelectedActionId == "macro";
    public bool IsLauncher => SelectedActionId == "launcher";
    public bool IsLink => SelectedActionId == "link";
    public bool IsObs => SelectedActionId == "obs";
    public bool IsStreamerBot => SelectedActionId == "streamerbot";
    public bool IsSound => SelectedActionId == "sound";
    public bool IsSteam => SelectedActionId == "steam";
    public bool IsSteamGame => IsSteam && SteamCommandValue == "launch";
    public bool IsSteamStatus => IsSteam && SteamCommandValue == "status";
    public bool IsSteamBigPicture => IsSteam && SteamCommandValue == "bigpicture";
    public bool SteamShowTogglePanel => IsSteam && SteamCommandValue != "bigpicture" && SteamCommandValue != "exit" && SteamCommandValue != "openclose";
    public bool ShowSteamRunningGameOptions => IsSteamGame && SteamIsToggle;
    public bool ShowSteamSecondStatusOptions => IsSteamStatus && SteamIsToggle;
    public string SteamToggleHintText => SteamCommandValue switch
    {
        "launch" => Localizer.Get("S1876"),
        "bigpicture" => Localizer.Get("S1877"),
        "status" => Localizer.Get("S1878"),
        "openclose" => Localizer.Get("S1879"),
        "friends" => Localizer.Get("S1880"),
        "downloads" => Localizer.Get("S1881"),
        _ => Localizer.Get("S1856"),
    };
    public string SteamGameSummaryText => string.IsNullOrWhiteSpace(SteamAppId) ? Localizer.Get("S1851") : $"App ID: {SteamAppId}";
    partial void OnSteamCommandValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsSteamGame));
        OnPropertyChanged(nameof(IsSteamStatus));
        OnPropertyChanged(nameof(IsSteamBigPicture));
        OnPropertyChanged(nameof(SteamShowTogglePanel));
        OnPropertyChanged(nameof(ShowSteamRunningGameOptions));
        OnPropertyChanged(nameof(ShowSteamSecondStatusOptions));
        OnPropertyChanged(nameof(SteamToggleHintText));
    }
    partial void OnSteamIsToggleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSteamRunningGameOptions));
        OnPropertyChanged(nameof(ShowSteamSecondStatusOptions));
    }
    partial void OnSteamAppIdChanged(string value)
    {
        OnPropertyChanged(nameof(SteamGameSummaryText));
        var choice = SteamGameOptions.FirstOrDefault(g => g.AppId == value);
        if (choice != null) SteamGameName = choice.Name;
    }
    public bool IsDeck => SelectedActionId == "deck";
    public bool IsProfile => SelectedActionId == "profile";
    public bool DeckNeedsPage => IsDeck && (DeckCommandValue == "goto" || DeckCommandValue == "momentary");
    public ActionCatalog.ObsCommandSpec CurrentObsSpec => ActionCatalog.ObsSpec(ObsCommandValue);
    public bool ObsNeedsName => IsObs && CurrentObsSpec.NameSource != ActionCatalog.ObsNameSource.None;
    public string ObsNameLabel => CurrentObsSpec.NameLabel;
    public bool ObsNeedsName2 => IsObs && CurrentObsSpec.SecondSource != ActionCatalog.ObsSecondSource.None;
    public string ObsName2Label => CurrentObsSpec.SecondLabel;
    public bool ObsNeedsValue => IsObs && CurrentObsSpec.ValueLabel is not null;
    public string ObsValueLabel => CurrentObsSpec.ValueLabel ?? "";
    public string ObsValueHint => CurrentObsSpec.ValueHint;

    /// <summary>§159 (G-01–G-03): modifier toggles of this step's
    /// virtual-key picker board. Plain state, like the main editor's
    /// <c>Mod*</c> flags; <see cref="PickVirtualKeyCommand"/> reads
    /// them when composing the chord. Never shared between steps:
    /// each step VM carries its own four flags.</summary>
    [ObservableProperty] private bool _modCtrl;
    [ObservableProperty] private bool _modShift;
    [ObservableProperty] private bool _modAlt;
    [ObservableProperty] private bool _modWin;

    /// <summary>§159 (G-03): the picker-board sources, the same static
    /// <see cref="ActionCatalog"/> lists the main editor binds. The
    /// board's <c>DataTemplate</c> buttons reach this step's command
    /// through <c>AncestorType=ItemsControl</c> (the ItemsControl
    /// inherits the step as its DataContext); the hard-coded grid
    /// buttons bind the command directly.</summary>
    public IReadOnlyList<string> PickerFunctionKeys => ActionCatalog.PickerFunctionKeys;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerRowNumbers => ActionCatalog.PickerRowNumbers;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerRowTop => ActionCatalog.PickerRowTop;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerNavKeys => ActionCatalog.PickerNavKeys;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerNumpadKeys => ActionCatalog.PickerNumpadKeys;

    /// <summary>§159 (G-07/G-08/G-13/G-14): icon-fetch status text for
    /// this step. Deliberately NEVER filled: icon auto-fetch writes
    /// the main card's <c>AutoIcon</c> and is a main-card concern, so
    /// no owner code assigns this property and the four status rows
    /// stay collapsed - the same visibility semantics as the main
    /// editor when no fetch has run. The property exists so the step
    /// template mirrors the main sections row for row.</summary>
    [ObservableProperty] private string _iconFetchStatus = "";
    public bool HasIconFetchStatus => !string.IsNullOrWhiteSpace(IconFetchStatus);
    partial void OnIconFetchStatusChanged(string value) => OnPropertyChanged(nameof(HasIconFetchStatus));

    /// <summary>§159 (G-12): whether a tracked-window record exists
    /// for THIS step's browser+profile key. Owner-maintained (the
    /// owner recomputes it whenever the step's exe, profile key,
    /// multi flag, or manual exe/path changes); hidden while
    /// <see cref="BrowserProfileMulti"/> is on, exactly like the
    /// main editor's rule.</summary>
    [ObservableProperty] private bool _hasTrackedBrowserWindow;

    // --- §159 (G-05/G-06): app drawer, per step -----------------------
    // Owner-filled from the shared app cache (see the owner's
    // LoadStepAppDrawerAsync); this class only owns the open/search/
    // selection state and the pick itself.

    [ObservableProperty] private bool _isAppDrawerOpen;
    [ObservableProperty] private string _appDrawerSearchText = "";
    [ObservableProperty] private bool _isLoadingApps;
    [ObservableProperty] private string _appDrawerEmptyText = "";
    [ObservableProperty] private bool _hasAppDrawerEmptyText;
    public ObservableCollection<AppInfoItem> FilteredAppDrawerItems { get; } = new();

    /// <summary>§159 (G-05): the drawer item the user picked, bound
    /// to the drawer <c>ListBox.SelectedItem</c>. The hook writes
    /// ONLY this step's <c>LauncherTarget</c> and closes this step's
    /// drawer. It NEVER touches the combination Label: the main
    /// handler fills the card Label, but no step owns a label and
    /// the codec has no label slot. A null value (selection cleared
    /// by a list rebuild) is a no-op and never clears the target.</summary>
    [ObservableProperty] private AppInfoItem? _selectedAppDrawerItem;

    /// <summary>§159 (G-06): the picked-app confirmation row. True
    /// only while the picked item's path is still this step's target,
    /// so editing the path by hand never leaves a stale confirmation
    /// behind - the same rule as the main editor's
    /// <c>HasPickedApp</c>, computed over <c>AppInfoItem.Path</c>
    /// (the item type has no <c>Exe</c> member).</summary>
    public bool HasPickedApp
        => SelectedAppDrawerItem is not null && SelectedAppDrawerItem.Path == LauncherTarget;

    public string PickedAppName
        => HasPickedApp ? Path.GetFileNameWithoutExtension(SelectedAppDrawerItem!.Path) : "";

    partial void OnIsAppDrawerOpenChanged(bool value)
    {
        // Opening the drawer needs the shared list (load-if-needed +
        // filter); closing needs nothing.
        if (value) RequestAppDrawerUpdate?.Invoke();
    }

    partial void OnAppDrawerSearchTextChanged(string value) => RequestAppDrawerUpdate?.Invoke();

    partial void OnSelectedAppDrawerItemChanged(AppInfoItem? value)
    {
        OnPropertyChanged(nameof(HasPickedApp));
        OnPropertyChanged(nameof(PickedAppName));
        if (value is null) return;
        LauncherTarget = value.Path;
        IsAppDrawerOpen = false;
    }

    partial void OnLauncherTargetChanged(string value)
    {
        OnPropertyChanged(nameof(HasPickedApp));
        OnPropertyChanged(nameof(PickedAppName));
    }

    public event Action? RequestManageBrowserGroups;
    public event Action? RequestManageMacros;
    /// <summary>§159: the owner opens the launcher file dialog
    /// (<see cref="EditorDialogSpecs.LauncherExe"/>) for THIS step
    /// and applies the chosen path to this step's
    /// <c>LauncherTarget</c>.</summary>
    public event Action? RequestBrowseLauncher;
    /// <summary>§159: the owner opens the sound file dialog
    /// (<see cref="EditorDialogSpecs.SoundFile"/>) for THIS step
    /// and applies the chosen path to this step's
    /// <c>SoundFile</c>. The §159 index defect's wiring: the step
    /// template had the label but no button.</summary>
    public event Action? RequestBrowseSound;
    /// <summary>§159: the owner opens the manual-browser .exe dialog
    /// (<see cref="EditorDialogSpecs.ManualBrowserExe"/>) for THIS
    /// step and applies the chosen path to this step's
    /// <c>ManualBrowserExe</c>.</summary>
    public event Action? RequestBrowseManualExe;
    /// <summary>§159: the owner opens the manual profile-folder
    /// dialog (<see cref="EditorDialogSpecs.ManualProfileFolder"/>)
    /// for THIS step and applies the chosen folder to this step's
    /// <c>ManualProfilePath</c> (the owner's manual-status refresh
    /// already follows that property).</summary>
    public event Action? RequestBrowseManualFolder;
    /// <summary>§159: the Pindai button for THIS step. The owner
    /// branches on <see cref="IsBrowserCustom"/>: CUSTOM opens the
    /// profile-folder dialog, otherwise the step's own browser
    /// lists are re-scanned - mirroring
    /// <c>PindaiBrowser_Click</c>, never the main card's lists.</summary>
    public event Action? RequestPindaiBrowsers;
    /// <summary>§159: the owner force-reloads the shared app list
    /// and refills THIS step's drawer (and every other open step
    /// drawer, whose cache just changed).</summary>
    public event Action? RequestRefreshAppDrawer;
    /// <summary>§159 (Worker B addition - the relay's event list
    /// covers refresh only): the owner ensures the shared app list
    /// is loaded and refilters THIS step's drawer. Raised when the
    /// step drawer opens and when its search text changes; unlike
    /// <see cref="RequestRefreshAppDrawer"/> it never force-reloads.</summary>
    public event Action? RequestAppDrawerUpdate;
    /// <summary>§159: the owner forgets the tracked-window record
    /// for THIS step's browser+profile key (same snapshot/restore
    /// save pattern as the main forget handler) and refreshes the
    /// tracked state of every step sharing the key.</summary>
    public event Action? RequestForgetTrackedBrowser;
    public event Action? RequestObsNamesRefresh;
    public event Action? RequestObsNames2Refresh;
    public event Action? RequestStreamerBotActionsRefresh;
    /// <summary>Raised by <see cref="DeleteStepCommand"/>; the owning
    /// combination item removes this step from its list.</summary>
    public event Action<ComboStepViewModel>? RequestDelete;

    [RelayCommand]
    private void ManageBrowserGroups() => RequestManageBrowserGroups?.Invoke();

    [RelayCommand]
    private void ManageMacros() => RequestManageMacros?.Invoke();

    /// <summary>§159 (G-03): compose this step's chord from THIS
    /// step's modifier toggles plus the picked token, through
    /// <see cref="ActionStepCodec.ComposeHotkey"/> - the same rule
    /// the main editor's <c>PickVirtualKey</c> uses. Writes only
    /// this step's <c>HotkeyText</c>.</summary>
    [RelayCommand]
    private void PickVirtualKey(string token)
        => HotkeyText = ActionStepCodec.ComposeHotkey(ModCtrl, ModShift, ModAlt, ModWin, token);

    /// <summary>§159 (G-04): browse for this step's launcher target.
    /// Closes this step's drawer first (mirroring
    /// <c>BrowseLauncher_Click</c>), then asks the owner for the
    /// dialog; the owner writes the result into this step only.</summary>
    [RelayCommand]
    private void BrowseLauncher()
    {
        IsAppDrawerOpen = false;
        RequestBrowseLauncher?.Invoke();
    }

    /// <summary>§159 (G-09, index defect): browse for this step's
    /// sound file. The owner opens the shared spec dialog and
    /// writes the result into this step's <c>SoundFile</c> only.</summary>
    [RelayCommand]
    private void BrowseSound() => RequestBrowseSound?.Invoke();

    /// <summary>§159 (G-11): browse for this step's manual browser
    /// .exe. The owner writes the result into this step's
    /// <c>ManualBrowserExe</c> only.</summary>
    [RelayCommand]
    private void BrowseManualExe() => RequestBrowseManualExe?.Invoke();

    /// <summary>§159 (G-11): browse for this step's manual profile
    /// folder. The owner writes the result into this step's
    /// <c>ManualProfilePath</c> only.</summary>
    [RelayCommand]
    private void BrowseManualFolder() => RequestBrowseManualFolder?.Invoke();

    /// <summary>§159 (G-10): Pindai for THIS step. The branch lives
    /// in the owner (<see cref="RequestPindaiBrowsers"/>): CUSTOM
    /// opens the folder dialog, otherwise this step's browser lists
    /// are re-scanned. This command itself never touches the main
    /// card's browser lists.</summary>
    [RelayCommand]
    private void PindaiBrowsers() => RequestPindaiBrowsers?.Invoke();

    /// <summary>§159 (G-05): force-reload the shared app list for
    /// THIS step's drawer. The owner does the loading and refills
    /// the step's filtered list.</summary>
    [RelayCommand]
    private void RefreshAppDrawer() => RequestRefreshAppDrawer?.Invoke();

    /// <summary>§159 (G-12): forget the tracked-window record for
    /// THIS step's browser+profile key. The owner derives the key
    /// from this step's fields and refreshes tracked state.</summary>
    [RelayCommand]
    private void ForgetTrackedBrowser() => RequestForgetTrackedBrowser?.Invoke();

    [RelayCommand]
    private void RefreshObsNames() => RequestObsNamesRefresh?.Invoke();

    [RelayCommand]
    private void RefreshObsNames2() => RequestObsNames2Refresh?.Invoke();

    [RelayCommand]
    private void RefreshStreamerBotActions() => RequestStreamerBotActionsRefresh?.Invoke();

    [RelayCommand]
    private void DeleteStep() => RequestDelete?.Invoke(this);

    partial void OnSelectedActionIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsHotkey));
        OnPropertyChanged(nameof(IsMedia));
        OnPropertyChanged(nameof(IsBasic));
        OnPropertyChanged(nameof(IsWindows));
        OnPropertyChanged(nameof(CurrentWindowsOptions));
        OnPropertyChanged(nameof(IsBrowser));
        OnPropertyChanged(nameof(IsBrowserGroup));
        OnPropertyChanged(nameof(IsMacro));
        OnPropertyChanged(nameof(IsLauncher));
        OnPropertyChanged(nameof(IsLink));
        OnPropertyChanged(nameof(IsObs));
        OnPropertyChanged(nameof(IsStreamerBot));
        OnPropertyChanged(nameof(IsSteam));
        OnPropertyChanged(nameof(IsSteamGame));
        OnPropertyChanged(nameof(IsSteamStatus));
        OnPropertyChanged(nameof(IsSound));
        OnPropertyChanged(nameof(IsDeck));
        OnPropertyChanged(nameof(IsProfile));
        OnPropertyChanged(nameof(DeckNeedsPage));
        OnPropertyChanged(nameof(ObsNeedsName));
        OnPropertyChanged(nameof(ObsNameLabel));
        OnPropertyChanged(nameof(ObsNeedsName2));
        OnPropertyChanged(nameof(ObsName2Label));
        OnPropertyChanged(nameof(ObsNeedsValue));
        OnPropertyChanged(nameof(ObsValueLabel));
        OnPropertyChanged(nameof(ObsValueHint));
        // §159 (SP-29): the OBS label/hint texts depend on the
        // command only, but raising them here too keeps the gated
        // label rows correct the moment the OBS section appears,
        // instead of relying on the command hook having fired.
        // No auto-fetch, no trigger-auto here: both are main-card
        // concerns (the card icon / the card trigger). A combo step
        // never owns either, and combos resolve Tap-only.
    }

    partial void OnBrowserExeChanged(string value)
    {
        OnPropertyChanged(nameof(IsBrowserCustom));
        OnPropertyChanged(nameof(IsBrowserDropdown));
        OnPropertyChanged(nameof(PindaiButtonText));
        // The owner refreshes this step's browser/profile lists.
    }

    partial void OnBrowserProfileMultiChanged(bool value)
    {
        if (value)
        {
            // Masuk mode multi: bawa pilihan tunggal yang aktif ke
            // dalam centang (bila belum ada centang). Mirror of the
            // main VM's hook.
            if (BrowserProfileKeys.Count == 0 && !string.IsNullOrEmpty(BrowserProfileKey))
                BrowserProfileKeys.Add(BrowserProfileKey);
        }
        else if (BrowserProfileKeys.Count > 0)
        {
            // Kembali ke tunggal: pakai centang pertama.
            BrowserProfileKey = BrowserProfileKeys[0];
        }
    }

    partial void OnObsCommandValueChanged(string value)
    {
        OnPropertyChanged(nameof(ObsNeedsName));
        OnPropertyChanged(nameof(ObsNameLabel));
        OnPropertyChanged(nameof(ObsNeedsName2));
        OnPropertyChanged(nameof(ObsName2Label));
        OnPropertyChanged(nameof(ObsNeedsValue));
        OnPropertyChanged(nameof(ObsValueLabel));
        OnPropertyChanged(nameof(ObsValueHint));
        // The owner refreshes ObsNameOptions for the new command.
    }

    partial void OnDeckCommandValueChanged(string value) => OnPropertyChanged(nameof(DeckNeedsPage));

    /// <summary>Materializes this step editor from a stored step:
    /// every field through the codec (same fallbacks as the main
    /// editor's load), the per-step legacy-windows session flag, and
    /// the stored delay (A5: round-tripped, never edited).</summary>
    public void LoadFromStep(ActionStep step)
    {
        var fields = ActionStepCodec.LoadFields(step, step.ActionId,
            ActionCatalog.WindowsCommands.Select(o => o.Value).ToList());
        HotkeyText = fields.HotkeyText;
        MediaValue = fields.MediaValue;
        LauncherTarget = fields.LauncherTarget;
        ObsCommandValue = fields.ObsCommandValue;
        ObsNameText = fields.ObsNameText;
        ObsName2Text = fields.ObsName2Text;
        ObsValueText = fields.ObsValueText;
        StreamerBotActionText = fields.StreamerBotActionText;
        StreamerBotArgsText = fields.StreamerBotArgsText;
        SteamCommandValue = fields.SteamCommandValue;
        SteamBigPictureMode = fields.SteamBigPictureMode;
        SteamAppId = fields.SteamAppId;
        SteamGameName = fields.SteamGameName;
        SteamStatusValue = fields.SteamStatusValue;
        SteamStatusSecondValue = fields.SteamStatusSecondValue;
        SteamIsToggle = fields.SteamIsToggle;
        SteamRunningGameAction = fields.SteamRunningGameAction;
        SoundFile = fields.SoundFile;
        DeckCommandValue = fields.DeckCommandValue;
        DeckPageId = fields.DeckPageId;
        BasicValue = fields.BasicValue;
        WindowsValue = fields.WindowsValue;
        BrowserExe = fields.BrowserExe;
        BrowserEngine = fields.BrowserEngine;
        BrowserProfileKey = fields.BrowserProfileKey;
        BrowserProfileKeys.Clear();
        foreach (var k in fields.BrowserProfileKeys) BrowserProfileKeys.Add(k);
        BrowserProfileMulti = fields.BrowserProfileMulti;
        BrowserOpenMode = fields.BrowserOpenMode;
        BrowserUrl = fields.BrowserUrl;
        BrowserUserDataDir = fields.BrowserUserDataDir;
        ManualBrowserExe = fields.ManualBrowserExe;
        ManualProfilePath = fields.ManualProfilePath;
        ManualEngine = fields.ManualEngine;
        ManualProfileDisplay = fields.ManualProfileDisplay;
        BrowserGroupId = fields.BrowserGroupId;
        MacroId = fields.MacroId;
        ProfileId = fields.ProfileId;
        LinkUrl = fields.LinkUrl;
        DelayAfterMs = step.DelayAfterMs;
        SelectedActionId = fields.SelectedActionId;
        _openedAsLegacyWindows = string.Equals(step.ActionId, "windows", StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(ActionOptions));
        OnPropertyChanged(nameof(CurrentWindowsOptions));
    }

    /// <summary>Builds the step this editor would persist, through
    /// the codec (byte-equivalent to the main editor's build for the
    /// same fields), with the stored delay overlaid (A5). Null for
    /// the no-action id - the owning combination skips null steps
    /// when writing <c>Steps</c>.</summary>
    public ActionStep? ToStep()
    {
        var step = ActionStepCodec.BuildStep(ToFields(), SelectedActionId, _openedAsLegacyWindows);
        if (step is not null) step.DelayAfterMs = DelayAfterMs;
        return step;
    }

    private StepParamFields ToFields()
    {
        var spec = CurrentObsSpec;
        return new StepParamFields
        {
            SelectedActionId = SelectedActionId,
            HotkeyText = HotkeyText,
            MediaValue = MediaValue,
            BasicValue = BasicValue,
            WindowsValue = WindowsValue,
            BrowserExe = BrowserExe,
            BrowserEngine = BrowserEngine,
            BrowserProfileKey = BrowserProfileKey,
            BrowserProfileKeys = BrowserProfileKeys.ToList(),
            BrowserProfileMulti = BrowserProfileMulti,
            BrowserGroupId = BrowserGroupId,
            MacroId = MacroId,
            BrowserOpenMode = BrowserOpenMode,
            BrowserUrl = BrowserUrl,
            BrowserUserDataDir = BrowserUserDataDir,
            ManualBrowserExe = ManualBrowserExe,
            ManualProfilePath = ManualProfilePath,
            ManualEngine = ManualEngine,
            ManualProfileDisplay = ManualProfileDisplay,
            ProfileId = ProfileId,
            LauncherTarget = LauncherTarget,
            LinkUrl = LinkUrl,
            ObsCommandValue = ObsCommandValue,
            ObsNameText = ObsNameText,
            ObsName2Text = ObsName2Text,
            ObsValueText = ObsValueText,
            StreamerBotActionText = StreamerBotActionText,
            StreamerBotArgsText = StreamerBotArgsText,
            SteamCommandValue = SteamCommandValue,
            SteamBigPictureMode = SteamBigPictureMode,
            SteamAppId = SteamAppId,
            SteamGameName = SteamGameName,
            SteamStatusValue = SteamStatusValue,
            SteamStatusSecondValue = SteamStatusSecondValue,
            SteamIsToggle = SteamIsToggle,
            SteamRunningGameAction = SteamRunningGameAction,
            SoundFile = SoundFile,
            DeckCommandValue = DeckCommandValue,
            DeckPageId = DeckPageId,
            ObsSpec = new ActionStepCodec.ObsParamSpec(spec.NameParam, spec.SecondParam, spec.ValueParam),
            BrowserProfileDisplay = AllBrowserProfiles.FirstOrDefault(pr => pr.Key == BrowserProfileKey)?.Display ?? "",
            BrowserProfileNames = BrowserProfileKeys
                .Select(k => AllBrowserProfiles.FirstOrDefault(pr => pr.Key == k)?.Display ?? "").ToList(),
            BrowserGroupDisplay = BrowserGroupOptions.FirstOrDefault(o => o.Value == BrowserGroupId)?.Display ?? "",
            MacroDisplay = MacroOptions.FirstOrDefault(o => o.Value == MacroId)?.Display ?? "",
            DeckPageDisplay = DeckPageOptions.FirstOrDefault(o => o.Value == DeckPageId)?.Display ?? "",
            ProfileDisplay = ProfileOptions.FirstOrDefault(o => o.Value == ProfileId)?.Display ?? "",
        };
    }
}
