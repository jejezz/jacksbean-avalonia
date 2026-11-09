using JackBean.Domain.Dock;
using JackBean.Domain.Window;

namespace JackBean.Domain.Tests;

public class DockPositionTests
{
    [Fact]
    public void ValidityRequiresFiniteValuesAndPositiveScale()
    {
        Assert.True(new DockPosition(5432, 300, 1.75).IsValid);
        Assert.False(new DockPosition(1, 2, 0).IsValid);
        Assert.False(new DockPosition(double.NaN, 2, 1).IsValid);
        Assert.False(new DockPosition(1, double.PositiveInfinity, 1).IsValid);
    }

    [Fact]
    public void FitPullsInsideWorkArea()
    {
        var work = new RectD(0, 0, 1000, 800);
        var r = new DockPosition(5000, 5000, 1).Fit(new SizeD(100, 80), work, 1);
        Assert.Equal(1000, r.Right); Assert.Equal(720, r.Top);
        var l = new DockPosition(-50, -50, 1).Fit(new SizeD(100, 80), work, 1);
        Assert.Equal(0, l.Left); Assert.Equal(0, l.Top);
        var tiny = new DockPosition(10, 10, 1).Fit(new SizeD(100, 80), new RectD(0, 0, 50, 50), 1);
        Assert.Equal(0, tiny.Left);
    }
}

