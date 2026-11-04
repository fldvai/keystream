# Keystream - Workflow & Architecture Guide

Read this first. It describes how the app actually works today: the data
flow, the rules that must not be broken, and the build/ship routine. There
is no `AUDIT.md` in this tree and no change log file is shipped yet; the
per-package test sheet `CARA_JALANKAN.txt` is a working file for PC testing
and is excluded from the push. When you change a mechanism, update the
matching section here in the same change - a comment that contradicts the
code is worse than no comment.

## 1. What the app is

A Windows app (.NET 8 / WPF) that turns any secondary USB/Bluetooth
keyboard into a macro pad / Stream Deck alternative. The secondary
keyboard ("deck") is identified once; its keystrokes are eaten before
they reach Windows and turned into actions (OBS control, media/volume,
virtual hotkeys, app/link launcher, soundboard, page switching). The
main keyboard is never touched. A PANIC passthrough (default F9) makes
the deck a normal keyboard instantly.

## 2. Solution map

| Project | Responsibility | Key files |
|---|---|---|
| `Keystream.Core` | Pure logic, no Windows deps: config model + store, device identity, key events, binding resolver, trigger engine, action contracts, profile packages | `Config/DeckConfig.cs`, `Config/ConfigStore.cs`, `Devices/DeviceId.cs`, `Engine/BindingResolver.cs`, `Engine/TriggerEngine.cs`, `Actions/IAction.cs`, `Config/ProfilePackage.cs` |
| `Keystream.Windows` | Interception driver P/Invoke + the Mode A service (single worker thread owns every native call) | `Interception/ModeAInterceptionService.cs` |
| `Keystream.Actions` | 18 `IAction` implementations + shared helpers. The card editor offers 15 action ids (`ActionCatalog.Actions`); `keydown`, `keyup`, and `mouse` exist only as recorded macro/combination steps, never as card-editor picks | `Hotkey/KeyChord.cs`, `Obs/ObsClient.cs`, `Sound/SoundAction.cs`, `Launcher/`, `Deck/PageSwitchAction.cs`, `Common/ParamReader.cs`, `Common/SendInputNative.cs`, `Browser/`, `Macro/`, `Mouse/`, `StreamerBot/`, `Windows/` |
| `Keystream.App` | WPF shell: host composition, all windows/view-models, tray, wizard, editor | `Host/AppHost.cs`, `ViewModels/`, `Views/`, `App.xaml` (global styles) |
| `Keystream.SelfCheck` | Console checks for the pure logic (targets `net8.0-windows`; see section 9 for the Linux VM run) | `Program.cs` |
| `Keystream.Smoke` | Hardware smoke harness (runs on the user's PC) | `Program.cs` |
| `Keystream.Setup` | One-click installer. In `Keystream.sln` for navigation but excluded from the default build configuration (no `Build.0` lines): build it only via `Keystream.Setup/build-setup.sh` (canonical; `build-setup.ps1` is the Windows-local alternative producing the same payload layout), which downloads the official Interception release, verifies hashes, packs the payload, and publishes the single-file exe. Its build input (`payload.zip`) is transient and gitignored; the installer exe travels inside `payload.zip` at `drivers/install-interception.exe`, it is not a separate Setup resource. See section 9 | `Program.cs`, `Keystream.Setup.csproj`, `build-setup.sh` |

## 3. Runtime data flow (one deck key press)

```
physical key
  → Interception driver (kernel) 
  → ModeAInterceptionService worker thread
      • stroke belongs to the deck?  → eat it, raise KeyEvent
      • anything else                 → forward immediately (main keyboard)
  → DeckEngine feeds TriggerEngine (per binding: Tap / LongPress 400 ms /
    DoubleTap 300 ms / Repeat; auto-repeat strokes never fire)
  → ActionDispatcher (bounded FIFO queue, drop counter is honest)
  → BindingResolver picks the binding for (scan code, E0):
        page bindings  <  legacy nav bindings  <  GLOBAL page bindings
        (key-level shadowing: a global binding replaces every binding
         below it for that key, and only for that key)
  → IAction.ExecuteAsync (OBS / media / hotkey / launcher / link / sound / deck)
  → result + tile flash + status line flow back to the UI
```

## 4. Threading rules (do not break these)

1. The Interception worker thread is the ONLY thread that forwards the
   main keyboard. It must never block on the UI: cross-thread UI calls
   use `Dispatcher.BeginInvoke`, never `Invoke`. (A synchronous Invoke
   once froze every keyboard while the editor modal was open.)
2. All native interception calls happen on that one worker thread.
3. Actions run on dispatcher threads; UI updates from actions go through
   events on `AppHost`, marshalled by the view models.

## 5. Config & identity

- One JSON config, schema v1, written atomically by `ConfigStore`
  (temp file + fsync + rotate `.bak1–3`; load falls back through backups).
- `DeckConfig.GlobalPage` holds bindings that apply to every page of
  every profile; it is NOT part of `.ksprofile` exports.
- Deck identity is a structured `DeviceId` (three namespaces:
  InterceptionHardwareId / RawInputDevicePath / LegacyUnverified).
  Matching is EXACT and same-namespace only - there is deliberately no
  VID/PID fallback (two identical keyboards must never be confused).
- Legacy configs migrate on load; a migrated deck identity is flagged
  `RequiresReidentification` and the wizard re-identifies it once.
- Profiles export/import as `.ksprofile` packages (validated first;
  import always creates a NEW profile, never overwrites). The older
  `.mkdprofile` format is accepted on import only, as a legacy marker
  (`ProfilePackage.LegacyFormatMarker`); new exports are always
  `.ksprofile`.

## 6. UI map

- **MainWindow** - header (deck + OBS status pills), sidebar (profile
  combo + Edit/Delete/New, 🌐 GLOBAL button, page list, add page,
  Settings), tile grid, status bar. Closing hides to tray; tray menu
  exits for real.
- **Tile** (172×172, square keycap) - the root border is the key's dark
  side edge (tile colour × 0.45 via `HexShade`); the gradient face rests
  lifted on a 6 px bottom edge and SINKS to 1 px with an inner top
  shadow while its deck key is held (mouse press nudges it too).
  Top-left: physical key badge; top-right: ⚠ conflict badge + 🗑
  quick-delete (with confirmation); center: big icon, label, `DetailText`. `DetailText` always shows the
  action's actual CONTENT (hotkey chord, media command, OBS command +
  scene, app name, page name…), never just the action type. Long tile
  strings scroll via `Views/MarqueeText` (ping-pong, centered when short).
  Clicking a tile opens the editor; clicking a page row ALWAYS activates
  that page (`MainViewModel.ActivatePage`) - including re-clicking the
  already-selected page, which is the only way out of global editing.
- **Binding editor** (`BindingEditorWindow`) - field order: physical key
  (+ ⌨ Ganti Tombol reassign, staged until Save; same-page conflicts
  offer a key SWAP), conflict warning, **Nama tampilan → Jenis aksi →
  Kapan dijalankan** → Ikon → Warna → the action's own panel.
  Trigger derivation: choosing a media volume command auto-sets
  Repeat; other media commands auto-set Tap; leaving media with Repeat
  resets to Tap. The note under the trigger explains an automatic choice
  and disappears when the user overrides it.
  Mic-mute tiles show LIVE state (v3.2.0): a red slash over the tile
  icon while the default microphone is muted - MainViewModel polls
  `MicMute.TryGetMuted` every second (plus an immediate refresh after
  any executed step), so mutes changed outside the app show too.
  Media commands are SendInput virtual keys, EXCEPT `MicMute`
  (v3.1.0): no mic-mute VK exists, so it toggles the default capture
  endpoint's mute through Core Audio (`MicMute.Toggle`, NAudio) - a
  true device-level mute for all apps, with the communications-role
  endpoint synced when it is a different device.
- **Settings** - deck identity and service/driver status, "Umum dan
  Profil" with the Language dropdown (Indonesian/English, applied on
  Save), Shortcut Master and Shortcut Macro sections, icon upload
  settings, OBS Studio and Streamer.bot connection sections, Log Level
  with log folder/clear controls, OBS/Streamer.bot connection status,
  panic hotkey, startup options, conflict-warning toggle, and Reset Data
  in the footer.
- **Press-to-map** - "+ Petakan Tombol Baru" opens `WaitingKeyWindow`,
  a small popup with a spinning ring and a Batal button; the next deck
  key press closes it and opens the editor (MainViewModel capture
  events: RequestWaitingCapture / CaptureCompleted).
- **Profile/Page modals** - rename, duplicate, delete (last one is
  protected), auto-profile trigger apps (picked from running apps).
- **Identification wizard** - press-any-key identification; never
  auto-picks the main keyboard.

## 7. Shared UI mechanisms (App.xaml)

- **POPUP RULE (mandatory, the user's standing order):** every popup,
  dialog, and notification in the app follows ONE pattern - a styled app
  window on the DialogWindow/WaitingKeyWindow model (dark title bar,
  `#171A21` surface, app buttons, `AppIcons` glyphs), reached through
  `AppDialog.Show(...)`. Raw `MessageBox` is FORBIDDEN anywhere in the
  app: it renders in the OS theme and breaks the visual system. New
  dialogs extend the pattern; they never fork it.
  `Keystream.Setup` is a standalone assembly with no reference to
  `Keystream.App`, so it cannot use `AppDialog`; its equivalent is the
  Setup-owned `Keystream.Setup.MessageDialog` window (same dark palette,
  `MessageDialog.Show(owner, title, message)`), and raw `MessageBox` is
  likewise forbidden there.
- **ICON RULE (mandatory):** all UI icons are `AppIcons` stroke vectors
  (24×24 grid, 1.8 stroke, round caps) rendered by `IconView` - tiles,
  buttons, sidebar, dialogs. Emoji are never UI icons (they render per
  font and clash). Tile icons are stored as catalog KEYS; legacy emoji
  values resolve via `AppIcons.ResolveKey`, unknown values fall back to
  a text glyph.


- **ScrollBar** - one global thin (6 px) dark style, PLUS overrides of the
  `SystemParameters.VerticalScrollBarWidthKey` / `HorizontalScrollBarHeightKey`
  resource keys in App.xaml: bars inside ListBox/ComboBox templates size
  themselves from those keys and can outrank the style, so both layers are
  required for the thin pattern to hold everywhere. Long lists disable
  their horizontal bar (`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`).
- **ComboBox** - one global dark template. Popup rules: width is EXACTLY
  the combo's width (long items can never push it past the window/screen
  edge), height capped by `MaxDropDownHeight` = 240 with inner scroll,
  Bottom placement flips above near the screen edge. Mouse wheel over a
  CLOSED combo scrolls the page, never the selection (class handler in
  `App.OnStartup`); an open dropdown scrolls itself.
- **TextBox** - one global template with a built-in clear (✕) button:
  a font glyph (`&#x2715;`, 11 px, `CyberTechFont`, SemiBold, `#F87171`,
  set by §162 to match the combination-panel clear glyph; `App.xaml`
  ClearBtn), visible only when the field has
  text and is editable; the click handler (`App.ClearTextBox_Click`)
  clears via the `Text` property so bindings update. Window-local
  TextBox styles must not be reintroduced - extend the global one.
  Keyed `Field` styles in modals use `BasedOn="{StaticResource {x:Type TextBox}}"`.
- **Option records** (`ActionOption`, `ValueOption`, `RunningAppOption`,
  `ProfileConfig`, `PageConfig`) override `ToString()` with their display
  text. The ComboBox selection box does not reliably use
  `DisplayMemberPath` - never rely on it alone; raw record dumps in the
  UI are always a missing `ToString()`.
- XAML gotcha: `<Run Text="{Binding …}">` binds TwoWay by default -
  bind get-only view-model properties with `Mode=OneWay` (this exact
  mistake once crashed startup).
- Explicit `ItemTemplate` property elements on ComboBoxes threw at
  startup on the user's machine (v2.9.13) - avoid them on combos.

## 8. Actions (ids and params)

| Id | Params | Notes |
|---|---|---|
| `hotkey` | `keys` | Chord text parsed by `KeyChord` (F1–F24, named keys without spaces - `PageUp` not `Page Up`, numpad incl. `NumpadEnter` via the extended flag). Sent as ONE `SendInput` batch. |
| `media` | `command` | One SendInput path (the audited app fired 3×). Volume pairs with the Repeat trigger. |
| `obs` | `command` + spec params | obs-websocket v5; `ObsClient` is "connected" only after the Identified handshake (two-stage SHA-256 auth). 20 commands in `ActionCatalog.ObsSpecs` drive the editor's cascading fields. |
| `launcher` | `target` | Opens an app/file/folder via ShellExecute. Editor offers: path + browse, and the running-apps picker (`RunningApps.ListWithPaths` - enumerates ALL top-level windows via EnumWindows; elevated apps are invisible to a non-elevated app). The tile's display name is the binding Label - there is no separate app-name field. |
| `link` | `url` | Opens a web address in the default browser; a missing scheme is forgiven (`NormalizeUrl`). Legacy `launcher` bindings whose target is a URL are PRESENTED as `link` in the editor. |
| `sound` | `file` | NAudio playback: wav/mp3/m4a/aac/wma/flac/ogg/aif/aiff. A new press stops and restarts the sound. |
| `deck` | `command`, `pageId`, `pageName` | Page switching from the deck (goto/next/prev/home). |
| `browser` | browser profile params | Opens a browser with a chosen profile, focuses its window, or toggles it (`BrowserAction`, `BrowserWindowTracker`). |
| `browsergroup` | group params | Opens a set of browser profiles with one press (`BrowserGroupAction`). |
| `macro` | `macroId`/`macroName` | Runs a named step sequence stored once in config. Nested macros are refused. |
| `mouse` | step params | Step-only: mouse movement/click steps produced by mouse recording, never a card-editor pick. |
| `windows-command`, `windows-tools` | `command` | Windows commands and Windows tools (the split of the legacy `windows` id in §129; one `WindowsAction` class serves all three ids). The legacy `windows` id still resolves for old cards. |
| `streamerbot` | `actionName`/`actionId` + optional JSON args | Triggers a Streamer.bot action over its local WebSocket server (outbound only; no event subscription). Authentication is not supported: turn authentication off on the Streamer.bot server. |

`SendInputNative.INPUT` must stay 40 bytes on x64 (a 32-byte struct made
every SendInput fail with error 87; the self-check pins the size).

`keydown` and `keyup` are step-only ids too: hold/release steps produced
only by down/up recording, registered so macros that contain them resolve
(`AppHost` action list). They are never offered as card-editor picks.

- `basic` (v3.3.0, +Undo/Redo/Open in v3.3.1): `command` ∈
  Cut/Copy/Paste/Undo/Redo/All/Find/New/Open/Print/Save/WebSearch.
  The eleven Ctrl chords are executed by COMPOSING HotkeyAction (one
  chord-sending path in the app); WebSearch is the exception -
  VK_BROWSER_SEARCH sent directly, media-style.
- `profile` (v3.3.0): `profileId`/`profileName`. Switches the MAIN
  profile via AppHost.SwitchToProfile (lands on that profile's first
  page; UI refreshes through the auto-profile event).
- Layer Modifier (v3.3.0) = deck `command=momentary` + `pageId`, run by
  the TriggerKind pair Momentary/MomentaryRelease: the TriggerEngine
  fires the release BEFORE consulting the binding map (the layer
  switch replaced the page mid-hold - without this the layer sticks
  forever), DeckEngine remembers the binding that started the hold
  (`_momentaryHeld`), `ActionContext.FiredKind` tells the action which
  phase fired, and PageSwitchAction keeps a stack of origin pages
  (release pops; empty stack = successful no-op). The editor derives
  the Momentary trigger automatically when the command is picked,
  mirroring how volume derives Repeat.

- Uploaded tile icons (v3.4.0): the editor's icon section is two
  sub-tabs - catalog (built-in vectors) and Upload (click for a file
  dialog, or drag & drop). `IconStore` converts every import to PNG
  at most 128×128 (aspect preserved, metadata stripped) and stores it
  content-addressed (SHA-256 of the converted bytes) in the single
  `%AppData%\Keystream\Icons` folder; the binding stores only
  "file:&lt;name&gt;.png". Import refuses files over
  `DeckSettings.IconUploadMaxKb` (default 2048, editable in Settings →
  Ikon Upload) and unsupported formats. Tiles render the file image;
  a missing file falls back to the gamepad vector, never a blank tile
  or a raw "file:..." string. Settings → Ikon Upload shows folder
  stats and cleans per-section (orphans = referenced by no binding,
  or all uploads) with a confirm dialog for the destructive one, then
  notifies the main window (`IconsChanged`) to re-resolve tiles.
- Automatic icons (v3.5.0, opt-in setting `AutoFetchIcons` in
  Settings → Ikon Upload): the editor fetches a tile's icon on its
  own - the website favicon for Buka URL (page icon links first via
  FaviconDiscovery, /favicon.ico fallback, browser-like User-Agent),
  the application's own icon for Buka Aplikasi (SHGetFileInfo large
  icon, ExtractAssociatedIcon fallback; both 32×32, upscaled by WPF
  at 48 px). Fetching runs only on field commit while the tile has
  no manually picked icon (a manual pick is never overwritten).
  v3.5.1 revises this per the user: the editor buttons are GONE -
  the setting checkbox is the only control. Smart priority:
  Upload > Auto > Default - auto-fetch replaces empty/catalog/glyph
  icons and refreshes its own previous value, but never overwrites a
  user upload. Saving awaits/starts the fetch ("sudah pasti": the
  saved tile never silently keeps the old icon). App-icon fallback:
  when extraction fails, the exe name guesses a website
  (WhatsApp.exe → whatsapp.com) and its favicon is tried. Fixed in
  the same release: TileIconKey fell back to the gamepad vector for
  EVERY file icon, so fetched/uploaded icons rendered doubled with
  the gamepad on top - the fallback now applies only when the file
  is actually gone. Fetched icons go through the same IconStore
  conversion + content-addressed storage as uploads.
- Icon tiers split (v3.6.0): auto icons now live in their own
  `BindingConfig.AutoIcon` field instead of sharing Icon with
  uploads. Before this, an auto icon from an earlier version sat
  in Icon, looked exactly like an upload, and silently vetoed
  every refresh - so edit-and-switch-apps never changed the card.
  Now: upload file (Icon) > auto file (AutoIcon) > catalog key
  (Icon) > default, on the tile AND in the editor preview; an
  explicit icon pick clears AutoIcon; auto-fetch writes only
  AutoIcon and is blocked only by a real upload. One-time load
  migration moves old launcher/link file icons from Icon to
  AutoIcon (tiles look identical; uploads on other action types
  are untouched). ReferencedNames covers both fields so cleanup
  never deletes a live auto icon. App icons are extracted at
  256x256 (PrivateExtractIcons) then 48x48 before the old 32x32
  chain - the 32->48 upscale was the visible "pecah" - and the
  tile image uses HighQuality scaling. Known limit: exported
  .ksprofile packages do NOT bundle icon files
  - on another PC those tiles fall back to the built-in icon.
- Launcher panel order (v3.5.2, per the user): running-apps list on
  TOP (label + Segarkan), then a "✓ <App> terpilih" confirmation row
  (shown only while the picked app's path is still the target -
  typing a different path clears the stale confirmation), then the
  Path + Cari… row, then the icon status line. Reason: Stream Deck
  auto-loads an app's icon for its Open action too - and fails for
  some apps as well (Outlook/Notes/Mail reports) - and its pick
  feels finished because of the dialog's Open confirm + automatic
  save + instant key preview. Keystream mirrors the closure
  without breaking the checkbox rule: the confirmation row closes
  the pick. v3.6.1 compacts it per the user: the row is just
  "✓ <App>" (no sentence), it sits BELOW the path row where the
  icon-fetch status line used to be, and that status line is gone
  from the launcher panel entirely ("Ikon diambil dari aplikasi
  (4,3 KB)" and friends were noise once fetching works) - the link
  panel keeps its status line, where favicon failures are common.

## 9. Build & ship

The build is a framework-dependent multi-file publish (FDD): the
published folder holds `Keystream.exe` plus its DLLs, and the machine
running it needs the .NET 8 Desktop Runtime. There is NO
`PublishSingleFile` in this build. Do not add
`-p:PublishSingleFile=true` or `-p:IncludeNativeLibrariesForSelfExtract=true`:
the verified publish below uses neither, and the output is 21 files
including `interception.dll`, which the publish already copies from
`Keystream.Windows` through the `Content` link in the App csproj, so no
manual `cp` of `interception.dll` is needed.

On Windows, with the .NET 8 SDK installed (pinned by `global.json`):

```console
dotnet build Keystream.App -c Release
dotnet build Keystream.SelfCheck -c Release
dotnet publish Keystream.App -c Release -r win-x64 --self-contained false -o publish-app
```

On the Linux build VM the same commands run through the VM SDK install;
`TMPDIR` is not optional there (`/tmp` is a small tmpfs):

```bash
export DOTNET_ROOT=~/dnx TMPDIR=~/tmpdir NUGET_PACKAGES=~/tmpdir/nugetpkgs \
       DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
~/dnx/dotnet build Keystream.App -c Release            # must be 0 errors
~/dnx/dotnet build Keystream.SelfCheck -c Release
# VM only: strip Microsoft.WindowsDesktop.App from the SelfCheck
# runtimeconfig (NAudio's WinForms dependency drags it in; the checks
# touch no WinForms types), then run the dll - expect ALL PASS.
~/dnx/dotnet publish Keystream.App -c Release -r win-x64 \
  --self-contained false -o /tmp/keystream-publish-verify
```

Builds must be 0 errors. The published exe is `Keystream.exe`
(AssemblyName in the App csproj), launched directly from
`publish-app\Keystream.exe`. Exit the old copy from the tray first -
a stale tray process is the #1 cause of "the fix isn't there".

Ship shape: `publish-app/` (build output, excluded from the push)
and the PC test sheet. There is no `CHANGELOG.md` yet
and no release exists; the changelog split is a Phase 2 item, recorded
in the Batch 1 cleanup manifest. Package names in current practice are
`keystream-...-full.zip` per § package, not `keystream-v2-<tag>.zip`.
`publish-staging/` in the work tree is an older duplicate of
`publish-app/` and is not the canonical output; it is excluded from the
push with the rest of the build output.

### One-click installer (`Keystream.Setup`)

`Keystream.Setup` is in `Keystream.sln` (Project entry + `ActiveCfg`)
but has NO `Build.0` lines: a plain `dotnet build Keystream.sln`
skips it, because its embedded resource (`payload.zip`) is a
transient build input that does not exist in a clean tree, and CSC
fails with CS1566 without it. The Interception installer exe is not a
Setup resource: it is packed inside `payload.zip` at
`drivers/install-interception.exe`. Never
re-add `Build.0` without also providing the input, and never commit
the input (gitignored).

Build the installer with the canonical script:

```bash
Keystream.Setup/build-setup.sh
```

The script downloads Interception v1.0.1 from the official release,
verifies the zip, DLL, and installer-exe SHA-256 hashes and fails hard
on mismatch, publishes `Keystream.App` framework-dependent for win-x64,
packs the payload (app files plus `interception.dll` at the root and
`install-interception.exe` under `drivers/`), publishes the single-file
Setup exe, leaves `KeystreamSetup.exe` in a work directory under `/tmp`
(overridable with `KEYSTREAM_SETUP_WORKDIR`), and deletes the transient
input (`payload.zip`) from the tree, including on failure.

`build-setup.ps1` is the Windows-local alternative: same payload layout,
same hash checks (zip, DLL, installer exe, plus the payload copy of the
installer exe), optional `-InterceptionZip` / `-InterceptionInstaller`
parameters for a local copy (relative paths resolve against the repository
root), and a best-effort self-signed Authenticode signature; it is not the
canonical path. The installer itself runs only
on Windows: extraction, shortcuts, driver install, and restart
behavior are PC checks (see CARA). Updates are NOT delivered by the
installer; the app offers a manual "Periksa Pembaruan" check at the
bottom of Settings (moved there from the About modal in §171). The
startup auto-check (if enabled) only checks and notifies — answering Yes
opens the release page in the browser; it never downloads or installs.
The manual check downloads the installer only after the user confirms
"Perbarui Sekarang".

The wizard lets the user choose the install folder (editable path plus a
Browse button; the path must be absolute and creatable). A fresh install
defaults to `%LOCALAPPDATA%\Keystream`; an upgrade defaults to the location
recorded in the uninstall registry value `InstallLocation`, so updating
installs in place instead of creating a second copy. Uninstall (including
the staged uninstaller) and the driver-helper lookup read that recorded
location, falling back to the default when it is missing. The app itself
needs no change: it locates its own files relative to its exe and keeps
config/data under `%AppData%\Keystream`.

The uninstaller is a separate small exe (`Uninstall.exe`, project
`Keystream.Uninstall`) sharing the uninstall sources with Setup (same
wizard, driver, registry, and staged-uninstaller code, compiled into
both exes). It travels inside `payload.zip`, is extracted with the app
files, and is the target of the uninstall registry value; Setup no
longer copies itself into the install folder, and deletes a stale
`KeystreamSetup.exe` copy left by older installers on upgrade. The
app's driver-missing wizard launches `Uninstall.exe --install-driver`
when no `KeystreamSetup.exe` copy is present.

### Setup string table, config merge & RTL (Wave 2, Phase B)

Setup/Uninstall are standalone exes with no reference to
`Keystream.Core`, so they carry their own table:
`tools/setup-strings.json` (keys `W0001…`, `W` = wizard, never
colliding with the app's `S####` keys) -> `tools/gen_setup_strings.py`
-> `Keystream.Setup/Localization/SetupStrings.g.cs` (shared into
`Keystream.Uninstall` by Link; 12 dicts, a missing member falls back
to English with a stderr warning, like `gen_strings.py`).
`SetupLocalizer` (same 12 codes + endonyms, duplicated by necessity)
resolves the saved language before any wizard window is created; the
installer dropdown (welcome panel only) re-renders live, and Finish
merges `settings.language` into `%AppData%\Keystream\config.json`
(case-insensitive `settings`/`language` lookup, atomic temp+move,
corrupt files left untouched, no-op when unchanged). A Setup-created
minimal file makes the app's `Load` return `Source=Main`, so
FreshDefault seeding of the 7 default Global cards is bypassed — a
confirmed-model consequence, no seeding invented. RTL:
`FlowDirectionHelper` (`Keystream.Core/Localization/FlowDirection.cs`,
reflection-based so Core stays WPF-free) sets `Window.FlowDirection`
from the current language (`ar` -> RightToLeft) via a `Loaded` class
handler plus `RefreshAllOpenWindows` on language change; the
`BrowserIconComposer` `FlowDirection.LeftToRight` (icon rasterisation)
is untouched. Tokens (`%APPDATA%\Keystream`, `D:\Keystream`,
`KeystreamStartup`, `Interception`, brand names) survive translation
verbatim; `ex.Message` tails stay English.

## 10. Verification honesty

The VM proves logic (self-check) and compilation only. Anything touching
the driver, real windows, real audio, or OBS is proven solely by the
user's run on their PC - never report those as verified from the VM.

## 11. UI overhaul v4.0 (24 Okt 2026, atas persetujuan user)
- Cangkang: semua jendela WindowStyle=None + WindowChrome (resize tetap
  OS-backed untuk jendela yang bisa diubah ukurannya; drag lewat header,
  klik tombol di header tidak ikut men-drag - lihat ShellChrome.cs).
  Bingkai luar tetap persegi di level OS agar resize/snap tidak rusak;
  aksen sudut-miring digambar di bingkai dalam MainWindow.
- Sidebar: 6 tombol hanya-ikon (tambah/edit/hapus/impor/ekspor/reset)
  di ATAS dropdown PROFILE DECK, keterangan via tooltip; daftar halaman
  di baris `*` sehingga scroll internal saat halaman banyak.
- Header utama: globe (Opsi Bahasa) + minimize + tutup. Opsi Bahasa
  switches the app language (Indonesian/English, §141): the header globe
  toggles it live and Settings holds the matching dropdown, applied on
  Save through the same `Localizer.SetLanguage`.
- Reset profil = hapus semua binding di semua halaman profil aktif
  (halaman & nama tetap), selalu di belakang dialog konfirmasi.
- Modal: header judul + garis biru + tombol ✕ di semua jendela
  (21 berkas `*Window.xaml` memakai `WindowStyle="None"`, ditambah
  panel rekaman mengambang);
  isi/field/perintah tiap modal tidak diubah.
- Bukti: build App 0 error, SelfCheck ALL PASS, cek struktural 50+ cek
  (semua binding/perintah lama masih ada). Verifikasi visual di Windows
  tetap di tangan user - VM tidak bisa menjalankan WPF.

## 12. String table pipeline

The localized strings in `Keystream.Core/Localization/Strings.g.cs` are
generated, never hand-edited. The generator is vendored under `tools/`.
The pipeline, in three lines:

```text
command: python tools/gen_strings.py
input:   tools/merged.json
output:  Keystream.Core/Localization/Strings.g.cs
```

The vendored script is the authority for its own arguments: confirm the
exact invocation against `tools/gen_strings.py` before running it, and
regenerate the table whenever a string key is added or changed.
Each key in `tools/merged.json` carries one member per language code
(12, canonical order: id, en, es, pt, fr, de, ru, zh, ja, ko, ar, tr);
the generator emits one dictionary per code and fills a member missing
for some language from English, listing every gap on stderr.

## 13. Keputusan desain — trade-off Batch 3 (1 Nov 2026)

Audit akar 1 Nov 2026 menemukan 35 trade-off desain (C-01..C-35).
Verdict user 1 Nov 2026 14:09 WIB: **dokumentasikan semua, tanpa ubah
perilaku**. Bagian ini adalah catatan keputusan tersebut — tiap butir
menyatakan perilaku saat ini dan bahwa perilaku itu DIPERTAHANKAN.
Perubahan perilaku atas butir mana pun butuh verdict terpisah.
Tak satu pun butir di bawah ini terbukti di PC; semuanya temuan kode.

- **C-01** `AppHost.cs` (WatchForeground) — Save gagal di tengah switch
  = switch hilang tanpa retry sampai fokus berubah. Dipertahankan:
  retry berisiko membanjiri CrashLog; kehilangan switch pulih sendiri
  pada perubahan fokus berikutnya.
- **C-02** `ChordMask.cs` (PendingChord) — dua modifier se-bit (mis.
  L-Ctrl+R-Ctrl) menyelesaikan capture prematur saat satu dilepas.
  Dipertahankan: konsekuensi bit-collapse yang didokumentasikan.
- **C-03** `ModeAInterceptionService.cs` — exception satu subscriber
  mematikan service driver permanen (tanpa isolasi); `Stop()` yang Join-nya
  gagal dapat meninggalkan worker lama sehingga `Start()` membuat context
  kedua. Dipertahankan untuk sekarang; perubahan butuh desain isolasi.
- **C-04** `ModifierMask.cs` + `CaptureChordAccumulator.cs` — duplikat
  subsistem chord §167 yang mati (satu-satunya pemakai `ModifierMask`
  adalah accumulator itu sendiri; accumulator tak dipakai di mana pun;
  perilaku sudah divergen halus dari `ChordMask`). Dipertahankan di tree
  (tidak dihapus) sampai ada perintah hapus; nol dampak runtime.
- **C-05** Panic (F9) hanya efektif dari keyboard utama dan ditelan
  selama capture; F9 di deck tak pernah jadi `WM_HOTKEY`.
  Dipertahankan: trade-off arsitektural (jalur panic = hotkey OS).
- **C-06** Semantik rantai gagal tak konsisten: kartu lanjut saat langkah
  gagal (`ActionDispatcher`), macro berhenti pada gagal pertama
  (`MacroAction`). Dipertahankan apa adanya; penyamaan butuh verdict
  (mengubah perilaku semua kartu/macro multi-langkah).
- **C-07** Eksekusi di luar dispatcher (playtest, playback standalone,
  Test editor) dapat menginterleave SendInput; state statik Steam +
  klien OBS/Streamer.bot diakses dua jalur. Dipertahankan; pemisahan
  jalur butuh desain.
- **C-08** Pelacakan jendela browser bisa salah pasang pada grup/launch
  bersamaan (diakui docstring). Dipertahankan; tile tunggal tak kena.
- **C-09** State toggle Steam hanya in-memory, tak membaca keadaan nyata;
  perubahan dari luar app membuat toggle desinkron. Dipertahankan.
- **C-10** Balasan Streamer.bot berisiko (RISKY): tanpa sinyal gagal
  eksplisit dihitung sukses; bentuk lain → daftar kosong dilaporkan
  sukses. Dipertahankan; protokol balasan Streamer.bot tak
  mendokumentasikan sinyal gagal.
- **C-11** Mode relatif mouse jatuh ke absolut diam-diam bila jendela
  target tak ditemukan. Disengaja (docstring); hanya gagal bila absolut
  pun tak ada. Dipertahankan.
- **C-12** Batas delay 10 detik memotong jeda rekaman panjang (clamp +
  `Validate` menolak >10000ms); jumlah langkah tak dibatasi.
  Dipertahankan.
- **C-13** `LinkAction` menerima skema apa pun (`mailto:`/`file:`/
  `javascript:`) → `Process.Start`, dilaporkan Ok; `BrowserAction` hanya
  http/https. Inkonsistensi dipertahankan untuk sekarang.
- **C-14** Tanpa auto-reconnect OBS/Streamer.bot; satu aksi lambat
  (~11 dtk) memblokir semua deck key (dispatcher satu worker).
  Dipertahankan; reconnect/worker paralel butuh desain.
- **C-15** String bilingual inline di Wizard + dialog restart, melewati
  tabel, tanpa langganan `LanguageChanged`. Pengecualian tercatat di
  sini; dipertahankan.
- **C-16** `Keystream.Setup` seluruhnya di luar lokalisasi (tak
  mereferensikan Core; semua teks hardcode Inggris). Pengecualian
  tercatat di sini; dipertahankan.
- **C-17** Drift dokumen/kode kecil: urutan fallback `Localizer` +
  normalisasi `SetLanguage` di komentar vs kode. Tak berdampak
  (himpunan kunci identik). Dipertahankan; komentar diperbaiki bila
  disentuh untuk alasan lain.
- **C-18** Klaim header generator tak benar + tanpa penjaga kebusukan:
  40 nilai EN duplikat antar-kunci (dedupe harfiah justru merusak,
  mis. S0010/S0167). Generator benar; kebusukan hanya terdeteksi
  dengan menjalankan ulang. Dipertahankan; duplikat nilai antar-kunci
  yang berbeda makna adalah sah.
- **C-19** Kerapuhan harness: SelfCheck tanpa pelindung throw + slicing
  `chromiumProfiles[0..2]` bisa crash tanpa ringkasan; satu wait 5 detik
  bergantung waktu (flake). Dipertahankan; perbaikan harness butuh
  perintah terpisah.
- **C-20** Celah cakupan SelfCheck: modul besar tanpa penyebutan
  (`ManualSwitchLock`, `ChordMask`, Steam, `MacroAction`,
  `UpdateChecker`, `IconStore`, dst.) + seluruh lapisan App.
  Dipertahankan; penambahan cakupan butuh perintah terpisah.
- **C-21** Smoke tanpa verdict otomatis: hanya 3 binding lokal, mencetak
  counter tanpa menegaskan apa pun, `return 0` apa pun yang terjadi.
  Dipertahankan; smoke adalah alat manual.
- **C-22** Catch-all `Load()` menyamarkan penyebab, membuang config yang
  sebagian besar valid; satu elemen null cukup membuang seluruh config
  aktif. Dipertahankan; pemulihan parsial butuh desain.
- **C-23** Konkuren tulis: gate benar di jalur utama; celah di pinggir
  (Load sebelum mutex, penulis `IconStore` tak-tergate saat reset,
  `schtasks` 3 detik di dalam gate menahan Save). Dipertahankan.
- **C-24** Duplikat menyalin verbatim (terverifikasi); catatan: tak
  menormalisasi (null → null), `CopyFrom` menyalin referensi list
  dangkal (laten, pemanggil sekali pakai). Dipertahankan.
- **C-25** Validasi saat load: `ChordMask` 8/255 tak pernah cocok
  (editor hanya 0–7) → kartu mati diam-diam via edit manual; impor
  hanya menambah sufiks sekali. Dipertahankan.
- **C-26** Instal MENYENTUH `%AppData%\Keystream` (`CreateDirectory` +
  tulis ulang `config.json` di luar jalur atomik `ConfigStore`); parse
  gagal → `catch {}` menelan segalanya, schtasks/Run batal diam-diam.
  Dipertahankan untuk sekarang; perubahan menyentuh installer + data
  user sehingga butuh verdict + tes PC khusus.
- **C-27** Fallback unduh driver runtime tanpa verifikasi hash; exe disk
  dijalankan tanpa cek hash (kontras `build-setup.sh`).
  `UninstallDriverOnly` tanpa fallback → return 1 diam-diam.
  Dipertahankan; pengetatan butuh verdict (jalur offline).
- **C-28** Registrasi uninstall/shortcut gagal diam-diam (exception
  ditelan, tak diperiksa) → instalasi sukses tanpa entri
  uninstall/shortcut. Dipertahankan; pelaporan jujur butuh verdict.
- **C-29** Tanpa rollback instalasi parsial; kill proses tanpa grace;
  fallback shortcut PowerShell (`-ExecutionPolicy Bypass`), kegagalan
  ditelan. Dipertahankan.
- **C-30** Balapan temp bersama di `IconStore.ImportBitmap` (dua impor
  bersamaan atas byte identik bisa gagal spur). Probabilitas rendah;
  dipertahankan.
- **C-31** Traversal ikon terbatas: `IconReference.FileName` verbatim
  sesudah prefix `file:`; hanya jalur tile berjaga → `file:..\..\…`
  dari config/paket bisa lolos keluar folder Icons untuk baca gambar
  (tulis memakai nama hash, aman). Dipertahankan; pengerasan butuh
  verdict (dampak: paket ikon pihak ketiga).
- **C-32** Catch kosong massal tanpa log (17× `SteamDiscovery`,
  `AppCatalogService`, rollback `Save()` di picker) — sebagian besar
  best-effort disengaja; jejak diagnostik hilang. Dipertahankan.
- **C-33** `ActionDispatcher.Dispose` menunggu worker maks. 2 detik lalu
  kembali; worker macet (konek OBS ~6s) jalan di latar, `Enqueue`
  sesudahnya dropped. Dipertahankan.
- **C-34** Jalur VM/programatik lolos dari guard UI-only Mode Pilih
  (hotkey master, tray, auto-switch, aksi deck membersihkan seleksi
  tanpa notice). Didokumentasikan di komentar; dipertahankan; apakah
  tray/hotkey perlu notice butuh putusan terpisah.
- **C-35** `build-setup.sh` mem-publish App tanpa `-r win-x64
  --self-contained false` (berbeda dari §9; csproj tak mem-pin RID).
  Dampaknya tak dapat dibuktikan tanpa build; divergensi flag tercatat
  di sini. Dipertahankan sampai build installer berikutnya membuktikan
  atau membantah.
