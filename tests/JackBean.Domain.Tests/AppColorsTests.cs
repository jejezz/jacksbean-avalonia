using JackBean.Domain.Theme;

namespace JackBean.Domain.Tests;

public class AppColorsTests
{
    [Fact]
    public void 색_구성은_6종이고_주색이_모두_다르다()
    {
        var all = Enum.GetValues<ColorPalette>();
        Assert.Equal(6, all.Length);
        Assert.Equal(6, all.Select(p => p.DarkPrimary()).Distinct().Count());
        Assert.Equal(6, all.Select(p => p.LightPrimary()).Distinct().Count());
    }

    [Fact]
    public void 새턴_다크는_원본_색이다()
    {
        var c = AppColors.For(ColorPalette.Saturn, dark: true);
        Assert.Equal(0xFF0A0E14u, c.Bg);
        Assert.Equal(0xFF151D27u, c.Surface);
        Assert.Equal(0xFF4C9DFFu, c.Primary);
        Assert.Equal(0xFFFFFFFFu, c.OnPrimary); // 새턴은 다크에서도 흰 글자
    }

    [Fact]
    public void 새턴_외_다크_색_구성은_밝은_주색이라_어두운_글자다()
    {
        foreach (var p in Enum.GetValues<ColorPalette>().Where(p => p is not (ColorPalette.Saturn or ColorPalette.HighContrast)))
            Assert.Equal(0xFF0A0E14u, AppColors.For(p, dark: true).OnPrimary);
    }

    [Fact]
    public void 라이트는_흰_글자_주색이다()
    {
        foreach (var p in Enum.GetValues<ColorPalette>().Where(p => p != ColorPalette.HighContrast))
            Assert.Equal(0xFFFFFFFFu, AppColors.For(p, dark: false).OnPrimary);
    }

    [Fact]
    public void 고대비는_순흑_순백이다()
    {
        var dark = AppColors.For(ColorPalette.HighContrast, dark: true);
        Assert.Equal(0xFF000000u, dark.Bg);
        Assert.Equal(0xFFFFFFFFu, dark.TextHi);
        var light = AppColors.For(ColorPalette.HighContrast, dark: false);
        Assert.Equal(0xFFFFFFFFu, light.Bg);
        Assert.Equal(0xFF000000u, light.TextHi);
    }
}
