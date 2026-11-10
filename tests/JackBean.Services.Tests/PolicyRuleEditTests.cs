using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.Services.Tests;

public class PolicyRuleEditTests
{
    private static PolicyStore Store(TempDir t) => new(PolicyOs.Windows, t.Path);
    private static async Task<JsonObject> ReadJson(PolicyStore s) => JsonNode.Parse(await File.ReadAllTextAsync(s.FilePath))!.AsObject();
    private static JsonObject Ext(params string[] values) => new() { ["kind"] = "extension", ["values"] = new JsonArray(values.Select(v => (JsonNode?)v).ToArray()) };

    /// <summary>기본 정책(explorer·default·terminal)에 앱 a, b 를 더한 저장소.</summary>
    private static async Task<PolicyStore> WithApps(TempDir t)
    {
        var s = Store(t);
        await s.LoadAsync();
        Assert.True((await s.UpsertAppAsync(new AppUpsert("a", "a.exe", ["{path}"]))).Ok);
        Assert.True((await s.UpsertAppAsync(new AppUpsert("b", "b.exe", ["{path}"]))).Ok);
        return s;
    }

    [Fact]
    public async Task OneAppWritesOpenOnlyAndManyWriteChoices()
    {
        using var t = new TempDir();
        var s = await WithApps(t);

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("img", Ext("png", "jpg"), ["a"]))).Ok);
        var one = (await ReadJson(s))["rules"]!.AsArray().OfType<JsonObject>().First(r => (string?)r["id"] == "img");
        Assert.Equal("a", (string?)one["open"]);
        Assert.False(one.ContainsKey("choices"));

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("img", Ext("png", "jpg"), ["b", "a"]))).Ok);
        var many = (await ReadJson(s))["rules"]!.AsArray().OfType<JsonObject>().First(r => (string?)r["id"] == "img");
        Assert.Null(many["open"]);   // Flutter 판은 이것을 "물어보기"로 읽는다
        Assert.Equal(["b", "a"], many["choices"]!.AsArray().Select(n => (string?)n));

        var doc = await s.LoadAsync();
        Assert.Empty(doc.Warnings);
        Assert.Equal(["b", "a"], doc.Rules.First(r => r.Id == "img").Choices);
    }

    [Fact]
    public async Task SavedRulesAreOrderedSpecificFirstAndFileFallbackLast()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        Assert.True((await s.UpsertRuleAsync(new RuleEdit("img", Ext("png"), ["a"]))).Ok);
        Assert.True((await s.UpsertRuleAsync(new RuleEdit("pdf", Ext("pdf"), ["b"]))).Ok);

        var ids = (await s.LoadAsync()).Rules.Select(r => r.Id).ToList();
        // 확장자 규칙들이 맨 위, 기본 정책의 drives/folders 가 그 아래, 그 밖의 파일(files)이 맨 아래.
        string[] expected = ["img", "pdf", "drives", "folders", "files"];
        Assert.Equal(expected.Order(), ids.Order());
        Assert.True(ids.IndexOf("img") < ids.IndexOf("drives"));
        Assert.True(ids.IndexOf("pdf") < ids.IndexOf("folders"));
        Assert.Equal("files", ids[^1]);
    }

    [Fact]
    public async Task EditingKeepsUnknownKeysAndOrderStaysStable()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("one", Ext("x1"), ["a"]));
        await s.UpsertRuleAsync(new RuleEdit("two", Ext("x2"), ["a"]));
        var json = await ReadJson(s);
        json["rules"]!.AsArray().OfType<JsonObject>().First(r => (string?)r["id"] == "one")["note"] = "내 메모";
        await File.WriteAllTextAsync(s.FilePath, json.ToJsonString());
        var before = (await s.LoadAsync()).Rules.Select(r => r.Id).ToList();

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("one", Ext("x1", "x1b"), ["b"]))).Ok);

        var after = await ReadJson(s);
        Assert.Equal("내 메모", (string?)after["rules"]!.AsArray().OfType<JsonObject>().First(r => (string?)r["id"] == "one")["note"]);
        Assert.Equal(before, (await s.LoadAsync()).Rules.Select(r => r.Id).ToList());   // 같은 자리끼리는 원래 순서
    }

    [Fact]
    public async Task RefusesOverlappingExtensionUnknownAppAndTooManyApps()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("img", Ext("png"), ["a"]));

        var overlap = await s.UpsertRuleAsync(new RuleEdit("other", Ext("png", "gif"), ["a"]));
        Assert.False(overlap.Ok);
        Assert.Contains(overlap.Problems!, p => p is { Kind: RuleProblemKind.ExtensionTaken, Detail: "png", Other: "img" });

        var ghost = await s.UpsertRuleAsync(new RuleEdit("g", Ext("gif"), ["ghost"]));
        Assert.Contains(ghost.Problems!, p => p.Kind == RuleProblemKind.UnknownApp);

        var many = await s.UpsertRuleAsync(new RuleEdit("m", Ext("gif"), Enumerable.Repeat("a", PolicyLimits.MaxAppsPerRule + 1).ToList()));
        Assert.Contains(many.Problems!, p => p.Kind == RuleProblemKind.TooManyApps);

        Assert.DoesNotContain("other", (await s.LoadAsync()).Rules.Select(r => r.Id));
    }

    [Fact]
    public async Task RuleLimitBlocksNewRulesAtTheFile()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        var json = await ReadJson(s);
        var rules = json["rules"]!.AsArray();
        for (var i = rules.Count; i < PolicyLimits.MaxRules; i++)
            rules.Add(new JsonObject { ["id"] = $"r{i}", ["when"] = Ext($"x{i}"), ["open"] = "a" });
        await File.WriteAllTextAsync(s.FilePath, json.ToJsonString());
        Assert.Equal(PolicyLimits.MaxRules, (await s.LoadAsync()).Rules.Count);

        var result = await s.UpsertRuleAsync(new RuleEdit("one-more", Ext("zzz"), ["a"]));
        Assert.False(result.Ok);
        Assert.Contains(result.Problems!, p => p.Kind == RuleProblemKind.RuleLimit);
        Assert.True((await s.UpsertRuleAsync(new RuleEdit("r10", Ext("x10", "extra"), ["b"]))).Ok);   // 있는 규칙을 고치는 것은 된다
    }

    [Fact]
    public async Task EmptyExtensionListIsRejectedAsBadCondition()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        var result = await s.UpsertRuleAsync(new RuleEdit("none", Ext(), ["a"]));
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task RemoveRuleKeepsTheRest()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("img", Ext("png"), ["a"]));
        Assert.True((await s.RemoveRuleAsync("img")).Ok);
        var doc = await s.LoadAsync();
        Assert.DoesNotContain("img", doc.Rules.Select(r => r.Id));
        Assert.Contains("explorer", doc.Apps.Keys);
        Assert.False((await s.RemoveRuleAsync("img")).Ok);
    }

    [Fact]
    public async Task RemovingAnAppOnlyTakesItOutOfRuleLists()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("multi", Ext("png"), ["a", "b"]));
        await s.UpsertRuleAsync(new RuleEdit("solo", Ext("pdf"), ["a"]));

        Assert.False((await s.RemoveAppAsync("a")).Ok);   // 거절 모드

        Assert.True((await s.RemoveAppAsync("a", ReferencedRules.MakeAsk)).Ok);
        var doc = await s.LoadAsync();
        Assert.DoesNotContain("a", doc.Apps.Keys);
        Assert.Equal("b", doc.Rules.First(r => r.Id == "multi").Open);        // 남은 앱 하나 → 바로 연다
        Assert.Empty(doc.Rules.First(r => r.Id == "solo").Choices);           // 마지막 앱이었다 → 물어보기
        Assert.Empty(doc.Warnings);
    }
}

public class DropWithChoicesTests
{
    private static async Task<(PolicyStore store, DropRouter router)> Setup(TempDir t)
    {
        var store = new PolicyStore(PolicyOs.Windows, t.Path);
        await store.LoadAsync();
        foreach (var id in new[] { "a", "b", "c" })
            await store.UpsertAppAsync(new AppUpsert(id, id + ".exe", ["{path}"]));
        var launched = new List<string>();
        var router = new DropRouter(store, new AppLauncher((cmd, args) => { launched.Add(cmd); return Task.CompletedTask; }));
        return (store, router);
    }

    private static JsonObject Ext(params string[] values) => new() { ["kind"] = "extension", ["values"] = new JsonArray(values.Select(v => (JsonNode?)v).ToArray()) };

    [Fact]
    public async Task TwoAppsAskAmongThoseOnly()
    {
        using var t = new TempDir();
        var (store, router) = await Setup(t);
        await store.UpsertRuleAsync(new RuleEdit("img", Ext("png"), ["b", "c"]));
        var png = Path.Combine(t.Path, "x.png");
        await File.WriteAllTextAsync(png, "x");

        var plan = await router.RouteAsync([PathClassifier.Classify(png)]);
        var ask = Assert.Single(plan.AskOutcomes);
        Assert.Equal(["b", "c"], ask.Candidates!.Select(a => a.Id));
        Assert.Equal(["b", "c"], plan.ChoiceApps().Select(a => a.Id));
    }

    [Fact]
    public async Task ChoiceAppsIsTheIntersectionAcrossItemsAndFallsBackToAll()
    {
        using var t = new TempDir();
        var (store, router) = await Setup(t);
        await store.UpsertRuleAsync(new RuleEdit("img", Ext("png"), ["a", "b"]));
        await store.UpsertRuleAsync(new RuleEdit("mus", Ext("mp3"), ["b", "c"]));
        await store.UpsertRuleAsync(new RuleEdit("vid", Ext("mp4"), ["c", "a"]));
        string Make(string name) { var p = Path.Combine(t.Path, name); File.WriteAllText(p, "x"); return p; }

        var both = await router.RouteAsync([PathClassifier.Classify(Make("x.png")), PathClassifier.Classify(Make("x.mp3"))]);
        Assert.Equal(["b"], both.ChoiceApps().Select(a => a.Id));   // 둘 다에 있는 앱만

        var none = await router.RouteAsync([PathClassifier.Classify(Make("y.png")), PathClassifier.Classify(Make("y.mp3")), PathClassifier.Classify(Make("y.mp4"))]);
        string[] everyApp = ["a", "b", "c", "explorer", "default", "terminal"];
        Assert.Equal(everyApp.Order(), none.ChoiceApps().Select(a => a.Id).Order());   // 교집합이 비면 등록된 앱 전부
    }

    [Fact]
    public async Task OneAppOpensWithoutAsking()
    {
        using var t = new TempDir();
        var (store, router) = await Setup(t);
        await store.UpsertRuleAsync(new RuleEdit("pdf", Ext("pdf"), ["c"]));
        var pdf = Path.Combine(t.Path, "x.pdf");
        await File.WriteAllTextAsync(pdf, "x");

        var plan = await router.RouteAsync([PathClassifier.Classify(pdf)]);
        Assert.Empty(plan.AskOutcomes);
        Assert.Equal("c", Assert.Single(plan.Outcomes.OfType<Opened>()).App.Id);
    }
}
