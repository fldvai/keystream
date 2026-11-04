using System.Threading.Channels;
using Keystream.Core.Localization;
using Keystream.Core.Actions;
using Keystream.Core.Config;

namespace Keystream.Core.Engine;

/// <summary>One unit of work for the dispatcher: run a binding's steps
/// for one trigger firing. <see cref="WheelDelta"/> (§156) carries the
/// coalesced wheel delta for ScrollDial drain jobs, in WHEEL_DELTA units
/// (120 per notch, signed); it is 0 for every other kind, including the
/// ScrollDial DOWN/UP capture events themselves, which never enqueue.</summary>
public sealed record DispatchJob(BindingConfig Binding, TriggerKind Trigger, int WheelDelta = 0);

public sealed record StepExecution(string KeyId, TriggerKind Trigger, string ActionId, ActionResult Result);

/// <summary>
/// FIFO action dispatcher (contract K3's execution half). A single worker
/// drains a bounded channel, so steps run strictly in press order and never
/// on the interception/driver thread - the audited code executed actions
/// synchronously inside the low-level hook callback, where one slow action
/// stalled every keystroke on the machine.
///
/// Failure honesty: an unregistered action id, a failed Validate, and an
/// exception inside an action all surface as a StepExecution with
/// Success=false - nothing is swallowed. If the queue is full, Enqueue
/// returns false and increments <see cref="DroppedJobs"/> rather than
/// blocking the caller or silently dropping the press.
/// </summary>
public sealed class ActionDispatcher : IDisposable
{
    private readonly IReadOnlyDictionary<string, IAction> _actions;
    private readonly Channel<DispatchJob> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private long _droppedJobs;

    public event Action<StepExecution>? StepExecuted;

    /// <summary>Dipicu sebelum satu langkah dieksekusi:
    /// (KeyId, ActionId). UI memakainya untuk umpan balik "sedang
    /// berjalan" pada langkah lambat (mis. konek OBS ~6 detik, §45).</summary>
    public event Action<string, string>? StepStarted;

    /// <summary>Dipicu setelah SEMUA langkah satu job selesai:
    /// (KeyId, langkah gagal, total langkah). UI memakainya untuk
    /// agregat multi-langkah - dulu UI hanya menampilkan hasil
    /// langkah terakhir, sehingga gagal-lalu-OK tampak "OK" (§45).</summary>
    public event Action<string, int, int>? JobFinished;

    public long DroppedJobs => Interlocked.Read(ref _droppedJobs);

    public ActionDispatcher(IEnumerable<IAction> actions, int capacity = 128)
    {
        _actions = actions.ToDictionary(a => a.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        _channel = Channel.CreateBounded<DispatchJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // TryWrite still fails fast when full
            SingleReader = true,
            SingleWriter = false,
        });
        _worker = Task.Run(RunWorkerAsync);
    }

    public bool Enqueue(DispatchJob job)
    {
        if (_channel.Writer.TryWrite(job)) return true;
        Interlocked.Increment(ref _droppedJobs);
        return false;
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                int failed = 0, total = 0;
                string keyId = "";
                try
                {
                    // Binding/Steps bisa null dari config korup ("steps": null eksplisit):
                    // tanpa guard, NRE di sini mematikan worker dan channel tak terkuras
                    // lagi. Core tidak bisa menulis CrashLog (milik Keystream.App,
                    // dependensi terbalik); langkah buruk dilaporkan lewat
                    // StepExecuted/JobFinished yang sudah ada.
                    if (job is null || job.Binding is null)
                        continue;
                    var binding = job.Binding;
                    keyId = binding.KeyId;
                    var steps = binding.Steps;
                    if (steps is null)
                    {
                        total++;
                        failed++;
                        StepExecuted?.Invoke(new StepExecution(binding.KeyId, job.Trigger, "",
                            ActionResult.Fail(Localizer.Format("S1050", ""))));
                    }
                    else
                    {
                        foreach (var step in steps)
                        {
                            total++;
                            try
                            {
                                // step/ActionId null: akses step.ActionId di bawah (StepStarted,
                                // TryGetValue) melempar di luar try ExecuteStepAsync - laporkan
                                // gagal, lanjut ke langkah berikutnya.
                                if (step is null || step.ActionId is null)
                                {
                                    failed++;
                                    StepExecuted?.Invoke(new StepExecution(binding.KeyId, job.Trigger, "",
                                        ActionResult.Fail(Localizer.Format("S1050", ""))));
                                    continue;
                                }
                                StepStarted?.Invoke(binding.KeyId, step.ActionId);
                                var result = await ExecuteStepAsync(step, job.Trigger, job.WheelDelta).ConfigureAwait(false);
                                if (!result.Success) failed++;
                                StepExecuted?.Invoke(new StepExecution(binding.KeyId, job.Trigger, step.ActionId, result));
                                if (step.DelayAfterMs > 0)
                                    await Task.Delay(step.DelayAfterMs, _cts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                // Termasuk lemparan handler StepStarted/StepExecuted: worker harus
                                // tetap hidup dan antrean terus terkuras; langkah ini gagal.
                                failed++;
                                StepExecuted?.Invoke(new StepExecution(binding.KeyId, job.Trigger, step?.ActionId ?? "",
                                    ActionResult.Fail(Localizer.Format("S1052", step?.ActionId ?? "", ex.GetType().Name, ex.Message))));
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Jaring pengaman terakhir (mis. getter Steps melempar): satu job buruk
                    // tidak boleh mematikan worker; JobFinished di bawah tetap jalan.
                }
                try { JobFinished?.Invoke(keyId, failed, total); }
                catch (OperationCanceledException) { throw; }
                catch { /* lemparan handler JobFinished tidak boleh membunuh worker */ }
            }
        }
        catch (OperationCanceledException) { /* dispose path */ }
    }

    private async Task<ActionResult> ExecuteStepAsync(ActionStep step, TriggerKind firedKind, int wheelDelta)
    {
        // ActionId null: TryGetValue(null) melempar ArgumentNullException di luar try di bawah.
        if (step is null || step.ActionId is null)
            return ActionResult.Fail(Localizer.Format("S1050", step?.ActionId ?? ""));

        if (!_actions.TryGetValue(step.ActionId, out var action))
            return ActionResult.Fail(Localizer.Format("S1050", step.ActionId));

        // Validate berjalan DI DALAM try: validasi atas param korup (mis.
        // profileKeys berisi angka) melempar, dan lemparan itu tidak boleh
        // membunuh worker - dulu Validate di luar try sehingga satu tile
        // korup mematikan SEMUA tile berikutnya tanpa jejak (§45 SIM-05).
        try
        {
            var validation = action.Validate(step.Params);
            if (!validation.IsValid)
                return ActionResult.Fail(Localizer.Format("S1051", string.Join("; ", validation.Errors)));

            return await action.ExecuteAsync(step.Params,
                new ActionContext { Cancellation = _cts.Token, FiredKind = firedKind, WheelDelta = wheelDelta }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ActionResult.Fail(Localizer.Format("S1052", step.ActionId, ex.GetType().Name, ex.Message));
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { /* worker already faulted/cancelled */ }
        _cts.Dispose();
    }
}
