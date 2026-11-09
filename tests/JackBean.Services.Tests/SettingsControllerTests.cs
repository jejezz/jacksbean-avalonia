using JackBean.Data;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;
using JackBean.Services;

namespace JackBean.Services.Tests;

public class SettingsControllerTests
{
    sealed class RecordingRepository(AppSettings? stored = null) : ISettingsRepository
    {
        public List<AppSettings> Saves { get; } = [];
        AppSettings _stored = stored ?? new AppSettings();

        public AppSettings Load() => _stored;

        public void Save(AppSettings settings)
        {
            lock (Saves) Saves.Add(settings);
            _stored = settings;
        }

        public int Count { get { lock (Saves) return Saves.Count; } }
    }

    [Fact]
    public void 시작할_때_저장소에서_읽는다()
    {
        var repo = new RecordingRepository(new AppSettings { Theme = ThemeMode.Dark });
        using var c = new SettingsController(repo);
        Assert.Equal(ThemeMode.Dark, c.Value.Theme);
    }

    [Fact]
    public void 바꾸면_상태가_바로_바뀌고_Flush하면_바로_저장된다()
    {
        var repo = new RecordingRepository();
        using var c = new SettingsController(repo, TimeSpan.FromMinutes(1));
        c.Update(s => s with { Theme = ThemeMode.Light });
        Assert.Equal(ThemeMode.Light, c.Value.Theme);
        Assert.Equal(0, repo.Count); // 아직 안 썼다
        c.Flush();
        Assert.Equal(ThemeMode.Light, Assert.Single(repo.Saves).Theme);
    }

    [Fact]
    public void 값이_바뀌면_Changed가_불리고_같은_값이면_불리지_않는다()
    {
        using var c = new SettingsController(new RecordingRepository(), TimeSpan.FromMinutes(1));
        var seen = new List<ThemeMode>();
        c.Changed += s => seen.Add(s.Theme);
        c.Update(s => s with { Theme = ThemeMode.Dark });
        c.Update(s => s); // 그대로
        Assert.Equal([ThemeMode.Dark], seen);
    }

    [Fact]
    public void 같은_값이면_다시_저장하지_않는다()
    {
        var repo = new RecordingRepository();
        using var c = new SettingsController(repo, TimeSpan.FromMinutes(1));
        c.Flush(); // 읽어 온 값 그대로: 저장할 것이 없다
        Assert.Equal(0, repo.Count);
        c.Update(s => s with { Theme = ThemeMode.Dark });
        c.Flush();
        c.Flush();
        Assert.Equal(1, repo.Count);
    }

    [Fact]
    public async Task 연속으로_바꿔도_잠시_뒤_마지막_값_한_번만_저장한다()
    {
        var repo = new RecordingRepository();
        using var c = new SettingsController(repo, TimeSpan.FromMilliseconds(100));
        for (var i = 1; i <= 20; i++) c.Update(x => x with { UiFontFamily = $"font{i}" });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (repo.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        await Task.Delay(300);

        Assert.Equal(1, repo.Count);
        Assert.Equal("font20", repo.Saves.Single().UiFontFamily);
    }

    [Fact]
    public void Dispose하면_남은_변경을_저장한다()
    {
        var repo = new RecordingRepository();
        var c = new SettingsController(repo, TimeSpan.FromMinutes(1));
        c.Update(s => s with { Palette = ColorPalette.Teal });
        c.Dispose();
        Assert.Equal(ColorPalette.Teal, Assert.Single(repo.Saves).Palette);
    }
}
