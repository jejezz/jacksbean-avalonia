using JackBean.Domain.Drop;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.Services.Tests;

/// <summary>`{paths}` — 여러 항목을 한 번에 받는 앱은 한 번만 띄운다.</summary>
public class MultiPathTests
{
    [Fact]
    public void ExpandArgsManySpreadsPathsAndKeepsOtherArgs()
    {
        Assert.Equal(["--x", "a", "b c", "d"], AppLauncher.ExpandArgsMany(["--x", "{paths}"], ["a", "b c", "d"]));
        Assert.Equal(["a", "a"], AppLauncher.ExpandArgsMany(["{path}", "{path}"], ["a", "b"]));   // {path} 는 첫 경로
        Assert.True(AppLauncher.AcceptsMany(new AppEntry("t", "jackbean:archive", ["{paths}"])));
        Assert.False(AppLauncher.AcceptsMany(new AppEntry("t", "x.exe", ["{path}"])));
    }

    private static async Task<(PolicyStore store, List<(string Cmd, IReadOnlyList<string> Args)> calls, DropRouter router)> Setup(TempDir t)
    {
        var store = new PolicyStore(PolicyOs.Windows, t.Path);
        await System.IO.File.WriteAllTextAsync(store.FilePath, """
            {"schema":2,"os":"windows",
             "apps":{"many":{"command":"many.exe","args":["{paths}"]},"one":{"command":"one.exe","args":["{path}"]}},
             "rules":[{"id":"zip","when":{"kind":"extension","extensions":["zip"]},"open":["many"]},
                      {"id":"txt","when":{"kind":"extension","extensions":["txt"]},"open":["one"]}]}
            """);
        var calls = new List<(string, IReadOnlyList<string>)>();
        var router = new DropRouter(store, new AppLauncher((c, a) => { calls.Add((c, a)); return Task.CompletedTask; }));
        return (store, calls, router);
    }

    private static DroppedItem Item(TempDir t, string name)
    {
        var p = Path.Combine(t.Path, name);
        System.IO.File.WriteAllText(p, "x");
        return PathClassifier.Classify(p);
    }

    [Fact]
    public async Task SeveralItemsForAMultiPathAppLaunchOnce()
    {
        using var t = new TempDir();
        var (_, calls, router) = await Setup(t);
        var items = new[] { Item(t, "a.zip"), Item(t, "b.zip"), Item(t, "c.txt"), Item(t, "d.txt") };

        var plan = await router.RouteAsync(items);

        Assert.Equal(3, calls.Count);                                     // many 1번 + one 2번
        var many = Assert.Single(calls, c => c.Cmd == "many.exe");
        Assert.Equal([items[0].Path, items[1].Path], many.Args);          // 두 zip 이 한 번에
        Assert.Equal(2, calls.Count(c => c.Cmd == "one.exe"));            // 한 항목씩만 받는 앱은 항목마다
        Assert.Equal(4, plan.Outcomes.Count);
        Assert.All(plan.Outcomes, o => Assert.IsType<Opened>(o));
        Assert.Equal(items.Select(i => i.Path), plan.Outcomes.Select(o => ((Opened)o).Item.Path));   // 순서가 그대로다
    }

    [Fact]
    public async Task OneItemForAMultiPathAppStillWorks()
    {
        using var t = new TempDir();
        var (_, calls, router) = await Setup(t);
        var item = Item(t, "a.zip");

        await router.RouteAsync([item]);

        var call = Assert.Single(calls);
        Assert.Equal([item.Path], call.Args);
    }

    [Fact]
    public async Task AChosenMultiPathAppGetsEveryItemAtOnce()
    {
        using var t = new TempDir();
        var (store, calls, router) = await Setup(t);
        var policy = await store.LoadAsync();
        var items = new[] { Item(t, "x.bin"), Item(t, "y.bin") };

        var outcomes = await router.OpenWithAsync(policy.Apps["many"], items);

        Assert.Single(calls);
        Assert.Equal(items.Select(i => i.Path), calls[0].Args);
        Assert.All(outcomes, o => Assert.IsType<Opened>(o));
    }
}
