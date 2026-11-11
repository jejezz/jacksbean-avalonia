using JackBean.Services.Notifications;

namespace JackBean.Services.Tests;

public class NotificationTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void ErrorsStayLongerThanInfo()
    {
        Assert.Equal(TimeSpan.FromSeconds(4), NotificationGate.DurationOf(NotificationKind.Info));
        Assert.True(NotificationGate.DurationOf(NotificationKind.Error) > NotificationGate.DurationOf(NotificationKind.Info));
        Assert.True(NotificationGate.DurationOf(NotificationKind.Tip) > NotificationGate.DurationOf(NotificationKind.Info));   // 팁은 읽을 시간이 더 필요하다
    }

    [Fact]
    public void CopyTextIncludesDetailWhenPresent()
    {
        Assert.Equal("실패", new Notification(NotificationKind.Error, "실패").CopyText);
        Assert.Equal("실패\nIOException: x", new Notification(NotificationKind.Error, "실패", "IOException: x").CopyText);
        Assert.Equal("실패", new Notification(NotificationKind.Error, "실패", "  ").CopyText);
    }

    [Fact]
    public void SuppressesSameNotificationWithinWindowOnly()
    {
        var time = new FakeTime();
        var gate = new NotificationGate(time);
        var a = new Notification(NotificationKind.Error, "A");
        Assert.True(gate.Accept(a));
        time.Now += TimeSpan.FromSeconds(1);
        Assert.False(gate.Accept(a));                                            // 곧바로 또 오면 거른다
        Assert.True(gate.Accept(new Notification(NotificationKind.Error, "B")));  // 다른 메시지는 보인다
        time.Now += TimeSpan.FromSeconds(1);
        Assert.True(gate.Accept(a));                                             // 사이에 다른 것이 있었으면 보인다
        time.Now += NotificationGate.DuplicateWindow;
        Assert.True(gate.Accept(a));                                             // 시간이 지나면 다시 보인다
        Assert.True(gate.Accept(new Notification(NotificationKind.Info, "A")));   // 종류가 다르면 보인다
    }
}
