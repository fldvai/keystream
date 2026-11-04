using System.Diagnostics;
using System.Text.Json.Nodes;
using Keystream.Core.Actions;
using Keystream.Core.Config;
using Keystream.Core.Devices;
using Keystream.Core.Engine;
using Keystream.Core.Input;
using Keystream.Windows.Interception;
using static Keystream.Windows.Interception.NativeInterception;

namespace Keystream.Smoke;

/// <summary>
/// Phase 2 end-to-end smoke harness (console, no UI). Proves the whole
/// stack on real hardware before any WPF exists:
///   Interception driver → ModeAInterceptionService → DeckEngine /
///   TriggerEngine → ActionDispatcher → a console action.
/// Identification reuses the Gate A pattern (its own short-lived context,
/// everything forwarded) feeding the pure DeckIdentification machine; the
/// service then runs with the identified InterceptionHardwareId.
/// Bindings under test (in-memory config):
///   deck 'A' (scan 0x1E)  Tap       -> prints "[ACTION] A TAP"
///   deck 'A'              LongPress -> prints "[ACTION] A LONG-PRESS"
///   deck 'B' (scan 0x30)  Tap       -> prints "[ACTION] B TAP"
/// Main keyboard must stay 100% normal throughout.
/// </summary>
internal static class Program
{
    private static volatile bool _quit;

    private static int Main()
    {
        Console.WriteLine("=== Keystream — Phase 2 smoke (service + engine + dispatcher) ===");
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows only.");
            return 1;
        }

        DeviceId? deck = IdentifyDeck();
        if (deck is null) return 1;

        var consoleAction = new ConsoleAction();
        using var dispatcher = new ActionDispatcher(new IAction[] { consoleAction });
        var engine = new DeckEngine(dispatcher);
        dispatcher.StepExecuted += ex =>
            Console.WriteLine($"  [dispatcher] {ex.KeyId}/{ex.Trigger}: {ex.ActionId} -> {(ex.Result.Success ? "OK" : "GAGAL: " + ex.Result.Error)}");

        var config = ConfigStore.CreateDefault();
        var testPage = new PageConfig();
        config.Profiles[0].Pages.Add(testPage);
        testPage.Bindings.Add(new BindingConfig
        {
            KeyId = "A", ScanCode = 0x1E, VirtualKey = 0x41, Label = "A",
            Trigger = TriggerKind.Tap,
            Steps = { new ActionStep { ActionId = "console.log", Params = new JsonObject { ["message"] = "A TAP" } } },
        });
        testPage.Bindings.Add(new BindingConfig
        {
            KeyId = "A", ScanCode = 0x1E, VirtualKey = 0x41, Label = "A",
            Trigger = TriggerKind.LongPress,
            Steps = { new ActionStep { ActionId = "console.log", Params = new JsonObject { ["message"] = "A LONG-PRESS" } } },
        });
        testPage.Bindings.Add(new BindingConfig
        {
            KeyId = "B", ScanCode = 0x30, VirtualKey = 0x42, Label = "B",
            Trigger = TriggerKind.Tap,
            Steps = { new ActionStep { ActionId = "console.log", Params = new JsonObject { ["message"] = "B TAP" } } },
        });
        engine.LoadPage(config);

        using var service = new ModeAInterceptionService();
        service.DeckKeyEvent += engine.ProcessEvent;
        try
        {
            service.Start(deck.Value);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("[FATAL] " + ex.Message);
            return 2;
        }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _quit = true; };

        Console.WriteLine();
        Console.WriteLine("=== SMOKE AKTIF ===");
        Console.WriteLine("Di keyboard DECK (fokus boleh di mana saja, mis. Notepad):");
        Console.WriteLine("  - tap 'A'          -> [ACTION] A TAP (di UP)");
        Console.WriteLine("  - tahan 'A' >=400ms -> [ACTION] A LONG-PRESS, lepasnya sepi");
        Console.WriteLine("  - tap 'B'          -> [ACTION] B TAP langsung di UP");
        Console.WriteLine("  - tombol deck lain -> dimakan, tanpa action");
        Console.WriteLine("Keyboard UTAMA harus tetap normal 100%. Klik console, Ctrl+C = selesai.");
        Console.WriteLine();

        while (!_quit)
        {
            engine.Tick(Stopwatch.GetTimestamp());
            Thread.Sleep(25);
        }

        service.Stop();
        var health = service.Health;
        Console.WriteLine();
        Console.WriteLine("┌─ SMOKE RESULT ───────────────────────────────────────────");
        Console.WriteLine($"│ Service state : {health.State}  (error: {health.LastError ?? "-"})");
        Console.WriteLine($"│ Strokes diterima: {health.EventsReceived}   Deck dimakan: {health.EventsEaten}");
        Console.WriteLine($"│ Job terjatuh (antrean penuh): {dispatcher.DroppedJobs}");
        Console.WriteLine("└───────────────────────────────────────────────────────────");
        return 0;
    }

    /// <summary>Gate A-pattern identification on a short-lived context:
    /// every stroke is forwarded (both keyboards keep working) while the
    /// pure DeckIdentification machine consumes key-down device ids.</summary>
    private static DeviceId? IdentifyDeck()
    {
        IntPtr context = interception_create_context();
        if (context == IntPtr.Zero)
        {
            Console.WriteLine("[FATAL] Driver Interception belum terinstal / belum reboot.");
            return null;
        }

        var machine = new DeckIdentification();
        var hwCache = new Dictionary<int, string>();
        try
        {
            interception_set_filter(context, KeyboardPredicate, FILTER_KEY_ALL);
            Console.WriteLine(machine.Stage switch
            {
                IdentificationStage.AwaitingDeck => "LANGKAH 1: Tekan satu tombol di keyboard KEDUA (deck)...",
                _ => "",
            });

            while (machine.Stage != IdentificationStage.Completed)
            {
                int device = interception_wait_with_timeout(context, 250);
                if (device == 0)
                {
                    if (_quit) return null;
                    continue;
                }
                if (interception_is_keyboard(device) == 0) continue;
                var stroke = default(InterceptionKeyStroke);
                if (interception_receive(context, device, ref stroke, 1) <= 0) continue;
                interception_send(context, device, ref stroke, 1); // forward everything

                bool isDown = (stroke.state & KEY_STATE_UP) == 0;
                if (!isDown) continue;

                if (!hwCache.TryGetValue(device, out string? hwId) || hwId.Length == 0)
                {
                    hwId = GetHardwareId(context, device);
                    if (hwId.Length > 0) hwCache[device] = hwId;
                }
                if (hwId.Length == 0) continue;

                var stageBefore = machine.Stage;
                var stage = machine.Observe(DeviceId.FromInterceptionHardwareId(hwId));
                if (machine.LastMessage is not null && stage != stageBefore || machine.Stage != stageBefore)
                    Console.WriteLine("  " + machine.LastMessage);
                if (stage == IdentificationStage.AwaitingMain && stageBefore == IdentificationStage.AwaitingDeck)
                    Console.WriteLine("LANGKAH 2: Tekan satu tombol di keyboard UTAMA...");
                if (stage == IdentificationStage.AwaitingDeckConfirm && stageBefore == IdentificationStage.AwaitingMain)
                    Console.WriteLine("LANGKAH 3: Tekan satu tombol lagi di keyboard KEDUA...");
            }
        }
        finally
        {
            interception_destroy_context(context);
        }

        Console.WriteLine($"Deck teridentifikasi: {machine.DeckCandidate!.Value.Value}");
        return machine.DeckCandidate;
    }

    private sealed class ConsoleAction : IAction
    {
        public ActionDescriptor Descriptor => new("console.log", "Console Log", "Smoke", "Prints a message.");

        public ValidationResult Validate(JsonObject parameters)
            => parameters.TryGetPropertyValue("message", out var m) && m is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0
                ? ValidationResult.Valid
                : ValidationResult.Invalid("message wajib string non-kosong");

        public Task<ActionResult> ExecuteAsync(JsonObject parameters, ActionContext context)
        {
            string message = parameters["message"]?.GetValue<string>() ?? "?";
            Console.WriteLine($"[ACTION] {message}");
            return Task.FromResult(ActionResult.Ok(1));
        }

        public Task<ActionResult> TestAsync(JsonObject parameters, ActionContext context)
            => ExecuteAsync(parameters, context);
    }
}
