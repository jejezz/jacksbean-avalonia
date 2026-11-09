using JackBean.Data;
using JackBean.Domain.Dock;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;
using JackBean.Domain.Window;

namespace JackBean.Data.Tests;

public class JsonSettingsRepositoryTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("jackbean_settings_").FullName;
    string File_ => Path.Combine(_dir, "settings.json");
    JsonSettingsRepository Repo() => new(File_);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void 파일이_없으면_기본값이다() => Assert.Equal(new AppSettings(), Repo().Load());

    [Fact]
    public void 저장한_값을_그대로_읽는다()
    {
        var saved = new AppSettings
        {
            Theme = ThemeMode.Dark, Palette = ColorPalette.Rose, TextScale = 1.15, Locale = AppLocale.English, UiFontFamily = "Menlo",
            Dock = new DockPosition(5432, 300, 1.75),
            Window = new WindowPlacement { X = 10, Y = 20, Width = 960, Height = 640, Scale = 2, Maximized = false },
        };
        Repo().Save(saved);
        Assert.Equal(saved, Repo().Load());
    }

    [Fact]
    public void 저장_키는_규약의_공통_이름이다()
    {
        Repo().Save(new AppSettings { Theme = ThemeMode.Light, Palette = ColorPalette.HighContrast, Locale = AppLocale.Korean });
        var json = File.ReadAllText(File_);
        Assert.Contains("\"theme_mode\": \"light\"", json);
        Assert.Contains("\"accent_palette\": \"highContrast\"", json);
        Assert.Contains("\"app_locale\": \"ko\"", json);
        Assert.Contains("\"text_scale\": 1", json);
    }

    [Fact]
    public void 깨진_파일은_broken으로_옮기고_기본값으로_시작한다()
    {
        File.WriteAllText(File_, "{ not json");
        Assert.Equal(new AppSettings(), Repo().Load());
        Assert.True(File.Exists(File_ + ".broken"));
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void 모르는_키는_무시하고_모르는_값은_기본값으로_읽는다()
    {
        File.WriteAllText(File_, """{"theme_mode":"purple","accent_palette":"rose","app_locale":"fr","text_scale":99,"future_key":{"a":1}}""");
        var s = Repo().Load();
        Assert.Equal(ThemeMode.System, s.Theme);          // 모르는 값 → 기본
        Assert.Equal(ColorPalette.Rose, s.Palette);
        Assert.Null(s.Locale);                            // 모르는 언어 → 시스템
        Assert.Equal(1.6, s.TextScale);                   // 범위 밖 → 범위 안으로
    }

    [Fact]
    public void 틀린_독_위치는_버린다()
    {
        File.WriteAllText(File_, """{"dock_position":{"right":100,"top":50,"scale":0}}""");
        Assert.Null(Repo().Load().Dock);
    }

    [Fact]
    public void 저장하면_임시_파일이_남지_않는다()
    {
        Repo().Save(new AppSettings());
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void 기본_파일_위치는_앱_이름_폴더_아래의_settings_json이다()
    {
        var path = JsonSettingsRepository.DefaultFile("JacksBean");
        Assert.EndsWith(Path.Combine("JacksBean", "settings.json"), path);
    }
}
