using JackBean.Services.DragWatch;

namespace JackBean.Services.Tests;

public class DragDetectorTests
{
    private static DragDetector Moved()
    {
        var d = new DragDetector();
        d.ButtonDown(100, 100);
        d.Move(100 + DragDetector.Threshold + 1, 100);
        return d;
    }

    [Fact]
    public void DragCursorAfterMoveStartsOnce_AndButtonUpEnds()
    {
        var d = Moved();
        Assert.True(d.Watching);
        Assert.Equal(DragSignal.Started, d.Cursor(dragCursor: true));
        Assert.Equal(DragSignal.None, d.Cursor(dragCursor: true));   // 한 번만
        Assert.False(d.Watching);
        Assert.Equal(DragSignal.Ended, d.ButtonUp());
        Assert.False(d.IsStarted);
    }

    [Fact]
    public void PlainCursor_IsNotADrag()   // 창 끌기·글자 선택: 커서가 화살표·I빔으로 남는다
    {
        var d = Moved();
        Assert.Equal(DragSignal.None, d.Cursor(dragCursor: false));
        Assert.Equal(DragSignal.None, d.ButtonUp());
    }

    [Fact]
    public void SmallMove_IsNotWatched()
    {
        var d = new DragDetector();
        d.ButtonDown(100, 100);
        d.Move(100 + DragDetector.Threshold, 100 - DragDetector.Threshold);
        Assert.False(d.Watching);
        Assert.Equal(DragSignal.None, d.Cursor(dragCursor: true));
    }

    [Fact]
    public void MoveWithoutButton_IsIgnored()
    {
        var d = new DragDetector();
        d.Move(500, 500);
        Assert.Equal(DragSignal.None, d.Cursor(dragCursor: true));
        Assert.Equal(DragSignal.None, d.ButtonUp());
    }

    [Fact]
    public void NewPress_ResetsPreviousState()
    {
        var d = Moved();
        d.Cursor(dragCursor: true);
        d.ButtonDown(0, 0);   // 떼는 것을 놓쳐도 다음 누름에서 처음부터
        Assert.False(d.IsStarted);
        Assert.False(d.Watching);
    }

    [Fact]
    public void UnsupportedOs_ReportsSo()
    {
        if (OperatingSystem.IsWindows()) return;
        using var w = DragWatchers.Create();
        Assert.False(w.IsSupported);
    }
}
