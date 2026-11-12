using JackBean.Domain.Drop;
using JackBean.Domain.Recent;
using JackBean.Services.Recent;

namespace JackBean.Services.Tests;

public class RecentStoreTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public async Task EmptyWithoutFileAndRemovingNothingCreatesNoFile()
    {
        using var t = new TempDir();
        var store = new RecentStore(t.Path);
        Assert.Empty(await store.ListAsync());
        await store.ClearAsync();
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task SavedEntriesSurviveARestart()
    {
        using var t = new TempDir();
        var store = new RecentStore(t.Path, ignoreCase: true);
        await store.AddAsync([new RecentEntry([@"C:\한글 폴더"], DropKind.Folder, "default", T)]);
        await store.AddAsync([new RecentEntry([@"C:\a.png", @"C:\b.png"], DropKind.File, "image-viewer", T), new RecentEntry([@"C:\x"], null, "zip", T)]);

        var again = await new RecentStore(t.Path, ignoreCase: true).ListAsync();
        Assert.Equal(["image-viewer", "zip", "default"], again.Select(e => e.AppId));   // 한 번에 더한 것은 그 순서대로 맨 위에
        Assert.Equal([@"C:\a.png", @"C:\b.png"], again[0].Paths);
        Assert.Null(again[1].Kind);
        Assert.Equal(DropKind.Folder, again[2].Kind);
        Assert.Equal(T, again[2].At);
        Assert.Contains("한글 폴더", await File.ReadAllTextAsync(store.FilePath));   // 경로를 \u 로 적지 않는다
    }

    [Fact]
    public async Task RemoveAndClear()
    {
        using var t = new TempDir();
        var store = new RecentStore(t.Path, ignoreCase: true);
        var a = new RecentEntry([@"C:\a"], DropKind.File, "x", T);
        var b = new RecentEntry([@"C:\b"], DropKind.File, "x", T);
        await store.AddAsync([a, b]);
        var changes = 0;
        store.Changed += () => changes++;
        await store.RemoveAsync(new RecentEntry([@"c:\A"], DropKind.File, "x", T));
        Assert.Equal([@"C:\b"], (await new RecentStore(t.Path).ListAsync()).Single().Paths);
        await store.ClearAsync();
        Assert.Empty(await new RecentStore(t.Path).ListAsync());
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task BrokenFileIsMovedAsideAndBadEntriesSkipped()
    {
        using var t = new TempDir();
        var store = new RecentStore(t.Path);
        await File.WriteAllTextAsync(store.FilePath, "{ not json");
        Assert.Empty(await store.ListAsync());
        Assert.True(File.Exists(store.FilePath + ".broken"));

        await File.WriteAllTextAsync(store.FilePath, """
            { "schema": 1, "entries": [
              { "paths": [], "app": "x", "at": "2026-11-12T09:00:00+09:00" },
              { "paths": ["C:\\ok"], "app": "", "at": "2026-11-12T09:00:00+09:00" },
              { "paths": ["C:\\ok"], "app": "x" },
              { "paths": ["C:\\ok"], "kind": "weird", "app": "x", "at": "2026-11-12T09:00:00+09:00" }
            ] }
            """);
        var list = await new RecentStore(t.Path).ListAsync();
        Assert.Null(Assert.Single(list).Kind);
    }
}
