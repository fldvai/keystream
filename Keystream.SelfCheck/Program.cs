using System.Diagnostics;
using Keystream.Core.Localization;
using System.Text.Json.Nodes;
using Keystream.Actions.Basic;
using Keystream.Actions.Browser;
using Keystream.Actions.Windows;
using Keystream.Actions.Common;
using Keystream.Actions.Deck;
using Keystream.Actions.Hotkey;
using Keystream.Actions.Launcher;
using Keystream.Actions.Media;
using Keystream.Actions.Obs;
using Keystream.Actions.Profile;
using Keystream.Actions.Sound;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Core.Devices;
using Keystream.Core.Engine;
using Keystream.Core.Icons;
using Keystream.Core.Input;
using Keystream.Windows.Interception;

// Phase 1 self-check: proves the K1/K2/K3/K4 contracts behave as specified.
// Same discipline as the Gate instruments: if this fails, the contracts are
// not trustworthy and Phase 2 must not be built on them.

// Optional: --lang en runs check output in English (default: Indonesian).
{
    var li = Array.IndexOf(args, "--lang");
    if (li >= 0 && li + 1 < args.Length) Localizer.SetLanguage(args[li + 1]);
}

int fails = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
    if (!ok) fails++;
}

Console.WriteLine(Localizer.Get("S1234"));
{
    var a = DeviceId.FromInterceptionHardwareId(@"HID\VID_413C&PID_2003\7&ABC");
    var sameCase = DeviceId.FromInterceptionHardwareId(@"hid\vid_413c&pid_2003\7&abc");
    var otherKind = DeviceId.FromRawInputPath(@"HID\VID_413C&PID_2003\7&ABC");
    var empty = DeviceId.FromInterceptionHardwareId("");
    Check(Localizer.Get("S1259"), a.Matches(sameCase));
    Check(Localizer.Get("S1260"), !a.Matches(otherKind));
    Check(Localizer.Get("S1261"), !empty.Matches(empty) && !a.Matches(empty));
    Check(Localizer.Get("S1262"), DeviceId.FromInterceptionHardwareId("HID\\X\0").Matches(DeviceId.FromInterceptionHardwareId(@"HID\X")));
    Check(Localizer.Get("S1263"), DeviceId.FromLegacyUnverified("x").Kind == DeviceIdKind.LegacyUnverified);
}

Console.WriteLine(Localizer.Get("S1235"));
{
    var enter = new KeyIdentity(0x1C, false, false);
    var numpadEnter = new KeyIdentity(0x1C, true, false);
    Check(Localizer.Get("S1264"), enter != numpadEnter);
    var ev = new KeyEvent(DeviceId.FromInterceptionHardwareId("HID\\X"), numpadEnter, 0x0D, KeyDirection.Down, false, 12345);
    Check(Localizer.Get("S1265"), ev.IsDown && ev.VirtualKey == 0x0D && ev.TimestampQpc == 12345);
}

Console.WriteLine(Localizer.Get("S1236"));
{
    IAction fake = new FakeBeepAction();
    var badParams = new JsonObject { ["frequency"] = "bukan-angka" };
    var goodParams = new JsonObject { ["frequency"] = 880 };
    Check(Localizer.Get("S1266"), !fake.Validate(badParams).IsValid);
    Check(Localizer.Get("S1267"), fake.Validate(goodParams).IsValid);
    var ctx = new ActionContext { Cancellation = CancellationToken.None };
    var r = await fake.ExecuteAsync(goodParams, ctx);
    Check(Localizer.Get("S1268"), r.Success && r.Error is null);
    var rt = await fake.TestAsync(goodParams, ctx);
    Check(Localizer.Get("S1269"), rt.Success);
}

Console.WriteLine(Localizer.Get("S1237"));
{
    string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-" + Guid.NewGuid().ToString("N"));
    var store = new ConfigStore(dir);

    var cfgA = ConfigStore.CreateDefault();
    cfgA.Settings.PauseHotkey = "F10";
    store.Save(cfgA);
    var loadedA = store.Load();
    Check(Localizer.Get("S1270"), loadedA.Source == LoadSource.Main && loadedA.Config.Settings.PauseHotkey == "F10");

    var cfgB = ConfigStore.CreateDefault();
    cfgB.Settings.PauseHotkey = "F11";
    store.Save(cfgB); // rotates A into .bak1
    File.WriteAllText(store.MainPath, "{ rusak total bukan json");
    var recovered = store.Load();
    Check(Localizer.Get("S1271"),
        recovered.Source == LoadSource.Backup1 && recovered.Config.Settings.PauseHotkey == "F10");

    File.Delete(store.MainPath);
    File.Delete(Path.Combine(dir, "config.json.bak1"));
    var fresh = store.Load();
    Check(Localizer.Get("S1272"), fresh.Source == LoadSource.FreshDefault && fresh.Config.Profiles.Count == 1);
    Directory.Delete(dir, recursive: true);
}

Console.WriteLine(Localizer.Get("S1238"));
{
    const string legacy = """
    {
      "TargetDeckPath": "\\\\?\\HID#VID_413C&PID_2003#7&e37a448&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}",
      "TargetDeckFriendlyName": "Dell Keyboard",
      "PauseHotkey": "F9",
      "StartWithWindows": true,
      "StartMinimized": false,
      "ActiveProfileId": "p1",
      "ActivePageId": "pg1",
      "ObsHost": "ws://127.0.0.1:4455",
      "ObsPassword": "",
      "Profiles": [
        {
          "Id": "p1", "Name": "Streaming",
          "AutoSwitchExe": ["obs64.exe"],
          "Pages": [
            { "Id": "pg1", "Name": "Main Page", "Bindings": [
              { "KeyId": "K1", "VKey": 124, "ScanCode": 88, "Label": "F13", "Icon": "", "BackgroundColor": "#1E222D",
                "ActionType": "Hotkey", "Description": "", "ActionParams": { "Key": "F13" } },
              { "KeyId": "K2", "VKey": 176, "ScanCode": 0, "Label": "Play", "Icon": "", "BackgroundColor": "#1E222D",
                "ActionType": "Media", "Description": "", "ActionParams": { "Command": "PlayPause" } },
              { "KeyId": "K3", "VKey": 0, "ScanCode": 0, "Label": "Scene", "Icon": "", "BackgroundColor": "#1E222D",
                "ActionType": "OBS", "Description": "", "ActionParams": { "Command": "SwitchScene", "SceneName": "Main" } },
              { "KeyId": "K4", "VKey": 0, "ScanCode": 0, "Label": "Kosong", "Icon": "", "BackgroundColor": "#1E222D",
                "ActionType": "None", "Description": "", "ActionParams": {} }
            ] }
          ]
        }
      ]
    }
    """;
    Check(Localizer.Get("S1273"), LegacyMigrator.LooksLegacy(legacy));
    var (migrated, report) = LegacyMigrator.Migrate(legacy);
    var binding0 = migrated.Profiles[0].Pages[0].Bindings[0];
    Check(Localizer.Get("S1274"), migrated.SchemaVersion == ConfigSchema.CurrentVersion);
    Check(Localizer.Get("S1275"), migrated.Profiles.Count == 1 && migrated.Settings.StartWithWindows && migrated.Settings.PauseHotkey == "F9");
    Check(Localizer.Get("S1276"),
        migrated.DeckDevice.Kind == nameof(DeviceIdKind.LegacyUnverified) && migrated.DeckDevice.RequiresReidentification);
    Check(Localizer.Get("S1277"),
        binding0.Steps.Count == 1 && binding0.Steps[0].ActionId == "hotkey" && binding0.Steps[0].Params["Key"]?.GetValue<string>() == "F13");
    Check(Localizer.Get("S1278"), migrated.Profiles[0].Pages[0].Bindings[3].Steps.Count == 0);
    Check(Localizer.Get("S1279"), report.BindingsMigrated == 3 && report.BindingsUnassigned == 1);

    // The migrated config must survive the new store too.
    string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-mig-" + Guid.NewGuid().ToString("N"));
    var store = new ConfigStore(dir);
    store.Save(migrated);
    var reloaded = store.Load();
    Check(Localizer.Get("S1280"), reloaded.Source == LoadSource.Main
        && reloaded.Config.Profiles[0].Pages[0].Bindings[0].Steps[0].ActionId == "hotkey");
    Directory.Delete(dir, recursive: true);
}

Console.WriteLine(Localizer.Get("S1239"));
{
    var keyA = new KeyIdentity(0x1E, false, false);
    var deckId = DeviceId.FromInterceptionHardwareId(@"HID\X");
    KeyEvent Down(long qpc, bool repeat = false) => new(deckId, keyA, 0x41, KeyDirection.Down, repeat, qpc);
    KeyEvent Up(long qpc) => new(deckId, keyA, 0x41, KeyDirection.Up, false, qpc);
    // TriggerEngine works in ms; the self-check feeds ms-shaped qpc values by
    // calling OnEvent/Tick directly with explicit ms (1 qpc tick := 1 ms here
    // would be wrong for DeckEngine, but TriggerEngine takes nowMs openly).
    TriggerEngine Engine(params TriggerKind[] kinds)
    {
        var eng = new TriggerEngine();
        eng.SetBindings(new Dictionary<KeyIdentity, IReadOnlySet<TriggerKind>>
        {
            [keyA] = new HashSet<TriggerKind>(kinds),
        });
        return eng;
    }

    // T1: tap-only fires on UP
    var t1 = Engine(TriggerKind.Tap);
    Check(Localizer.Get("S1281"), t1.OnEvent(Down(0), 0).Count == 0);
    var t1f = t1.OnEvent(Up(50), 50);
    Check(Localizer.Get("S1282"), t1f.Count == 1 && t1f[0].Kind == TriggerKind.Tap);

    // T2: long-press fires via Tick at threshold, once; UP after = nothing
    var t2 = Engine(TriggerKind.LongPress);
    t2.OnEvent(Down(0), 0);
    Check(Localizer.Get("S1283"), t2.Tick(399).Count == 0);
    var t2f = t2.Tick(400);
    Check(Localizer.Get("S1284"), t2f.Count == 1 && t2f[0].Kind == TriggerKind.LongPress);
    Check(Localizer.Get("S1285"), t2.Tick(450).Count == 0 && t2.OnEvent(Up(500), 500).Count == 0);

    // T3: tap+double - tap pending, expires into Tap; fresh pair -> DoubleTap on 2nd down
    var t3 = Engine(TriggerKind.Tap, TriggerKind.DoubleTap);
    t3.OnEvent(Down(0), 0);
    Check(Localizer.Get("S1286"), t3.OnEvent(Up(50), 50).Count == 0);
    var t3f = t3.Tick(351);
    Check(Localizer.Get("S1287"), t3f.Count == 1 && t3f[0].Kind == TriggerKind.Tap);
    var t3b = Engine(TriggerKind.Tap, TriggerKind.DoubleTap);
    t3b.OnEvent(Down(0), 0); t3b.OnEvent(Up(50), 50);
    var t3bf = t3b.OnEvent(Down(200), 200);
    Check(Localizer.Get("S1288"), t3bf.Count == 1 && t3bf[0].Kind == TriggerKind.DoubleTap);
    Check(Localizer.Get("S1289"), t3b.OnEvent(Up(250), 250).Count == 0 && t3b.Tick(900).Count == 0);

    // T4: auto-repeat tidak menembak ulang
    var t4 = Engine(TriggerKind.Tap);
    t4.OnEvent(Down(0), 0);
    Check(Localizer.Get("S1290"), t4.OnEvent(Down(30, repeat: true), 30).Count == 0);
    Check(Localizer.Get("S1291"), t4.OnEvent(Up(60), 60).Count == 1);

    // T6: Repeat (v2.3, untuk volume) - DOWN menembak langsung, repeat ikut, UP sepi
    var tR = Engine(TriggerKind.Repeat);
    var tRf = tR.OnEvent(Down(0), 0);
    Check(Localizer.Get("S1292"), tRf.Count == 1 && tRf[0].Kind == TriggerKind.Repeat);
    Check(Localizer.Get("S1293"), tR.OnEvent(Down(80, repeat: true), 80).Count == 1);
    Check(Localizer.Get("S1294"), tR.OnEvent(Down(110, repeat: true), 110).Count == 1);
    Check(Localizer.Get("S1295"), tR.OnEvent(Up(150), 150).Count == 0 && tR.Tick(500).Count == 0);

    // T7: Momentary (v3.3.0, Layer Modifier) - DOWN fires Momentary once,
    // repeats add nothing, UP fires MomentaryRelease - even after the
    // binding map changed mid-hold (the layer switch replaces the page).
    var t7 = Engine(TriggerKind.Momentary);
    var t7f = t7.OnEvent(Down(0), 0);
    Check(Localizer.Get("S1296"), t7f.Count == 1 && t7f[0].Kind == TriggerKind.Momentary);
    Check(Localizer.Get("S1297"), t7.OnEvent(Down(60, repeat: true), 60).Count == 0);
    var t7u = t7.OnEvent(Up(120), 120);
    Check(Localizer.Get("S1298"), t7u.Count == 1 && t7u[0].Kind == TriggerKind.MomentaryRelease);
    var t7b = Engine(TriggerKind.Momentary);
    t7b.OnEvent(Down(0), 0);
    t7b.SetBindings(new Dictionary<KeyIdentity, IReadOnlySet<TriggerKind>>()); // halaman berganti di tengah tahan
    var t7bu = t7b.OnEvent(Up(100), 100);
    Check(Localizer.Get("S1299"), t7bu.Count == 1 && t7bu[0].Kind == TriggerKind.MomentaryRelease);
    Check(Localizer.Get("S1300"), t7b.OnEvent(Up(150), 150).Count == 0);

    // T5: tap+long - tahan lama menembak Long, UP tidak menembak Tap
    var t5 = Engine(TriggerKind.Tap, TriggerKind.LongPress);
    t5.OnEvent(Down(0), 0);
    t5.Tick(400);
    Check(Localizer.Get("S1301"), t5.OnEvent(Up(600), 600).Count == 0);

    // T6: tanpa Tick, UP yang melewati ambang menembak LongPress
    var t6 = Engine(TriggerKind.LongPress, TriggerKind.Tap);
    t6.OnEvent(Down(0), 0);
    var t6f = t6.OnEvent(Up(450), 450);
    Check(Localizer.Get("S1302"), t6f.Count == 1 && t6f[0].Kind == TriggerKind.LongPress);
}

Console.WriteLine(Localizer.Get("S1240"));
{
    var recorder = new RecordingAction();
    using var dispatcher = new ActionDispatcher(new IAction[] { recorder });
    var executions = new List<StepExecution>();
    var done = new ManualResetEventSlim();
    dispatcher.StepExecuted += ex =>
    {
        lock (executions) executions.Add(ex);
        if (executions.Count >= 4) done.Set();
    };

    BindingConfig Job(string keyId, params string[] actionIds) => new()
    {
        KeyId = keyId,
        Steps = actionIds.Select(id => new ActionStep { ActionId = id }).ToList(),
    };
    dispatcher.Enqueue(new DispatchJob(Job("K1", "rec", "rec"), TriggerKind.Tap));
    dispatcher.Enqueue(new DispatchJob(Job("K2", "tidak-ada", "rec"), TriggerKind.Tap));
    Check(Localizer.Get("S1303"), done.Wait(TimeSpan.FromSeconds(5)));
    lock (executions)
    {
        Check(Localizer.Get("S1304"), executions.Select(e => e.KeyId).SequenceEqual(new[] { "K1", "K1", "K2", "K2" }));
        Check(Localizer.Get("S1305"), executions.Count == 4 && !executions[2].Result.Success);
        Check(Localizer.Get("S1306"), executions.Count == 4 && executions[3].Result.Success);
    }
    Check(Localizer.Get("S1307"), dispatcher.DroppedJobs == 0);
}

Console.WriteLine(Localizer.Get("S1241"));
{
    var recorder = new RecordingAction();
    using var dispatcher = new ActionDispatcher(new IAction[] { recorder });
    var engine = new DeckEngine(dispatcher);
    var executed = new ManualResetEventSlim();
    dispatcher.StepExecuted += _ => executed.Set();

    var cfg = ConfigStore.CreateDefault();
    var testPage = new PageConfig();
    cfg.Profiles[0].Pages.Add(testPage);
    testPage.Bindings.Add(new BindingConfig
    {
        KeyId = "K-A", ScanCode = 0x1E, VirtualKey = 0x41, Label = "A",
        Trigger = TriggerKind.Tap,
        Steps = { new ActionStep { ActionId = "rec" } },
    });
    engine.LoadPage(cfg);
    var deckId = DeviceId.FromInterceptionHardwareId(@"HID\X");
    var keyA = new KeyIdentity(0x1E, false, false);
    long qpcMs = Stopwatch.Frequency / 1000; // 1 ms in qpc ticks
    engine.ProcessEvent(new KeyEvent(deckId, keyA, 0x41, KeyDirection.Down, false, 0));
    engine.ProcessEvent(new KeyEvent(deckId, keyA, 0x41, KeyDirection.Up, false, 50 * qpcMs));
    Check(Localizer.Get("S1308"), executed.Wait(TimeSpan.FromSeconds(5)));
    Check(Localizer.Get("S1309"),
        recorder.Count == 1);

    var ident = new DeckIdentification();
    var deck = DeviceId.FromInterceptionHardwareId(@"HID\DECK");
    var main = DeviceId.FromInterceptionHardwareId(@"HID\MAIN");
    Check(Localizer.Get("S1310"), ident.Observe(deck) == IdentificationStage.AwaitingMain);
    Check(Localizer.Get("S1311"), ident.Observe(deck) == IdentificationStage.AwaitingMain);
    Check(Localizer.Get("S1312"), ident.Observe(main) == IdentificationStage.AwaitingDeckConfirm);
    Check(Localizer.Get("S1313"), ident.Observe(main) == IdentificationStage.AwaitingDeckConfirm);
    Check(Localizer.Get("S1314"), ident.Observe(deck) == IdentificationStage.Completed
        && ident.DeckCandidate!.Value.Matches(deck));
}

Console.WriteLine(Localizer.Get("S1242"));
{
    Check(Localizer.Get("S1315"), KeyChord.TryParse("F13", out var c1, out _) && c1!.MainKey == 0x7C && c1.Modifiers.Count == 0);
    Check(Localizer.Get("S1316"), KeyChord.TryParse("F24", out var c2, out _) && c2!.MainKey == 0x87);
    Check(Localizer.Get("S1317"), !KeyChord.TryParse("F25", out _, out _));
    Check("Ctrl+Shift+F13", KeyChord.TryParse("Ctrl+Shift+F13", out var c3, out _)
        && c3!.MainKey == 0x7C && c3.Modifiers.SequenceEqual(new ushort[] { 0x11, 0x10 }));
    Check(Localizer.Get("S1319"), KeyChord.TryParse("a", out var c4, out _) && c4!.MainKey == (ushort)'A');
    Check(Localizer.Get("S1320"), !KeyChord.TryParse("Ctrl+Banana", out _, out var err) && err.Length > 0);
    Check(Localizer.Get("S1321"), !KeyChord.TryParse("F13+Ctrl", out _, out _));
    Check(Localizer.Get("S1322"), !KeyChord.TryParse("  ", out _, out _));
    Check(Localizer.Get("S1323"),
        KeyChord.TryParse("Enter", out var c5, out _) && c5!.MainKey == 0x0D);
    Check(Localizer.Get("S1324"), KeyChord.TryParse(Localizer.Get("S0956"), out var k1, out _) && k1!.MainKey == 0x91);
    Check(Localizer.Get("S1325"), KeyChord.TryParse(Localizer.Get("S0957"), out var k2, out _) && k2!.MainKey == 0x90);
    Check(Localizer.Get("S1326"),
        KeyChord.TryParse(Localizer.Get("S0994"), out var k3, out _) && k3!.MainKey == 0x6F
        && KeyChord.TryParse(Localizer.Get("S0992"), out var k4, out _) && k4!.MainKey == 0x6B
        && KeyChord.TryParse(Localizer.Get("S0995"), out var k5, out _) && k5!.MainKey == 0x6E);
    Check(Localizer.Get("S1327"),
        KeyChord.TryParse("Minus", out var k6, out _) && k6!.MainKey == 0xBD
        && KeyChord.TryParse("Slash", out var k7, out _) && k7!.MainKey == 0xBF
        && KeyChord.TryParse("Backslash", out var k8, out _) && k8!.MainKey == 0xDC);
    Check(Localizer.Get("S1328"),
        KeyChord.TryParse("-", out var k9, out _) && k9!.MainKey == 0xBD
        && KeyChord.TryParse(",", out var k10, out _) && k10!.MainKey == 0xBC);
    Check(Localizer.Get("S1329"), KeyChord.TryParse("Menu", out var k11, out _) && k11!.MainKey == 0x5D);
    Check(Localizer.Get("S1330"),
        KeyChord.TryParse("NumpadEnter", out var k12, out _) && k12!.MainKey == 0x0D && k12.MainExtended
        && KeyChord.TryParse("Enter", out var k13, out _) && !k13!.MainExtended);
    Check(Localizer.Get("S1331"),
        KeyChord.TryParse("Ctrl+Minus", out var k14, out _) && k14!.MainKey == 0xBD && k14.Modifiers.Count == 1);
}

Console.WriteLine(Localizer.Get("S1243"));
{
    IAction hotkey = new HotkeyAction();
    Check(Localizer.Get("S1332"), hotkey.Validate(new JsonObject { ["Key"] = "F13" }).IsValid);
    Check(Localizer.Get("S1333"), !hotkey.Validate(new JsonObject { ["keys"] = "Ctrl+Banana" }).IsValid);

    IAction media = new MediaAction();
    Check(Localizer.Get("S1334"), media.Validate(new JsonObject { ["Command"] = "PlayPause" }).IsValid);
    Check(Localizer.Get("S1335"), media.Validate(new JsonObject { ["command"] = "volume up" }).IsValid);
    Check(Localizer.Get("S1336"), !media.Validate(new JsonObject { ["command"] = "Explode" }).IsValid);
    Check(Localizer.Get("S1337"), media.Validate(new JsonObject { ["command"] = "MicMute" }).IsValid);
    Check(Localizer.Get("S1338"), MediaAction.Commands.Count == 9);
    Check(Localizer.Get("S1339"), media.Validate(new JsonObject { ["command"] = "MediaPlayer" }).IsValid);

    IAction launcher = new LauncherAction();
    Check(Localizer.Get("S1340"), !launcher.Validate(new JsonObject()).IsValid);
    Check(Localizer.Get("S1341"), launcher.Validate(new JsonObject { ["Target"] = "https://example.com" }).IsValid);

    IAction link = new LinkAction();
    Check(Localizer.Get("S1342"), link.Validate(new JsonObject { ["url"] = "https://youtube.com" }).IsValid);
    Check(Localizer.Get("S1343"), !link.Validate(new JsonObject { ["url"] = "" }).IsValid);
    Check(Localizer.Get("S1344"), LinkAction.NormalizeUrl("youtube.com") == "https://youtube.com"
        && LinkAction.NormalizeUrl("https://x.id") == "https://x.id");
    IAction sound = new SoundAction();
    Check(Localizer.Get("S1345"), sound.Validate(new JsonObject { ["file"] = "C:\\sfx\\ding.wav" }).IsValid);
    Check(Localizer.Get("S1346"), sound.Validate(new JsonObject { ["file"] = "x.mp3" }).IsValid);
    Check(Localizer.Get("S1347"), sound.Validate(new JsonObject { ["file"] = "x.m4a" }).IsValid && sound.Validate(new JsonObject { ["file"] = "x.flac" }).IsValid && sound.Validate(new JsonObject { ["file"] = "x.ogg" }).IsValid);
    Check(Localizer.Get("S1348"), !sound.Validate(new JsonObject { ["file"] = "x.txt" }).IsValid);
}

Console.WriteLine(Localizer.Get("S1244"));
{
    // Independent recomputation of the obs-websocket auth composition -
    // deliberately NOT calling ObsClient.ComputeAuthentication here.
    string secret = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("pw" + "SA")));
    string expectedAuth = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(secret + "CH")));
    Check(Localizer.Get("S1349"),
        ObsClient.ComputeAuthentication("pw", "SA", "CH") == expectedAuth);

    var fake = new FakeObsTransport();
    using var client = new ObsClient(() => fake) { RequestTimeout = TimeSpan.FromMilliseconds(400) };
    bool connected = await client.ConnectAsync("ws://127.0.0.1:4455", "pw");
    Check(Localizer.Get("S1350"), connected && client.IsConnected);
    Check(Localizer.Get("S1351"), fake.IdentifyAuthentication == expectedAuth);

    var version = await client.RequestAsync("GetVersion");
    Check(Localizer.Get("S1352"), version.Success && version.Data?["obsVersion"]?.GetValue<string>() == "30.0.0");

    var noReply = await client.RequestAsync("NeverReplies");
    Check(Localizer.Get("S1353"), !noReply.Success && noReply.Error == Localizer.Format("S1197", client.RequestTimeout.TotalSeconds, "NeverReplies"));

    var obsAction = new ObsAction(client, () => ("ws://127.0.0.1:4455", "pw"));
    var ctx = new ActionContext { Cancellation = CancellationToken.None };
    var sceneResult = await obsAction.ExecuteAsync(
        new JsonObject { ["Command"] = "SwitchScene", ["SceneName"] = "Main" }, ctx);
    Check(Localizer.Get("S1354"), sceneResult.Success);
    Check(Localizer.Get("S1355"), !obsAction.Validate(new JsonObject { ["command"] = "Explode" }).IsValid);
    Check(Localizer.Get("S1356"),
        !obsAction.Validate(new JsonObject { ["command"] = "SwitchScene" }).IsValid);
}

Console.WriteLine(Localizer.Get("S1245"));
{
    var cfg = ConfigStore.CreateDefault();
    cfg.Profiles[0].Name = "Umum";
    var gaming = new ProfileConfig { Name = "Gaming", AutoSwitchExe = new List<string> { "game.exe", "OBS64" } };
    cfg.Profiles.Add(gaming);
    Check(Localizer.Get("S1357"), AutoProfileMatcher.FindProfileForProcess(cfg, "game.exe")?.Name == "Gaming");
    Check(Localizer.Get("S1358"), AutoProfileMatcher.FindProfileForProcess(cfg, "Game")?.Name == "Gaming");
    Check(Localizer.Get("S1359"), AutoProfileMatcher.FindProfileForProcess(cfg, "obs64.exe")?.Name == "Gaming");
    Check(Localizer.Get("S1360"), AutoProfileMatcher.FindProfileForProcess(cfg, "chrome.exe") is null);
    Check(Localizer.Get("S1361"), AutoProfileMatcher.FindProfileForProcess(cfg, " ") is null);
}

Console.WriteLine(Localizer.Get("S1246"));
{
    var profile = new ProfileConfig
    {
        Name = "Streaming Ku",
        Pages =
        {
            new PageConfig
            {
                Name = "Main",
                Bindings =
                {
                    new BindingConfig
                    {
                        KeyId = "K-1E", ScanCode = 0x1E, VirtualKey = 0x41, Label = "A",
                        Steps = { new ActionStep { ActionId = "hotkey", Params = new JsonObject { ["keys"] = "F13" } } },
                    },
                    new BindingConfig
                    {
                        KeyId = "K-99", ScanCode = 0x30, VirtualKey = 0x42, Label = "B",
                        Steps = { new ActionStep { ActionId = "plugin.asing" } },
                    },
                    new BindingConfig { KeyId = "K-RUSAK", ScanCode = 0, Label = "Tanpa scan code" },
                },
            },
        },
    };
    string json = ProfilePackage.Serialize(profile);
    var known = new HashSet<string> { "hotkey", "media", "obs", "launcher", "link", "sound" };
    var report = ProfilePackage.Parse(json, known);
    Check(Localizer.Get("S1362"), report.Profile.Name == "Streaming Ku" && report.BindingsImported == 2);
    Check(Localizer.Get("S1363"), report.BindingsDropped == 1);
    Check(Localizer.Get("S1364"), report.Warnings.Count == 1
        && report.Profile.Pages[0].Bindings.Any(b => b.Steps.Any(s => s.ActionId == "plugin.asing")));

    Check(Localizer.Get("S1365"), ThrowsInvalid(() => ProfilePackage.Parse("""{"format":"lain","packageVersion":1,"profile":{}}""")));
    Check(Localizer.Get("S1366"), !ThrowsInvalid(() => ProfilePackage.Parse(
        ProfilePackage.Serialize(profile).Replace("\"format\": \"ksprofile\"", "\"format\": \"mkdprofile\""))));
    Check(Localizer.Get("S1367"), ThrowsInvalid(() => ProfilePackage.Parse(
        ProfilePackage.Serialize(profile).Replace("\"packageVersion\": 1", "\"packageVersion\": 99"))));
    Check(Localizer.Get("S1368"), ThrowsInvalid(() => ProfilePackage.Parse("{ bukan json")));
    Check(Localizer.Get("S1369"), ThrowsInvalid(() => ProfilePackage.Parse(
        ProfilePackage.Serialize(new ProfileConfig { Name = "Kosong" }))));

    static bool ThrowsInvalid(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}

Console.WriteLine(Localizer.Get("S1247"));
{
    // Ditemukan user di hardware (23 Okt 2026): struct INPUT versi pertama
    // berukuran 32 byte (union hanya KEYBDINPUT) sehingga SendInput
    // menolak semua panggilan dengan errno 87 (invalid parameter).
    // Ukuran yang benar di x64 = 40 byte (union diukur oleh MOUSEINPUT).
    Check(Localizer.Get("S1370"), SendInputNative.InputStructSize == 40);
}

Console.WriteLine(Localizer.Get("S1248"));
{
    var sceneData = JsonNode.Parse("""
        { "scenes": [ { "sceneName": "Scene 1", "sceneIndex": 0 },
                      { "sceneName": "BRB", "sceneIndex": 1 },
                      { "sceneIndex": 2 } ],
          "currentProgramSceneName": "Scene 1" }
        """) as JsonObject;
    Check(Localizer.Get("S1371"),
        ObsClient.ParseSceneNames(sceneData).SequenceEqual(new[] { "Scene 1", "BRB" }));
    var inputData = JsonNode.Parse("""
        { "inputs": [ { "inputName": "Mic/Aux", "inputKind": "wasapi_input_capture" },
                      { "inputName": "Desktop Audio", "inputKind": "wasapi_output_capture" } ] }
        """) as JsonObject;
    Check(Localizer.Get("S1372"), ObsClient.ParseInputNames(inputData).SequenceEqual(new[] { "Mic/Aux", "Desktop Audio" }));
    Check(Localizer.Get("S1373"), ObsClient.ParseSceneNames(null).Count == 0 && ObsClient.ParseInputNames(null).Count == 0);
}

Console.WriteLine(Localizer.Get("S1249"));
{
    var items = JsonNode.Parse("""
        { "sceneItems": [ { "sourceName": "Webcam", "sceneItemId": 3 },
                          { "sourceName": "Overlay", "sceneItemId": 4 } ] }
        """) as JsonObject;
    Check(Localizer.Get("S1374"), ObsClient.ParseSceneItemNames(items).SequenceEqual(new[] { "Webcam", "Overlay" }));
    var filters = JsonNode.Parse("""
        { "filters": [ { "filterName": "Noise Suppression" }, { "filterName": "Blur" } ] }
        """) as JsonObject;
    Check(Localizer.Get("S1375"), ObsClient.ParseFilterNames(filters).SequenceEqual(new[] { "Noise Suppression", "Blur" }));
    var colls = JsonNode.Parse("""
        { "sceneCollections": [ { "sceneCollectionName": "Gaming" }, { "sceneCollectionName": "IRL" } ] }
        """) as JsonObject;
    Check(Localizer.Get("S1376"), ObsClient.ParseSceneCollectionNames(colls).SequenceEqual(new[] { "Gaming", "IRL" }));
    var profs = JsonNode.Parse("""{ "profiles": [ "Profil A", "Profil B" ] }""") as JsonObject;
    Check(Localizer.Get("S1377"), ObsClient.ParseProfileNames(profs).SequenceEqual(new[] { "Profil A", "Profil B" }));
    var withKind = JsonNode.Parse("""
        { "inputs": [ { "inputName": "BRB Text", "inputKind": "text_gdiplus_v2" },
                      { "inputName": "Mic", "inputKind": "wasapi_input_capture" } ] }
        """) as JsonObject;
    var parsedInputs = ObsClient.ParseInputs(withKind);
    Check(Localizer.Get("S1378"),
        parsedInputs.Count == 2 && parsedInputs[0].Kind.Contains("text") && !parsedInputs[1].Kind.Contains("text"));

    var act = new ObsAction(new ObsClient(() => throw new InvalidOperationException("no transport")), () => ("ws://127.0.0.1:4455", ""));
    JsonObject P(params (string K, string V)[] ps)
    {
        var o = new JsonObject();
        foreach (var (k, v) in ps) o[k] = v;
        return o;
    }
    Check(Localizer.Get("S1379"), act.Validate(P(("command", "SaveReplay"))).IsValid);
    Check(Localizer.Get("S1380"),
        act.Validate(P(("command", "ToggleSceneItem"), ("sceneName", "Main"))).IsValid == false
        && act.Validate(P(("command", "ToggleSceneItem"), ("sceneName", "Main"), ("itemName", "Webcam"))).IsValid);
    Check(Localizer.Get("S1381"),
        !act.Validate(P(("command", "SetInputVolume"), ("inputName", "Musik"), ("percent", "140"))).IsValid
        && act.Validate(P(("command", "SetInputVolume"), ("inputName", "Musik"), ("percent", "40"))).IsValid);
    Check(Localizer.Get("S1382"),
        act.Validate(P(("command", "SetText"), ("inputName", "BRB Text"), ("text", ""))).IsValid
        && !act.Validate(P(("command", "SetText"), ("inputName", "BRB Text"))).IsValid);
    Check(Localizer.Get("S1383"), act.Validate(P(("command", "Screenshot"), ("sourceName", "Main"))).IsValid);
    Check(Localizer.Get("S1384"), !act.Validate(P(("command", "TriggerHotkey"))).IsValid);
}

Console.WriteLine(Localizer.Get("S1250"));
{
    var pages = new List<(string Id, string Name)> { ("p1", "Main"), ("p2", "Soundboard"), ("p3", "Game") };
    string current = "p1";
    string? switchedTo = null;
    var act = new PageSwitchAction(() => pages, () => current,
        id => { if (pages.All(p => p.Id != id)) return false; switchedTo = id; current = id; return true; });
    JsonObject P(params (string K, string V)[] ps)
    {
        var o = new JsonObject();
        foreach (var (k, v) in ps) o[k] = v;
        return o;
    }
    var ctx = new ActionContext { Cancellation = CancellationToken.None };
    Check(Localizer.Get("S1385"), act.Validate(P(("command", "goto"), ("pageId", "p2"))).IsValid);
    Check(Localizer.Get("S1386"),
        !act.Validate(P(("command", "goto"), ("pageId", "px"))).IsValid);
    var r1 = await act.ExecuteAsync(P(("command", "goto"), ("pageId", "p3")), ctx);
    Check(Localizer.Get("S1387"), r1.Success && switchedTo == "p3");
    current = "p3";
    var r2 = await act.ExecuteAsync(P(("command", "next")), ctx);
    Check(Localizer.Get("S1388"), r2.Success && switchedTo == "p1");
    current = "p1";
    var r3 = await act.ExecuteAsync(P(("command", "prev")), ctx);
    Check(Localizer.Get("S1389"), r3.Success && switchedTo == "p3");
    Check(Localizer.Get("S1390"), !act.Validate(P(("command", "explode"))).IsValid);
    current = "p3";
    var r4 = await act.ExecuteAsync(P(("command", "home")), ctx);
    Check(Localizer.Get("S1391"), r4.Success && switchedTo == "p1");
}

Console.WriteLine(Localizer.Get("S1251"));
{
    var pages = new List<(string Id, string Name)> { ("p1", "Main"), ("p2", "Soundboard") };
    string current = "p1";
    var act = new PageSwitchAction(() => pages, () => current,
        id => { if (pages.All(p => p.Id != id)) return false; current = id; return true; });
    JsonObject P(params (string K, string V)[] ps)
    {
        var o = new JsonObject();
        foreach (var (k, v) in ps) o[k] = v;
        return o;
    }
    var down = new ActionContext { Cancellation = CancellationToken.None, FiredKind = TriggerKind.Momentary };
    var up = new ActionContext { Cancellation = CancellationToken.None, FiredKind = TriggerKind.MomentaryRelease };
    Check(Localizer.Get("S1392"), !act.Validate(P(("command", "momentary"))).IsValid);
    var a1 = await act.ExecuteAsync(P(("command", "momentary"), ("pageId", "p2")), down);
    Check(Localizer.Get("S1393"), a1.Success && current == "p2");
    var a2 = await act.ExecuteAsync(P(("command", "momentary"), ("pageId", "p2")), up);
    Check(Localizer.Get("S1394"), a2.Success && current == "p1");
    var a3 = await act.ExecuteAsync(P(("command", "momentary"), ("pageId", "p2")), up);
    Check(Localizer.Get("S1395"), a3.Success && current == "p1");

    var basic = new BasicAction();
    foreach (var c in new[] { "Cut", "Copy", "Paste", "Undo", "Redo", "All", "Find", "New", "Open", "Print", "Save", "WebSearch" })
        Check(Localizer.Format("S1396", c), basic.Validate(P(("command", c))).IsValid);
    Check(Localizer.Get("S1397"), !basic.Validate(P(("command", "Explode"))).IsValid);
    Check(Localizer.Get("S1398"), BasicAction.Chords.Count == 11);

    var profiles = new List<(string Id, string Name)> { ("profA", "Utama"), ("profB", "Gaming") };
    string curProfile = "profA";
    var pact = new ProfileSwitchAction(() => profiles, () => curProfile,
        id => { if (profiles.All(p => p.Id != id)) return false; curProfile = id; return true; });
    Check(Localizer.Get("S1399"), pact.Validate(P(("profileId", "profB"))).IsValid);
    Check(Localizer.Get("S1400"), !pact.Validate(new JsonObject()).IsValid);
    Check(Localizer.Get("S1401"), !pact.Validate(P(("profileId", "px"))).IsValid);
    var pr = await pact.ExecuteAsync(P(("profileId", "profB")), down);
    Check(Localizer.Get("S1402"), pr.Success && curProfile == "profB");
    var pr2 = await pact.ExecuteAsync(P(("profileId", "profB")), down);
    Check(Localizer.Get("S1403"), pr2.Success && curProfile == "profB");
}

Console.WriteLine(Localizer.Get("S1252"));
{
    Check(Localizer.Get("S1404"), IconReference.IsFileIcon("file:abc123.png"));
    Check(Localizer.Get("S1405"), !IconReference.IsFileIcon("mic"));
    Check(Localizer.Get("S1406"), IconReference.FileName("file:abc123.png") == "abc123.png");
    Check(Localizer.Get("S1407"), IconReference.FileName("mic") is null);
    Check(Localizer.Get("S1408"), IconReference.FileName("file:") is null);
    Check(Localizer.Get("S1409"),
        IconReference.FileNameForHash("ABCDEF0123456789FF") == "abcdef0123456789.png");
    Check(Localizer.Get("S1410"), IconReference.ValidateSize(1024, 2048 * 1024) is null);
    Check(Localizer.Get("S1411"),
        IconReference.ValidateSize(3 * 1024 * 1024, 2048 * 1024) is not null);
    Check(Localizer.Get("S1412"), IconReference.MaxDimension == 128);
}

Console.WriteLine(Localizer.Get("S1253"));
{
    var page = new Uri("https://example.com/sub/page");
    string html = "<html><head>"
        + "<link rel=\"stylesheet\" href=\"style.css\">"
        + "<link rel=\"icon\" href=\"/fav.png\">"
        + "<link rel=\"apple-touch-icon\" href=\"touch.png\">"
        + "<link href=\"late.ico\" rel=\"shortcut icon\">"
        + "</head></html>";
    var cands = FaviconDiscovery.DiscoverCandidates(html, page);
    Check(Localizer.Get("S1413"),
        cands.Count >= 1 && cands[0] == new Uri("https://example.com/sub/touch.png"));
    Check(Localizer.Get("S1414"),
        cands.Count == 4
        && cands[1] == new Uri("https://example.com/fav.png")
        && cands[2] == new Uri("https://example.com/sub/late.ico")
        && cands[3] == new Uri("https://example.com/favicon.ico"));
    var none = FaviconDiscovery.DiscoverCandidates("<html></html>", page);
    Check(Localizer.Get("S1415"),
        none.Count == 1 && none[0] == new Uri("https://example.com/favicon.ico"));
    var dup = FaviconDiscovery.DiscoverCandidates(
        "<link rel=\"icon\" href=\"/favicon.ico\">", page);
    Check(Localizer.Get("S1416"),
        dup.Count == 1 && dup[0] == new Uri("https://example.com/favicon.ico"));
    Check(Localizer.Get("S1417"),
        IconReference.GuessDomain(@"C:\Program Files\WhatsApp\WhatsApp.exe") == "whatsapp.com");
    Check(Localizer.Get("S1418"),
        IconReference.GuessDomain(@"C:\x\My App v2.exe") == "myappv2.com");
    Check("domain guess: " + Localizer.Get("S1419"),
        IconReference.GuessDomain("a.exe") is null && IconReference.GuessDomain("") is null);
    var mig = new Keystream.Core.Config.BindingConfig { Icon = "file:abc.png", Steps = new() { new Keystream.Core.Config.ActionStep { ActionId = "launcher" } } };
    Check(Localizer.Get("S1420"),
        mig.MigrateLegacyAutoIcon() && mig.AutoIcon == "file:abc.png" && mig.Icon == "");
    Check(Localizer.Get("S1421"),
        !mig.MigrateLegacyAutoIcon());
    var migLink = new Keystream.Core.Config.BindingConfig { Icon = "file:abc.png", Steps = new() { new Keystream.Core.Config.ActionStep { ActionId = "link" } } };
    Check(Localizer.Get("S1422"),
        migLink.MigrateLegacyAutoIcon() && migLink.AutoIcon == "file:abc.png");
    var hot = new Keystream.Core.Config.BindingConfig { Icon = "file:abc.png", Steps = new() { new Keystream.Core.Config.ActionStep { ActionId = "hotkey" } } };
    Check(Localizer.Get("S1423"),
        !hot.MigrateLegacyAutoIcon() && hot.Icon == "file:abc.png");
    var cat = new Keystream.Core.Config.BindingConfig { Icon = "gamepad", Steps = new() { new Keystream.Core.Config.ActionStep { ActionId = "launcher" } } };
    Check(Localizer.Get("S1424"),
        !cat.MigrateLegacyAutoIcon() && cat.Icon == "gamepad");
}

Console.WriteLine(Localizer.Get("S1254"));
{
    BindingConfig MkBinding(ushort scan, string actionId, string cmd) => new()
    {
        KeyId = $"K-{scan:X2}",
        ScanCode = scan,
        Trigger = TriggerKind.Tap,
        Steps = { new ActionStep { ActionId = actionId, Params = new JsonObject { ["command"] = cmd } } },
    };
    var cfg = new DeckConfig
    {
        Profiles =
        {
            new ProfileConfig
            {
                Name = "P",
                Pages =
                {
                    new PageConfig
                    {
                        Name = "Main",
                        Bindings =
                        {
                            MkBinding(0x1E, "media", "PlayPause"), // tombol A versi halaman
                            MkBinding(0x30, "media", "Stop"),      // tombol B versi halaman
                        },
                    },
                },
                NavBindings = { MkBinding(0x1E, "deck", "next") }, // tombol A = navigasi global
            },
        },
    };
    cfg.ActiveProfileId = cfg.Profiles[0].Id;
    var resolved = BindingResolver.Resolve(cfg, cfg.Profiles[0].Pages[0].Id);
    var keyA = new KeyIdentity(0x1E, false, false);
    var keyB = new KeyIdentity(0x30, false, false);
    Check(Localizer.Get("S1425"),
        resolved.Bindings.TryGetValue((keyA, TriggerKind.Tap), out var aBind) && aBind.Steps[0].ActionId == "deck");
    Check(Localizer.Get("S1426"),
        resolved.Bindings.TryGetValue((keyB, TriggerKind.Tap), out var bBind) && bBind.Steps[0].ActionId == "media");
    Check(Localizer.Get("S1427"),
        resolved.TriggerMap.TryGetValue(keyA, out var aSet) && aSet.Count == 1 && aSet.Contains(TriggerKind.Tap));
}

Console.WriteLine(Localizer.Get("S1255"));
{
    var working = DriverStatus.Check(serviceRunning: true);
    Check(Localizer.Get("S1428"), working.State == DriverState.Working);
    var notRunning = DriverStatus.Check(serviceRunning: false);
    Check(Localizer.Get("S1429"),
        OperatingSystem.IsWindows() || notRunning.State == DriverState.Unknown);
}

Console.WriteLine(Localizer.Get("S1256"));
{
    BindingConfig Mk(ushort scan, string actionId, string cmd, TriggerKind trig = TriggerKind.Tap) => new()
    {
        KeyId = $"K-{scan:X2}",
        ScanCode = scan,
        Trigger = trig,
        Steps = { new ActionStep { ActionId = actionId, Params = new JsonObject { ["command"] = cmd } } },
    };
    var cfg = new DeckConfig
    {
        Profiles =
        {
            new ProfileConfig
            {
                Name = "P",
                Pages = { new PageConfig { Name = "Main", Bindings = { Mk(0x1E, "media", "PlayPause"), Mk(0x30, "media", "Stop") } } },
                NavBindings = { Mk(0x1E, "deck", "next") },
            },
        },
        GlobalPage = new PageConfig { Name = "Global", Bindings = { Mk(0x1E, "obs", "ToggleRecord") } },
    };
    cfg.ActiveProfileId = cfg.Profiles[0].Id;
    var resolved = BindingResolver.Resolve(cfg, cfg.Profiles[0].Pages[0].Id);
    var keyA = new KeyIdentity(0x1E, false, false);
    var keyB = new KeyIdentity(0x30, false, false);
    Check(Localizer.Get("S1430"),
        resolved.Bindings.TryGetValue((keyA, TriggerKind.Tap), out var gBind) && gBind.Steps[0].ActionId == "obs");
    Check(Localizer.Get("S1431"),
        resolved.Bindings.TryGetValue((keyB, TriggerKind.Tap), out var bBind) && bBind.Steps[0].ActionId == "media");

    var cfg2 = new DeckConfig
    {
        Profiles =
        {
            new ProfileConfig { Name = "A", NavBindings = { Mk(0x4D, "deck", "next") } },
            new ProfileConfig { Name = "B", NavBindings = { Mk(0x4D, "deck", "prev"), Mk(0x4B, "deck", "home") } },
        },
    };
    bool changed1 = GlobalMigration.MigrateNavBindingsToGlobalPage(cfg2);
    Check(Localizer.Get("S1432"),
        changed1 && cfg2.GlobalPage.Bindings.Count == 2
        && cfg2.Profiles.All(p => p.NavBindings.Count == 0));
    bool changed2 = GlobalMigration.MigrateNavBindingsToGlobalPage(cfg2);
    Check(Localizer.Get("S1433"), !changed2 && cfg2.GlobalPage.Bindings.Count == 2);
}

Console.WriteLine(Localizer.Get("S1257"));
{
    var keyP = new KeyIdentity(0x1E, false, false);
    var keyC = new KeyIdentity(0x30, false, false);
    var keyF2 = new KeyIdentity(0x31, false, false);
    var keyD = new KeyIdentity(0x20, false, false);
    KeyCombinationConfig Combo(ushort second) => new()
    {
        SecondScanCode = second, SecondKeyName = "C",
        Steps = { new ActionStep { ActionId = "rec" } },
    };
    BindingConfig Parent(ushort scan, TriggerKind trig, KeyCombinationConfig? combo = null)
    {
        var b = new BindingConfig { KeyId = $"K-{scan:X2}", ScanCode = scan, Trigger = trig };
        if (combo is not null) b.Combinations.Add(combo);
        return b;
    }
    BindingConfig Plain(ushort scan, TriggerKind trig = TriggerKind.Tap) => new()
    {
        KeyId = $"K-{scan:X2}", ScanCode = scan, Trigger = trig,
        Steps = { new ActionStep { ActionId = "rec" } },
    };
    DeckConfig MkCfg(BindingConfig[] page, BindingConfig[]? global = null, BindingConfig[]? nav = null)
    {
        var profile = new ProfileConfig
        {
            Name = "P",
            Pages = { new PageConfig { Name = "Main", Bindings = { } } },
        };
        foreach (var b in page) profile.Pages[0].Bindings.Add(b);
        if (nav is not null) foreach (var b in nav) profile.NavBindings.Add(b);
        var cfg = new DeckConfig { Profiles = { profile } };
        cfg.ActiveProfileId = cfg.Profiles[0].Id;
        cfg.GlobalPage = new PageConfig { Name = "Global" };
        if (global is not null) foreach (var b in global) cfg.GlobalPage.Bindings.Add(b);
        return cfg;
    }

    var rTap = BindingResolver.Resolve(MkCfg(new[] { Parent(0x1E, TriggerKind.Tap, Combo(0x30)) }));
    Check(Localizer.Get("S1434"), rTap.Combinations.ContainsKey((keyP, keyC)));
    var rMom = BindingResolver.Resolve(MkCfg(new[] { Parent(0x1E, TriggerKind.Momentary, Combo(0x30)) }));
    Check(Localizer.Get("S1435"), !rMom.Combinations.ContainsKey((keyP, keyC)));
    var rRep = BindingResolver.Resolve(MkCfg(new[] { Parent(0x1E, TriggerKind.Repeat, Combo(0x30)) }));
    Check(Localizer.Get("S1436"), !rRep.Combinations.ContainsKey((keyP, keyC)));
    var rLong = BindingResolver.Resolve(MkCfg(new[] { Parent(0x1E, TriggerKind.LongPress, Combo(0x30)) }));
    Check(Localizer.Get("S1437"), !rLong.Combinations.ContainsKey((keyP, keyC)));

    var rGlob = BindingResolver.Resolve(MkCfg(
        new[] { Parent(0x1E, TriggerKind.Tap, Combo(0x30)) },
        new[] { Parent(0x1E, TriggerKind.Tap, Combo(0x20)) }));
    Check(Localizer.Get("S1438"), !rGlob.Combinations.ContainsKey((keyP, keyC)));
    Check(Localizer.Get("S1439"), rGlob.Combinations.ContainsKey((keyP, keyD)));
    var rNav = BindingResolver.Resolve(MkCfg(
        new[] { Parent(0x1E, TriggerKind.Tap, Combo(0x30)) }, null, new[] { Plain(0x1E) }));
    Check(Localizer.Get("S1440"), !rNav.Combinations.ContainsKey((keyP, keyC)));

    var deckId = DeviceId.FromInterceptionHardwareId(@"HID\X");
    long qpcMs = Stopwatch.Frequency / 1000;
    {
        var recorder = new RecordingAction();
        using var dispatcher = new ActionDispatcher(new IAction[] { recorder });
        var engine = new DeckEngine(dispatcher);
        engine.LoadPage(MkCfg(new[] { Plain(0x1E) }));
        engine.ProcessEvent(new KeyEvent(deckId, keyP, 0x41, KeyDirection.Down, false, 0));
        engine.LoadPage(MkCfg(Array.Empty<BindingConfig>()));
        engine.ProcessEvent(new KeyEvent(deckId, keyP, 0x41, KeyDirection.Up, false, 50 * qpcMs));
        Thread.Sleep(500);
        Check(Localizer.Get("S1441"), recorder.Count == 0);
    }
    {
        var recorder = new RecordingAction();
        using var dispatcher = new ActionDispatcher(new IAction[] { recorder });
        var fired = new ManualResetEventSlim();
        dispatcher.StepExecuted += _ => fired.Set();
        var engine = new DeckEngine(dispatcher);
        engine.LoadPage(MkCfg(new[] { Parent(0x1E, TriggerKind.Tap, Combo(0x30)) }));
        engine.ProcessEvent(new KeyEvent(deckId, keyP, 0x41, KeyDirection.Down, false, 0));
        engine.ProcessEvent(new KeyEvent(deckId, keyC, 0x42, KeyDirection.Down, false, 10 * qpcMs));
        Check(Localizer.Get("S1442"), fired.Wait(TimeSpan.FromSeconds(5)) && recorder.Count == 1);
        engine.LoadPage(MkCfg(Array.Empty<BindingConfig>()));
        engine.ProcessEvent(new KeyEvent(deckId, keyC, 0x42, KeyDirection.Up, false, 20 * qpcMs));
        engine.ProcessEvent(new KeyEvent(deckId, keyP, 0x41, KeyDirection.Up, false, 30 * qpcMs));
        Thread.Sleep(500);
        Check(Localizer.Get("S1443"), recorder.Count == 1);
    }
    {
        var recorder = new RecordingAction();
        using var dispatcher = new ActionDispatcher(new IAction[] { recorder });
        var fired = new ManualResetEventSlim();
        dispatcher.StepExecuted += _ => fired.Set();
        var engine = new DeckEngine(dispatcher);
        engine.LoadPage(MkCfg(new[]
        {
            Parent(0x1E, TriggerKind.Tap, Combo(0x30)),
            Parent(0x31, TriggerKind.Tap, Combo(0x30)),
        }));
        engine.ProcessEvent(new KeyEvent(deckId, keyP, 0x41, KeyDirection.Down, false, 0));
        engine.ProcessEvent(new KeyEvent(deckId, keyF2, 0x42, KeyDirection.Down, false, 10 * qpcMs));
        engine.ProcessEvent(new KeyEvent(deckId, keyC, 0x43, KeyDirection.Down, false, 20 * qpcMs));
        Check(Localizer.Get("S1444"), fired.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(300);
        Check(Localizer.Get("S1445"), recorder.Count == 1);
    }
}

Console.WriteLine(Localizer.Get("S1258"));
{
    JsonObject P(params (string K, string V)[] ps)
    {
        var o = new JsonObject();
        foreach (var (k, v) in ps) o[k] = v;
        return o;
    }
    var ctx = new ActionContext { Cancellation = CancellationToken.None };

    var win = new WindowsAction();
    foreach (var c in new[] { "SwitchWindows", "SnapTopLeft", "LockPC", "TaskManager", "MagnifyIn", "SettingsDisplay", "BrowserBack", "Mail",
        // §126: ke-28 command peluncuran exe/msc/cpl harus valid - sebelum mekanisme
        // Launches mendarat, Validate() menolak semuanya (S1208).
        // §127: 6 command admin (Launches biasa, tanpa runas) ikut valid.
        "BackupRestore", "CommandPrompt", "ComputerManagement", "ControlPanel", "DiskDefragmenter", "DeviceManager",
        "DirectXDiagnostic", "DiskCleanup", "DiskManagement", "EventViewer", "GroupPolicyEditor", "Netstat",
        "NetworkConnections", "NetworkInformation", "PerformanceMonitor", "PowerOptions", "ProgramsAndFeatures",
        "ResourceMonitor", "SecurityMaintenance", "Services", "SharedFolders", "SystemConfiguration",
        "SystemInformation", "SystemProperties", "SystemRestore", "TaskScheduler", "WindowsFeatures", "WindowsPowerShell",
        "CheckDisk", "DISM", "FlushDNS", "MSRT", "RegistryEditor", "SystemFileChecker" })
        Check(Localizer.Format("S1446", c), win.Validate(P(("command", c))).IsValid);
    Check(Localizer.Get("S1447"), !win.Validate(P(("command", "Explode"))).IsValid);
    // Keyword filter BUKAN command: "gpedit"/"msconfig" hanya hidup di
    // kolom cari picker, tidak pernah lolos Validate().
    Check(Localizer.Get("S1447"), !win.Validate(P(("command", "gpedit.msc"))).IsValid);
    Check(Localizer.Get("S1447"), !win.Validate(P(("command", "msconfig.exe"))).IsValid);
    // §127: nama file/keyword admin BUKAN command - hanya Value katalog
    // ("CheckDisk", "DISM", ...) yang lolos; "dism"/"flushdns" sendiri
    // ADALAH kunci ternormalisasi Value, jadi valid dan tidak di sini.
    Check("windows: nama file/keyword §127 BUKAN command (sfc.exe/dism.exe/chkdsk.exe/regedit.exe/mrt.exe/ipconfig.exe/sfc/chkdsk/regedit/mrt ditolak)",
        !win.Validate(P(("command", "sfc.exe"))).IsValid && !win.Validate(P(("command", "dism.exe"))).IsValid
        && !win.Validate(P(("command", "chkdsk.exe"))).IsValid && !win.Validate(P(("command", "regedit.exe"))).IsValid
        && !win.Validate(P(("command", "mrt.exe"))).IsValid && !win.Validate(P(("command", "ipconfig.exe"))).IsValid
        && !win.Validate(P(("command", "sfc"))).IsValid && !win.Validate(P(("command", "chkdsk"))).IsValid
        && !win.Validate(P(("command", "regedit"))).IsValid && !win.Validate(P(("command", "mrt"))).IsValid);
    // Label hitungan literal (bukan S1448): teks S1448 masih menyebut
    // "60 perintah" dari era sebelum §126; menambah string baru dilarang
    // §126, dan label basi di log SelfCheck lebih menyesatkan daripada
    // label literal. S1448 kini tak dipakai di sini.
    Check("windows: 94 perintah terdaftar (60 lama + 28 §126 + 6 §127)",
        WindowsAction.Chords.Count + WindowsAction.Sequences.Count + WindowsAction.VirtualKeys.Count + WindowsAction.Uris.Count + WindowsAction.Launches.Count + 1 == 94);
    Check("windows: 34 peluncuran terdaftar (28 §126 + 6 §127)", WindowsAction.Launches.Count == 34);
    Check(Localizer.Get("S1449"),
        !WindowsAction.IsKnownCommand("BrowserSearch"));
    // §129 (pekerja A) - pemisahan inti, executor yang sama. Label
    // literal seperti cek §127 di atas: string baru hanya untuk nama
    // type (S1681–S1684), bukan untuk label cek.
    {
        var cmdKeys = WindowsAction.Chords.Keys.Concat(WindowsAction.Sequences.Keys)
            .Concat(WindowsAction.VirtualKeys.Keys).Append(WindowsAction.LockCommand).ToList();
        var toolKeys = WindowsAction.Uris.Keys.Concat(WindowsAction.Launches.Keys).ToList();
        Check("§129: partisi mekanisme 52 + 42 = 94, disjoint",
            cmdKeys.Count == 52 && toolKeys.Count == 42
            && WindowsAction.Chords.Count == 40 && WindowsAction.Sequences.Count == 4
            && WindowsAction.VirtualKeys.Count == 7 && WindowsAction.Uris.Count == 8
            && cmdKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 52
            && toolKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 42
            && !cmdKeys.Any(k => toolKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            && cmdKeys.Concat(toolKeys).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 94);
        Check("§129: tiap kunci partisi diklasifikasikan tepat satu mekanisme",
            cmdKeys.All(k => WindowsAction.IsCommandMechanism(k) && !WindowsAction.IsToolsMechanism(k))
            && toolKeys.All(k => WindowsAction.IsToolsMechanism(k) && !WindowsAction.IsCommandMechanism(k)));
        var legacy = new WindowsAction();
        var cmdAct = new WindowsAction(WindowsAction.CommandId);
        var toolAct = new WindowsAction(WindowsAction.ToolsId);
        Check("§129: tiga id terdaftar dari satu kelas executor (descriptor berbeda, Validate sama)",
            legacy.Descriptor.Id == "windows" && cmdAct.Descriptor.Id == "windows-command"
            && toolAct.Descriptor.Id == "windows-tools"
            && legacy.Descriptor.DisplayName == Localizer.Get("S0577")
            && cmdAct.Descriptor.DisplayName == Localizer.Get("S1681")
            && toolAct.Descriptor.DisplayName == Localizer.Get("S1683")
            && legacy.Descriptor.Category == cmdAct.Descriptor.Category
            && cmdAct.Descriptor.Category == toolAct.Descriptor.Category);
        var all94 = cmdKeys.Concat(toolKeys).ToList();
        Check("§129: type lama + kedua type baru menerima ke-94 Value (executor bersama)",
            all94.All(k => legacy.Validate(P(("command", k))).IsValid
                && cmdAct.Validate(P(("command", k))).IsValid
                && toolAct.Validate(P(("command", k))).IsValid));
        Check("§129: migrasi-saat-edit: legacy -> split sesuai mekanisme, sisanya no-op",
            cmdKeys.All(k => WindowsAction.ResolveSplitActionId("windows", k) == "windows-command")
            && toolKeys.All(k => WindowsAction.ResolveSplitActionId("windows", k) == "windows-tools")
            && WindowsAction.ResolveSplitActionId("WINDOWS", "LockPC") == "windows-command"
            && WindowsAction.ResolveSplitActionId("windows", "SettingsDisplay") == "windows-tools"
            && WindowsAction.ResolveSplitActionId("windows", "Explode") is null
            && WindowsAction.ResolveSplitActionId("windows-command", "SwitchWindows") is null
            && WindowsAction.ResolveSplitActionId("windows-tools", "Services") is null
            && WindowsAction.ResolveSplitActionId(null, "SwitchWindows") is null
            && WindowsAction.ResolveSplitActionId("basic", "SwitchWindows") is null);
        Check("§129: id deskriptor asing jatuh ke legacy (tidak mendaftarkan id liar)",
            new WindowsAction("windows-commandx").Descriptor.Id == "windows");
    }

    string tmp = Path.Combine(Path.GetTempPath(), "keystream-selfcheck-" + Guid.NewGuid().ToString("N"));
    string userData = Path.Combine(tmp, "User Data");
    Directory.CreateDirectory(Path.Combine(userData, "Default"));
    File.WriteAllText(Path.Combine(userData, "Default", "Preferences"), "{\"profile\":{\"name\":\"Kerja\"}}");
    Directory.CreateDirectory(Path.Combine(userData, "Profile 1"));
    File.WriteAllText(Path.Combine(userData, "Profile 1", "Preferences"), "{\"profile\":{\"name\":\"Pribadi\"}}");
    Directory.CreateDirectory(Path.Combine(userData, "Profile 2"));
    string ffDir = Path.Combine(tmp, "Firefox");
    Directory.CreateDirectory(ffDir);
    File.WriteAllText(Path.Combine(ffDir, "profiles.ini"),
        "[Profile0]\nName=Utama\nIsRelative=1\nPath=abc.default\n\n[Profile1]\nName=Kerja\nIsRelative=1\nPath=def.kerja\n");
    var chromium = new DetectedBrowser("t", "Test Chromium", Path.Combine(tmp, "chrome.exe"), "Chromium", userData, false);
    var chromiumProfiles = BrowserDiscovery.GetProfiles(chromium);
    Check(Localizer.Get("S1450"), chromiumProfiles.Count == 3);
    Check(Localizer.Get("S1451"), chromiumProfiles[0].Display == "Kerja" && chromiumProfiles[1].Display == "Pribadi");
    Check(Localizer.Get("S1452"),
        chromiumProfiles[2].Key == "Profile 2" && chromiumProfiles[2].Display == "Profile 2");
    string dupData = Path.Combine(tmp, "Dup Data");
    Directory.CreateDirectory(Path.Combine(dupData, "Default"));
    File.WriteAllText(Path.Combine(dupData, "Default", "Preferences"), "{\"profile\":{\"name\":\"Person 1\"}}");
    Directory.CreateDirectory(Path.Combine(dupData, "Profile 1"));
    File.WriteAllText(Path.Combine(dupData, "Profile 1", "Preferences"), "{\"profile\":{\"name\":\"Person 1\"}}");
    var dupProfiles = BrowserDiscovery.GetProfiles(new DetectedBrowser("d", "Dup", Path.Combine(tmp, "x.exe"), "Chromium", dupData, false));
    Check(Localizer.Get("S1453"),
        dupProfiles.Count == 2 && dupProfiles[0].Display == "Person 1 (Default)" && dupProfiles[1].Display == "Person 1 (Profile 1)");
    Check(Localizer.Get("S1454"),
        new DetectedBrowser("t", "Thorium", "x.exe", "Chromium", "", false).ToString() == "Thorium"
        && dupProfiles[0].ToString() == "Person 1 (Default)");
    var firefox = new DetectedBrowser("f", "Test Firefox", Path.Combine(tmp, "firefox.exe"), "Firefox", ffDir, false);
    var ffProfiles = BrowserDiscovery.GetProfiles(firefox);
    Check(Localizer.Get("S1455"),
        ffProfiles.Count == 2 && ffProfiles[0].Key == "Utama" && ffProfiles[1].Key == "Kerja");
    var customs = new List<CustomBrowserConfig>
    {
        new() { Name = "Thorium Portabel", ExePath = Path.Combine(tmp, "thorium.exe"), Engine = "Chromium", UserDataDir = userData },
    };
    var detected = BrowserDiscovery.DetectBrowsers(customs);
    Check(Localizer.Get("S1456"), detected.Any(b => b.IsCustom && b.Name == "Thorium Portabel"));
    Check(Localizer.Get("S1457"), BrowserDiscovery.NormalizeEngine("firefox") == "Firefox");

    var bact = new BrowserAction();
    Check(Localizer.Get("S1458"),
        bact.Validate(P(("browserExe", "C:\\x\\chrome.exe"), ("profileKey", "Default"))).IsValid);
    Check(Localizer.Get("S1459"),
        !bact.Validate(P(("browserExe", "C:\\x\\chrome.exe"))).IsValid);
    var groups = new List<BrowserProfileGroup>();
    var gact = new BrowserGroupAction(() => groups);
    Check(Localizer.Get("S1460"),
        !(await gact.ExecuteAsync(P(("groupId", "nope")), ctx)).Success);
    groups.Add(new BrowserProfileGroup
    {
        Id = "g1", Name = "Tes", Mode = "Simultaneous",
        Entries = new() { new() { BrowserExe = Path.Combine(tmp, "missing.exe"), Engine = "Chromium", ProfileKey = "Default", ProfileDisplay = "Default" } },
    });
    Check(Localizer.Get("S1461"),
        !(await gact.ExecuteAsync(P(("groupId", "g1")), ctx)).Success);
    Check(Localizer.Get("S1462"),
        bact.Validate(P(("browserExe", "C:\\x\\chrome.exe"), ("profileKey", "Default"), ("openMode", "Focus"))).IsValid
        && bact.Validate(P(("browserExe", "C:\\x\\chrome.exe"), ("profileKey", "Default"), ("openMode", "Toggle"))).IsValid
        && !bact.Validate(P(("browserExe", "C:\\x\\chrome.exe"), ("profileKey", "Default"), ("openMode", "Explode"))).IsValid);
    Check(Localizer.Get("S1463"),
        BrowserAction.NormalizeOpenMode(null) == "New"
        && BrowserAction.NormalizeOpenMode("focus") == "Focus"
        && BrowserAction.NormalizeOpenMode("Toggle") == "Toggle"
        && BrowserAction.NormalizeOpenMode("apa pun") == "New");
    Check(Localizer.Get("S1464"),
        new BrowserProfileGroup().OpenMode == "New");
    Check(Localizer.Get("S1465"),
        BrowserWindowTracker.Find(new List<BrowserWindowTracking>
            { new() { BrowserExe = "C:\\x\\chrome.exe", ProfileKey = "Default", Hwnd = 123 } },
            "c:\\x\\chrome.exe", "Default")?.Hwnd == 123);
Directory.CreateDirectory(Path.Combine(tmp, "ManualChromium"));
File.WriteAllText(Path.Combine(tmp, "ManualChromium", "Preferences"), "{\"profile\":{\"name\":\"ManualKerja\"}}");
Directory.CreateDirectory(Path.Combine(tmp, "ManualFirefox"));
File.WriteAllText(Path.Combine(tmp, "ManualFirefox", "prefs.js"), "");
Directory.CreateDirectory(Path.Combine(tmp, "EmptyFolder"));
Check(Localizer.Get("S1466"),
    BrowserDiscovery.TryDetectProfileFolder(Path.Combine(tmp, "ManualChromium"), out string mEng, out string mName)
    && mEng == "Chromium" && mName == "ManualKerja");
Check(Localizer.Get("S1467"),
    BrowserDiscovery.TryDetectProfileFolder(Path.Combine(tmp, "ManualFirefox"), out string fEng, out string fName)
    && fEng == "Firefox" && fName == "ManualFirefox");
Check(Localizer.Get("S1468"),
    !BrowserDiscovery.TryDetectProfileFolder(Path.Combine(tmp, "EmptyFolder"), out _, out _));
Check(Localizer.Get("S1469"),
    bact.Validate(P(("browserExe", Path.Combine(tmp, "chrome.exe")), ("profilePath", Path.Combine(tmp, "ManualChromium")))).IsValid);
Check(Localizer.Get("S1470"),
    !bact.Validate(P(("browserExe", Path.Combine(tmp, "chrome.exe")), ("profilePath", Path.Combine(tmp, "EmptyFolder")))).IsValid);
Check(Localizer.Get("S1471"),
    !(await bact.ExecuteAsync(P(("browserExe", Path.Combine(tmp, "missing.exe")), ("profilePath", Path.Combine(tmp, "ManualChromium"))), ctx)).Success);
Check(Localizer.Get("S1472"),
    BrowserAction.NormalizeUrl("https://example.com/a?b=1").Normalized == "https://example.com/a?b=1");
Check(Localizer.Get("S1473"),
    BrowserAction.NormalizeUrl("example.com").Normalized == "https://example.com/");
Check(Localizer.Get("S1474"),
    BrowserAction.NormalizeUrl("www.example.com").Normalized == "https://www.example.com/");
Check(Localizer.Get("S1475"),
    BrowserAction.NormalizeUrl("  ").Normalized is null);
Check(Localizer.Get("S1476"),
    BrowserAction.NormalizeUrl("javascript:alert(1)").Error is not null);
Check(Localizer.Get("S1477"),
    BrowserAction.NormalizeUrl("file:///etc/passwd").Error is not null);
Check(Localizer.Get("S1478"),
    BrowserAction.NormalizeUrl("https://example.com/a b").Error is not null);
Check(Localizer.Get("S1479"),
    !bact.Validate(P(("browserExe", Path.Combine(tmp, "chrome.exe")), ("profileKey", "Default"), ("url", "javascript:x"))).IsValid);
Check(Localizer.Get("S1480"),
    (await bact.ExecuteAsync(P(("browserExe", Path.Combine(tmp, "missing.exe")), ("profileKey", "Default"), ("url", "javascript:x")), ctx)).Error?.Contains("URL") == true);
Check(Localizer.Get("S1481"),
    bact.Validate(P(("browserExe", Path.Combine(tmp, "chrome.exe")), ("profileKey", "Default"))).IsValid);
var failDetail = await bact.ExecuteAsync(P(("browserExe", Path.Combine(tmp, "missing.exe")), ("profileKey", "Default")), ctx);
Check(Localizer.Get("S1482"),
    !failDetail.Success && failDetail.Detail is null);
string udDir = Path.Combine(tmp, "PortabelUserData");
Directory.CreateDirectory(Path.Combine(udDir, "Default"));
File.WriteAllText(Path.Combine(udDir, "Default", "Preferences"), "{\"profile\":{\"name\":\"Portabel1\"}}");
Directory.CreateDirectory(Path.Combine(udDir, "Profile 1"));
File.WriteAllText(Path.Combine(udDir, "Profile 1", "Preferences"), "{\"profile\":{\"name\":\"Portabel2\"}}");
Directory.CreateDirectory(Path.Combine(udDir, "System Profile"));
Directory.CreateDirectory(Path.Combine(udDir, "Guest Profile"));
var scanned = BrowserDiscovery.GetProfiles(new DetectedBrowser("s", "Scan", "x.exe", "Chromium", udDir, false));
Check(Localizer.Get("S1483"),
    scanned.Count == 2 && scanned[0].Key == "Default" && scanned[1].Key == "Profile 1");
Check(Localizer.Get("S1484"),
    !BrowserDiscovery.TryDetectProfileFolder(udDir, out _, out _));
Check(Localizer.Get("S1485"),
    new BrowserGroupEntry().UserDataDir == "" && new BrowserGroupEntry().ProfilePath == "");
Check(Localizer.Get("S1486"),
    new BrowserGroupEntry().Url == ""
    && BrowserDiscovery.KnownBrowserNames().Any(k => k.Name == "Vivaldi" && k.Engine == "Chromium")
    && BrowserDiscovery.KnownBrowserNames().Any(k => k.Name == "Opera GX" && k.Engine == "Chromium")
    && BrowserDiscovery.KnownBrowserNames().Any(k => k.Name == "Arc" && k.Engine == "Chromium")
    && BrowserDiscovery.KnownBrowserNames().Any(k => k.Name == "Zen" && k.Engine == "Firefox")
    && BrowserDiscovery.KnownBrowserNames().Any(k => k.Name == "LibreWolf" && k.Engine == "Firefox"));
string dryExe = Path.Combine(tmp, "dry.exe");
File.WriteAllText(dryExe, "fake");
var dryGroup = new BrowserProfileGroup { Id = "gd", Name = "GDry", Mode = "Simultaneous",
    Entries = new List<BrowserGroupEntry>
    {
        new() { BrowserExe = dryExe, Engine = "Chromium", ProfileKey = "Default", ProfileDisplay = "Ok" },
        new() { BrowserExe = dryExe, Engine = "Chromium", ProfileKey = "Default", ProfileDisplay = "UrlBuruk", Url = "javascript:x" },
        new() { BrowserExe = Path.Combine(tmp, "hilang.exe"), Engine = "Chromium", ProfileKey = "Default", ProfileDisplay = "ExeHilang" },
    } };
var dryCustoms = new List<CustomBrowserConfig> { new() { Name = "Dry", ExePath = dryExe, Engine = "Chromium", UserDataDir = udDir } };
var dry = BrowserGroupDryRun.CheckGroup(dryGroup, dryCustoms);
Check(Localizer.Get("S1487"),
    dry.Count == 3 && dry[0].Ok && !dry[1].Ok && !dry[2].Ok && BrowserGroupDryRun.Summarize(dry) == Localizer.Format("S1126", 1, 3));
    Directory.Delete(tmp, recursive: true);
}

Console.WriteLine("== Language tables (12) + config tolerance ==");
{
    // Check names below are LITERAL English strings (not Localizer.Get):
    // dev-harness labels stay English under every --lang. Deliberate
    // deviation from the S-label style used above.
    // (7) Full-suite runs under --lang es / --lang ar are the
    // coordinator's runs; the --lang path at the top of this file needs
    // no change (it calls SetLanguage, which now normalizes through
    // NormalizeLanguage and accepts all 12 codes).
    string startupLang = Localizer.Language;
    var tables = new (string Code, IReadOnlyDictionary<string, string> Table)[]
    {
        ("id", Strings.Id), ("en", Strings.En), ("es", Strings.Es),
        ("pt", Strings.Pt), ("fr", Strings.Fr), ("de", Strings.De),
        ("ru", Strings.Ru), ("zh", Strings.Zh), ("ja", Strings.Ja),
        ("ko", Strings.Ko), ("ar", Strings.Ar), ("tr", Strings.Tr),
    };

    // (1) All 12 dicts: same key set, 1,639 keys.
    var enKeys = new HashSet<string>(Strings.En.Keys);
    Check("lang: 12 dicts share one key set of 1658 keys",
        tables.All(t => t.Table.Count == 1658)
        && tables.All(t => new HashSet<string>(t.Table.Keys).SetEquals(enKeys)));

    // (2) {n} placeholder multisets match across all 12 dicts, per key.
    List<string> Placeholders(string s)
    {
        var found = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"\{(\d+)(?::[^}]*)?\}"))
            found.Add(m.Groups[1].Value);
        found.Sort(StringComparer.Ordinal);
        return found;
    }
    bool placeholdersMatch = true;
    foreach (var key in enKeys)
    {
        if (!Strings.En.TryGetValue(key, out var enVal)) { placeholdersMatch = false; break; }
        var expected = Placeholders(enVal);
        foreach (var (_, table) in tables)
        {
            if (!table.TryGetValue(key, out var v) || !Placeholders(v).SequenceEqual(expected)) { placeholdersMatch = false; break; }
        }
        if (!placeholdersMatch) break;
    }
    Check("lang: {n} placeholder multisets match across all 12 dicts", placeholdersMatch);

    // (3) Normalization cases.
    Check("lang: NormalizeLanguage null/empty/whitespace -> en",
        Localizer.NormalizeLanguage(null) == "en"
        && Localizer.NormalizeLanguage("") == "en"
        && Localizer.NormalizeLanguage("   ") == "en");
    Check("lang: NormalizeLanguage EN/ID case-fold, ' pt ' trims",
        Localizer.NormalizeLanguage("EN") == "en"
        && Localizer.NormalizeLanguage("ID") == "id"
        && Localizer.NormalizeLanguage(" pt ") == "pt");
    Check("lang: NormalizeLanguage xx/pt-BR/zh-CN -> en",
        Localizer.NormalizeLanguage("xx") == "en"
        && Localizer.NormalizeLanguage("pt-BR") == "en"
        && Localizer.NormalizeLanguage("zh-CN") == "en");
    Check("lang: NormalizeLanguage accepts all 12 codes verbatim",
        new[] { "id", "en", "es", "pt", "fr", "de", "ru", "zh", "ja", "ko", "ar", "tr" }
            .All(c => Localizer.NormalizeLanguage(c) == c));
    Localizer.SetLanguage("ES");
    bool setEs = Localizer.Language == "es";
    Localizer.SetLanguage("xx");
    bool setXx = Localizer.Language == "en";
    Check("lang: SetLanguage applies NormalizeLanguage (ES->es, xx->en)", setEs && setXx);

    // (5) Registry: exactly 12 codes in canonical order, unique, endonyms exact, Code upper-cased.
    var expectedLangs = new (string Code, string Endonym)[]
    {
        ("id", "Bahasa"), ("en", "English"), ("es", "Español"),
        ("pt", "Português"), ("fr", "Français"), ("de", "Deutsch"),
        ("ru", "Русский"), ("zh", "中文 (简体)"), ("ja", "日本語"),
        ("ko", "한국어"), ("ar", "العربية"), ("tr", "Türkçe"),
    };
    Check("lang: registry is exactly the 12 canonical codes in order",
        Localizer.Languages.Select(l => l.Code).SequenceEqual(expectedLangs.Select(e => e.Code)));
    Check("lang: registry codes unique, endonyms exact",
        Localizer.Languages.Select(l => l.Code).Distinct().Count() == 12
        && Localizer.Languages.Select(l => l.Endonym).SequenceEqual(expectedLangs.Select(e => e.Endonym)));
    bool codesUpper = true;
    foreach (var (code, _) in expectedLangs)
    {
        Localizer.SetLanguage(code);
        if (Localizer.Code != code.ToUpperInvariant() || Localizer.Language != code) codesUpper = false;
    }
    Check("lang: Code == code.ToUpperInvariant() for all 12 codes", codesUpper);

    // (4) Fallback: chosen -> English -> key; en never consults the id dict.
    Localizer.SetLanguage("en");
    Check("lang: en never consults the id dict",
        enKeys.Where(k => Strings.Id.TryGetValue(k, out var idV) && idV != Strings.En[k])
            .All(k => Localizer.Get(k) == Strings.En[k]));
    Localizer.SetLanguage("es");
    bool unknownEs = Localizer.Get("S9999-NO-SUCH-KEY") == "S9999-NO-SUCH-KEY";
    Localizer.SetLanguage("en");
    bool unknownEn = Localizer.Get("S9999-NO-SUCH-KEY") == "S9999-NO-SUCH-KEY";
    Check("lang: unknown key returns the key itself under es and en", unknownEs && unknownEn);
    // The generated tables share one key set, so the chosen->English leg
    // is exercised by temporarily removing one key from a dict and
    // restoring it (try/finally): without the English leg Get would
    // return the key; with an id leg it would return the Indonesian
    // value once English is removed too.
    bool fallbackLegs = false;
    Localizer.SetLanguage("es");
    if (Strings.Es is Dictionary<string, string> esDict && Strings.En is Dictionary<string, string> enDict)
    {
        string probe = "S0001";
        string esValue = esDict[probe];
        string enValue = enDict[probe];
        string idValue = Strings.Id[probe];
        try
        {
            bool removedEs = esDict.Remove(probe);
            bool chosenToEnglish = removedEs && Localizer.Get(probe) == enValue;
            bool removedEn = enDict.Remove(probe);
            bool noIdLeg = removedEn && Localizer.Get(probe) == probe && idValue != probe;
            fallbackLegs = chosenToEnglish && noIdLeg;
        }
        finally
        {
            esDict[probe] = esValue;
            enDict[probe] = enValue;
        }
        fallbackLegs = fallbackLegs && esDict.Count == 1658 && enDict.Count == 1658
            && esDict[probe] == esValue && enDict[probe] == enValue;
    }
    Check("lang: fallback chosen -> English -> key (id leg deleted)", fallbackLegs);

    // (6) Invariant keys identical in all 12 dicts.
    var invariantKeys = enKeys.Where(k => Strings.Id.TryGetValue(k, out var idV) && idV == Strings.En[k]).ToList();
    Check("lang: 259 invariant keys (id==en) identical in all 12 dicts",
        invariantKeys.Count == 259
        && invariantKeys.All(k => tables.All(t => t.Table.TryGetValue(k, out var v) && v == Strings.En[k])));
    Check("lang: S0956/S0957/S0992/S0994/S0995 identical in all 12 dicts",
        new[] { "S0956", "S0957", "S0992", "S0994", "S0995" }
            .All(k => tables.All(t => t.Table.TryGetValue(k, out var v) && v == Strings.En[k])));

    // (8) OpenMode == "New" + NormalizeOpenMode unchanged under a non-English language.
    Localizer.SetLanguage("es");
    Check("lang: OpenMode == New + NormalizeOpenMode unchanged under es",
        new BrowserProfileGroup().OpenMode == "New"
        && BrowserAction.NormalizeOpenMode(null) == "New"
        && BrowserAction.NormalizeOpenMode("focus") == "Focus"
        && BrowserAction.NormalizeOpenMode("Toggle") == "Toggle"
        && BrowserAction.NormalizeOpenMode("apa pun") == "New");
    Localizer.SetLanguage("ar");
    Check("lang: OpenMode == New + NormalizeOpenMode unchanged under ar",
        new BrowserProfileGroup().OpenMode == "New"
        && BrowserAction.NormalizeOpenMode("focus") == "Focus"
        && BrowserAction.NormalizeOpenMode(null) == "New");
    Localizer.SetLanguage(startupLang);

    // (7) The --lang startup path accepts all 12 codes via NormalizeLanguage
    // (no restriction in the argument handling; SetLanguage normalizes).
    string? langArg = null;
    int langIdx = Array.IndexOf(args, "--lang");
    if (langIdx >= 0 && langIdx + 1 < args.Length) langArg = args[langIdx + 1];
    Check("lang: --lang startup value accepted via NormalizeLanguage",
        Localizer.Language == (langArg is null ? "en" : Localizer.NormalizeLanguage(langArg)));
}

{
    // Config tolerance T1-T15 (from the config audit). Same K4-block
    // pattern as the S1237 block above: fresh GUID temp dir per case,
    // Load asserted side-effect-free (directory listing unchanged by
    // Load alone), dir deleted after.
    string[] ListFiles(string dir) => Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    (string Dir, ConfigStore Store, LoadResult Loaded, string[] Before) LoadFresh(string json)
    {
        string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new ConfigStore(dir);
        File.WriteAllText(store.MainPath, json);
        var before = ListFiles(dir);
        return (dir, store, store.Load(), before);
    }

    // T1: minimal config.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"schemaVersion":1,"settings":{"language":"es"}}""");
        Check("config T1: minimal config loads Main, es, 0 profiles, defaults",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "es"
            && loaded.Config.Profiles.Count == 0 && loaded.Config.Settings.PauseHotkey == "F9");
        Check("config T1: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T2: PascalCase variant.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"SchemaVersion":1,"Settings":{"Language":"es"},"Profiles":[]}""");
        Check("config T2: PascalCase variant loads Main, es, 0 profiles",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "es"
            && loaded.Config.Profiles.Count == 0);
        Check("config T2: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T3: {} -> en defaults.
    {
        var (dir, _, loaded, before) = LoadFresh("{}");
        Check("config T3: {} loads Main, en defaults, 0 profiles",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "en"
            && loaded.Config.Profiles.Count == 0);
        Check("config T3: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T4: {"settings":null} -> en, no crash.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"schemaVersion":1,"settings":null}""");
        Check("config T4: settings null loads Main, en, no crash",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "en");
        Check("config T4: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T5: {"settings":{"language":null}} -> Language==null in memory, no throw.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"schemaVersion":1,"settings":{"language":null}}""");
        Check("config T5: language null loads Main, Language==null, no throw",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language is null);
        Check("config T5: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T6: null profiles + language.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"schemaVersion":1,"profiles":null,"settings":{"language":"fr"}}""");
        Check("config T6: null profiles + language loads Main, fr, 0 profiles",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "fr"
            && loaded.Config.Profiles.Count == 0);
        Check("config T6: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T7: unknown fields ignored.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"schemaVersion":1,"unknownField":123,"settings":{"language":"de","bogus":true},"extra":{"x":1}}""");
        Check("config T7: unknown fields ignored, loads Main, de",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "de");
        Check("config T7: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T8: legacy trap - profiles without schemaVersion migrates, language lost.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"profiles":[],"settings":{"language":"es"}}""");
        Check("config T8: no schemaVersion + profiles loads MigratedLegacy, en, 0 profiles",
            loaded.Source == LoadSource.MigratedLegacy && loaded.Config.Settings.Language == "en"
            && loaded.Config.Profiles.Count == 0);
        Check("config T8: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T9: schemaVersion 99 -> FreshDefault.
    {
        var (dir, _, loaded, before) = LoadFresh("""{"schemaVersion":99,"settings":{"language":"es"}}""");
        Check("config T9: schemaVersion 99 loads FreshDefault, en, 1 profile",
            loaded.Source == LoadSource.FreshDefault && loaded.Config.Settings.Language == "en"
            && loaded.Config.Profiles.Count == 1);
        Check("config T9: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T10: corrupt main + valid bak1 -> Backup1 with backup values (K4 pattern).
    {
        string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-cfg-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigStore(dir);
        var cfgA = ConfigStore.CreateDefault();
        cfgA.Settings.PauseHotkey = "F10";
        cfgA.Settings.Language = "es";
        store.Save(cfgA);
        var cfgB = ConfigStore.CreateDefault();
        cfgB.Settings.PauseHotkey = "F11";
        cfgB.Settings.Language = "fr";
        store.Save(cfgB); // rotates A into .bak1
        File.WriteAllText(store.MainPath, "{ rusak total bukan json");
        var before = ListFiles(dir);
        var loaded = store.Load();
        Check("config T10: corrupt main + valid bak1 loads Backup1 with backup values",
            loaded.Source == LoadSource.Backup1 && loaded.Config.Settings.PauseHotkey == "F10"
            && loaded.Config.Settings.Language == "es");
        Check("config T10: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T11: corrupt main, no backups -> FreshDefault, corrupt bytes untouched.
    // Bytes + listing are captured BEFORE Load (LoadFresh loads eagerly,
    // so it cannot prove Load left the bytes alone).
    {
        string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new ConfigStore(dir);
        File.WriteAllText(store.MainPath, "{ rusak total bukan json");
        string corruptBytes = File.ReadAllText(store.MainPath);
        var before = ListFiles(dir);
        var loaded = store.Load();
        Check("config T11: corrupt main, no backups loads FreshDefault",
            loaded.Source == LoadSource.FreshDefault && loaded.Config.Profiles.Count == 1);
        Check("config T11: corrupt bytes untouched by Load",
            File.ReadAllText(store.MainPath) == corruptBytes && ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T12: empty / "null" / "[]" files -> FreshDefault.
    {
        var (dir, _, loaded, before) = LoadFresh("");
        Check("config T12: empty file loads FreshDefault",
            loaded.Source == LoadSource.FreshDefault);
        Check("config T12: empty file Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }
    {
        var (dir, _, loaded, before) = LoadFresh("null");
        Check("config T12: 'null' file loads FreshDefault",
            loaded.Source == LoadSource.FreshDefault);
        Check("config T12: 'null' file Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }
    {
        var (dir, _, loaded, before) = LoadFresh("[]");
        Check("config T12: '[]' file loads FreshDefault",
            loaded.Source == LoadSource.FreshDefault);
        Check("config T12: '[]' file Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T13: JsonNode merge simulation - edit only settings.language, all else survives.
    {
        string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-cfg-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigStore(dir);
        var cfg = ConfigStore.CreateDefault();
        cfg.Settings.PauseHotkey = "F10";
        cfg.Settings.Language = "en";
        cfg.Profiles[0].Name = "KeepMe";
        store.Save(cfg);
        var root = JsonNode.Parse(File.ReadAllText(store.MainPath))!.AsObject();
        root["settings"]!.AsObject()["language"] = "ja";
        File.WriteAllText(store.MainPath, root.ToJsonString());
        var before = ListFiles(dir);
        var loaded = store.Load();
        Check("config T13: JsonNode merge edits only language, rest survives",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "ja"
            && loaded.Config.Settings.PauseHotkey == "F10" && loaded.Config.Profiles[0].Name == "KeepMe");
        Check("config T13: Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T14: round-trip Save->Load preserves language, bak1 equals original
    // bytes, unknown fields dropped after Save.
    {
        string dir = Path.Combine(Path.GetTempPath(), "mkd-selfcheck-cfg-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigStore(dir);
        var cfg1 = ConfigStore.CreateDefault();
        cfg1.Settings.Language = "es";
        cfg1.Settings.PauseHotkey = "F10";
        store.Save(cfg1);
        byte[] bytes1 = File.ReadAllBytes(store.MainPath);
        var cfg2 = ConfigStore.CreateDefault();
        cfg2.Settings.Language = "fr";
        cfg2.Settings.PauseHotkey = "F11";
        store.Save(cfg2);
        byte[] bak1Bytes = File.ReadAllBytes(Path.Combine(dir, "config.json.bak1"));
        var loaded = store.Load();
        Check("config T14: round-trip Save->Load preserves language",
            loaded.Source == LoadSource.Main && loaded.Config.Settings.Language == "fr"
            && loaded.Config.Settings.PauseHotkey == "F11");
        Check("config T14: bak1 equals original bytes after second Save",
            bak1Bytes.SequenceEqual(bytes1));
        File.WriteAllText(store.MainPath, """{"schemaVersion":1,"settings":{"language":"es"},"mysteryField":42}""");
        var reloaded = store.Load();
        store.Save(reloaded.Config);
        string savedText = File.ReadAllText(store.MainPath);
        var before = ListFiles(dir);
        var reloaded2 = store.Load();
        Check("config T14: unknown fields dropped after Save, language survives",
            !savedText.Contains("mysteryField") && reloaded2.Source == LoadSource.Main
            && reloaded2.Config.Settings.Language == "es");
        Check("config T14: reload Load is side-effect-free", ListFiles(dir).SequenceEqual(before));
        Directory.Delete(dir, recursive: true);
    }

    // T15: all 12 codes load verbatim into Settings.Language.
    {
        bool all12 = true;
        foreach (var code in new[] { "id", "en", "es", "pt", "fr", "de", "ru", "zh", "ja", "ko", "ar", "tr" })
        {
            var (dir, _, loaded, before) = LoadFresh($"{{\"schemaVersion\":1,\"settings\":{{\"language\":\"{code}\"}}}}");
            all12 = all12 && loaded.Source == LoadSource.Main
                && loaded.Config.Settings.Language == code
                && ListFiles(dir).SequenceEqual(before);
            Directory.Delete(dir, recursive: true);
        }
        Check("config T15: all 12 codes load verbatim into Settings.Language", all12);
    }

    // A13: registry language handoff seam (Setup writes the wizard language
    // to the registry when no main config.json exists, so Load still returns
    // FreshDefault; the app consumes it once on FreshDefault only).
    Check("config A13: DecideSetupAction(true) is MergeIntoFile",
        LanguageHandoff.DecideSetupAction(true) == LanguageHandoff.SetupAction.MergeIntoFile);
    Check("config A13: DecideSetupAction(false) is WriteRegistry",
        LanguageHandoff.DecideSetupAction(false) == LanguageHandoff.SetupAction.WriteRegistry);
    Check("config A13: DecidePendingLanguage(true, \"es\") returns \"es\"",
        LanguageHandoff.DecidePendingLanguage(true, "es") == "es");
    Check("config A13: DecidePendingLanguage(true, whitespace) returns null",
        LanguageHandoff.DecidePendingLanguage(true, "  ") is null);
    Check("config A13: DecidePendingLanguage(true, null) returns null",
        LanguageHandoff.DecidePendingLanguage(true, null) is null);
    Check("config A13: DecidePendingLanguage(true, empty) returns null",
        LanguageHandoff.DecidePendingLanguage(true, "") is null);
    Check("config A13: DecidePendingLanguage(false, \"es\") returns null",
        LanguageHandoff.DecidePendingLanguage(false, "es") is null);
    Check("config A13: DecidePendingLanguage(false, null) returns null",
        LanguageHandoff.DecidePendingLanguage(false, null) is null);
    Check("config A13: RegistryValueName is \"Language\"",
        LanguageHandoff.RegistryValueName == "Language");
    Check("config A13: DecidePendingLanguage(true, \"xx-unknown\") passes value through unnormalized",
        LanguageHandoff.DecidePendingLanguage(true, "xx-unknown") == "xx-unknown");
}

Console.WriteLine(fails == 0 ? "=== SELF-CHECK: ALL PASS ===" : $"=== SELF-CHECK: {fails} FAIL ===");
return fails == 0 ? 0 : 1;

/// <summary>Scripted obs-websocket server: sends Hello (with auth) on
/// connect, answers Identify with Identified, answers GetVersion and
/// SetCurrentProgramScene requests by echoing their requestId, and stays
/// silent for anything else (to exercise the timeout path).</summary>
internal sealed class FakeObsTransport : IObsTransport
{
    private readonly Queue<string> _inbound = new();
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? IdentifyAuthentication { get; private set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellation)
    {
        Enqueue("""{"op":0,"d":{"obsWebSocketVersion":"5.5.2","rpcVersion":1,"authentication":{"challenge":"CH","salt":"SA"}}}""");
        return Task.CompletedTask;
    }

    public Task SendAsync(string json, CancellationToken cancellation)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        int op = root?["op"]?.GetValue<int>() ?? -1;
        var d = root?["d"] as JsonObject;
        if (op == 1)
        {
            IdentifyAuthentication = d?["authentication"]?.GetValue<string>();
            Enqueue("""{"op":2,"d":{"negotiatedRpcVersion":1}}""");
        }
        else if (op == 6)
        {
            string type = d?["requestType"]?.GetValue<string>() ?? "";
            string id = d?["requestId"]?.GetValue<string>() ?? "";
            if (type is "GetVersion" or "SetCurrentProgramScene")
            {
                string data = type == "GetVersion" ? "{\"obsVersion\":\"30.0.0\"}" : "{}";
                Enqueue("{\"op\":7,\"d\":{\"requestType\":\"" + type + "\",\"requestId\":\"" + id
                    + "\",\"requestStatus\":{\"result\":true,\"code\":100},\"responseData\":" + data + "}}");
            }
        }
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        while (true)
        {
            lock (_inbound)
            {
                if (_inbound.Count > 0) return _inbound.Dequeue();
            }
            await _signal.Task.WaitAsync(cancellation);
        }
    }

    private void Enqueue(string json)
    {
        lock (_inbound) _inbound.Enqueue(json);
        var old = Interlocked.Exchange(ref _signal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
    }

    public Task CloseAsync() => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class RecordingAction : IAction
{
    private int _count;
    public int Count => _count;
    public ActionDescriptor Descriptor => new("rec", "Recorder", "Test", "Counts executions.");

    public ValidationResult Validate(JsonObject parameters) => ValidationResult.Valid;

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
    {
        Interlocked.Increment(ref _count);
        return Task.FromResult(ActionResult.Ok(1));
    }

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}

internal sealed class FakeBeepAction : IAction
{
    public ActionDescriptor Descriptor => new("fake.beep", "Fake Beep", "Test", "Self-check only.");

    public ValidationResult Validate(JsonObject parameters)
        => parameters.TryGetPropertyValue("frequency", out var v) && v is JsonValue jv && jv.TryGetValue<int>(out _)
            ? ValidationResult.Valid
            : ValidationResult.Invalid("frequency wajib int");

    public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
        => Task.FromResult(Validate(parameters).IsValid ? ActionResult.Ok(1) : ActionResult.Fail("params invalid"));

    public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
        => ExecuteAsync(parameters, context);
}
