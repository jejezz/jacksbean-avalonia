using JackBean.Domain.Window;

namespace JackBean.Domain.Tests;

public class WindowPlacementTests
{
    // 두 모니터: 왼쪽 주 모니터 1920×1040(작업 영역, 배율 1), 오른쪽 2560×1400(배율 2). 위치·작업 영역은 물리 픽셀.
    static readonly DisplayArea Left = new(new RectD(0, 0, 1920, 1040), 1.0, Primary: true);
    static readonly DisplayArea Right = new(new RectD(1920, 0, 2560, 1400), 2.0);
    static readonly DisplayArea[] Both = [Left, Right];

    static WindowPlacement Saved(double x, double y, double w, double h, double scale = 1.0, bool max = false) =>
        new() { X = x, Y = y, Width = w, Height = h, Scale = scale, Maximized = max };

    [Fact]
    public void 가운데에_놓는다()
    {
        var t = PlacementMath.CenterOn(Left, 1200, 720);
        Assert.Equal(new PlacementTarget(360, 160, 1200, 720), t);
    }

    [Fact]
    public void 배율이_큰_모니터에서도_논리_크기_기준으로_가운데에_놓는다()
    {
        // 1200×720 논리 = 2400×1440 픽셀은 이 모니터보다 커서 줄어든다: 작업 영역 2560×1400 → 논리 1280×700
        var t = PlacementMath.CenterOn(Right, 1200, 720);
        Assert.Equal(1200, t.Width);
        Assert.Equal(700, t.Height);
        Assert.Equal(1920 + (2560 - 2400) / 2.0, t.X);
        Assert.Equal(0, t.Y);
    }

    [Fact]
    public void 작은_모니터에서는_작업_영역에_맞춰_줄인다()
    {
        var small = new DisplayArea(new RectD(0, 0, 1000, 600), 1.0, Primary: true);
        var t = PlacementMath.CenterOn(small, 1200, 720);
        Assert.Equal(new PlacementTarget(0, 0, 1000, 600), t);
    }

    [Fact]
    public void 오른쪽_모니터에_있던_창은_오른쪽_모니터에_그대로_돌아온다()
    {
        var r = PlacementMath.Restore(Saved(2200, 100, 1000, 600, scale: 2.0), Both);
        Assert.Equal(new PlacementTarget(2200, 100, 1000, 600), r);
    }

    [Fact]
    public void 경계에_걸려_있던_창은_더_많이_걸친_모니터_안쪽으로_당겨_넣는다()
    {
        // x 1800~2600 중 왼쪽 모니터에 120, 오른쪽 모니터에 680 걸쳤다 → 오른쪽 모니터로, 그 왼쪽 가장자리까지 당겨진다.
        var r = PlacementMath.Restore(Saved(1800, 100, 800, 600), Both);
        Assert.NotNull(r);
        Assert.Equal(1920, r!.Value.X);
        Assert.Equal(800, r.Value.Width); // 논리 크기는 그대로 (배율 2: 1600픽셀)
    }

    [Fact]
    public void 왼쪽_모니터_오른쪽_끝에_걸려_있던_창은_왼쪽_모니터_안으로_당겨_넣는다()
    {
        // x 1300~2100: 왼쪽에 620, 오른쪽에 180 걸쳤다 → 왼쪽 모니터 안으로 (오른쪽 끝이 1920에 맞는다).
        var r = PlacementMath.Restore(Saved(1300, 100, 800, 600), Both);
        Assert.NotNull(r);
        Assert.Equal(1920 - 800, r!.Value.X);
    }

    [Fact]
    public void 두_모니터에_걸친_창은_더_많이_걸친_모니터로_간다()
    {
        var r = PlacementMath.Restore(Saved(1500, 100, 800, 500), Both); // 왼쪽 420, 오른쪽 380
        Assert.NotNull(r);
        Assert.True(r!.Value.X + r.Value.Width <= 1920);
    }

    [Fact]
    public void 떼어낸_모니터에_있던_창은_null()
    {
        Assert.Null(PlacementMath.Restore(Saved(5000, 100, 800, 600), [Left]));
    }

    [Fact]
    public void 겨우_닿은_정도로는_보인다고_치지_않는다()
    {
        Assert.Null(PlacementMath.Restore(Saved(1900, 100, 800, 600), [Left])); // 가로로 20만 걸침
    }

    [Fact]
    public void 모니터보다_큰_창은_맞춰_줄인다()
    {
        var r = PlacementMath.Restore(Saved(0, 0, 3000, 2000), [Left]);
        Assert.Equal(new PlacementTarget(0, 0, 1920, 1040), r);
    }

    [Fact]
    public void 손상된_저장값은_null()
    {
        Assert.Null(PlacementMath.Restore(new WindowPlacement(), Both));
        Assert.Null(PlacementMath.Restore(Saved(0, 0, -5, 600), Both));
        Assert.Null(PlacementMath.Restore(Saved(0, 0, 800, 600, scale: 0), Both));
        Assert.Null(PlacementMath.Restore(Saved(double.NaN, 0, 800, 600), Both));
    }

    [Fact]
    public void 주_모니터를_우선하고_없으면_첫_모니터_모니터가_없으면_null()
    {
        Assert.Same(Left, PlacementMath.PrimaryOf([Right, Left]));
        Assert.Same(Right, PlacementMath.PrimaryOf([Right, new DisplayArea(new RectD(0, 0, 1, 1), 1)]));
        Assert.Null(PlacementMath.PrimaryOf([]));
    }

    [Fact]
    public void 필드가_빠진_값은_읽히지만_쓸_수_없는_값으로_취급한다()
    {
        Assert.False(new WindowPlacement { X = 1, Y = 2 }.IsValid);
        Assert.True(Saved(1, 2, 3, 4).IsValid);
    }

    [Fact]
    public void 최대화_상태는_일반_크기를_덮어쓰지_않고_max만_켠다()
    {
        var before = Saved(100, 100, 900, 600);
        var after = PlacementMath.Updated(before, 0, 0, 1920, 1040, 1.0, maximized: true);
        Assert.Equal(before with { Maximized = true }, after);
    }

    [Fact]
    public void 최대화를_풀면_새_일반_크기를_저장하고_max를_끈다()
    {
        var before = Saved(100, 100, 900, 600, max: true);
        var after = PlacementMath.Updated(before, 200, 150, 1000, 700, 1.0, maximized: false);
        Assert.Equal(Saved(200, 150, 1000, 700), after);
    }

    [Fact]
    public void 위치를_모르면_이전_위치를_유지하고_이전도_없으면_저장하지_않는다()
    {
        var before = Saved(100, 100, 900, 600);
        Assert.Equal(Saved(100, 100, 1000, 700), PlacementMath.Updated(before, null, null, 1000, 700, 1.0, false));
        Assert.Null(PlacementMath.Updated(null, null, null, 1000, 700, 1.0, false));
        Assert.Null(PlacementMath.Updated(null, 0, 0, 1000, 700, 1.0, maximized: true));
    }
}
