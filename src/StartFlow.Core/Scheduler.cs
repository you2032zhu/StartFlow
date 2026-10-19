namespace StartFlow.Core;

public sealed record LaunchOutcome(RunStatus Status, string Message = "", bool Retryable = false, Task<LaunchOutcome>? Completion = null);
public interface ITargetLauncher { Task<LaunchOutcome> LaunchAsync(StartupItem item, CancellationToken cancellation); }
public sealed record ExecutionRequest(Guid Id, StartupPlan Plan, DateTimeOffset Reference);

public sealed class Scheduler(ITargetLauncher launcher, TimeProvider? clock = null) : IAsyncDisposable
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Queue<ExecutionRequest> queue = new();
    private CancellationTokenSource generation = new();
    private Task? pump;
    private ExecutionRequest? current;
    private ExecutionRequest? latest;
    private bool stopping;
    private readonly List<RunResult> results = [];
    public event Action? Changed;
    public ExecutionRequest? Current { get { lock (gate) return current; } }
    public ExecutionRequest? Latest { get { lock (gate) return latest; } }
    public ExecutionRequest[] Waiting { get { lock (gate) return queue.ToArray(); } }
    public RunResult[] Results { get { lock (gate) return results.ToArray(); } }
    public bool Enqueue(StartupPlan plan, DateTimeOffset reference, bool allowDuplicate = false)
    {
        lock (gate)
        {
            if (stopping) return false;
            if (!allowDuplicate && (current?.Plan.Id == plan.Id || queue.Any(q => q.Plan.Id == plan.Id))) return false;
            if (queue.Count >= 20) return false;
            queue.Enqueue(new(Guid.NewGuid(), ConfigJson.Clone(plan), reference));
            if (pump == null || pump.IsCompleted) pump = Task.Run(() => PumpAsync(generation.Token));
        }
        Changed?.Invoke(); return true;
    }
    private async Task PumpAsync(CancellationToken ct)
    {
        while (true)
        {
            ExecutionRequest request;
            lock (gate)
            {
                if (ct.IsCancellationRequested || queue.Count == 0) { current = null; pump = null; break; }
                latest = current = request = queue.Dequeue(); results.Clear();
                results.AddRange(request.Plan.Items.Where(i => i.Enabled).Select(i => new RunResult { RunId = request.Id, ItemId = i.Id, Plan = request.Plan.Name, Item = i.Name }));
            }
            Changed?.Invoke();
            var pending = new List<Task>();
            try
            {
                var sequentialDue = time.GetUtcNow().AddSeconds(request.Plan.InitialDelaySeconds);
                var items = request.Plan.Items.Where(i => i.Enabled).ToArray();
                if (request.Plan.Mode == ScheduleMode.Timeline)
                {
                    var confirmed = time.GetUtcNow();
                    items = items.Select((item, index) => (item, index, due: request.Reference.AddSeconds(request.Plan.InitialDelaySeconds + item.Seconds)))
                        .OrderBy(x => x.due <= confirmed ? DateTimeOffset.MinValue : x.due).ThenBy(x => x.index).Select(x => x.item).ToArray();
                }
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    var due = request.Plan.Mode == ScheduleMode.Timeline
                        ? request.Reference.AddSeconds(request.Plan.InitialDelaySeconds + item.Seconds)
                        : sequentialDue.AddSeconds(item.Seconds);
                    await Until(due, ct);
                    RunResult result; lock (gate) result = results.Single(r => r.ItemId == item.Id);
                    // First request is awaited to preserve submission order. Completion and retries are independent.
                    var first = await Attempt(item, result, ct);
                    pending.Add(FinishItem(item, result, first, ct));
                    sequentialDue = time.GetUtcNow();
                }
                await Task.WhenAll(pending);
            }
            catch (OperationCanceledException) { try { await Task.WhenAll(pending); } catch (OperationCanceledException) { } }
            finally
            {
                lock (gate) foreach (var r in results.Where(r => r.Status is RunStatus.Waiting or RunStatus.RetryWaiting or RunStatus.Launching or RunStatus.Running)) { r.Status = RunStatus.Cancelled; r.Notify(); }
                Changed?.Invoke();
            }
        }
        Changed?.Invoke();
    }
    private async Task Until(DateTimeOffset due, CancellationToken ct)
    { var delay = due - time.GetUtcNow(); if (delay > TimeSpan.Zero) await Task.Delay(delay, time, ct); ct.ThrowIfCancellationRequested(); }
    private async Task<LaunchOutcome> Attempt(StartupItem item, RunResult result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { result.Status = RunStatus.Launching; result.StartedAt ??= time.GetUtcNow(); result.Attempts++; }
        Changed?.Invoke();
        LaunchOutcome outcome;
        try { outcome = await launcher.LaunchAsync(item, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { outcome = new(RunStatus.Failed, ex.Message, true); }
        ct.ThrowIfCancellationRequested();
        Update(result, outcome); return outcome;
    }
    private void Update(RunResult result, LaunchOutcome outcome)
    { lock (gate) { result.Status = outcome.Status; result.Message = outcome.Message; result.Notify(); } Changed?.Invoke(); }
    private async Task FinishItem(StartupItem item, RunResult result, LaunchOutcome outcome, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                if (outcome.Completion != null) { outcome = await outcome.Completion.WaitAsync(ct); Update(result, outcome); }
                ct.ThrowIfCancellationRequested();
                if (outcome.Status != RunStatus.Failed || !outcome.Retryable || result.Attempts > item.RetryCount) return;
                Update(result, new(RunStatus.RetryWaiting, outcome.Message));
                await Task.Delay(TimeSpan.FromSeconds(item.RetryIntervalSeconds), time, ct);
                outcome = await Attempt(item, result, ct);
            }
        }
        catch (OperationCanceledException) { Update(result, new(RunStatus.Cancelled)); }
        catch (Exception ex) { Update(result, new(RunStatus.Failed, ex.Message)); }
    }
    public async Task StopAsync()
    {
        Task? active; CancellationTokenSource old;
        lock (gate) { stopping = true; old = generation; old.Cancel(); queue.Clear(); active = pump; }
        Changed?.Invoke();
        if (active != null) await active;
        lock (gate) { if (ReferenceEquals(generation, old)) generation = new(); stopping = false; }
        old.Dispose();
    }
    public async ValueTask DisposeAsync() { await StopAsync(); generation.Dispose(); }
}
