using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.Services.Tests;

public class PolicyRuleEditTests
{
    private static PolicyStore Store(TempDir t) => new(PolicyOs.Windows, t.Path);
    private static async Task<JsonObject> ReadJson(PolicyStore s) => JsonNode.Parse(await File.ReadAllTextAsync(s.FilePath))!.AsObject();
    private static RuleCondition Ext(params string[] values) => RuleCondition.Extension(values)!;
    private static JsonObject RuleJson(JsonObject json, string id) => json["rules"]!.AsArray().OfType<JsonObject>().First(r => (string?)r["id"] == id);

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
    public async Task SavesOpenAsListOfAnySizeInOrder()
    {
        using var t = new TempDir();
        var s = await WithApps(t);

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("img", Ext("png", "jpg"), ["a"]))).Ok);
        Assert.Equal(["a"], RuleJson(await ReadJson(s), "img")["open"]!.AsArray().Select(n => (string?)n));

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("img", Ext("png", "jpg"), ["b", "a"]))).Ok);
        Assert.Equal(["b", "a"], RuleJson(await ReadJson(s), "img")["open"]!.AsArray().Select(n => (string?)n));

        var none = await s.UpsertRuleAsync(new RuleEdit("img", Ext("png", "jpg"), []));   // 앱이 하나도 없는 규칙은 저장하지 못한다
        Assert.False(none.Ok);
        Assert.Contains(none.Problems!, p => p.Kind == RuleProblemKind.NoApps);
        var doc = await s.LoadAsync();
        Assert.Empty(doc.Warnings);
        Assert.Equal(["b", "a"], doc.Rules.First(r => r.Id == "img").Choices);   // 그대로
    }

    [Fact]
    public async Task SavedRulesAreOrderedExtensionsFirstAndFileFallbackLast()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        Assert.True((await s.UpsertRuleAsync(new RuleEdit("img", Ext("png"), ["a"]))).Ok);
        Assert.True((await s.UpsertRuleAsync(new RuleEdit("pdf", Ext("pdf"), ["b"]))).Ok);

        var ids = (await s.LoadAsync()).Rules.Select(r => r.Id).ToList();
        Assert.True(ids.IndexOf("img") < ids.IndexOf("drives"));
        Assert.True(ids.IndexOf("pdf") < ids.IndexOf("folders"));
        Assert.Equal("files", ids[^1]);
        Assert.Equal(5, ids.Count);
    }

    [Fact]
    public async Task EditingKeepsUnknownKeysAndOrderStaysStable()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("one", Ext("x1"), ["a"]));
        await s.UpsertRuleAsync(new RuleEdit("two", Ext("x2"), ["a"]));
        var json = await ReadJson(s);
        RuleJson(json, "one")["note"] = "내 메모";
        await File.WriteAllTextAsync(s.FilePath, json.ToJsonString());
        var before = (await s.LoadAsync()).Rules.Select(r => r.Id).ToList();

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("one", Ext("x1", "x1b"), ["b"]))).Ok);

        Assert.Equal("내 메모", (string?)RuleJson(await ReadJson(s), "one")["note"]);
        Assert.Equal(before, (await s.LoadAsync()).Rules.Select(r => r.Id).ToList());   // 같은 자리끼리는 원래 순서
    }

    [Fact]
    public async Task RefusesOverlappingExtensionUnknownAppTooManyAppsAndSecondKindRule()
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

        var second = await s.UpsertRuleAsync(new RuleEdit("folders2", RuleCondition.Folder, ["a"]));
        Assert.Contains(second.Problems!, p => p is { Kind: RuleProblemKind.KindTaken, Other: "folders" });

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
            rules.Add(new JsonObject { ["id"] = $"r{i}", ["when"] = Ext($"x{i}").ToJson(), ["open"] = new JsonArray("a") });
        await File.WriteAllTextAsync(s.FilePath, json.ToJsonString());
        Assert.Equal(PolicyLimits.MaxRules, (await s.LoadAsync()).Rules.Count);

        var result = await s.UpsertRuleAsync(new RuleEdit("one-more", Ext("zzz"), ["a"]));
        Assert.False(result.Ok);
        Assert.Contains(result.Problems!, p => p.Kind == RuleProblemKind.RuleLimit);
        Assert.True((await s.UpsertRuleAsync(new RuleEdit("r10", Ext("x10", "extra"), ["b"]))).Ok);   // 있는 규칙을 고치는 것은 된다
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
        Assert.Equal(["b"], doc.Rules.First(r => r.Id == "multi").Choices);   // 남은 앱 하나 → 바로 연다
        Assert.Empty(doc.Rules.First(r => r.Id == "solo").Choices);           // 마지막 앱이었다 → 물어보기
        Assert.Empty(doc.Warnings);
    }

    // --- 항상 이렇게 열기 ---

    [Fact]
    public async Task RememberFolderAndDriveReuseTheExistingRule()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        var result = await s.RememberAsync([new(SuggestionKind.Folder), new(SuggestionKind.Drive)], "a");
        Assert.True(result.Ok, result.Error);
        var doc = await s.LoadAsync();
        Assert.Equal(3, doc.Rules.Count);                                  // 새 규칙이 쌓이지 않는다
        Assert.Equal(["a"], doc.Rules.First(r => r.Id == "folders").Choices);
        Assert.Equal(["a"], doc.Rules.First(r => r.Id == "drives").Choices);
    }

    [Fact]
    public async Task RememberFolderCreatesTheRuleWhenMissing()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.RemoveRuleAsync("folders");
        Assert.True((await s.RememberAsync([new(SuggestionKind.Folder)], "b")).Ok);
        var doc = await s.LoadAsync();
        Assert.Equal(["b"], doc.Rules.Single(r => r.When.Kind == RuleKind.Folder).Choices);
        Assert.Empty(doc.Warnings);
    }

    [Fact]
    public async Task RememberExtensionMovesItOutOfItsGroupRule()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("img", Ext("png", "jpg"), ["a", "b"]));

        Assert.True((await s.RememberAsync([new(SuggestionKind.Extension, "png")], "b")).Ok);

        var doc = await s.LoadAsync();
        Assert.Equal(["jpg"], doc.Rules.First(r => r.Id == "img").When.Extensions);               // 묶음에서 빠지고
        Assert.Equal(["b"], doc.Rules.First(r => r.Id == "auto-ext-png").Choices);                // 그 확장자만의 규칙이 생긴다
        Assert.Empty(RuleCheckOverlaps(doc));                                                      // 겹치지 않는다
        var ids = doc.Rules.Select(r => r.Id).ToList();
        Assert.True(ids.IndexOf("auto-ext-png") < ids.IndexOf("drives"));                           // 구체적인 규칙이 위
    }

    [Fact]
    public async Task RememberExtensionDeletesTheGroupRuleWhenItBecomesEmptyAndUpdatesOwnRule()
    {
        using var t = new TempDir();
        var s = await WithApps(t);
        await s.UpsertRuleAsync(new RuleEdit("only-png", Ext("png"), ["a", "b"]));

        Assert.True((await s.RememberAsync([new(SuggestionKind.Extension, ".PNG")], "a")).Ok);
        var doc = await s.LoadAsync();
        Assert.DoesNotContain("only-png", doc.Rules.Select(r => r.Id));                            // 비어서 지운다
        Assert.Equal(["a"], doc.Rules.Single(r => r.Id == "auto-ext-png").Choices);

        Assert.True((await s.RememberAsync([new(SuggestionKind.Extension, "png")], "b")).Ok);      // 다시 고르면 앱만 바뀐다
        doc = await s.LoadAsync();
        Assert.Equal(1, doc.Rules.Count(r => r.When.Kind == RuleKind.Extension));
        Assert.Equal(["b"], doc.Rules.Single(r => r.Id == "auto-ext-png").Choices);
    }

    private static IReadOnlyList<string> RuleCheckOverlaps(PolicyDocument doc) =>
        doc.Rules.Where(r => r.When.Kind == RuleKind.Extension).SelectMany(r => r.When.Extensions).GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
}

public class DropWithChoicesTests
{
    private static async Task<(PolicyStore store, DropRouter router)> Setup(TempDir t)
    {
        var store = new PolicyStore(PolicyOs.Windows, t.Path);
        await store.LoadAsync();
        foreach (var id in new[] { "a", "b", "c" })
            await store.UpsertAppAsync(new AppUpsert(id, id + ".exe", ["{path}"]));
        var router = new DropRouter(store, new AppLauncher((cmd, args) => Task.CompletedTask));
        return (store, router);
    }

    private static RuleCondition Ext(params string[] values) => RuleCondition.Extension(values)!;

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
    public async Task ChoiceAppsIsTheIntersectionAcrossItems()
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
        Assert.Empty(none.ChoiceApps());          // 함께 열 수 있는 앱이 없다 — 등록된 앱 전부를 내놓지 않는다
        Assert.False(none.HasUnlinkedAsk);        // (연결된 앱이 없어서가 아니라 겹치는 앱이 없어서)
    }

    [Fact]
    public async Task FolderOnlyOffersFolderRelatedApps()
    {
        using var t = new TempDir();
        var (store, router) = await Setup(t);   // a, b, c 는 등록만 했고 어느 규칙에도 없다(pdf 앱 같은 것)
        var dir = Directory.CreateDirectory(Path.Combine(t.Path, "docs")).FullName;

        var plan = await router.RouteAsync([PathClassifier.Classify(dir)]);
        Assert.Equal(["explorer", "terminal"], plan.ChoiceApps().Select(a => a.Id));   // 폴더 규칙에 연결된 앱뿐
        Assert.False(plan.HasUnlinkedAsk);
    }

    [Fact]
    public async Task RuleWithoutAppsHasNothingToChooseAndIsFlagged()
    {
        using var t = new TempDir();
        var (store, router) = await Setup(t);
        await store.RemoveAppAsync("explorer", ReferencedRules.MakeAsk);
        await store.RemoveAppAsync("terminal", ReferencedRules.MakeAsk);   // 폴더 규칙에 앱이 하나도 안 남는다
        var dir = Directory.CreateDirectory(Path.Combine(t.Path, "docs")).FullName;

        var plan = await router.RouteAsync([PathClassifier.Classify(dir)]);
        Assert.Empty(plan.ChoiceApps());
        Assert.True(plan.HasUnlinkedAsk);   // 규칙 편집에서 앱을 연결하라고 안내할 상황
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
