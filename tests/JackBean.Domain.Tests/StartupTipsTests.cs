using JackBean.Domain.Settings;
using JackBean.Domain.Tips;

namespace JackBean.Domain.Tests;

public class StartupTipsTests
{
    [Fact]
    public void BeforeFirstDrop_AlwaysTheDropTip_WithoutAdvancing()
    {
        var s = new AppSettings();
        var (tip, next) = StartupTips.OnStartup(s, autostart: false)!.Value;
        Assert.Equal(TipId.DropOnBean, tip);
        Assert.Equal(s, next);
    }

    [Fact]
    public void AfterFirstDrop_RotatesAndWraps()
    {
        var s = new AppSettings { DockDroppedOnce = true };
        var seen = new List<TipId>();
        for (var i = 0; i < StartupTips.Rotation.Count + 1; i++)
        {
            var (tip, next) = StartupTips.OnStartup(s, autostart: false)!.Value;
            seen.Add(tip);
            s = next;
        }
        Assert.Equal(StartupTips.Rotation.Concat([StartupTips.Rotation[0]]), seen);
        Assert.DoesNotContain(TipId.DropOnBean, seen);
    }

    [Fact]
    public void Autostart_OrTurnedOff_ShowsNothing()
    {
        Assert.Null(StartupTips.OnStartup(new AppSettings(), autostart: true));
        Assert.Null(StartupTips.OnStartup(new AppSettings { StartupTips = false }, autostart: false));
    }

    [Fact]
    public void MenuShowsTipEvenWhenTurnedOff()
    {
        var (tip, _) = StartupTips.Next(new AppSettings { StartupTips = false, DockDroppedOnce = true });
        Assert.Equal(StartupTips.Rotation[0], tip);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public void BadStoredIndex_StaysInRange(int stored)
    {
        var (tip, next) = StartupTips.Next(new AppSettings { DockDroppedOnce = true, StartupTipsNext = stored });
        Assert.Contains(tip, StartupTips.Rotation);
        Assert.InRange(next.StartupTipsNext, 0, StartupTips.Rotation.Count - 1);
    }
}
