using JackBean.Domain.Dock;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;

namespace JackBean.Domain.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DefaultsFollowTheConventions()
    {
        var s = new AppSettings();
        Assert.Equal(ThemeMode.System, s.Theme);
        Assert.Equal(ColorPalette.Saturn, s.Palette);
        Assert.Equal(1.0, s.TextScale);
        Assert.Null(s.Locale);
        Assert.Null(s.UiFontFamily);
        Assert.Null(s.Dock);
    }

    [Theory]
    [InlineData(0.5, 0.8)]
    [InlineData(1.02, 1.0)]
    [InlineData(1.17, 1.15)]
    [InlineData(9.0, 1.6)]
    [InlineData(double.NaN, 1.0)]
    public void TextScaleSnapsToFivePercentWithinRange(double input, double expected) =>
        Assert.Equal(expected, TextScales.Snap(input));

    [Fact]
    public void NormalizedFixesScaleAndDropsInvalidDockPosition()
    {
        var s = new AppSettings { TextScale = 9, Dock = new DockPosition(1, 2, 0) }.Normalized();
        Assert.Equal(1.6, s.TextScale);
        Assert.Null(s.Dock);
        var ok = new AppSettings { Dock = new DockPosition(100, 200, 1.5) }.Normalized();
        Assert.Equal(new DockPosition(100, 200, 1.5), ok.Dock);
    }

    [Theory]
    [InlineData(null, "ko", AppLocale.Korean)]
    [InlineData(null, "en", AppLocale.English)]
    [InlineData(null, "ja", AppLocale.English)]
    [InlineData(AppLocale.Korean, "en", AppLocale.Korean)]
    [InlineData(AppLocale.English, "ko", AppLocale.English)]
    public void SystemLanguageIsKoreanOnlyWhenOsIsKorean(AppLocale? setting, string os, AppLocale expected) =>
        Assert.Equal(expected, Localization.Resolve(setting, os));
}
