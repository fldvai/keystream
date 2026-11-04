using Keystream.Core.Devices;
using Keystream.Core.Localization;
using static Keystream.Windows.Interception.NativeInterception;

namespace Keystream.Windows.Interception;

/// <summary>
/// A short-lived identification session on its OWN interception context
/// (the pattern proven by the Gate A instrument and the Phase 2 smoke):
/// every stroke is forwarded unchanged so both keyboards keep working,
/// while the DeviceId of each key-DOWN is reported to the caller, who
/// feeds it to the pure DeckIdentification state machine.
///
/// Lifecycle mirrors ModeAInterceptionService: all native calls happen on
/// the session's worker thread; the context is destroyed exactly once when
/// the loop exits; Stop() only sets a flag and joins. The main service
/// must be STOPPED while a session runs (one active consumer at a time is
/// the discipline the host enforces).
/// </summary>
public sealed class IdentificationSession : IDisposable
{
    private Thread? _worker;
    private volatile bool _stopRequested;

    public event Action<DeviceId>? KeyDownObserved;

    /// <summary>Dipicu bila sesi gagal total (context nol / native
    /// melempar, mis. interception.dll hilang). Dulu kedua kegagalan
    /// ini ditelan diam-diam dan wizard menggantung di LANGKAH 1
    /// selamanya (§45).</summary>
    public event Action<string>? IdentificationFailed;

    public void Start()
    {
        if (_worker is { IsAlive: true })
            throw new InvalidOperationException(Localizer.Get("S1217"));
        _stopRequested = false;
        _worker = new Thread(WorkerMain) { IsBackground = true, Name = "MKD-Identify" };
        _worker.Start();
    }

    public void Stop()
    {
        _stopRequested = true;
        _worker?.Join(TimeSpan.FromSeconds(3));
        _worker = null;
    }

    private void WorkerMain()
    {
        IntPtr context = IntPtr.Zero;
        var hwCache = new Dictionary<int, string>();
        try
        {
            context = interception_create_context();
            if (context == IntPtr.Zero)
            {
                var state = DriverStatus.ProbeKernelState();
                if (state == DriverState.PendingRestart)
                {
                    IdentificationFailed?.Invoke(Localizer.Get("S1923"));
                }
                else
                {
                    IdentificationFailed?.Invoke(Localizer.Get("S1218"));
                }
                return;
            }

            interception_set_filter(context, KeyboardPredicate, FILTER_KEY_ALL);
            while (!_stopRequested)
            {
                int device = interception_wait_with_timeout(context, 200);
                if (device == 0) continue;
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

                KeyDownObserved?.Invoke(DeviceId.FromInterceptionHardwareId(hwId));
            }
        }
        catch (Exception ex)
        {
            // Teardown errors must not take the host down - but a
            // session that died (mis. DllNotFound) must SAY so instead
            // of hanging the wizard. Suppressed only when Stop() already
            // requested the exit (teardown race, not a real failure).
            if (!_stopRequested)
                IdentificationFailed?.Invoke(Localizer.Format("S1219", ex.Message));
        }
        finally
        {
            if (context != IntPtr.Zero) interception_destroy_context(context);
        }
    }

    public void Dispose() => Stop();
}
