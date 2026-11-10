using JackBean.Services;

namespace JackBean.Services.Tests;

public class DockExpansionTests
{
    sealed class FakeTime : TimeProvider
    {
        readonly List<(Action<object?> cb, object? state, DateTimeOffset due, FakeTimer t)> _timers = [];
        DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var t = new FakeTimer(); _timers.Add((s => callback(s), state, _now + dueTime, t)); return t; }
        public void Advance(TimeSpan d)
        {
            _now += d;
            foreach (var x in _timers.ToList())
                if (!x.t.Disposed && x.due <= _now) { _timers.Remove(x); x.cb(x.state); }
        }
        sealed class FakeTimer : ITimer
        {
            public bool Disposed;
            public bool Change(TimeSpan a, TimeSpan b) => true;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public void ExpandsImmediatelyShrinksAfterDelayAndCancelsOnReenter()
    {
        var time = new FakeTime();
        var log = new List<bool>();
        using var e = new DockExpansion(log.Add, time: time);
        e.Expand(); e.Expand();
        Assert.Equal([true], log);
        e.ShrinkSoon();
        time.Advance(TimeSpan.FromMilliseconds(300));
        e.Expand();                                   // 되돌아오면 취소
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal([true], log);
        e.ShrinkSoon();
        time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal([true], log);
        time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Equal([true, false], log);
        e.ShrinkSoon(); time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal([true, false], log);             // 이미 줄었으면 다시 부르지 않는다
    }
}

