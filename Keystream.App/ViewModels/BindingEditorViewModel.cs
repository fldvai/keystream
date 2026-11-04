using System.Collections.ObjectModel;
using Keystream.Core.Localization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keystream.Actions.Common;
using Keystream.Actions.Browser;
using Keystream.Actions.Windows;
using Keystream.Actions.Steam;
using Keystream.App.Host;
using Keystream.App.Services;
using Keystream.Core.Icons;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Core.Engine;
using Keystream.Core.Input;
using Keystream.Windows.Interception;

namespace Keystream.App.ViewModels;

/// <summary>
/// The simple tile editor (UI v2): one tile = one obvious action. The user
/// picks an action from friendly Indonesian options and fills exactly the
/// one field that action needs - a dropdown for media/OBS commands, a text
/// box for a hotkey or scene name, a path with a Browse button for
/// launcher/sound. No raw key=value parameter text anywhere.
///
/// The persisted model stays the Phase 1 multi-step BindingConfig: the
/// editor edits step 1 and preserves any additional steps untouched
/// (their existence is shown as a note), so advanced configs built
/// earlier are not destroyed by the simple editor.
/// </summary>
public partial class BindingEditorViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly PageConfig _page;
    private readonly BindingConfig _binding;

    /// <summary>Snapshot halaman (JSON) saat editor dibuka. Batal dan
    /// Save-gagal memulihkan dari sini - dulu mutasi editor sudah
    /// menempel di objek live sehingga "Batal" tidak membatalkan
    /// apa-apa dan save berikutnya mempersistenkan edit yang
    /// dibatalkan (§45).</summary>
    private readonly string _pageSnapshotJson;

    /// <summary>§129: whether THIS editor session opened a legacy
    /// "windows" card (type list keeps the legacy entry for display);
    /// never rewritten on load.</summary>
    private readonly bool _openedAsLegacyWindows;

    /// <summary>§129: the type dropdown for this session - the catalog
    /// list, plus the legacy "windows" entry only when the card opened
    /// as one. Set once in the ctor; never rebuilt from
    /// SelectedActionId, so switching types mid-edit cannot drop the
    /// legacy entry out from under an open legacy card.</summary>
    private readonly IReadOnlyList<ActionCatalog.ActionOption> _actionOptions;

    /// <summary>Kunci profil tersimpan yang hilang dari daftar live
    /// (sentinel "(tidak ditemukan)" sedang terpilih). Selama ini
    /// terisi, Simpan ditolak - editor tidak boleh me-retarget
    /// diam-diam ke profil pertama (§45).</summary>
    private string? _missingBrowserProfileKey;

    public event Action<bool>? RequestClose; // true = saved/deleted (changed), false = cancelled
    public AppHost Host => _host;
    public event Action? RequestManageBrowserGroups;
    public event Action? RequestManageMacros;

    /// <summary>§159: the window opens a file/folder dialog for a
    /// combination step. The owner forwards a step's
    /// <c>RequestBrowse*</c> / Pindai-custom request here with the
    /// shared <see cref="EditorDialogSpecs"/> spec and an apply
    /// callback that writes the chosen path into THAT step only;
    /// the window resolves the spec's keys and invokes the callback
    /// on OK. The step VM itself stays host-free and never opens a
    /// dialog.</summary>
    public event Action<EditorDialogSpec, Action<string>>? RequestStepFileDialog;

    public BindingEditorViewModel(AppHost host, PageConfig page, BindingConfig binding)
    {
        _host = host;
        _page = page;
        _binding = binding;
        _pageSnapshotJson = JsonSerializer.Serialize(page.Bindings, ConfigStore.JsonOptions);

        Label = binding.Label;
        Icon = binding.Icon;
        AutoIcon = binding.AutoIcon;
        ColorHex = string.IsNullOrWhiteSpace(binding.BackgroundColor) ? "#2A2F3A" : binding.BackgroundColor;
        BuildSwatches();
        PhysicalKeyText = binding.ChordMask == ChordMask.None
            ? (string.IsNullOrWhiteSpace(binding.KeyName) ? binding.KeyId : binding.KeyName)
            // §168: a chord-mapped card shows the chord AS its key.
            : ChordMask.Format(binding.ChordMask,
                string.IsNullOrWhiteSpace(binding.KeyName) ? binding.KeyId : binding.KeyName);
        SelectedTrigger = binding.Trigger.ToString();
        IsCardActive = binding.IsActive;
        ExtraStepsNote = binding.Steps.Count > 1
            ? Localizer.Format("S0502", binding.Steps.Count - 1)
            : "";

        var first = binding.Steps.FirstOrDefault();
        SelectedActionId = first?.ActionId ?? ActionCatalog.NoActionId;
        // §129: a card opened as legacy "windows" keeps its type visible
        // in THIS editor session (the legacy entry is inserted into the
        // session's option list only) so the type dropdown never goes
        // blank. New cards never see it: ActionCatalog.Actions has no
        // "windows" entry. The id itself is NOT rewritten here - the
        // migration happens only in BuildStep, i.e. on save.
        _openedAsLegacyWindows = string.Equals(first?.ActionId, "windows", StringComparison.OrdinalIgnoreCase);
        if (_openedAsLegacyWindows)
        {
            var options = ActionCatalog.Actions.ToList();
            int at = options.FindIndex(a => a.ActionId == "windows-command");
            options.Insert(at < 0 ? 0 : at, new ActionCatalog.ActionOption("windows", "S0577", "S0578"));
            _actionOptions = options;
        }
        else
        {
            _actionOptions = ActionCatalog.Actions;
        }
        if (first is not null)
        {
            // §158: field load delegates to the pure codec (same
            // fallbacks, same normalizations, same launcher↔link
            // split - the resolved id comes back on the bag).
            // SelectedActionId is assigned first, as before (it was
            // set from the step at the top of the ctor), so the
            // per-field partial hooks fire in the same order.
            var fields = ActionStepCodec.LoadFields(first, first.ActionId, WindowsCommandValues);
            SelectedActionId = fields.SelectedActionId;
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
            BrowserProfileMulti = fields.BrowserProfileMulti;
            BrowserProfileKeys.Clear();
            foreach (string k in fields.BrowserProfileKeys) BrowserProfileKeys.Add(k);
            BrowserOpenMode = fields.BrowserOpenMode;
            BrowserUrl = fields.BrowserUrl;
            BrowserUserDataDir = fields.BrowserUserDataDir;
            ManualBrowserExe = fields.ManualBrowserExe;
            _manualEngine = fields.ManualEngine;
            _manualProfileDisplay = fields.ManualProfileDisplay;
            ManualProfilePath = fields.ManualProfilePath;
            BrowserGroupId = fields.BrowserGroupId;
            MacroId = fields.MacroId;
            ProfileId = fields.ProfileId;
            LinkUrl = fields.LinkUrl;
        }
        if (IconReference.IsFileIcon(Icon))
            IconUploadInfo = Localizer.Format("S0503", IconReference.FileName(Icon));
        else if (IconReference.IsFileIcon(AutoIcon))
            IconUploadInfo = Localizer.Format("S0504", IconReference.FileName(AutoIcon));
        DeckPageOptions = host.ActiveProfilePages()
            .Select(pg => new ActionCatalog.ValueOption(pg.Id, pg.Name)).ToList();
        RefreshBrowsers();
        RefreshBrowserGroups();
        RefreshMacros();
        _browserPickerReady = true;
        RefreshTrackedBrowserState();
        ProfileOptions = host.AllProfiles()
            .Select(pf => new ActionCatalog.ValueOption(pf.Id, pf.Name)).ToList();
        if (IsObs && ObsNeedsName) _ = RefreshObsNamesAsync();
        if (IsStreamerBot) _ = RefreshStreamerBotActionsAsync();

        _originalScan = _stagedScan = binding.ScanCode;
        _originalE0 = _stagedE0 = binding.IsE0;
        _originalE1 = _stagedE1 = binding.IsE1;
        _originalMask = _stagedMask = binding.ChordMask;
        _stagedName = string.IsNullOrWhiteSpace(binding.KeyName)
            ? KeyNames.GetDisplayName(binding.ScanCode, binding.IsE0)
            : KeyNames.Normalize(binding.KeyName);
        SharedKeyWarning = BuildSharedKeyWarning();
        _suppressAutoFetch = false;

        foreach (var c in binding.Combinations)
        {
            // §158: a combo materializes its stored steps; a legacy
            // combo (Steps empty) materializes exactly the one step
            // ToStep() builds from its legacy ActionId/ActionTarget,
            // so the engine sees the same step either way. The
            // second-key capture fields and Label are unchanged.
            var item = new CombinationItemViewModel
            {
                Id = c.Id,
                SecondScanCode = c.SecondScanCode,
                SecondIsE0 = c.SecondIsE0,
                SecondKeyName = string.IsNullOrWhiteSpace(c.SecondKeyName) ? Localizer.Get("S0505") : c.SecondKeyName,
                Label = c.Label,
            };
            if (c.Steps.Count > 0)
            {
                foreach (var s in c.Steps)
                {
                    var step = new ComboStepViewModel();
                    step.LoadFromStep(s);
                    item.AddStep(step);
                }
            }
            else
            {
                var step = new ComboStepViewModel();
                step.LoadFromStep(c.ToStep());
                item.AddStep(step);
            }
            Combinations.Add(item);
            WireComboItem(item);
        }
        CaptureBaseline();
    }

    /// <summary>Set by the window: asks whether unsaved changes may be
    /// discarded (S0906, pola yang sama seperti SwapPrompt).
    /// Null = no UI attached (headless): Cancel proceeds silently,
    /// like before.</summary>
    public Func<string, bool>? DiscardPrompt { get; set; }

    /// <summary>True once a close path has been decided (Save/Delete
    /// succeeded, or Cancel already confirmed): Window_Closing must
    /// not ask a second time.</summary>
    public bool DiscardConfirmed { get; private set; }

    private string _baselineJson = "";

    /// <summary>Whether the staged editor state differs from the state
    /// at open (or at the last successful Save). Compared on the
    /// canonical form of exactly what Save would persist - label,
    /// icon, colour, trigger, staged key, step 1 params, combinations
    /// - so typing a value back to its original, or an auto-fetch
    /// that changed nothing, is NOT a change: Batal stays silent,
    /// correctly. Display-only state (ErrorText, fetch status,
    /// warnings) is excluded.</summary>
    public bool HasUnsavedChanges() => _baselineJson.Length > 0 && CurrentStateJson() != _baselineJson;

    private void CaptureBaseline() => _baselineJson = CurrentStateJson();

    private string CurrentStateJson()
    {
        var step = BuildStep();
        var state = new JsonObject
        {
            // §168a: the label entry mirrors exactly what Save would
            // persist (chord text when the name is blank).
            ["label"] = string.IsNullOrWhiteSpace(Label) ? ChordMask.Format(_stagedMask, _binding.KeyName) : Label.Trim(),
            ["icon"] = (Icon ?? "").Trim(),
            ["autoIcon"] = AutoIcon ?? "",
            ["color"] = (ColorHex ?? "").ToUpperInvariant(),
            ["trigger"] = Enum.TryParse<TriggerKind>(SelectedTrigger, out var trigger) ? trigger.ToString() : TriggerKind.Tap.ToString(),
            ["isActive"] = IsCardActive,
            ["scan"] = _stagedScan,
            ["e0"] = _stagedE0,
            ["e1"] = _stagedE1,
            ["mask"] = _stagedMask,
            ["step"] = step is null ? null : new JsonObject { ["action"] = step.ActionId, ["params"] = step.Params },
            ["combinations"] = JsonSerializer.SerializeToNode(Combinations.Select(c => c.ToConfig()).ToList(), ConfigStore.JsonOptions),
        };
        return CanonicalJson(state);
    }

    /// <summary>Canonical JSON: object keys sorted recursively, so
    /// param order (file order vs BuildStep order) never counts as
    /// a change.</summary>
    private static string CanonicalJson(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => JsonSerializer.Serialize(kv.Key) + ":" + CanonicalJson(kv.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(CanonicalJson)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    /// <summary>Defensive restore for close paths that bypass Cancel
    /// (Alt+F4, taskbar close): re-apply the open-time snapshot so no
    /// live mutation survives a discarded edit. Idempotent - safe to
    /// call when nothing changed.</summary>
    public void RestoreDiscardedChanges()
    {
        RestorePageSnapshot();
        _host.ReloadActivePage();
    }

    private static bool LooksLikeUrl(string target) => ActionStepCodec.LooksLikeUrl(target);

    [ObservableProperty] private string _sharedKeyWarning = "";
    public bool HasSharedKeyWarning => SharedKeyWarning.Length > 0;
    partial void OnSharedKeyWarningChanged(string value) => OnPropertyChanged(nameof(HasSharedKeyWarning));

    /// <summary>Set by the window: shows the conflict popup and returns
    /// (proceed, dontShowAgain). Null = no UI attached (proceed).</summary>
    public Func<string, (bool Proceed, bool DontShowAgain)>? ConflictPrompt { get; set; }

    private bool IsGlobalPage => ReferenceEquals(_page, _host.Config.GlobalPage);

    /// <summary>The ⚠ conflict for THIS binding's key, in plain words, or
    /// null when the key is free in the other scope. Editing a profile
    /// page: a Global binding for the key wins everywhere. Editing the
    /// Global page: profile bindings for the key get shadowed.</summary>
    private string? BuildConflictMessage() => BuildConflictMessage(_stagedScan, _stagedE0, _stagedE1, _stagedMask, ChordMask.Format(_stagedMask, _stagedName));

    /// <summary>§132: does the staged key conflict with any ACTIVE
    /// binding in <paramref name="others"/>? Decided ONLY by the
    /// single rule BindingResolver.DuplicateKeys: a probe carrying
    /// the staged key joins the other bindings, and the key counts
    /// as a duplicate only when at least two ACTIVE bindings share
    /// it - the probe plus at least one active other. A
    /// staged-inactive card never conflicts (the probe is not
    /// counted), and keyless probes (ScanCode 0) never count either,
    /// per the helper's contract. Do not reimplement this rule here.</summary>
    private bool StagedKeyConflictsWith(ushort scan, bool e0, bool e1, byte mask, IEnumerable<BindingConfig> others)
    {
        if (!IsCardActive) return false;
        var probe = new BindingConfig { ScanCode = scan, IsE0 = e0, IsE1 = e1, ChordMask = mask, IsActive = true };
        return BindingResolver.DuplicateKeys(others.Append(probe)).Contains((scan, e0, mask));
    }

    private string? BuildConflictMessage(ushort scan, bool e0, bool e1, byte mask, string keyName)
    {
        // §132: an inactive card cannot conflict - staged-inactive
        // returns null before any scope check, so the Save-time
        // prompt (which calls this same method) never fires for it.
        if (!IsCardActive) return null;
        if (IsGlobalPage)
        {
            var names = new List<string>();
            if (_host.Config.ActiveProfile is { } profile
                && StagedKeyConflictsWith(scan, e0, e1, mask, profile.Pages.SelectMany(pg => pg.Bindings)))
                foreach (var pg in profile.Pages)
                    if (pg.Bindings.Any(b => b.IsActive && b.ScanCode == scan && b.IsE0 == e0 && b.IsE1 == e1 && b.ChordMask == mask)
                        && !names.Contains(pg.Name))
                        names.Add(pg.Name);
            return names.Count == 0
                ? null
                : Localizer.Format("S0506", keyName, string.Join(", ", names)) +
                  Localizer.Get("S0507");
        }
        var others = _host.Config.GlobalPage.Bindings.Where(b => !ReferenceEquals(b, _binding));
        if (!StagedKeyConflictsWith(scan, e0, e1, mask, others)) return null;
        var globalHit = others
            .FirstOrDefault(b => b.IsActive && b.ScanCode == scan && b.IsE0 == e0 && b.IsE1 == e1 && b.ChordMask == mask);
        return globalHit is null
            ? null
            : Localizer.Format("S0508", keyName, globalHit.Label) +
              Localizer.Get("S0509");
    }

    /// <summary>§124: SATU warning sederhana untuk staged key - tampil
    /// bila kunci staged dipakai binding lain di scope mana pun:
    /// duplikat same-page di _page (logika yang sama dengan S1634 di
    /// MainViewModel.RefreshBindings: binding lain dengan scan+E0
    /// yang sama) ATAU konflik lintas-scope (BuildConflictMessage).
    /// Bila keduanya kena, tetap SATU teks S1635 - tidak pernah dua.
    /// Hanya display; perilaku ConflictPrompt saat Simpan tidak
    /// berubah (Save tetap memanggil BuildConflictMessage sendiri).
    /// Di-refresh di ctor, ApplyPickedKey, dan saat checkbox Aktif
    /// berubah - tiga-satunya tempat staged key / staged flag
    /// berubah. Inactive bindings never count (DuplicateKeys).</summary>
    private string BuildSharedKeyWarning()
    {
        bool samePage = StagedKeyConflictsWith(_stagedScan, _stagedE0, _stagedE1, _stagedMask,
            _page.Bindings.Where(b => !ReferenceEquals(b, _binding)));
        return samePage || BuildConflictMessage() is not null
            ? Localizer.Get("S1635")
            : "";
    }

    // --- Ganti Tombol: reassign this binding to another physical key ---
    private readonly ushort _originalScan;
    private readonly bool _originalE0;
    private readonly bool _originalE1;
    private readonly byte _originalMask;
    private ushort _stagedScan;
    private bool _stagedE0;
    private bool _stagedE1;
    private byte _stagedMask;
    private string _stagedName = "";
    private BindingConfig? _swapPartner;
    private bool _listeningSubscribed;

    /// <summary>§168: pending chord state for the in-editor reassign
    /// capture - the SAME accumulator and rule as the dashboard
    /// capture (MainViewModel). Reset on start/stop.</summary>
    private readonly PendingChord _reassignChord = new();

    // §123 butir 5 - kepemilikan SuppressEngineEvents BERSAMA untuk
    // kedua listening di VM ini (Ganti Tombol + kombinasi): nilai flag
    // sebelum listening PERTAMA disimpan sekali; tiap jenis listening
    // hanya melepas jatahnya sendiri; flag dipulihkan ke nilai
    // tersimpan hanya saat jatah TERAKHIR dilepas. Jadi StopListening
    // tidak pernah membersihkan supresi combo yang masih aktif, dan
    // sebaliknya; Stop yang dipanggil tanpa listening aktif (mis.
    // OnWindowClosed) tidak menyentuh flag sama sekali - perilaku lama
    // yang selalu menulis false bisa membersihkan supresi pemilik lain.
    private bool _suppressBeforeListening;
    private int _listeningSuppressCount;
    private bool _reassignSuppressOwned;
    private bool _comboSuppressOwned;

    private void AcquireListeningSuppression(ref bool owned)
    {
        if (owned) return;
        if (_listeningSuppressCount == 0)
            _suppressBeforeListening = _host.SuppressEngineEvents;
        _listeningSuppressCount++;
        owned = true;
        _host.SuppressEngineEvents = true;
    }

    private void ReleaseListeningSuppression(ref bool owned)
    {
        if (!owned) return;
        owned = false;
        _listeningSuppressCount--;
        if (_listeningSuppressCount <= 0)
        {
            _listeningSuppressCount = 0;
            _host.SuppressEngineEvents = _suppressBeforeListening;
        }
    }

    [ObservableProperty] private bool _isListeningForKey;
    public bool IsNotListeningForKey => !IsListeningForKey;
    partial void OnIsListeningForKeyChanged(bool value) => OnPropertyChanged(nameof(IsNotListeningForKey));

    /// <summary>Set by the window: asks whether to swap keys with the
    /// binding that currently owns the picked key (same page).</summary>
    public Func<string, bool>? SwapPrompt { get; set; }

    [RelayCommand]
    private void StartReassign()
    {
        if (IsListeningForKey) return;
        IsListeningForKey = true;
        _reassignChord.Reset();
        AcquireListeningSuppression(ref _reassignSuppressOwned); // the picked key must not fire its old action
        if (!_listeningSubscribed)
        {
            _host.DeckEventObserved += OnDeckKeyForReassign;
            _listeningSubscribed = true;
        }
    }

    [RelayCommand]
    private void CancelReassign() => StopListening();

    public void OnWindowClosed()
    {
        StopListening();
        StopCapturingSecondKey();
    }

    private void StopListening()
    {
        IsListeningForKey = false;
        _reassignChord.Reset();
        ReleaseListeningSuppression(ref _reassignSuppressOwned);
        if (_listeningSubscribed)
        {
            _host.DeckEventObserved -= OnDeckKeyForReassign;
            _listeningSubscribed = false;
        }
    }

    private void OnDeckKeyForReassign(KeyEvent e)
    {
        // §168: same accumulation rule as the dashboard capture -
        // modifier DOWNs absorb, a release emptying the mask
        // completes with the first-pressed modifier as a single key.
        if (!IsListeningForKey || e.IsRepeat) return;
        if (!e.IsDown)
        {
            var single = _reassignChord.ReleaseUp(e.Key);
            if (single is null) return;
            ushort scan = single.Value.Key.ScanCode;
            bool e0 = single.Value.Key.IsE0;
            bool e1 = single.Value.Key.IsE1;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ApplyPickedKey(scan, e0, e1, ChordMask.None));
            return;
        }
        if (_reassignChord.AbsorbDown(e.Key, e.VirtualKey)) return;
        ushort mainScan = e.Key.ScanCode;
        bool mainE0 = e.Key.IsE0;
        bool mainE1 = e.Key.IsE1;
        byte mask = _reassignChord.Mask;
        _reassignChord.Reset();
        // Back on the UI thread, asynchronously - never block the worker.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ApplyPickedKey(mainScan, mainE0, mainE1, mask));
    }

    private void ApplyPickedKey(ushort scan, bool e0, bool e1, byte mask)
    {
        StopListening();
        string name = KeyNames.GetDisplayName(scan, e0);
        // §168: the staged key is (key, mask) - unchanged-key check
        // and the swap search both include the mask.
        if (scan == _stagedScan && e0 == _stagedE0 && e1 == _stagedE1 && mask == _stagedMask) return;
        var other = _page.Bindings.FirstOrDefault(b =>
            !ReferenceEquals(b, _binding) && b.ScanCode == scan && b.IsE0 == e0 && b.IsE1 == e1 && b.ChordMask == mask);
        if (other is not null)
        {
            bool swap = SwapPrompt?.Invoke(
                Localizer.Format("S0510", ChordMask.Format(mask, name), other.Label)) ?? false;
            if (!swap)
            {
                ErrorText = Localizer.Format("S0511", ChordMask.Format(mask, name), other.Label);
                return;
            }
            _swapPartner = other;
        }
        else
        {
            _swapPartner = null;
        }
        _stagedScan = scan;
        _stagedE0 = e0;
        _stagedE1 = e1;
        _stagedMask = mask;
        _stagedName = name;
        PhysicalKeyText = ChordMask.Format(mask, name);
        OnPropertyChanged(nameof(CombinationPromptText));
        SharedKeyWarning = BuildSharedKeyWarning();
        ErrorText = "";
    }

    private static string KeyIdFor(ushort scan, bool e0) => $"K-{scan:X2}{(e0 ? "E" : "")}";

    private void ApplyStagedKeyChange()
    {
        if (_stagedScan == _binding.ScanCode && _stagedE0 == _binding.IsE0 && _stagedE1 == _binding.IsE1 && _stagedMask == _binding.ChordMask) return;
        if (_swapPartner is not null)
        {
            _swapPartner.ScanCode = _originalScan;
            _swapPartner.IsE0 = _originalE0;
            _swapPartner.IsE1 = _originalE1;
            _swapPartner.ChordMask = _originalMask;
            _swapPartner.KeyId = KeyIdFor(_originalScan, _originalE0);
            _swapPartner.KeyName = KeyNames.GetDisplayName(_originalScan, _originalE0);
        }
        _binding.ScanCode = _stagedScan;
        _binding.IsE0 = _stagedE0;
        _binding.IsE1 = _stagedE1;
        _binding.ChordMask = _stagedMask;
        _binding.KeyId = KeyIdFor(_stagedScan, _stagedE0);
        _binding.KeyName = _stagedName;
    }

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _icon = "";
    [ObservableProperty] private string _autoIcon = "";
    [ObservableProperty] private string _colorHex = "#2A2F3A";

    /// <summary>§132: staged active flag - checked = the card runs.
    /// Staged like Label/Trigger: written to _binding only on Save
    /// (a Save failure restores it via the page snapshot + CopyFrom);
    /// toggling recomputes the shared-key warning. Never writes
    /// _binding live.</summary>
    [ObservableProperty] private bool _isCardActive = true;
    partial void OnIsCardActiveChanged(bool value) => SharedKeyWarning = BuildSharedKeyWarning();

    /// <summary>§134 C2: quick action beside the duplicate-key warning -
    /// EXACTLY the checkbox path: sets the staged IsCardActive flag to
    /// false through the same generated property, so
    /// OnIsCardActiveChanged recomputes SharedKeyWarning (empty when
    /// inactive) and the checkbox shows off. Staged only: _binding is
    /// written on Save; Cancel restores the open-time snapshot, so the
    /// deactivation is discarded with the rest of the edit.</summary>
    [RelayCommand]
    private void DeactivateCard() => IsCardActive = false;
    [ObservableProperty] private string _physicalKeyText = "";

    public string KeyCapBadge
    {
        get
        {
            string t = (PhysicalKeyText ?? "").Trim();
            if (t.StartsWith("Key ", StringComparison.OrdinalIgnoreCase))
                return t.Substring(4).ToUpperInvariant();
            if (t.Equals("Space", StringComparison.OrdinalIgnoreCase)) return "SPACE";
            if (t.Equals("Return", StringComparison.OrdinalIgnoreCase) || t.Equals("Enter", StringComparison.OrdinalIgnoreCase)) return "ENTER";
            if (t.Equals("Escape", StringComparison.OrdinalIgnoreCase)) return "ESC";
            if (t.Equals("Back", StringComparison.OrdinalIgnoreCase) || t.Equals("Backspace", StringComparison.OrdinalIgnoreCase)) return "BKSP";
            return t.ToUpperInvariant();
        }
    }

    public string ScanCodeHex => Localizer.Format("S0512", _stagedScan, (_stagedE0 ? " [E0]" : ""));

    public string DeckHardwareStatusText
    {
        get
        {
            var d = _host.Config.DeckDevice;
            if (!string.IsNullOrWhiteSpace(d.FriendlyName))
                return Localizer.Format("S0513", d.FriendlyName);
            if (!string.IsNullOrWhiteSpace(d.Value))
                return Localizer.Format("S0514", d.Kind);
            return Localizer.Get("S0515");
        }
    }

    partial void OnPhysicalKeyTextChanged(string value)
    {
        OnPropertyChanged(nameof(KeyCapBadge));
        OnPropertyChanged(nameof(ScanCodeHex));
        OnPropertyChanged(nameof(DeckHardwareStatusText));
    }
    [ObservableProperty] private string _selectedTrigger = "Tap";
    [ObservableProperty] private string _triggerAutoNote = "";
    public bool HasTriggerAutoNote => TriggerAutoNote.Length > 0;
    // §155: mechanism text of the selected trigger - the ComboBox
    // tooltip (closed combo) and the persistent text under the combo
    // share this one source with the per-item tooltips (ValueOption.Hint).
    // Null for unknown trigger values: a null ToolTip shows nothing
    // (an empty-string ToolTip would show an empty box).
    public string? TriggerHint => ActionCatalog.TriggerOptions
        .FirstOrDefault(o => o.Value == SelectedTrigger)?.Hint;
    private bool _settingTriggerAuto;
    partial void OnTriggerAutoNoteChanged(string value) => OnPropertyChanged(nameof(HasTriggerAutoNote));

    private void SetTriggerAuto(string trigger, string note)
    {
        _settingTriggerAuto = true;
        SelectedTrigger = trigger;
        TriggerAutoNote = note;
        _settingTriggerAuto = false;
    }

    partial void OnSelectedTriggerChanged(string value)
    {
        OnPropertyChanged(nameof(TriggerHint));
        if (!_settingTriggerAuto && TriggerAutoNote.Length > 0)
            TriggerAutoNote = ""; // user mengambil alih - catatan otomatis hilang
    }
    [ObservableProperty] private string _selectedActionId = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _resultText = "";
    // Footer modal (di luar ScrollViewer) memakai pasangan ini agar
    // pesan Save/Test tidak tenggelam di bawah lipatan scroll -
    // pola yang sama dengan HasIconFetchStatus.
    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);
    public bool HasResultText => !string.IsNullOrWhiteSpace(ResultText);
    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasErrorText));
    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResultText));
    public string ExtraStepsNote { get; }

    // Per-action fields
    [ObservableProperty] private string _hotkeyText = "";

    [ObservableProperty] private string _mediaValue = "PlayPause";
    [ObservableProperty] private string _basicValue = "Copy";
    [ObservableProperty] private string _windowsValue = "SwitchWindows";
    [ObservableProperty] private string _browserExe = "";
    [ObservableProperty] private string _browserEngine = "Chromium";
    [ObservableProperty] private string _browserProfileKey = "";
    // Multi-profil (25 Okt 2026): centang di panel pemilih, disimpan
    // di param tile sendiri (profileKeys/profileNames) - BUKAN grup.
    // Grup (BrowserProfileGroups + aksi browsergroup) tidak dibaca,
    // tidak ditulis, dan tidak disentuh sama sekali oleh fitur ini.
    [ObservableProperty] private bool _browserProfileMulti;
    public ObservableCollection<string> BrowserProfileKeys { get; } = new();
    [ObservableProperty] private string _browserGroupId = "";
    [ObservableProperty] private string _macroId = "";
    [ObservableProperty] private string _browserOpenMode = "New";
    [ObservableProperty] private string _browserUrl = "";
    // Folder User Data untuk tile dari browser kustom (temuan riset 4):
    // kosong = lokasi default exe. Dipertahankan saat browsernya hilang
    // dari daftar (entri "tidak ditemukan") agar tidak hilang saat save.
    [ObservableProperty] private string _browserUserDataDir = "";
    [ObservableProperty] private string _manualBrowserExe = "";
    [ObservableProperty] private string _manualProfilePath = "";
    [ObservableProperty] private string _manualProfileStatus = "";
    public const string CustomBrowserSentinel = ActionStepCodec.CustomBrowserSentinel;
    /// <summary>§158: the catalog values the codec's
    /// <see cref="ActionStepCodec.NormalizeWindows"/> matches
    /// against. Single evaluation per load; the catalog list itself
    /// is static.</summary>
    private static IReadOnlyList<string> WindowsCommandValues =>
        ActionCatalog.WindowsCommands.Select(o => o.Value).ToList();
    public bool IsBrowserCustom => BrowserExe == CustomBrowserSentinel;
    public bool IsBrowserDropdown => !IsBrowserCustom;
    public string PindaiButtonText => IsBrowserCustom ? Localizer.Get("S0516") : Localizer.Get("S0517");
    private string _manualEngine = "Chromium";
    private string _manualProfileDisplay = "";
    public ObservableCollection<BrowserChoice> BrowserOptions { get; } = new();
    public ObservableCollection<BrowserProfileChoice> BrowserProfiles { get; } = new();
    [ObservableProperty] private string _browserProfilePlaceholder = Localizer.Get("S0075");
    public ObservableCollection<ActionCatalog.ValueOption> BrowserGroupOptions { get; } = new();
    public ObservableCollection<ActionCatalog.ValueOption> MacroOptions { get; } = new();
    private List<BrowserProfileChoice> _allBrowserProfiles = new();
    private bool _browserPickerReady;

    public sealed record BrowserChoice(string ExePath, string Engine, string Display, string UserDataDir, bool IsCustom = false, bool IsMissing = false)
    {
        public override string ToString() => Display;
    }
    public sealed record BrowserProfileChoice(string Key, string Display)
    {
        public override string ToString() => Display;
    }

    public sealed record SteamGameChoice(string AppId, string Name, string? ExecutablePath, string? IconPath)
    {
        public string Display => Name;
        public override string ToString() => Name;
    }

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
    [ObservableProperty] private bool _isLoadingSteamGames;
    public ObservableCollection<SteamGameChoice> SteamGameOptions { get; } = new();
    public IReadOnlyList<ActionCatalog.ValueOption> SteamCommands => ActionCatalog.SteamCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> SteamBigPictureOptions => ActionCatalog.SteamBigPictureOptions;
    public IReadOnlyList<ActionCatalog.ValueOption> SteamStatuses => ActionCatalog.SteamStatuses;
    public IReadOnlyList<ActionCatalog.ValueOption> SteamRunningGameActions => ActionCatalog.SteamRunningGameActions;
    [ObservableProperty] private string _profileId = "";
    [ObservableProperty] private string _launcherTarget = "";

    /// <summary>User-given app name/description for a launcher binding
    /// (v2.9.24) - shown on the tile; auto-suggested from the picked
    /// app's file description while empty.</summary>
    [ObservableProperty] private string _linkUrl = "";

    public System.Collections.ObjectModel.ObservableCollection<RunningAppOption> LauncherAppOptions { get; } = new();
    [ObservableProperty] private RunningAppOption? _selectedLauncherApp;

    /// <summary>The running app actually picked, shown as a "Dipilih"
    /// confirmation row (v3.5.2): true only while the picked app's path
    /// is still the current target, so editing the path by hand never
    /// leaves a stale confirmation behind.</summary>
    public bool HasPickedApp
        => SelectedLauncherApp is not null && SelectedLauncherApp.Exe == LauncherTarget;

    public string PickedAppName
        => HasPickedApp ? Path.GetFileNameWithoutExtension(SelectedLauncherApp!.Exe) : "";

    partial void OnSelectedLauncherAppChanged(RunningAppOption? value)
    {
        OnPropertyChanged(nameof(HasPickedApp));
        OnPropertyChanged(nameof(PickedAppName));
        if (value is null) return;
        LauncherTarget = value.Exe;
    }

    [RelayCommand]
    private void RefreshLauncherApps()
    {
        LauncherAppOptions.Clear();
        foreach (var o in RunningApps.ListWithPaths()) LauncherAppOptions.Add(o);
    }

    // --- APP DRAWER (Laci Aplikasi) ---
    [ObservableProperty] private bool _isAppDrawerOpen;
    [ObservableProperty] private string _appDrawerSearchText = "";
    [ObservableProperty] private bool _isLoadingApps;
    [ObservableProperty] private string _appDrawerEmptyText = "";
    [ObservableProperty] private bool _hasAppDrawerEmptyText;
    public ObservableCollection<AppInfoItem> FilteredAppDrawerItems { get; } = new();
    private List<AppInfoItem> _allAppDrawerItems = new();

    partial void OnIsAppDrawerOpenChanged(bool value)
    {
        if (value && _allAppDrawerItems.Count == 0)
        {
            _ = LoadAppDrawerAsync();
        }
    }

    partial void OnAppDrawerSearchTextChanged(string value)
    {
        ApplyAppDrawerFilter();
    }

    private async Task LoadAppDrawerAsync(bool forceRefresh = false)
    {
        IsLoadingApps = true;
        try
        {
            var apps = await AppCatalogService.GetAppsAsync(forceRefresh);
            _allAppDrawerItems = apps;
            RunOnUi(ApplyAppDrawerFilter);
        }
        catch { }
        finally
        {
            IsLoadingApps = false;
            // Filter ulang setelah loading selesai: empty-state hanya
            // boleh muncul saat daftar final sudah diketahui (§45).
            RunOnUi(ApplyAppDrawerFilter);
        }
    }

    private void ApplyAppDrawerFilter()
    {
        string query = (AppDrawerSearchText ?? "").Trim();
        FilteredAppDrawerItems.Clear();
        var matches = string.IsNullOrWhiteSpace(query)
            ? (IEnumerable<AppInfoItem>)_allAppDrawerItems
            : _allAppDrawerItems.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var item in matches.Take(80))
        {
            FilteredAppDrawerItems.Add(item);
        }
        // Empty-state (§45): dulu laci kosong tampil kosong melompong
        // tanpa keterangan - gagal pindai dan "tidak ada hasil" kini
        // dibedakan pesannya.
        HasAppDrawerEmptyText = !IsLoadingApps && FilteredAppDrawerItems.Count == 0;
        AppDrawerEmptyText = !HasAppDrawerEmptyText ? ""
            : _allAppDrawerItems.Count == 0
                ? Localizer.Get("S0518")
                : Localizer.Get("S0519");
    }

    [RelayCommand]
    private async Task RefreshAppDrawerAsync()
    {
        await LoadAppDrawerAsync(forceRefresh: true);
    }

    [RelayCommand]
    private void SelectAppDrawerItem(AppInfoItem item)
    {
        if (item is null) return;
        LauncherTarget = item.Path;
        if (string.IsNullOrWhiteSpace(Label) || Label == PhysicalKeyText || Label == "Unassigned")
        {
            Label = item.Name;
        }
        IsAppDrawerOpen = false;
    }

    // --- COMBINATIONS (Kombinasi Tombol Deck) ---
    public ObservableCollection<CombinationItemViewModel> Combinations { get; } = new();
    private CombinationItemViewModel? _activeCapturingCombo;
    private bool _listeningComboSubscribed;

    public string CombinationPromptText
        => Localizer.Format("S0520", PhysicalKeyText);

    [RelayCommand]
    private void AddCombination()
    {
        var item = new CombinationItemViewModel
        {
            SecondKeyName = Localizer.Get("S0521"),
            IsListeningForSecondKey = true,
        };
        // §158: a fresh combo starts with one default (hotkey) step,
        // the same default the old single ActionId="hotkey" carried.
        item.CreateStep();
        Combinations.Add(item);
        WireComboItem(item);
        StartCaptureSecondKey(item);
    }

    [RelayCommand]
    private void DeleteCombination(CombinationItemViewModel item)
    {
        if (ReferenceEquals(_activeCapturingCombo, item))
            StopCapturingSecondKey();
        Combinations.Remove(item);
    }

    [RelayCommand]
    private void StartCaptureSecondKey(CombinationItemViewModel item)
    {
        if (_activeCapturingCombo is not null)
            _activeCapturingCombo.IsListeningForSecondKey = false;

        _activeCapturingCombo = item;
        item.IsListeningForSecondKey = true;
        // Ganti combo di tengah capture tidak mengakuisisi ulang:
        // owned sudah true, Acquire no-op dan nilai tersimpan pertama
        // tetap yang dipakai saat pelepas terakhir.
        AcquireListeningSuppression(ref _comboSuppressOwned);
        if (!_listeningComboSubscribed)
        {
            _host.DeckEventObserved += OnDeckKeyForCombo;
            _listeningComboSubscribed = true;
        }
    }

    private void StopCapturingSecondKey()
    {
        if (_activeCapturingCombo is not null)
        {
            _activeCapturingCombo.IsListeningForSecondKey = false;
            _activeCapturingCombo = null;
        }
        ReleaseListeningSuppression(ref _comboSuppressOwned);
        if (_listeningComboSubscribed)
        {
            _host.DeckEventObserved -= OnDeckKeyForCombo;
            _listeningComboSubscribed = false;
        }
    }

    private void OnDeckKeyForCombo(KeyEvent e)
    {
        if (_activeCapturingCombo is null || !e.IsDown || e.IsRepeat) return;
        ushort scan = e.Key.ScanCode;
        bool e0 = e.Key.IsE0;
        if (scan == _stagedScan && e0 == _stagedE0) return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_activeCapturingCombo is null) return;
            _activeCapturingCombo.SecondScanCode = scan;
            _activeCapturingCombo.SecondIsE0 = e0;
            _activeCapturingCombo.SecondKeyName = KeyNames.GetDisplayName(scan, e0);
            StopCapturingSecondKey();
        });
    }

    // --- COMBINATION STEPS (§158): owner-side wiring ----------------
    // The step view-models are host-free. This VM is the only place
    // that touches the host: it pushes live lists into each step,
    // refreshes per-step browser/OBS/Streamer.bot discovery when a
    // step's fields change, and forwards a step's manage-manager
    // requests to its own handlers (which the window owns). Second-
    // key capture above is untouched.

    private void WireComboItem(CombinationItemViewModel item)
    {
        item.RequestManageBrowserGroups += () => RequestManageBrowserGroups?.Invoke();
        item.RequestManageMacros += () => RequestManageMacros?.Invoke();
        foreach (var step in item.Steps) AttachComboStep(step);
        item.Steps.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (ComboStepViewModel s in e.NewItems) AttachComboStep(s);
        };
    }

    private void AttachComboStep(ComboStepViewModel step)
    {
        step.SetActionOptions(_actionOptions);
        SyncStepLiveLists(step);
        step.PropertyChanged += OnComboStepPropertyChanged;
        step.RequestObsNamesRefresh += () => _ = RefreshStepObsNamesAsync(step);
        step.RequestObsNames2Refresh += () => _ = RefreshStepObsNames2Async(step);
        step.RequestStreamerBotActionsRefresh += () => _ = RefreshStepStreamerBotActionsAsync(step);
        step.RequestRefreshSteamGames += RefreshSteamGames;
        // §159: step-owned browse/forget/drawer wiring. Every
        // closure captures THIS step, so a command on step A can
        // only ever mutate step A - the shared-handler trap is
        // closed by construction. Dialogs go through the window
        // (RequestStepFileDialog) with the shared spec; the drawer
        // and forget flows are handled here, per step.
        step.RequestBrowseLauncher += () => RequestStepFileDialog?.Invoke(
            EditorDialogSpecs.LauncherExe, path => step.LauncherTarget = path);
        step.RequestBrowseSound += () => RequestStepFileDialog?.Invoke(
            EditorDialogSpecs.SoundFile, path => step.SoundFile = path);
        step.RequestBrowseManualExe += () => RequestStepFileDialog?.Invoke(
            EditorDialogSpecs.ManualBrowserExe, path => step.ManualBrowserExe = path);
        step.RequestBrowseManualFolder += () => RequestStepFileDialog?.Invoke(
            EditorDialogSpecs.ManualProfileFolder, path => step.ManualProfilePath = path);
        step.RequestPindaiBrowsers += () =>
        {
            // Mirror of PindaiBrowser_Click, per step: CUSTOM opens
            // the profile-folder dialog; otherwise re-scan THIS
            // step's browser lists only.
            if (step.IsBrowserCustom)
                RequestStepFileDialog?.Invoke(
                    EditorDialogSpecs.ManualProfileFolder, path => step.ManualProfilePath = path);
            else
                RefreshStepBrowsers(step);
        };
        step.RequestForgetTrackedBrowser += () => ForgetStepTrackedBrowser(step);
        step.RequestRefreshAppDrawer += () => _ = LoadStepAppDrawerAsync(step, forceRefresh: true);
        step.RequestAppDrawerUpdate += () => _ = LoadStepAppDrawerAsync(step, forceRefresh: false);
        RefreshStepBrowsers(step);
        RefreshStepManualStatus(step);
        RefreshStepTrackedBrowserState(step);
        if (step.IsObs && step.ObsNeedsName) _ = RefreshStepObsNamesAsync(step);
        if (step.IsStreamerBot) _ = RefreshStepStreamerBotActionsAsync(step);
    }

    /// <summary>Fills a step's owner-pushed option lists from the
    /// live library/page/profile state (the same sources the main
    /// editor's own lists use).</summary>
    private void SyncStepLiveLists(ComboStepViewModel step)
    {
        step.DeckPageOptions.Clear();
        foreach (var pg in _host.ActiveProfilePages())
            step.DeckPageOptions.Add(new ActionCatalog.ValueOption(pg.Id, pg.Name));
        step.ProfileOptions.Clear();
        foreach (var pf in _host.AllProfiles())
            step.ProfileOptions.Add(new ActionCatalog.ValueOption(pf.Id, pf.Name));
        RefreshStepBrowserGroups(step);
        RefreshStepMacros(step);
        RefreshStepSteamGames(step);
    }

    private void OnComboStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ComboStepViewModel step) return;
        switch (e.PropertyName)
        {
            case nameof(ComboStepViewModel.SelectedActionId):
                if (step.SelectedActionId == "browser") RefreshStepBrowsers(step);
                else if (step.SelectedActionId == "browsergroup") RefreshStepBrowserGroups(step);
                else if (step.SelectedActionId == "macro") RefreshStepMacros(step);
                else if (step.SelectedActionId == "steam") RefreshStepSteamGames(step);
                else if (step.IsObs && step.ObsNeedsName) _ = RefreshStepObsNamesAsync(step);
                else if (step.IsStreamerBot) _ = RefreshStepStreamerBotActionsAsync(step);
                break;
            case nameof(ComboStepViewModel.ObsCommandValue):
                if (step.IsObs && step.ObsNeedsName) _ = RefreshStepObsNamesAsync(step);
                break;
            case nameof(ComboStepViewModel.ObsNameText):
                if (step.IsObs && step.ObsNeedsName2) _ = RefreshStepObsNames2Async(step);
                break;
            case nameof(ComboStepViewModel.BrowserExe):
                // Mirror of OnBrowserExeChanged minus auto-fetch and
                // tracked-window state (main-card concerns only):
                // the old browser's checked profiles die with it.
                step.BrowserProfileKeys.Clear();
                RefreshStepBrowsers(step);
                break;
            case nameof(ComboStepViewModel.BrowserProfileKey):
            case nameof(ComboStepViewModel.BrowserProfileMulti):
            case nameof(ComboStepViewModel.ManualBrowserExe):
                // §159 (G-12): the Forget button's visibility key
                // changes with the tracked-window key. (BrowserExe
                // is covered above via RefreshStepBrowsers, and
                // ManualProfilePath below refreshes both status
                // and tracked state.)
                RefreshStepTrackedBrowserState(step);
                break;
            case nameof(ComboStepViewModel.ManualProfilePath):
                RefreshStepManualStatus(step);
                RefreshStepTrackedBrowserState(step);
                break;
        }
    }

    /// <summary>Per-step mirror of <see cref="RefreshBrowsers"/>:
    /// the same detection, missing-browser sentinel, engine/dir
    /// rules, and CUSTOM entry, computed for THIS step's exe.</summary>
    private void RefreshStepBrowsers(ComboStepViewModel step)
    {
        var detected = BrowserDiscovery.DetectBrowsers(_host.Config.CustomBrowsers);
        step.BrowserOptions.Clear();
        foreach (var b in detected)
            step.BrowserOptions.Add(new BrowserChoice(b.ExePath, b.Engine,
                b.Name + (b.IsCustom ? Localizer.Get("S0540") : "") + (File.Exists(b.ExePath) ? "" : Localizer.Get("S0541")),
                b.UserDataDir, b.IsCustom, !File.Exists(b.ExePath)));
        if (step.BrowserExe != CustomBrowserSentinel && !string.IsNullOrEmpty(step.BrowserExe)
            && step.BrowserOptions.All(o => o.ExePath != step.BrowserExe))
        {
            step.BrowserOptions.Add(new BrowserChoice(step.BrowserExe, step.BrowserEngine,
                Path.GetFileNameWithoutExtension(step.BrowserExe) + Localizer.Get("S0541"),
                step.BrowserUserDataDir, false, true));
        }
        else if (step.BrowserExe != CustomBrowserSentinel && !string.IsNullOrEmpty(step.BrowserExe))
        {
            var selected = step.BrowserOptions.First(o => o.ExePath == step.BrowserExe);
            step.BrowserEngine = selected.Engine;
            if (selected.IsCustom)
                step.BrowserUserDataDir = selected.UserDataDir;
            else if (!selected.IsMissing)
                step.BrowserUserDataDir = "";
        }
        step.BrowserOptions.Add(new BrowserChoice(CustomBrowserSentinel, "Chromium", "CUSTOM", ""));
        RefreshStepBrowserProfiles(step);
        RefreshStepTrackedBrowserState(step);
    }

    /// <summary>Per-step mirror of <see cref="RefreshBrowserProfiles"/>
    /// (same placeholder/sentinel/default-first rules). One
    /// deliberate difference: an unreadable profile folder sets no
    /// error text - steps have no footer of their own, and the old
    /// single-target combo had no validation at all.</summary>
    private void RefreshStepBrowserProfiles(ComboStepViewModel step)
    {
        var browser = step.BrowserOptions.FirstOrDefault(o => o.ExePath == step.BrowserExe);
        if (browser is null || string.IsNullOrEmpty(browser.ExePath))
        {
            step.AllBrowserProfiles = new List<BrowserProfileChoice>();
            step.BrowserProfiles.Clear();
            step.BrowserProfileKey = "";
            step.BrowserProfilePlaceholder = Localizer.Get("S0075");
            return;
        }
        step.BrowserEngine = browser.Engine;
        var scan = BrowserDiscovery.GetProfilesDetailed(
                new DetectedBrowser("", "", browser.ExePath, browser.Engine, browser.UserDataDir, IsCustom: false));
        step.AllBrowserProfiles = scan.Profiles
            .Select(pr => new BrowserProfileChoice(pr.Key, pr.Display)).ToList();
        if (step.AllBrowserProfiles.Count == 0)
        {
            step.BrowserProfiles.Clear();
            step.BrowserProfileKey = "";
            step.BrowserProfilePlaceholder = Localizer.Get("S0544");
            return;
        }
        step.BrowserProfilePlaceholder = Localizer.Get("S0075");
        step.BrowserProfiles.Clear();
        foreach (var pr in step.AllBrowserProfiles) step.BrowserProfiles.Add(pr);
        if (string.IsNullOrEmpty(step.BrowserProfileKey))
        {
            step.BrowserProfileKey = step.AllBrowserProfiles[0].Key;
        }
        else if (step.AllBrowserProfiles.All(pr => pr.Key != step.BrowserProfileKey))
        {
            if (!step.BrowserProfileMulti)
            {
                var sentinel = new BrowserProfileChoice(step.BrowserProfileKey,
                    step.BrowserProfileKey + Localizer.Get("S0541"));
                step.AllBrowserProfiles.Add(sentinel);
                step.BrowserProfiles.Add(sentinel);
            }
        }
    }

    private void RefreshStepBrowserGroups(ComboStepViewModel step)
    {
        step.BrowserGroupOptions.Clear();
        foreach (var g in _host.Config.BrowserProfileGroups)
            step.BrowserGroupOptions.Add(new ActionCatalog.ValueOption(g.Id, g.Name));
    }

    /// <summary>Per-step mirror of <see cref="RefreshMacros"/>
    /// (same missing-macro sentinel).</summary>
    private void RefreshStepMacros(ComboStepViewModel step)
    {
        step.MacroOptions.Clear();
        foreach (var m in _host.Config.Macros)
            step.MacroOptions.Add(new ActionCatalog.ValueOption(m.Id, m.Name));
        if (!string.IsNullOrEmpty(step.MacroId) && step.MacroOptions.All(o => o.Value != step.MacroId))
            step.MacroOptions.Add(new ActionCatalog.ValueOption(step.MacroId, Localizer.Get("S0545")));
    }

    private void RefreshStepManualStatus(ComboStepViewModel step)
    {
        var (status, engine, display) = ManualProfileStatusFor((step.ManualProfilePath ?? "").Trim(), step.ManualEngine, step.ManualProfileDisplay);
        step.ManualProfileStatus = status;
        step.ManualEngine = engine;
        step.ManualProfileDisplay = display;
    }

    /// <summary>§159 (G-12): per-step mirror of
    /// <see cref="RefreshTrackedBrowserState"/> - the same key
    /// derivation (manual exe + profile path when CUSTOM, exe +
    /// profile key otherwise) and the same multi-profile hide rule,
    /// computed for THIS step's fields. No picker-ready guard: steps
    /// attach after the picker is ready, and
    /// <see cref="BrowserWindowTracker.Find"/> over the config list
    /// is safe at any time.</summary>
    private void RefreshStepTrackedBrowserState(ComboStepViewModel step)
    {
        if (step.BrowserProfileMulti) { step.HasTrackedBrowserWindow = false; return; }
        string exe;
        string key;
        if (step.BrowserExe == CustomBrowserSentinel)
        {
            exe = (step.ManualBrowserExe ?? "").Trim();
            key = (step.ManualProfilePath ?? "").Trim();
        }
        else
        {
            exe = step.BrowserExe;
            key = step.BrowserProfileKey;
        }
        step.HasTrackedBrowserWindow = exe.Length > 0 && key.Length > 0
            && BrowserWindowTracker.Find(_host.Config.BrowserWindowTracking, exe, key) is not null;
    }

    /// <summary>§159 (G-12): forgets the tracked-window record for
    /// THIS step's browser+profile key. Mirrors
    /// <see cref="ForgetTrackedBrowser"/> exactly - same key
    /// derivation, same snapshot/restore on a failed save, same
    /// footer message on failure (steps have no footer of their
    /// own; the failure belongs to the editor). On success the
    /// tracked state of every step is refreshed, since another
    /// step may share the forgotten key.</summary>
    private void ForgetStepTrackedBrowser(ComboStepViewModel step)
    {
        string exe;
        string key;
        if (step.BrowserExe == CustomBrowserSentinel)
        {
            exe = (step.ManualBrowserExe ?? "").Trim();
            key = (step.ManualProfilePath ?? "").Trim();
        }
        else
        {
            exe = step.BrowserExe;
            key = step.BrowserProfileKey;
        }
        var tracking = _host.Config.BrowserWindowTracking;
        var removed = tracking.Where(t =>
            string.Equals(t.BrowserExe, exe, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ProfileKey, key, StringComparison.Ordinal)).ToList();
        BrowserWindowTracker.Remove(tracking, exe, key);
        try { _host.Save(); }
        catch (Exception ex)
        {
            tracking.AddRange(removed);
            ErrorText = Localizer.Get("S0937") + " " + ex.Message;
            return;
        }
        RefreshTrackedBrowserState();
        foreach (var s in Combinations.SelectMany(c => c.Steps)) RefreshStepTrackedBrowserState(s);
    }

    /// <summary>§159 (G-05): per-step mirror of
    /// <see cref="ApplyAppDrawerFilter"/> - the same query match
    /// (name or path, case-insensitive), the same 80-item cap, and
    /// the same empty-state texts (S0518/S0519) - applied to THIS
    /// step's search text and filtered list, over the owner's
    /// shared <c>_allAppDrawerItems</c> cache.</summary>
    private void RefreshStepAppDrawer(ComboStepViewModel step)
    {
        string query = (step.AppDrawerSearchText ?? "").Trim();
        step.FilteredAppDrawerItems.Clear();
        var matches = string.IsNullOrWhiteSpace(query)
            ? (IEnumerable<AppInfoItem>)_allAppDrawerItems
            : _allAppDrawerItems.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var item in matches.Take(80))
        {
            step.FilteredAppDrawerItems.Add(item);
        }
        step.HasAppDrawerEmptyText = !step.IsLoadingApps && step.FilteredAppDrawerItems.Count == 0;
        step.AppDrawerEmptyText = !step.HasAppDrawerEmptyText ? ""
            : _allAppDrawerItems.Count == 0
                ? Localizer.Get("S0518")
                : Localizer.Get("S0519");
    }

    /// <summary>§159 (G-05): loads the shared app list for a step
    /// drawer, reusing the main loader
    /// (<see cref="LoadAppDrawerAsync"/>) and its cache. A forced
    /// refresh reloads and refilters every step drawer (their cache
    /// just changed); an open/search update loads only when the
    /// cache is empty - mirroring the main drawer's load-on-open -
    /// and refilters just the requesting step. The step's own
    /// loading flag drives its progress line; the main drawer's
    /// flag is untouched by the step's state.</summary>
    private async Task LoadStepAppDrawerAsync(ComboStepViewModel step, bool forceRefresh)
    {
        if (forceRefresh || _allAppDrawerItems.Count == 0)
        {
            step.IsLoadingApps = true;
            try { await LoadAppDrawerAsync(forceRefresh); }
            finally
            {
                step.IsLoadingApps = false;
                RunOnUi(() =>
                {
                    if (forceRefresh)
                    {
                        foreach (var s in Combinations.SelectMany(c => c.Steps)) RefreshStepAppDrawer(s);
                    }
                    else RefreshStepAppDrawer(step);
                });
            }
        }
        else RunOnUi(() => RefreshStepAppDrawer(step));
    }

    /// <summary>Per-step mirror of <see cref="RefreshObsNamesAsync"/>:
    /// fills the step's primary OBS dropdown from the live OBS, per
    /// the step command's spec.</summary>
    private async Task RefreshStepObsNamesAsync(ComboStepViewModel step)
    {
        if (!step.IsObs || !step.ObsNeedsName) return;
        if (!_host.Obs.IsConnected) { RunOnUi(() => step.ObsListHint = Localizer.Get("S0531")); return; }
        RunOnUi(() => step.ObsListHint = Localizer.Get("S0532"));
        var spec = step.CurrentObsSpec;
        (IReadOnlyList<string> Names, string? Error) result = spec.NameSource switch
        {
            ActionCatalog.ObsNameSource.Scenes => await _host.Obs.GetSceneNamesAsync(),
            ActionCatalog.ObsNameSource.Inputs => await _host.Obs.GetInputNamesAsync(),
            ActionCatalog.ObsNameSource.Collections => await _host.Obs.GetSceneCollectionNamesAsync(),
            ActionCatalog.ObsNameSource.Profiles => await _host.Obs.GetProfileNamesAsync(),
            ActionCatalog.ObsNameSource.TextInputs => await LoadTextInputsAsync(),
            ActionCatalog.ObsNameSource.ScenesAndInputs => await LoadScenesAndInputsAsync(),
            _ => (Array.Empty<string>(), null),
        };
        RunOnUi(() =>
        {
            step.ObsNameOptions.Clear();
            foreach (var n in result.Names) step.ObsNameOptions.Add(n);
            step.ObsListHint = result.Error is not null
                ? Localizer.Get("S0533") + result.Error
                : result.Names.Count == 0
                    ? Localizer.Get("S0534")
                    : Localizer.Format("S0535", result.Names.Count);
        });
        if (step.ObsNeedsName2) _ = RefreshStepObsNames2Async(step);
    }

    /// <summary>Per-step mirror of <see cref="RefreshObsNames2Async"/>:
    /// fills the step's cascading second OBS dropdown.</summary>
    private async Task RefreshStepObsNames2Async(ComboStepViewModel step)
    {
        if (!step.IsObs || !step.ObsNeedsName2) return;
        if (!_host.Obs.IsConnected) { RunOnUi(() => step.ObsList2Hint = ""); return; }
        if (string.IsNullOrWhiteSpace(step.ObsNameText))
        {
            RunOnUi(() => { step.ObsName2Options.Clear(); step.ObsList2Hint = Localizer.Get("S0536"); });
            return;
        }
        var spec = step.CurrentObsSpec;
        string primary = step.ObsNameText;
        var result = spec.SecondSource == ActionCatalog.ObsSecondSource.SceneItems
            ? await _host.Obs.GetSceneItemNamesAsync(primary)
            : await _host.Obs.GetFilterNamesAsync(primary);
        if (step.ObsNameText != primary) return; // primary changed while loading
        RunOnUi(() =>
        {
            step.ObsName2Options.Clear();
            foreach (var n in result.Names) step.ObsName2Options.Add(n);
            step.ObsList2Hint = result.Error is not null
                ? Localizer.Get("S0537") + result.Error
                : result.Names.Count == 0 ? Localizer.Get("S0538") : Localizer.Format("S0539", result.Names.Count);
        });
    }

    /// <summary>Per-step mirror of
    /// <see cref="RefreshStreamerBotActionsAsync"/>.</summary>
    private async Task RefreshStepStreamerBotActionsAsync(ComboStepViewModel step)
    {
        if (!step.IsStreamerBot) return;
        if (!_host.StreamerBot.IsConnected)
        {
            RunOnUi(() => step.StreamerBotDiscoveryHint = Localizer.Get("S1756"));
            return;
        }
        var (actions, error) = await _host.StreamerBot
            .GetActionsAsync(CancellationToken.None).ConfigureAwait(false);
        RunOnUi(() =>
        {
            if (error is not null)
            {
                step.StreamerBotDiscoveryHint = Localizer.Format("S1755", error);
                return;
            }
            step.StreamerBotActionOptions.Clear();
            foreach (var a in actions)
            {
                string name = string.IsNullOrWhiteSpace(a.Name) ? (a.Id ?? "") : a.Name;
                if (!string.IsNullOrWhiteSpace(name)) step.StreamerBotActionOptions.Add(name);
            }
            step.StreamerBotDiscoveryHint = "";
        });
    }
    [ObservableProperty] private string _obsCommandValue = "SwitchScene";
    [ObservableProperty] private string _obsNameText = "";
    [ObservableProperty] private string _obsName2Text = "";
    [ObservableProperty] private string _obsValueText = "";
    [ObservableProperty] private string _streamerBotActionText = "";
    [ObservableProperty] private string _streamerBotArgsText = "";
    [ObservableProperty] private string _soundFile = "";
    [ObservableProperty] private string _deckCommandValue = "goto";
    [ObservableProperty] private string _deckPageId = "";
    [ObservableProperty] private string _iconUploadInfo = "";
    [ObservableProperty] private string _iconFetchStatus = "";
    private bool _fetchingIcon;
    private Task? _fetchTask;
    private string? _lastAutoFetchKey;
    private bool _lastAutoFetchOk;
    private bool _suppressAutoFetch = true; // ctor load must not trigger auto-fetch
    [ObservableProperty] private string? _uploadedIconPath;
    [ObservableProperty] private bool _showIconUpload;
    public IReadOnlyList<ActionCatalog.ValueOption> DeckPageOptions { get; } = new List<ActionCatalog.ValueOption>();
    public IReadOnlyList<ActionCatalog.ValueOption> ProfileOptions { get; } = new List<ActionCatalog.ValueOption>();

    public IReadOnlyList<ActionCatalog.ActionOption> ActionOptions => _actionOptions;
    public IReadOnlyList<ActionCatalog.ValueOption> MediaOptions => ActionCatalog.MediaCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> BasicOptions => ActionCatalog.BasicCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> WindowsOptions => ActionCatalog.WindowsCommands;
    /// <summary>§129: the picker source for the selected type - the
    /// matching partition for a split id, the full legacy union for
    /// a legacy "windows" card (its value may sit in either half).</summary>
    public IReadOnlyList<ActionCatalog.ValueOption> CurrentWindowsOptions => SelectedActionId switch
    {
        "windows-command" => ActionCatalog.WindowsCommandCommands,
        "windows-tools" => ActionCatalog.WindowsToolsCommands,
        _ => ActionCatalog.WindowsCommands,
    };
    public IReadOnlyList<ActionCatalog.ValueOption> WindowsCommandOptions => ActionCatalog.WindowsCommandCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> WindowsToolsOptions => ActionCatalog.WindowsToolsCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> BrowserOpenModeOptions => new[]
    {
        new ActionCatalog.ValueOption("New", Localizer.Get("S0522")),
        new ActionCatalog.ValueOption("Focus", Localizer.Get("S0523")),
        new ActionCatalog.ValueOption("Toggle", Localizer.Get("S0524")),
    };
    public IReadOnlyList<ActionCatalog.ValueOption> DeckOptions => ActionCatalog.DeckCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> ObsOptions => ActionCatalog.ObsCommands;
    public IReadOnlyList<ActionCatalog.ValueOption> TriggerOptions => ActionCatalog.TriggerOptions;
    public IReadOnlyList<string> ColorPresets => ActionCatalog.ColorPresets;
    public IReadOnlyList<string> PickerFunctionKeys => ActionCatalog.PickerFunctionKeys;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerRowNumbers => ActionCatalog.PickerRowNumbers;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerRowTop => ActionCatalog.PickerRowTop;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerNavKeys => ActionCatalog.PickerNavKeys;
    public IReadOnlyList<ActionCatalog.ValueOption> PickerNumpadKeys => ActionCatalog.PickerNumpadKeys;

    [ObservableProperty] private bool _modCtrl;
    [ObservableProperty] private bool _modShift;
    [ObservableProperty] private bool _modAlt;
    [ObservableProperty] private bool _modWin;

    [RelayCommand]
    private void PickVirtualKey(string token)
        // §159: delegates to the codec so the main pick and every
        // step pick compose chords by the same rule. Byte-identical
        // for every token the UI passes (non-empty); an empty token
        // no longer leaves a trailing "+" (unreachable from the UI).
        => HotkeyText = ActionStepCodec.ComposeHotkey(ModCtrl, ModShift, ModAlt, ModWin, token);
    public IReadOnlyList<string> IconPresets => ActionCatalog.IconPresets;

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
    public bool IsDeck => SelectedActionId == "deck";
    public bool IsProfile => SelectedActionId == "profile";
    public bool DeckNeedsPage => IsDeck && (DeckCommandValue == "goto" || DeckCommandValue == "momentary");
    private ActionCatalog.ObsCommandSpec CurrentObsSpec => ActionCatalog.ObsSpec(ObsCommandValue);
    public bool ObsNeedsName => IsObs && CurrentObsSpec.NameSource != ActionCatalog.ObsNameSource.None;
    public string ObsNameLabel => CurrentObsSpec.NameLabel;
    public bool ObsNeedsName2 => IsObs && CurrentObsSpec.SecondSource != ActionCatalog.ObsSecondSource.None;
    public string ObsName2Label => CurrentObsSpec.SecondLabel;
    public bool ObsNeedsValue => IsObs && CurrentObsSpec.ValueLabel is not null;
    public string ObsValueLabel => CurrentObsSpec.ValueLabel ?? "";
    public string ObsValueHint => CurrentObsSpec.ValueHint;

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

    public string SteamGameSummaryText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SteamAppId))
                return Localizer.Get("S1851");
            return $"App ID: {SteamAppId}";
        }
    }

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
        if (choice != null)
        {
            SteamGameName = choice.Name;
            if (string.IsNullOrWhiteSpace(Label) || Label == PhysicalKeyText || Label == "Unassigned")
            {
                Label = choice.Name;
            }
            if (!_suppressAutoFetch && AutoFetchEnabled && SelectedActionId == "steam")
            {
                _ = AutoFetchSteamGameIconAsync(choice);
            }
        }
    }

    partial void OnSelectedActionIdChanged(string value)
    {
        if (value != "media" && SelectedTrigger == "Repeat")
            SetTriggerAuto("Tap", Localizer.Get("S0525"));
        if (value != "deck" && SelectedTrigger == "Momentary")
            SetTriggerAuto("Tap", Localizer.Get("S0526"));
        OnPropertyChanged(nameof(IsHotkey));
        OnPropertyChanged(nameof(IsMedia));
        OnPropertyChanged(nameof(IsBasic));
        OnPropertyChanged(nameof(IsWindows));
        OnPropertyChanged(nameof(CurrentWindowsOptions));
        OnPropertyChanged(nameof(IsBrowser));
        OnPropertyChanged(nameof(IsBrowserGroup));
        OnPropertyChanged(nameof(IsMacro));
        if (value == "browser") RefreshBrowsers();
        if (value == "browsergroup") RefreshBrowserGroups();
        if (value == "macro") RefreshMacros();
        // §129 (pekerja B): masuk ke keluarga Windows = coba fetch
        // ikon Tools (guard di dalam: hanya command Launches yang
        // punya sumber ikon; command/chord/ms-settings dilewati).
        if (IsWindows) _ = AutoFetchWindowsToolAsync();
        OnPropertyChanged(nameof(IsLauncher));
        OnPropertyChanged(nameof(IsLink));
        if (value == "launcher" && LauncherAppOptions.Count == 0) RefreshLauncherApps();
        OnPropertyChanged(nameof(IsObs));
        OnPropertyChanged(nameof(IsStreamerBot));
        if (IsStreamerBot) _ = RefreshStreamerBotActionsAsync();
        OnPropertyChanged(nameof(IsSteam));
        OnPropertyChanged(nameof(IsSteamGame));
        OnPropertyChanged(nameof(IsSteamStatus));
        OnPropertyChanged(nameof(IsSteamBigPicture));
        OnPropertyChanged(nameof(SteamShowTogglePanel));
        OnPropertyChanged(nameof(ShowSteamRunningGameOptions));
        OnPropertyChanged(nameof(ShowSteamSecondStatusOptions));
        if (value == "steam")
        {
            if (SteamGameOptions.Count == 0) RefreshSteamGames();
            if (SteamCommandValue == "launch" && !string.IsNullOrWhiteSpace(SteamAppId) && !_suppressAutoFetch && AutoFetchEnabled)
            {
                var choice = SteamGameOptions.FirstOrDefault(g => g.AppId == SteamAppId);
                if (choice != null)
                {
                    _ = AutoFetchSteamGameIconAsync(choice);
                }
            }
        }
        OnPropertyChanged(nameof(IsSound));
        OnPropertyChanged(nameof(IsDeck));
        OnPropertyChanged(nameof(IsProfile));
        OnPropertyChanged(nameof(DeckNeedsPage));
        OnPropertyChanged(nameof(ObsNeedsName));
        if (IsObs) _ = RefreshObsNamesAsync();
    }

    /// <summary>§129 (pekerja B): ganti command Windows = coba fetch
    /// ikon Tools, pola yang sama seperti ganti exe browser §95 /
    /// ganti target launcher. Guard suppress menahan fetch selama
    /// ctor load; resolver menahan command non-peluncuran.</summary>
    partial void OnWindowsValueChanged(string value)
    {
        if (_suppressAutoFetch || !AutoFetchEnabled || !IsWindows) return;
        _ = AutoFetchWindowsToolAsync();
    }

    partial void OnMediaValueChanged(string value)
    {
        // Alur (usul user): Jenis aksi -> Perintah -> mode jalan MENYESUAIKAN.
        // Volume = Ditahan-mengulang (satu-satunya cara yang terasa benar);
        // perintah media lain = Tap. User tetap boleh menimpa sesudahnya.
        if (value is "VolumeUp" or "VolumeDown")
        {
            if (SelectedTrigger != "Repeat")
                SetTriggerAuto("Repeat", Localizer.Get("S0527"));
        }
        else if (SelectedTrigger == "Repeat")
        {
            SetTriggerAuto("Tap", Localizer.Get("S0528"));
        }
    }

    partial void OnDeckCommandValueChanged(string value)
    {
        OnPropertyChanged(nameof(DeckNeedsPage));
        // Layer Modifier only makes sense as a hold: down = visit the
        // layer, up = return. Derive the trigger like volume derives
        // Repeat; the user can still override afterwards.
        if (value == "momentary")
        {
            if (SelectedTrigger != "Momentary")
                SetTriggerAuto("Momentary", Localizer.Get("S0529"));
        }
        else if (SelectedTrigger == "Momentary")
        {
            SetTriggerAuto("Tap", Localizer.Get("S0530"));
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
        _ = RefreshObsNamesAsync();
    }

    partial void OnObsNameTextChanged(string value)
    {
        if (ObsNeedsName2) _ = RefreshObsNames2Async();
    }

    // --- Live OBS discovery: scene/input names as a dropdown -------------

    public ObservableCollection<string> ObsNameOptions { get; } = new();
    public ObservableCollection<string> ObsName2Options { get; } = new();
    [ObservableProperty] private string _obsListHint = "";
    [ObservableProperty] private string _obsList2Hint = "";

    // --- Live Streamer.bot discovery: action names as a dropdown (§147) ---

    public ObservableCollection<string> StreamerBotActionOptions { get; } = new();
    [ObservableProperty] private string _streamerBotDiscoveryHint = "";

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    [RelayCommand]
    private Task RefreshObsNames() => RefreshObsNamesAsync();

    [RelayCommand]
    private Task RefreshObsNames2() => RefreshObsNames2Async();

    private void NotConnectedHint() =>
        RunOnUi(() => ObsListHint = Localizer.Get("S0531"));

    /// <summary>Fills the primary dropdown from the live OBS, per the
    /// command's spec: scenes, inputs, text inputs only, scenes+inputs,
    /// collections, or profiles.</summary>
    public async Task RefreshObsNamesAsync()
    {
        if (!IsObs || !ObsNeedsName) return;
        if (!_host.Obs.IsConnected) { NotConnectedHint(); return; }
        RunOnUi(() => ObsListHint = Localizer.Get("S0532"));
        var spec = CurrentObsSpec;
        (IReadOnlyList<string> Names, string? Error) result = spec.NameSource switch
        {
            ActionCatalog.ObsNameSource.Scenes => await _host.Obs.GetSceneNamesAsync(),
            ActionCatalog.ObsNameSource.Inputs => await _host.Obs.GetInputNamesAsync(),
            ActionCatalog.ObsNameSource.Collections => await _host.Obs.GetSceneCollectionNamesAsync(),
            ActionCatalog.ObsNameSource.Profiles => await _host.Obs.GetProfileNamesAsync(),
            ActionCatalog.ObsNameSource.TextInputs => await LoadTextInputsAsync(),
            ActionCatalog.ObsNameSource.ScenesAndInputs => await LoadScenesAndInputsAsync(),
            _ => (Array.Empty<string>(), null),
        };
        RunOnUi(() =>
        {
            ObsNameOptions.Clear();
            foreach (var n in result.Names) ObsNameOptions.Add(n);
            ObsListHint = result.Error is not null
                ? Localizer.Get("S0533") + result.Error
                : result.Names.Count == 0
                    ? Localizer.Get("S0534")
                    : Localizer.Format("S0535", result.Names.Count);
        });
        if (ObsNeedsName2) _ = RefreshObsNames2Async();
    }

    private async Task<(IReadOnlyList<string> Names, string? Error)> LoadTextInputsAsync()
    {
        var (inputs, error) = await _host.Obs.GetInputsAsync();
        if (error is not null) return (Array.Empty<string>(), error);
        return (inputs.Where(i => i.Kind.Contains("text", StringComparison.OrdinalIgnoreCase))
                      .Select(i => i.Name).ToList(), null);
    }

    private async Task<(IReadOnlyList<string> Names, string? Error)> LoadScenesAndInputsAsync()
    {
        var scenes = await _host.Obs.GetSceneNamesAsync();
        if (scenes.Error is not null) return scenes;
        var inputs = await _host.Obs.GetInputNamesAsync();
        if (inputs.Error is not null) return inputs;
        return (scenes.Names.Concat(inputs.Names).Distinct().ToList(), null);
    }

    /// <summary>Fills the cascading second dropdown: sources inside the
    /// chosen scene, or filters on the chosen input.</summary>
    public async Task RefreshObsNames2Async()
    {
        if (!IsObs || !ObsNeedsName2) return;
        if (!_host.Obs.IsConnected) { RunOnUi(() => ObsList2Hint = ""); return; }
        if (string.IsNullOrWhiteSpace(ObsNameText))
        {
            RunOnUi(() => { ObsName2Options.Clear(); ObsList2Hint = Localizer.Get("S0536"); });
            return;
        }
        var spec = CurrentObsSpec;
        string primary = ObsNameText;
        var result = spec.SecondSource == ActionCatalog.ObsSecondSource.SceneItems
            ? await _host.Obs.GetSceneItemNamesAsync(primary)
            : await _host.Obs.GetFilterNamesAsync(primary);
        if (ObsNameText != primary) return; // primary changed while loading
        RunOnUi(() =>
        {
            ObsName2Options.Clear();
            foreach (var n in result.Names) ObsName2Options.Add(n);
            ObsList2Hint = result.Error is not null
                ? Localizer.Get("S0537") + result.Error
                : result.Names.Count == 0 ? Localizer.Get("S0538") : Localizer.Format("S0539", result.Names.Count);
        });
    }

    /// <summary>§147 - fills the action-name dropdown from the live
    /// Streamer.bot, mirroring <see cref="RefreshObsNamesAsync"/>:
    /// only when connected; when disconnected or on failure the
    /// options are left untouched, the hint carries the honest
    /// reason, and saving is never blocked (the name stays a
    /// free-text field).</summary>
    [RelayCommand]
    private Task RefreshStreamerBotActions() => RefreshStreamerBotActionsAsync();

    public async Task RefreshStreamerBotActionsAsync()
    {
        if (!IsStreamerBot) return;
        if (!_host.StreamerBot.IsConnected)
        {
            RunOnUi(() => StreamerBotDiscoveryHint = Localizer.Get("S1756"));
            return;
        }
        var (actions, error) = await _host.StreamerBot
            .GetActionsAsync(CancellationToken.None).ConfigureAwait(false);
        RunOnUi(() =>
        {
            if (error is not null)
            {
                StreamerBotDiscoveryHint = Localizer.Format("S1755", error);
                return;
            }
            StreamerBotActionOptions.Clear();
            foreach (var a in actions)
            {
                string name = string.IsNullOrWhiteSpace(a.Name) ? (a.Id ?? "") : a.Name;
                if (!string.IsNullOrWhiteSpace(name)) StreamerBotActionOptions.Add(name);
            }
            StreamerBotDiscoveryHint = "";
        });
    }

    private static string NormalizeBasic(string? raw) => ActionStepCodec.NormalizeBasic(raw);

    private static string NormalizeMedia(string? raw) => ActionStepCodec.NormalizeMedia(raw);

    private static string NormalizeObs(string? raw) => ActionStepCodec.NormalizeObs(raw);

    public static string NormalizeWindows(string? raw) => ActionStepCodec.NormalizeWindows(raw, WindowsCommandValues);

    [RelayCommand]
    public void RefreshSteamGames()
    {
        var install = SteamDiscovery.GetInstallInfo();
        IsSteamInstalled = install.IsInstalled;
        if (!install.IsInstalled) return;

        IsLoadingSteamGames = true;
        _ = Task.Run(() =>
        {
            try
            {
                var games = SteamDiscovery.DiscoverGames();
                RunOnUi(() =>
                {
                    SteamGameOptions.Clear();
                    foreach (var g in games)
                    {
                        SteamGameOptions.Add(new SteamGameChoice(g.AppId, g.Name, g.ExecutablePath, g.IconPath));
                    }

                    if (!string.IsNullOrWhiteSpace(SteamAppId) && SteamGameOptions.All(g => g.AppId != SteamAppId))
                    {
                        string fallbackName = string.IsNullOrWhiteSpace(SteamGameName) ? $"App {SteamAppId}" : SteamGameName;
                        SteamGameOptions.Insert(0, new SteamGameChoice(SteamAppId, $"{fallbackName} [Offline]", null, null));
                    }

                    foreach (var step in Combinations.SelectMany(c => c.Steps))
                    {
                        RefreshStepSteamGames(step);
                    }

                    OnPropertyChanged(nameof(SteamGameSummaryText));

                    if (SelectedActionId == "steam" && SteamCommandValue == "launch" && !string.IsNullOrWhiteSpace(SteamAppId) && string.IsNullOrEmpty(AutoIcon) && !_suppressAutoFetch && AutoFetchEnabled)
                    {
                        var choice = SteamGameOptions.FirstOrDefault(g => g.AppId == SteamAppId);
                        if (choice != null)
                        {
                            _ = AutoFetchSteamGameIconAsync(choice);
                        }
                    }
                });
            }
            finally
            {
                RunOnUi(() => IsLoadingSteamGames = false);
            }
        });
    }

    private void RefreshStepSteamGames(ComboStepViewModel step)
    {
        step.SteamGameOptions.Clear();
        foreach (var g in SteamGameOptions)
        {
            step.SteamGameOptions.Add(g);
        }
    }

    [RelayCommand]
    public void RefreshBrowsers()
    {
        var detected = BrowserDiscovery.DetectBrowsers(_host.Config.CustomBrowsers);
        BrowserOptions.Clear();
        foreach (var b in detected)
            BrowserOptions.Add(new BrowserChoice(b.ExePath, b.Engine,
                b.Name + (b.IsCustom ? Localizer.Get("S0540") : "") + (File.Exists(b.ExePath) ? "" : Localizer.Get("S0541")),
                b.UserDataDir, b.IsCustom, !File.Exists(b.ExePath)));
        // TIDAK ada lagi item "Belum memilih" di daftar (25 Okt 2026):
        // opsi yang bisa dipilih padahal tidak ada gunanya dipilih adalah
        // logika cacad. Keadaan belum-memilih ditampilkan sebagai
        // placeholder ComboBox (Tag="Belum memilih" di XAML + watermark
        // template App.xaml), bukan sebagai item.
        if (BrowserExe != CustomBrowserSentinel && !string.IsNullOrEmpty(BrowserExe)
            && BrowserOptions.All(o => o.ExePath != BrowserExe))
        {
            // Browser tile ini sudah tidak terinstal: tetap tampilkan agar
            // pengguna tahu kenapa tile-nya gagal, bukan kotak kosong.
            // Dir tersimpan dipertahankan di entri ini (lihat aturan di
            // OnBrowserExeChanged) agar tidak hilang saat save ulang.
            BrowserOptions.Add(new BrowserChoice(BrowserExe, BrowserEngine,
                Path.GetFileNameWithoutExtension(BrowserExe) + Localizer.Get("S0541"),
                BrowserUserDataDir, false, true));
        }
        else if (BrowserExe != CustomBrowserSentinel && !string.IsNullOrEmpty(BrowserExe))
        {
            // (Penjaga empty di sini penting: tanpa item placeholder,
            // BrowserExe kosong tidak boleh masuk ke First() di bawah -
            // tidak ada opsi dengan ExePath kosong di daftar.)
            var selected = BrowserOptions.First(o => o.ExePath == BrowserExe);
            BrowserEngine = selected.Engine;
            // Segarkan dir dari opsi terpilih juga saat pindai ulang (bila
            // dir browser kustom diubah di manager); aturan sama seperti
            // OnBrowserExeChanged, entri hilang tidak disentuh.
            if (selected.IsCustom)
                BrowserUserDataDir = selected.UserDataDir;
            else if (!selected.IsMissing)
                BrowserUserDataDir = "";
        }
        // CUSTOM selalu tersedia, bahkan bila tidak ada browser terdeteksi -
        // inilah jalan keluar untuk browser portabel/tak dikenal.
        BrowserOptions.Add(new BrowserChoice(CustomBrowserSentinel, "Chromium", "CUSTOM", ""));
        RefreshBrowserProfiles();
        // §158: langkah kombinasi memakai daftar browser yang sama;
        // pindai ulang kartu utama menyegarkan langkahnya juga (di
        // ctor Combinations masih kosong, jadi loop ini no-op di sana).
        foreach (var step in Combinations.SelectMany(c => c.Steps)) RefreshStepBrowsers(step);
    }

    public void RefreshBrowserProfiles()
    {
        var browser = BrowserOptions.FirstOrDefault(o => o.ExePath == BrowserExe);
        if (browser is null || string.IsNullOrEmpty(browser.ExePath))
        {
            // Belum memilih browser: daftar kosong, placeholder yang bicara.
            _allBrowserProfiles = new List<BrowserProfileChoice>();
            BrowserProfiles.Clear();
            BrowserProfileKey = "";
            BrowserProfilePlaceholder = Localizer.Get("S0075");
            return;
        }
        BrowserEngine = browser.Engine;
        var scan = BrowserDiscovery.GetProfilesDetailed(
                new DetectedBrowser("", "", browser.ExePath, browser.Engine, browser.UserDataDir, IsCustom: false));
        _allBrowserProfiles = scan.Profiles
            .Select(pr => new BrowserProfileChoice(pr.Key, pr.Display)).ToList();
        // §45: daftar yang terpotong karena izin dulu tampil tanpa
        // keterangan apa pun - beri catatan (tidak memblokir Simpan).
        string unreadableNote = Localizer.Get("S0543");
        if (scan.Unreadable) ErrorText = unreadableNote;
        else if (ErrorText == unreadableNote) ErrorText = "";
        if (_allBrowserProfiles.Count == 0)
        {
            BrowserProfiles.Clear();
            BrowserProfileKey = "";
            BrowserProfilePlaceholder = Localizer.Get("S0544");
            return;
        }
        BrowserProfilePlaceholder = Localizer.Get("S0075");
        BrowserProfiles.Clear();
        foreach (var pr in _allBrowserProfiles) BrowserProfiles.Add(pr);
        if (string.IsNullOrEmpty(BrowserProfileKey))
        {
            // Tile baru: default ke profil pertama (perilaku lama).
            BrowserProfileKey = _allBrowserProfiles[0].Key;
            _missingBrowserProfileKey = null;
        }
        else if (_allBrowserProfiles.All(pr => pr.Key != BrowserProfileKey))
        {
            // Kunci tersimpan hilang dari daftar live: JANGAN retarget
            // diam-diam ke [0] (§45) - itu mengubah data permanen
            // tanpa persetujuan. Tampilkan sentinel seperti pola
            // browser-hilang di atas, dan tolak Simpan (lihat Save()).
            // Tile multi dikecualikan: kuncinya hanya satu dari daftar
            // centang, dan eksekusi multi memakai daftar itu.
            if (!BrowserProfileMulti)
            {
                _missingBrowserProfileKey = BrowserProfileKey;
                var sentinel = new BrowserProfileChoice(BrowserProfileKey,
                    BrowserProfileKey + Localizer.Get("S0541"));
                _allBrowserProfiles.Add(sentinel);
                BrowserProfiles.Add(sentinel);
            }
            else _missingBrowserProfileKey = null;
        }
        else _missingBrowserProfileKey = null;
    }

    public void RefreshBrowserGroups()
    {
        BrowserGroupOptions.Clear();
        foreach (var g in _host.Config.BrowserProfileGroups)
            BrowserGroupOptions.Add(new ActionCatalog.ValueOption(g.Id, g.Name));
        // §158: langkah kombinasi memakai daftar grup yang sama.
        foreach (var step in Combinations.SelectMany(c => c.Steps)) RefreshStepBrowserGroups(step);
    }

    /// <summary>Macro dropdown options, rebuilt from the live library
    /// every time the macro section opens or the manager closes. A card
    /// whose macro vanished from the library keeps a sentinel entry
    /// (same no-silent-retarget rule as the missing-browser-profile
    /// pattern) - and Save is refused by MacroAction.Validate.</summary>
    public void RefreshMacros()
    {
        MacroOptions.Clear();
        foreach (var m in _host.Config.Macros)
            MacroOptions.Add(new ActionCatalog.ValueOption(m.Id, m.Name));
        if (!string.IsNullOrEmpty(MacroId) && MacroOptions.All(o => o.Value != MacroId))
            MacroOptions.Add(new ActionCatalog.ValueOption(MacroId, Localizer.Get("S0545")));
        // §158: langkah kombinasi memakai daftar macro yang sama.
        foreach (var step in Combinations.SelectMany(c => c.Steps)) RefreshStepMacros(step);
    }

    [RelayCommand]
    private void ManageBrowserGroups() => RequestManageBrowserGroups?.Invoke();

    [RelayCommand]
    private void ManageMacros() => RequestManageMacros?.Invoke();

    partial void OnBrowserProfileMultiChanged(bool value)
    {
        if (value)
        {
            // Masuk mode multi: bawa pilihan tunggal yang aktif ke
            // dalam centang (bila belum ada centang).
            if (BrowserProfileKeys.Count == 0 && !string.IsNullOrEmpty(BrowserProfileKey))
                BrowserProfileKeys.Add(BrowserProfileKey);
        }
        else if (BrowserProfileKeys.Count > 0)
        {
            // Kembali ke tunggal: pakai centang pertama.
            BrowserProfileKey = BrowserProfileKeys[0];
        }
        RefreshTrackedBrowserState();
    }

    partial void OnBrowserExeChanged(string value)
    {
        OnPropertyChanged(nameof(IsBrowserCustom));
        OnPropertyChanged(nameof(IsBrowserDropdown));
        OnPropertyChanged(nameof(PindaiButtonText));
        if (!_browserPickerReady) return;
        // Centang milik browser lama - ganti browser = centang hangus.
        BrowserProfileKeys.Clear();
        // Folder User Data mengikuti browser yang dipilih - tapi hanya
        // untuk browser kustom (cakupan aman temuan riset 4); browser
        // terdeteksi memakai lokasi defaultnya (dir = ""). Entri yang
        // hilang dari daftar mempertahankan dir tersimpannya.
        var selected = BrowserOptions.FirstOrDefault(o => o.ExePath == BrowserExe);
        if (selected is not null && selected.IsCustom)
            BrowserUserDataDir = selected.UserDataDir;
        else if (selected is not null && !selected.IsMissing && BrowserExe != CustomBrowserSentinel)
            BrowserUserDataDir = "";
        RefreshBrowserProfiles();
        RefreshTrackedBrowserState();
        // §95: ganti browser = ikon kartu mengikuti exe baru (pola yang
        // sama seperti ganti target launcher); guard suppress menahan
        // fetch selama ctor load, picker-ready menahan selama setup.
        if (!_suppressAutoFetch && AutoFetchEnabled && SelectedActionId == "browser")
            _ = AutoFetchBrowserAsync();
    }

    partial void OnBrowserProfileKeyChanged(string value)
    {
        RefreshTrackedBrowserState();
        if (value != _missingBrowserProfileKey) _missingBrowserProfileKey = null;
    }

    partial void OnManualBrowserExeChanged(string value)
    {
        RefreshTrackedBrowserState();
        // §95: exe manual CUSTOM berubah = fetch ikon exe itu.
        if (!_suppressAutoFetch && AutoFetchEnabled && SelectedActionId == "browser")
            _ = AutoFetchBrowserAsync();
    }

    [ObservableProperty] private bool _hasTrackedBrowserWindow;

    /// <summary>Perawatan pelacakan per tile (temuan riset 5): tombol
    /// "Lupakan jendela ini" hanya tampil bila ada rekaman untuk tile
    /// ini - obat untuk salah tempel jendela saudara tanpa memusnahkan
    /// semua pelacakan. Kunci sama seperti aksi browser: path manual
    /// bila CUSTOM, kunci profil bila dropdown.</summary>
    private void RefreshTrackedBrowserState()
    {
        if (!_browserPickerReady) { HasTrackedBrowserWindow = false; return; }
        // Multi: tombol "Lupakan jendela ini" disembunyikan - untuk
        // satu set profil, tombol per-tile itu ambigu (jendela profil
        // yang mana?), dan melupakan satu rekaman bisa membuat tile
        // grup yang memakai profil sama membuka jendela ganda.
        // Lupa per-jendela tetap tersedia di daftar Settings.
        if (BrowserProfileMulti) { HasTrackedBrowserWindow = false; return; }
        string exe;
        string key;
        if (BrowserExe == CustomBrowserSentinel)
        {
            exe = (ManualBrowserExe ?? "").Trim();
            key = (ManualProfilePath ?? "").Trim();
        }
        else
        {
            exe = BrowserExe;
            key = BrowserProfileKey;
        }
        HasTrackedBrowserWindow = exe.Length > 0 && key.Length > 0
            && BrowserWindowTracker.Find(_host.Config.BrowserWindowTracking, exe, key) is not null;
    }

    [RelayCommand]
    private void ForgetTrackedBrowser()
    {
        string exe;
        string key;
        if (BrowserExe == CustomBrowserSentinel)
        {
            exe = (ManualBrowserExe ?? "").Trim();
            key = (ManualProfilePath ?? "").Trim();
        }
        else
        {
            exe = BrowserExe;
            key = BrowserProfileKey;
        }
        // D12: Save tanpa try/catch di sini dulu melempar ke dispatcher
        // (crash) bila file konfigurasi terkunci - dan Remove sudah
        // terlanjur memutasi memori. Pola §46 seperti Save/Delete:
        // snapshot dulu, gagal simpan = kembalikan + tampilkan pesan
        // di footer (S0937), jangan ditelan diam, jangan crash.
        var tracking = _host.Config.BrowserWindowTracking;
        var removed = tracking.Where(t =>
            string.Equals(t.BrowserExe, exe, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.ProfileKey, key, StringComparison.Ordinal)).ToList();
        BrowserWindowTracker.Remove(tracking, exe, key);
        try { _host.Save(); }
        catch (Exception ex)
        {
            tracking.AddRange(removed);
            ErrorText = Localizer.Get("S0937") + " " + ex.Message;
            return;
        }
        RefreshTrackedBrowserState();
    }

    partial void OnManualProfilePathChanged(string value)
    {
        RefreshManualStatus();
        RefreshTrackedBrowserState();
    }

    private void RefreshManualStatus()
    {
        var (status, engine, display) = ManualProfileStatusFor((ManualProfilePath ?? "").Trim(), _manualEngine, _manualProfileDisplay);
        ManualProfileStatus = status;
        _manualEngine = engine;
        _manualProfileDisplay = display;
    }

    /// <summary>§158: the manual-profile-folder evaluation shared by
    /// the main card (<see cref="RefreshManualStatus"/>) and combo
    /// steps (<see cref="RefreshStepManualStatus"/>). Pure in/out
    /// over the trimmed path plus the carried engine/display:
    /// empty path clears the display; a missing folder keeps the
    /// carried display when one exists; a detected folder replaces
    /// both; an undetected existing folder falls back to the folder
    /// name. Verbatim rules from the old <c>RefreshManualStatus</c>.</summary>
    private static (string Status, string Engine, string Display) ManualProfileStatusFor(
        string path, string engine, string display)
    {
        if (path.Length == 0) return ("", engine, "");
        if (!Directory.Exists(path))
        {
            string kept = display.Length == 0
                ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : display;
            return (Localizer.Get("S0546"), engine, kept);
        }
        if (BrowserDiscovery.TryDetectProfileFolder(path, out string detectedEngine, out string detectedDisplay))
            return (Localizer.Format("S0547", detectedEngine, detectedDisplay), detectedEngine, detectedDisplay);
        return (Localizer.Get("S0548"),
            engine,
            Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }

    /// <summary>§158: the single funnel into Save/Test and the dirty
    /// baseline, delegating to the pure codec. The §129
    /// migrate-on-save rule (a legacy "windows" card resolves to its
    /// split id by command mechanism, only on save) lives in the
    /// codec now; opening or cancelling an old card still leaves its
    /// stored id untouched.</summary>
    private ActionStep? BuildStep() => ActionStepCodec.BuildStep(CollectFields(), SelectedActionId, _openedAsLegacyWindows);

    /// <summary>§158: gathers the current editor fields plus the
    /// plain inputs the codec needs from this VM's live lists (the
    /// OBS param spec and the resolved display names), exactly as
    /// the old inline <c>BuildStep</c> read them.</summary>
    private StepParamFields CollectFields()
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
            ManualEngine = _manualEngine,
            ManualProfileDisplay = _manualProfileDisplay,
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
            BrowserProfileDisplay = _allBrowserProfiles.FirstOrDefault(pr => pr.Key == BrowserProfileKey)?.Display ?? "",
            BrowserProfileNames = BrowserProfileKeys
                .Select(k => _allBrowserProfiles.FirstOrDefault(pr => pr.Key == k)?.Display ?? "").ToList(),
            BrowserGroupDisplay = BrowserGroupOptions.FirstOrDefault(o => o.Value == BrowserGroupId)?.Display ?? "",
            MacroDisplay = MacroOptions.FirstOrDefault(o => o.Value == MacroId)?.Display ?? "",
            DeckPageDisplay = DeckPageOptions.FirstOrDefault(o => o.Value == DeckPageId)?.Display ?? "",
            ProfileDisplay = ProfileOptions.FirstOrDefault(o => o.Value == ProfileId)?.Display ?? "",
        };
    }

    // --- icon & colour pickers with a visible selection (v2.9.3) ---
    public sealed partial class SwatchOption : ObservableObject
    {
        public string Value { get; init; } = "";
        [ObservableProperty] private bool _isSelected;
    }

    public System.Collections.ObjectModel.ObservableCollection<SwatchOption> IconOptions { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<SwatchOption> ColorOptions { get; } = new();

    private void BuildSwatches()
    {
        IconOptions.Clear();
        foreach (var glyph in ActionCatalog.IconPresets)
            IconOptions.Add(new SwatchOption { Value = glyph, IsSelected = glyph == Icon });
        ColorOptions.Clear();
        foreach (var hex in ActionCatalog.ColorPresets)
            ColorOptions.Add(new SwatchOption { Value = hex, IsSelected = string.Equals(hex, ColorHex, StringComparison.OrdinalIgnoreCase) });
    }
    partial void OnIconChanged(string value)
    {
        foreach (var o in IconOptions) o.IsSelected = o.Value == value;
        // An explicit icon pick (catalog key, upload, or typed value)
        // replaces any auto icon - the pick is the newer decision.
        // Suppressed during ctor load, where Icon is just restored.
        if (!_suppressAutoFetch) AutoIcon = "";
        RefreshIconPreview();
    }

    partial void OnAutoIconChanged(string value) => RefreshIconPreview();

    /// <summary>Preview follows the same Upload > Auto > Default
    /// chain as the tile: the file image when either tier has one,
    /// else the catalog vector/glyph.</summary>
    private void RefreshIconPreview()
    {
        UploadedIconPath = UploadedFilePath(Icon) ?? UploadedFilePath(AutoIcon);
        OnPropertyChanged(nameof(IconPreviewKey));
        OnPropertyChanged(nameof(IconPreviewGlyph));
    }

    /// <summary>Full path of the uploaded icon file for the current icon
    /// value, or null when it is not a file icon / the file is gone.</summary>
    private string? UploadedFilePath(string value)
    {
        string? name = IconReference.FileName(value);
        if (name is null) return null;
        string path = Path.Combine(_host.IconStore.Folder, name);
        return File.Exists(path) ? path : null;
    }

    public bool HasIconFetchStatus => !string.IsNullOrWhiteSpace(IconFetchStatus);

    partial void OnIconFetchStatusChanged(string value)
        => OnPropertyChanged(nameof(HasIconFetchStatus));

    public bool IconTabCatalogSelected => !ShowIconUpload;
    public bool IconTabUploadSelected => ShowIconUpload;

    partial void OnShowIconUploadChanged(bool value)
    {
        OnPropertyChanged(nameof(IconTabCatalogSelected));
        OnPropertyChanged(nameof(IconTabUploadSelected));
    }

    [RelayCommand]
    private void SetIconTab(string tab) => ShowIconUpload = tab == "upload";

    /// <summary>Imports an image file as this tile's icon (v3.4.0):
    /// conversion + strict storage live in IconStore; the editor only
    /// adopts the resulting reference. Returns success + a message the
    /// window shows on failure (success is shown inline instead).</summary>
    public (bool Ok, string Message) ImportIconFile(string path)
    {
        try
        {
            long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
            var result = _host.IconStore.Import(path, maxBytes);
            Icon = result.IconValue;
            IconUploadInfo = Localizer.Format("S0550", result.StoredWidth, result.StoredHeight) +
                Localizer.Format("S0551", IconReference.FormatSize(result.StoredBytes), IconReference.FormatSize(result.OriginalBytes));
            return (true, IconUploadInfo);
        }
        catch (IconImportException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, Localizer.Format("S0552", ex.Message));
        }
    }

    private bool AutoFetchEnabled => _host.Config.Settings.AutoFetchIcons;

    /// <summary>Smart priority (v3.6.0, the user's rule): Upload >
    /// Auto > Default. Auto-fetch writes the separate AutoIcon
    /// field, so the only thing that blocks it is a user upload
    /// (a file icon in Icon). Catalog keys, glyphs, and empty
    /// icons are all "default" tier and never block a refresh -
    /// which is what makes edit-and-switch-apps update the card:
    /// before v3.6.0 the previous auto icon sat in Icon, looked
    /// exactly like an upload, and silently vetoed every refresh.</summary>
    private bool MayAutoFetch => !IconReference.IsFileIcon(Icon);

    partial void OnLinkUrlChanged(string value)
    {
        if (_suppressAutoFetch || !AutoFetchEnabled || SelectedActionId != "link") return;
        _ = AutoFetchLinkAsync(value);
    }

    partial void OnLauncherTargetChanged(string value)
    {
        OnPropertyChanged(nameof(HasPickedApp));
        OnPropertyChanged(nameof(PickedAppName));
        if (_suppressAutoFetch || !AutoFetchEnabled || SelectedActionId != "launcher") return;
        _ = AutoFetchAppAsync(value);
    }

    private async Task AutoFetchLinkAsync(string value)
    {
        string key = "link:" + value.Trim();
        if (key == _lastAutoFetchKey || _fetchingIcon || !MayAutoFetch) return;
        _fetchTask = FetchLinkIconAsync(manual: false);
        await _fetchTask;
    }

    private async Task AutoFetchAppAsync(string value)
    {
        string key = "app:" + value.Trim();
        if (key == _lastAutoFetchKey || _fetchingIcon || !MayAutoFetch) return;
        _fetchTask = FetchAppIconAsync(manual: false);
        await _fetchTask;
    }

    private async Task AutoFetchSteamGameIconAsync(SteamGameChoice choice)
    {
        if (_fetchingIcon || !MayAutoFetch) return;
        string key = "steam:" + choice.AppId;
        if (key == _lastAutoFetchKey) return;
        _lastAutoFetchKey = key;
        _lastAutoFetchOk = false;
        _fetchingIcon = true;
        IconFetchStatus = Localizer.Get("S0559");

        try
        {
            string? iconFile = choice.IconPath;
            if (string.IsNullOrEmpty(iconFile) || !File.Exists(iconFile))
            {
                if (!string.IsNullOrEmpty(choice.ExecutablePath) && File.Exists(choice.ExecutablePath))
                {
                    iconFile = choice.ExecutablePath;
                }
                else
                {
                    iconFile = SteamDiscovery.FindGameIconPath(choice.AppId);
                }
            }

            if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
            {
                long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
                string ext = Path.GetExtension(iconFile).ToLowerInvariant();

                if (ext is ".ico" or ".png" or ".jpg" or ".jpeg")
                {
                    var result = _host.IconStore.Import(iconFile, maxBytes);
                    AutoIcon = result.IconValue;
                    _lastAutoFetchOk = true;
                    IconFetchStatus = Localizer.Format("S0563", IconReference.FormatSize(result.StoredBytes));
                    return;
                }
                else
                {
                    // It is an executable (.exe)
                    var bitmap = Services.AppIconExtractor.Extract(iconFile);
                    if (bitmap is not null)
                    {
                        var result = _host.IconStore.ImportBitmap(bitmap, 0, maxBytes);
                        AutoIcon = result.IconValue;
                        _lastAutoFetchOk = true;
                        IconFetchStatus = Localizer.Format("S0563", IconReference.FormatSize(result.StoredBytes));
                        return;
                    }
                }
            }

            IconFetchStatus = Localizer.Get("S0562");
        }
        catch (IconImportException ex)
        {
            IconFetchStatus = ex.Message;
        }
        catch (Exception ex)
        {
            IconFetchStatus = Localizer.Format("S0557", ex.Message);
        }
        finally
        {
            _fetchingIcon = false;
        }
    }

    /// <summary>§95: exe efektif kartu browser - exe manual saat CUSTOM,
    /// exe dropdown saat bukan. Dipakai fetch ikon profil + grup.</summary>
    private string EffectiveBrowserExe =>
        IsBrowserCustom ? ManualBrowserExe.Trim() : BrowserExe.Trim();

    /// <summary>§95: daftar exe anggota grup (urutan dipertahankan -
    /// urutan menentukan sel komposit), kosong bila grup tak ada.</summary>
    private List<string> GroupExePaths(string groupId) =>
        _host.Config.BrowserProfileGroups
            .FirstOrDefault(g => g.Id == groupId)?.Entries
            .Select(e => e.BrowserExe.Trim())
            .Where(p => p.Length > 0)
            .ToList() ?? new List<string>();

    partial void OnBrowserGroupIdChanged(string value)
    {
        if (_suppressAutoFetch || !AutoFetchEnabled || SelectedActionId != "browsergroup") return;
        _ = AutoFetchBrowserAsync();
    }

    private async Task AutoFetchBrowserAsync()
    {
        List<string> exes;
        string key;
        if (SelectedActionId == "browser")
        {
            string exe = EffectiveBrowserExe;
            if (exe.Length == 0) return;
            exes = new List<string> { exe };
            key = "browser:" + exe;
        }
        else if (SelectedActionId == "browsergroup")
        {
            exes = GroupExePaths(BrowserGroupId);
            if (exes.Count == 0) return;
            key = "browsergroup:" + BrowserGroupId + "|" + string.Join("|", exes);
        }
        else return;
        if (key == _lastAutoFetchKey || _fetchingIcon || !MayAutoFetch) return;
        _fetchTask = FetchBrowserIconAsync(exes, key);
        await _fetchTask;
    }

    /// <summary>§95: fetch ikon browser - satu exe → ikon exe itu;
    /// banyak exe → komposit BrowserIconComposer. Gagal ekstrak semua
    /// = ikon katalog bertahan (S1552), bukan error; upload user
    /// tetap memblokir lewat MayAutoFetch seperti link/launcher.</summary>
    private async Task FetchBrowserIconAsync(IReadOnlyList<string> exes, string key)
    {
        if (_fetchingIcon) return;
        _lastAutoFetchKey = key;
        _lastAutoFetchOk = false;
        _fetchingIcon = true;
        IconFetchStatus = Localizer.Get("S0559");
        try
        {
            var bitmap = Services.BrowserIconComposer.Compose(exes);
            if (bitmap is null)
            {
                IconFetchStatus = Localizer.Get("S1552");
                return;
            }
            long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
            var result = _host.IconStore.ImportBitmap(bitmap, 0, maxBytes);
            AutoIcon = result.IconValue;
            _lastAutoFetchOk = true;
            IconFetchStatus = Localizer.Format("S0563", IconReference.FormatSize(result.StoredBytes));
        }
        catch (IconImportException ex)
        {
            IconFetchStatus = ex.Message;
        }
        catch (Exception ex)
        {
            IconFetchStatus = Localizer.Format("S0557", ex.Message);
        }
        finally
        {
            _fetchingIcon = false;
        }
    }

    /// <summary>§129 (pekerja B): fetch ikon kartu Windows Tools -
    /// AutoIcon diambil dari file target peluncurannya lewat
    /// AppIconExtractor yang sama dengan launcher/browser (§95/§103).
    /// Hanya command Launches yang sampai ke sini (resolver
    /// mengembalikan null untuk chord/ms-settings/lock); guard
    /// suppress + MayAutoFetch sama seperti fetch lainnya - pilihan
    /// ikon eksplisit user (file upload) tetap menang, dan prioritas
    /// ikon kartu lain tidak berubah.</summary>
    private async Task AutoFetchWindowsToolAsync()
    {
        if (!IsWindows) return;
        string? source = Views.WindowsToolIconResolver.IconSourcePath(WindowsValue);
        if (source is null) return;
        string key = Views.WindowsToolIconResolver.FetchKey(WindowsValue, source);
        if (key == _lastAutoFetchKey || _fetchingIcon || !MayAutoFetch) return;
        _fetchTask = FetchWindowsToolIconAsync(source, key);
        await _fetchTask;
    }

    /// <summary>§129 (pekerja B): ekstrak ikon dari file sumber
    /// resolver, simpan sebagai AutoIcon lewat IconStore yang sama
    /// dengan ikon browser/launcher. Gagal ekstrak (file hilang,
    /// shell tak memberi ikon - mis. .msc di edisi Home) = ikon
    /// katalog/default bertahan (S0562), bukan error; AutoIcon lama
    /// tidak dihapus - dan ikon TIDAK PERNAH menggagalkan save,
    /// karena fetch ini hanya berjalan sebelum/di luar Save, yang
    /// failures-nya ditangkap di sini seperti fetch lainnya.</summary>
    private async Task FetchWindowsToolIconAsync(string sourcePath, string key)
    {
        if (_fetchingIcon) return;
        _lastAutoFetchKey = key;
        _lastAutoFetchOk = false;
        _fetchingIcon = true;
        IconFetchStatus = Localizer.Get("S0559");
        try
        {
            var bitmap = Services.AppIconExtractor.Extract(sourcePath);
            if (bitmap is null)
            {
                IconFetchStatus = Localizer.Get("S0562");
                return;
            }
            long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
            var result = _host.IconStore.ImportBitmap(bitmap, 0, maxBytes);
            AutoIcon = result.IconValue;
            _lastAutoFetchOk = true;
            IconFetchStatus = Localizer.Format("S0563", IconReference.FormatSize(result.StoredBytes));
        }
        catch (IconImportException ex)
        {
            IconFetchStatus = ex.Message;
        }
        catch (Exception ex)
        {
            IconFetchStatus = Localizer.Format("S0557", ex.Message);
        }
        finally
        {
            _fetchingIcon = false;
        }
    }

    /// <summary>"Sudah pasti" (v3.5.1): saving must not outrun a
    /// pending fetch, and a target that was never fetched (field
    /// never lost focus) gets one fetch attempt before the icon is
    /// written - otherwise the saved tile silently keeps the old icon.</summary>
    private async Task EnsureFetchBeforeSaveAsync()
    {
        if (!AutoFetchEnabled) return;
        if (SelectedActionId == "link")
        {
            string key = "link:" + LinkUrl.Trim();
            if (LinkUrl.Trim().Length == 0 || !MayAutoFetch) return;
            if (_fetchingIcon)
            {
                if (_fetchTask is not null) await _fetchTask;
                return;
            }
            if (key != _lastAutoFetchKey || !_lastAutoFetchOk)
                await FetchLinkIconAsync(manual: false);
        }
        else if (SelectedActionId == "launcher")
        {
            string key = "app:" + LauncherTarget.Trim();
            if (LauncherTarget.Trim().Length == 0 || !MayAutoFetch) return;
            if (_fetchingIcon)
            {
                if (_fetchTask is not null) await _fetchTask;
                return;
            }
            if (key != _lastAutoFetchKey || !_lastAutoFetchOk)
                await FetchAppIconAsync(manual: false);
        }
        else if (SelectedActionId == "browser")
        {
            // §95: kartu Browser Profile memakai ikon exe browser yang
            // dipilih (termasuk Custom → exe manual), bukan ikon katalog.
            string exe = EffectiveBrowserExe;
            if (exe.Length == 0 || !MayAutoFetch) return;
            string key = "browser:" + exe;
            if (_fetchingIcon)
            {
                if (_fetchTask is not null) await _fetchTask;
                return;
            }
            if (key != _lastAutoFetchKey || !_lastAutoFetchOk)
                await FetchBrowserIconAsync(new[] { exe }, key);
        }
        else if (SelectedActionId == "browsergroup")
        {
            // §95: kartu Grup memakai ikon gabungan (komposit) semua exe
            // anggotanya; kunci memuat daftar exe agar edit grup + simpan
            // ulang kartu ikut menyegarkan ikonnya.
            var exes = GroupExePaths(BrowserGroupId);
            if (exes.Count == 0 || !MayAutoFetch) return;
            string key = "browsergroup:" + BrowserGroupId + "|" + string.Join("|", exes);
            if (_fetchingIcon)
            {
                if (_fetchTask is not null) await _fetchTask;
                return;
            }
            if (key != _lastAutoFetchKey || !_lastAutoFetchOk)
                await FetchBrowserIconAsync(exes, key);
        }
        else if (IsWindows)
        {
            // §129 (pekerja B): kartu Windows Tools memakai ikon file
            // target peluncurannya; kartu Command / ms-settings tidak
            // punya sumber ikon (resolver null) dan dilewati tanpa
            // mengubah AutoIcon apa pun - "sudah pasti" yang sama:
            // command yang belum pernah di-fetch mendapat satu
            // percobaan sebelum ikon ditulis saat save.
            string? source = Views.WindowsToolIconResolver.IconSourcePath(WindowsValue);
            if (source is null || !MayAutoFetch) return;
            string key = Views.WindowsToolIconResolver.FetchKey(WindowsValue, source);
            if (_fetchingIcon)
            {
                if (_fetchTask is not null) await _fetchTask;
                return;
            }
            if (key != _lastAutoFetchKey || !_lastAutoFetchOk)
                await FetchWindowsToolIconAsync(source, key);
        }
    }

    private async Task FetchLinkIconAsync(bool manual)
    {
        string url = LinkUrl.Trim();
        if (url.Length == 0)
        {
            if (manual) IconFetchStatus = Localizer.Get("S0553");
            return;
        }
        if (_fetchingIcon) return;
        _lastAutoFetchKey = "link:" + url;
        _lastAutoFetchOk = false;
        _fetchingIcon = true;
        IconFetchStatus = Localizer.Get("S0554");
        try
        {
            // §171: cache-first - ikon tersimpan dipakai tanpa unduh;
            // status memakai ukuran file tersimpan yang sebenarnya.
            if (_host.IconStore.TryGetCachedFavicon(url) is string cached)
            {
                AutoIcon = cached;
                _lastAutoFetchOk = true;
                long cachedBytes = _host.IconStore.StoredBytes(cached) ?? 0;
                IconFetchStatus = Localizer.Format("S0556", IconReference.FormatSize(cachedBytes));
                return;
            }
            byte[]? data = await FaviconFetcher.FetchAsync(url);
            if (data is null)
            {
                IconFetchStatus = Localizer.Get("S0555");
                return;
            }
            long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
            var result = _host.IconStore.ImportBytes(data, maxBytes);
            AutoIcon = result.IconValue;
            _host.IconStore.SetCachedFavicon(url, result.IconValue);
            _lastAutoFetchOk = true;
            IconFetchStatus = Localizer.Format("S0556", IconReference.FormatSize(result.StoredBytes));
        }
        catch (IconImportException ex)
        {
            IconFetchStatus = ex.Message;
        }
        catch (Exception ex)
        {
            IconFetchStatus = Localizer.Format("S0557", ex.Message);
        }
        finally
        {
            _fetchingIcon = false;
        }
    }

    private async Task FetchAppIconAsync(bool manual)
    {
        string target = LauncherTarget.Trim();
        if (target.Length == 0)
        {
            if (manual) IconFetchStatus = Localizer.Get("S0558");
            return;
        }
        if (_fetchingIcon) return;
        _lastAutoFetchKey = "app:" + target;
        _lastAutoFetchOk = false;
        _fetchingIcon = true;
        IconFetchStatus = Localizer.Get("S0559");
        try
        {
            var bitmap = AppIconExtractor.Extract(target);
            if (bitmap is null)
            {
                // Second chance (v3.5.1): the app itself gives no
                // icon - Store apps live in a permission-restricted
                // folder, for example - so try the app's website
                // favicon instead of giving up.
                IconFetchStatus = Localizer.Get("S0560");
                if (IconReference.GuessDomain(target) is string domain)
                {
                    // §171: cache-first juga untuk fallback favicon.
                    if (_host.IconStore.TryGetCachedFavicon("https://" + domain) is string cached)
                    {
                        AutoIcon = cached;
                        _lastAutoFetchOk = true;
                        IconFetchStatus = Localizer.Format("S0561", domain);
                        return;
                    }
                    if (await FaviconFetcher.FetchAsync("https://" + domain) is byte[] web)
                    {
                        long webMax = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
                        var webResult = _host.IconStore.ImportBytes(web, webMax);
                        AutoIcon = webResult.IconValue;
                        _host.IconStore.SetCachedFavicon("https://" + domain, webResult.IconValue);
                        _lastAutoFetchOk = true;
                        IconFetchStatus = Localizer.Format("S0561", domain);
                        return;
                    }
                }
                IconFetchStatus = Localizer.Get("S0562");
                return;
            }
            long maxBytes = (long)_host.Config.Settings.IconUploadMaxKb * 1024;
            var result = _host.IconStore.ImportBitmap(bitmap, 0, maxBytes);
            AutoIcon = result.IconValue;
            _lastAutoFetchOk = true;
            IconFetchStatus = Localizer.Format("S0563", IconReference.FormatSize(result.StoredBytes));
        }
        catch (IconImportException ex)
        {
            IconFetchStatus = ex.Message;
        }
        catch (Exception ex)
        {
            IconFetchStatus = Localizer.Format("S0557", ex.Message);
        }
        finally
        {
            _fetchingIcon = false;
        }
    }

    /// <summary>The vector key the current icon text resolves to (null for a typed glyph).</summary>
    public string? IconPreviewKey => UploadedIconPath is null ? Views.AppIcons.ResolveKey(Icon) : null;

    /// <summary>A typed emoji/glyph to preview when the icon text is not a vector key.</summary>
    public string? IconPreviewGlyph => IconPreviewKey is null && UploadedIconPath is null && !IconReference.IsFileIcon(Icon) && !string.IsNullOrWhiteSpace(Icon) ? Icon.Trim() : null;

    partial void OnColorHexChanged(string value)
    {
        foreach (var o in ColorOptions)
            o.IsSelected = string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void PickColor(string color) => ColorHex = color;

    [RelayCommand]
    private void PickIcon(string glyph) => Icon = glyph;

    [RelayCommand]
    private void PickCustomColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        if (System.Drawing.ColorTranslator.FromHtml(ColorHex) is { } seed)
            dialog.Color = seed;
        dialog.FullOpen = true;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            ColorHex = Localizer.Format("S0564", c.R, c.G, c.B);
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        ErrorText = "";
        var step = BuildStep();
        if (step is null) { ResultText = Localizer.Get("S0565"); return; }
        if (!_host.ActionsById.TryGetValue(step.ActionId, out var action)) { ErrorText = Localizer.Get("S0566"); return; }
        var vr = action.Validate(step.Params);
        if (!vr.IsValid) { ErrorText = string.Join("; ", vr.Errors); return; }
        var r = await action.TestAsync(step.Params, new ActionContext { Cancellation = CancellationToken.None });
        ResultText = r.Success ? Localizer.Get("S0567") : Localizer.Get("S0568") + r.Error;
    }

    [RelayCommand]
    private async Task Save()
    {
        ErrorText = "";
        await EnsureFetchBeforeSaveAsync();
        // ⚠ popup (bisa dimatikan user): simpan binding untuk tombol yang
        // lingkup lain sudah pakai harus keputusan sadar, bukan kecelakaan.
        if (BuildConflictMessage() is string conflict
            && !_host.Config.Settings.SuppressConflictWarning
            && ConflictPrompt is not null)
        {
            var (proceed, dontShowAgain) = ConflictPrompt(conflict);
            if (dontShowAgain)
            {
                // Bendera saja, tanpa Save langsung: Save utama di bawah
                // yang mempersistenkannya. Dulu Save di sini bisa
                // melempar sebelum save utama dan menjadi dialog crash.
                _host.Config.Settings.SuppressConflictWarning = true;
            }
            if (!proceed) return; // batal - dialog editor tetap terbuka
        }
        var step = BuildStep();
        if (step is not null)
        {
            if (!_host.ActionsById.TryGetValue(step.ActionId, out var action))
            {
                // Aksi tak terdaftar (mis. binding lama yang tipenya sudah
                // tidak ada): tolak seperti TestAsync() - jangan simpan kartu
                // mati diam-diam yang tidak menjalankan apa pun.
                ErrorText = Localizer.Get("S0566");
                return;
            }
            var vr = action.Validate(step.Params);
            if (!vr.IsValid)
            {
                ErrorText = Localizer.Get("S0569") + string.Join("; ", vr.Errors);
                return;
            }
        }
        // Profil tile ini sudah tidak ditemukan: tolak Simpan sampai
        // pengguna memilih profil lain - jangan simpan retarget liar.
        if (SelectedActionId == "browser" && !BrowserProfileMulti
            && _missingBrowserProfileKey is not null && BrowserProfileKey == _missingBrowserProfileKey)
        {
            ErrorText = Localizer.Get("S0570");
            return;
        }

        ApplyStagedKeyChange();
        // §168a: a blank display name falls back to the chord text,
        // mirroring the new-card default ("Alt+V", not "Key V").
        _binding.Label = string.IsNullOrWhiteSpace(Label) ? ChordMask.Format(_binding.ChordMask, _binding.KeyName) : Label.Trim();
        _binding.Icon = Icon?.Trim() ?? "";
        _binding.AutoIcon = AutoIcon;
        _binding.BackgroundColor = ColorHex;
        _binding.Trigger = Enum.TryParse<TriggerKind>(SelectedTrigger, out var trigger) ? trigger : TriggerKind.Tap;
        _binding.IsActive = IsCardActive;
        _binding.Combinations = Combinations.Select(c => c.ToConfig()).ToList();
        var rest = _binding.Steps.Skip(1).ToList();
        _binding.Steps = step is null ? new List<ActionStep>() : new List<ActionStep> { step };
        if (step is not null) _binding.Steps.AddRange(rest);

        // Save gagal (disk/izin) TIDAK BOLEH menjadi dialog crash
        // dengan memori sudah termutasi (§45): pulihkan snapshot,
        // tampilkan ErrorText, editor tetap terbuka.
        try { _host.Save(); }
        catch (Exception ex)
        {
            RestorePageSnapshot();
            _host.ReloadActivePage();
            ErrorText = Localizer.Get("S0937") + " " + ex.Message;
            return;
        }
        _host.ReloadActivePage();
        DiscardConfirmed = true;
        CaptureBaseline();
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Delete()
    {
        _page.Bindings.Remove(_binding);
        try { _host.Save(); }
        catch (Exception ex)
        {
            RestorePageSnapshot();
            _host.ReloadActivePage();
            ErrorText = Localizer.Get("S0916") + " " + ex.Message;
            return;
        }
        _host.ReloadActivePage();
        DiscardConfirmed = true;
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        // Discard diam dilarang: edit yang belum disimpan harus
        // dikonfirmasi dulu (S0906, string yang sama dengan macro).
        // Tanpa perubahan, Batal tetap diam - benar. Konfirmasi di
        // sini, SEBELUM restore, agar jawaban "Tidak" tidak
        // meninggalkan snapshot yang sudah dipulihkan. Window_Closing
        // tidak bertanya lagi berkat DiscardConfirmed.
        if (!DiscardConfirmed && HasUnsavedChanges()
            && DiscardPrompt is not null && !DiscardPrompt(Localizer.Get("S0906")))
            return;
        DiscardConfirmed = true;
        RestorePageSnapshot();
        _host.ReloadActivePage();
        RequestClose?.Invoke(false);
    }

    /// <summary>Memulihkan bindings halaman ke keadaan saat editor
    /// dibuka, DI TEMPAT (objek yang sama, bukan ganti referensi -
    /// tile di jendela utama membungkus objek-objek ini). Bila jumlah
    /// berubah (tidak terjadi di jalur Batal/Save-gagal), ganti daftar
    /// sebagai jalan terakhir.</summary>
    private void RestorePageSnapshot()
    {
        var restored = JsonSerializer.Deserialize<List<BindingConfig>>(_pageSnapshotJson, ConfigStore.JsonOptions);
        if (restored is null) return;
        if (restored.Count == _page.Bindings.Count)
        {
            for (int i = 0; i < restored.Count; i++)
                _page.Bindings[i].CopyFrom(restored[i]);
        }
        else
        {
            _page.Bindings.Clear();
            _page.Bindings.AddRange(restored);
        }
    }
}

/// <summary>§158 - one combination: the second-key capture fields
/// (unchanged flow: Add → listening → deck press → name; pill
/// re-capture; × delete) plus the ordered step list edited with the
/// main-editor action pattern. The old single <see cref="ActionId"/>/
/// <see cref="ActionTarget"/> pair is kept ONLY as computed
/// pass-throughs over the first step, because the current XAML
/// (<c>BindingEditorWindow.xaml</c>) still binds both (action
/// dropdown + target textbox) - Worker B's rewritten combo section
/// must bind the step rows instead and must not bind these. The
/// setters exist for that legacy binding alone: they write through
/// to the first step's single param field.</summary>
public sealed partial class CombinationItemViewModel : ObservableObject
{
    public CombinationItemViewModel()
    {
        Steps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ActionId));
            OnPropertyChanged(nameof(ActionTarget));
        };
    }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private ushort _secondScanCode;
    [ObservableProperty] private bool _secondIsE0;
    [ObservableProperty] private string _secondKeyName = Localizer.Get("S0505");
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _isListeningForSecondKey;

    /// <summary>Ordered steps, edited row by row in the main-editor
    /// pattern. Empty only when the user deleted every step (see
    /// <see cref="ToConfig"/> for the persisted form of that edge).</summary>
    public ObservableCollection<ComboStepViewModel> Steps { get; } = new();

    public event Action? RequestManageBrowserGroups;
    public event Action? RequestManageMacros;

    /// <summary>Adds a step and wires its delete/manage forwarding.
    /// The owner VM attaches its own live-list wiring separately
    /// (see <c>BindingEditorViewModel.WireComboItem</c>).</summary>
    public void AddStep(ComboStepViewModel step)
    {
        Steps.Add(step);
        step.RequestDelete += OnStepRequestDelete;
        step.RequestManageBrowserGroups += ForwardManageBrowserGroups;
        step.RequestManageMacros += ForwardManageMacros;
        step.PropertyChanged += OnStepPropertyChanged;
    }

    /// <summary>Creates a default (hotkey) step, adds it, and returns
    /// it. The default matches the old single-action default
    /// (<c>ActionId = "hotkey"</c>).</summary>
    public ComboStepViewModel CreateStep()
    {
        var step = new ComboStepViewModel();
        AddStep(step);
        return step;
    }

    /// <summary>Bound by the combo section's "Tambah langkah"
    /// button (S0196). Creates a default step; the owner VM wires
    /// live lists into it via the Steps CollectionChanged hook.</summary>
    [RelayCommand]
    private void AddNewStep() => CreateStep();

    /// <summary>Alias for <see cref="AddNewStepCommand"/> under the
    /// name the combo section binds (Worker B's assumed name, flagged
    /// in the XAML comment). The same command instance; both names
    /// invoke <see cref="CreateStep"/>.</summary>
    public IRelayCommand AddStepCommand => AddNewStepCommand;

    private void OnStepRequestDelete(ComboStepViewModel step)
    {
        step.RequestDelete -= OnStepRequestDelete;
        step.RequestManageBrowserGroups -= ForwardManageBrowserGroups;
        step.RequestManageMacros -= ForwardManageMacros;
        step.PropertyChanged -= OnStepPropertyChanged;
        Steps.Remove(step);
    }

    private void ForwardManageBrowserGroups() => RequestManageBrowserGroups?.Invoke();
    private void ForwardManageMacros() => RequestManageMacros?.Invoke();

    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ActionId));
        OnPropertyChanged(nameof(ActionTarget));
    }

    /// <summary>Legacy pass-through: the first step's id, via the
    /// reverse of <c>ToStep</c>'s mapping. No steps (or a no-action
    /// first step) reads as the old default, "hotkey".</summary>
    public string ActionId
    {
        get
        {
            var step = Steps.FirstOrDefault()?.ToStep();
            return step is null ? "hotkey" : ActionStepCodec.LegacyFields(step).ActionId;
        }
        set
        {
            var step = Steps.FirstOrDefault() ?? CreateStep();
            if (!string.Equals(step.SelectedActionId, value, StringComparison.Ordinal))
                step.SelectedActionId = value;
        }
    }

    /// <summary>Legacy pass-through: the first step's target, via the
    /// reverse of <c>ToStep</c>'s mapping. No steps reads as "".</summary>
    public string ActionTarget
    {
        get
        {
            var step = Steps.FirstOrDefault()?.ToStep();
            return step is null ? "" : ActionStepCodec.LegacyFields(step).ActionTarget;
        }
        set
        {
            var step = Steps.FirstOrDefault() ?? CreateStep();
            ApplyLegacyTarget(step, value);
        }
    }

    private static void ApplyLegacyTarget(ComboStepViewModel step, string target)
    {
        switch (step.SelectedActionId)
        {
            case "hotkey": step.HotkeyText = target; break;
            case "launcher": step.LauncherTarget = target; break;
            case "link": step.LinkUrl = target; break;
            case "media": step.MediaValue = target; break;
            case "obs": step.ObsCommandValue = target; break;
            case "basic": step.BasicValue = target; break;
            default: step.LauncherTarget = target; break; // ToStep's default writes "target"
        }
    }

    public KeyCombinationConfig ToConfig()
    {
        var steps = Steps
            .Select(s => s.ToStep())
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
        // Legacy fields stay in sync with the FIRST step (reverse of
        // ToStep's mapping for the six mapped kinds; unmapped kinds
        // keep their id with an empty target) so the engine label
        // fallback and any legacy reader still see the old pair.
        // Edge: every step deleted → Steps empty + the old default
        // pair (hotkey, ""), exactly what a fresh combo persisted
        // before §158.
        var (legacyId, legacyTarget) = steps.Count > 0
            ? ActionStepCodec.LegacyFields(steps[0])
            : ("hotkey", "");
        return new KeyCombinationConfig
        {
            Id = Id,
            SecondScanCode = SecondScanCode,
            SecondIsE0 = SecondIsE0,
            SecondKeyName = SecondKeyName,
            Label = Label,
            ActionId = legacyId,
            ActionTarget = legacyTarget,
            Steps = steps,
        };
    }
}
