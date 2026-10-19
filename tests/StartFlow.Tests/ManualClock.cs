namespace StartFlow.Tests;

// A deterministic TimeProvider: advancing time fires due timers without real waiting or polling.
public sealed class ManualClock : TimeProvider
{
    private readonly object gate = new();
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<ManualTimer> timers = [];
    public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
    public override long GetTimestamp() => GetUtcNow().UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public int TimerCount { get { lock (gate) return timers.Count(t => !t.Disposed && t.Due != null); } }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate) { var timer = new ManualTimer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer; }
    }
    public void Advance(double seconds)
    {
        DateTimeOffset target; lock (gate) target = now.AddSeconds(seconds);
        while (true)
        {
            ManualTimer? timer;
            lock (gate)
            {
                timer = timers.Where(t => !t.Disposed && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (timer == null) { now = target; return; }
                now = timer.Due!.Value; timer.Due = timer.Period > TimeSpan.Zero ? now + timer.Period : null;
            }
            timer.Callback(timer.State);
        }
    }
    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback; public object? State { get; } = state;
        public DateTimeOffset? Due; public TimeSpan Period; public bool Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) { lock (clock.gate) { if (Disposed) return false; Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.now + dueTime; Period = period; return true; } }
        public void Dispose() { lock (clock.gate) { Disposed = true; clock.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
