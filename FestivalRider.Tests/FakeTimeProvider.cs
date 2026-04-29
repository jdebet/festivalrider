namespace FestivalRider.Tests;

public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly List<FakeTimer> _timers = new();

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _now.UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan duration)
    {
        var target = _now + duration;
        // Step in increments to fire timers in order
        while (_timers.Any(t => !t.Disposed) && _now < target)
        {
            var nextDue = _timers.Where(t => !t.Disposed).Min(t => t.NextFireUtc);
            if (nextDue > target) break;
            _now = nextDue;
            foreach (var timer in _timers.Where(t => !t.Disposed && t.NextFireUtc <= _now).ToList())
            {
                timer.Fire();
                if (timer.Period == Timeout.InfiniteTimeSpan)
                    timer.Disposed = true;
                else
                    timer.NextFireUtc = _now + timer.Period;
            }
        }
        _now = target;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new FakeTimer(this, callback, state, period)
        {
            NextFireUtc = _now + dueTime
        };
        _timers.Add(t);
        return t;
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        public TimeSpan Period { get; private set; }
        public DateTimeOffset NextFireUtc { get; set; }
        public bool Disposed { get; set; }

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            Period = period;
        }

        public void Fire() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            NextFireUtc = _owner.GetUtcNow() + dueTime;
            Period = period;
            return true;
        }

        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
