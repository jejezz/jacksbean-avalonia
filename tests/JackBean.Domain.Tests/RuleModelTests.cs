using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class RuleModelTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    private static PolicyDocument Doc(string rulesJson, string appsJson = """{"a":{"command":"a.exe"},"b":{"command":"b.exe"},"c":{"command":"c.exe"}}""") =>
        PolicyDocument.FromJson(Json($$"""{"schema":2,"os":"windows","apps":{{appsJson}},"rules":{{rulesJson}}}"""));

    private static DroppedItem File(string name) => new(Path.Combine(Path.GetTempPath(), name), DropKind.File, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());

    private static Rule Ext(string id, string[] exts, params string[] apps) => new(id, RuleCondition.Extension(exts)!, apps);

    // --- 조건 ---

    [Fact]
    public void ConditionRoundTripsThroughJson()
    {
        foreach (var when in new[] { RuleCondition.Drive, RuleCondition.Folder, RuleCondition.File, RuleCondition.Extension([".PNG", "jpg", "png"])! })
        {
            var back = RuleCondition.Parse(when.ToJson())!;
            Assert.Equal(when.Kind, back.Kind);
            Assert.Equal(when.Extensions, back.Extensions);
        }
        Assert.Equal(["png", "jpg"], RuleCondition.Extension([".PNG", "jpg", "png"])!.Extensions);   // 소문자·점 없음·중복 없음
    }

    [Fact]
    public void ConditionRejectsBadShapes()
    {
        Assert.Null(RuleCondition.Extension([]));
        Assert.Null(RuleCondition.Extension(["", "."]));
        Assert.Null(RuleCondition.Parse(Json("""{"kind":"extension","extensions":[]}""")));
        Assert.Null(RuleCondition.Parse(Json("""{"kind":"extension"}""")));
        Assert.Null(RuleCondition.Parse(Json("""{"kind":"extension","extensions":[1]}""")));
        Assert.Null(RuleCondition.Parse(Json("""{"kind":"hasGit"}""")));   // 없앤 조건
        Assert.Null(RuleCondition.Parse(Json("""{"kind":"nameGlob","pattern":"*"}""")));
        Assert.Null(RuleCondition.Parse(null));
    }

    // --- open 읽기·쓰기 ---

    [Fact]
    public void ReadsOpenAsAList()
    {
        Assert.Equal(["a"], RuleChoices.Read(Json("""{"open":["a"]}""")));
        Assert.Equal(["a", "b"], RuleChoices.Read(Json("""{"open":["a","b","a"]}""")));   // 중복은 처음 것만
        Assert.Empty(RuleChoices.Read(Json("""{"open":[]}""")) ?? throw new Exception("null"));
        Assert.Empty(RuleChoices.Read(Json("""{}""")) ?? throw new Exception("null"));
        Assert.Empty(RuleChoices.Read(Json("""{"open":null}""")) ?? throw new Exception("null"));   // null 은 없는 것과 같다
    }

    [Theory]
    [InlineData("""{"open":"a"}""")]       // 문자열 하나는 이제 틀린 모양이다
    [InlineData("""{"open":["a",3]}""")]
    [InlineData("""{"open":["a",""]}""")]
    public void MalformedOpenIsNull(string rule) => Assert.Null(RuleChoices.Read(Json(rule)));

    [Fact]
    public void WriteKeepsOtherKeys()
    {
        var rule = Json("""{"id":"x","note":"내 메모"}""");
        RuleChoices.Write(rule, ["a", "b"]);
        Assert.Equal(["a", "b"], rule["open"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("내 메모", (string?)rule["note"]);
        RuleChoices.Write(rule, []);
        Assert.Empty(rule["open"]!.AsArray());
    }

    // --- 판정 ---

    [Fact]
    public void RuleWithTwoAppsAsksAmongThemOnly()
    {
        var d = Doc("""[{"id":"img","when":{"kind":"extension","extensions":["png"]},"open":["b","a"]}]""");
        var ask = Assert.IsType<Ask>(d.Decide(File("x.png")));
        Assert.Equal(["b", "a"], ask.Candidates!.Select(x => x.Id));   // 적은 순서 그대로
    }

    [Fact]
    public void RuleWithOneAppOpensDirectlyAndNoneAsksAll()
    {
        var d = Doc("""[{"id":"p","when":{"kind":"extension","extensions":["pdf"]},"open":["c"]},{"id":"any","when":{"kind":"file"},"open":[]}]""");
        Assert.Equal("c", Assert.IsType<OpenWith>(d.Decide(File("x.pdf"))).App.Id);
        Assert.Null(Assert.IsType<Ask>(d.Decide(File("x.zip"))).Candidates);   // null = 등록된 앱 전부
    }

    [Fact]
    public void UnknownAppsInOpenAreDroppedAndAllMissingIsMissingApp()
    {
        var some = Doc("""[{"id":"r","when":{"kind":"file"},"open":["a","ghost","b"]}]""");
        Assert.Equal(["a", "b"], Assert.IsType<Ask>(some.Decide(File("x.txt"))).Candidates!.Select(x => x.Id));

        var none = Doc("""[{"id":"r","when":{"kind":"file"},"open":["ghost","phantom"]}]""");
        Assert.Equal("ghost", Assert.IsType<MissingApp>(none.Decide(File("x.txt"))).AppId);
    }

    [Fact]
    public void RulesUsingFindsRulesByAnyOfTheirApps()
    {
        var d = Doc("""[{"id":"one","when":{"kind":"file"},"open":["a"]},{"id":"two","when":{"kind":"folder"},"open":["a","b"]}]""");
        Assert.Equal(["one", "two"], d.RulesUsing("a").Select(r => r.Id));
        Assert.Equal(["two"], d.RulesUsing("b").Select(r => r.Id));
        Assert.Empty(d.RulesUsing("c"));
    }

    [Fact]
    public void MoreThanTenAppsKeepsFirstTenWithWarning()
    {
        var ids = string.Join(",", Enumerable.Range(1, 12).Select(i => $"\"a{i}\""));
        var d = Doc($$"""[{"id":"r","when":{"kind":"file"},"open":[{{ids}}]}]""");
        Assert.Equal(PolicyLimits.MaxAppsPerRule, d.Rules[0].Choices.Count);
        Assert.Single(d.Warnings);
    }

    [Fact]
    public void MoreThanLimitRulesAreKeptWithOneWarning()
    {
        var rules = string.Join(",", Enumerable.Range(0, PolicyLimits.MaxRules + 3)
            .Select(i => $$"""{"id":"r{{i}}","when":{"kind":"extension","extensions":["x{{i}}"]},"open":["a"]}"""));
        var d = Doc($"[{rules}]");
        Assert.Equal(PolicyLimits.MaxRules + 3, d.Rules.Count);   // 지우지 않는다
        Assert.Single(d.Warnings);
    }

    // --- 확장자 묶음 ---

    [Fact]
    public void EveryExtensionBelongsToOneGroupOnly()
    {
        var all = ExtensionGroups.All.SelectMany(g => g.Extensions).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());   // 묶음 둘을 함께 골라도 겹치지 않는다
        Assert.All(all, e => Assert.Equal(e, ExtensionGroups.Normalize(e)));   // 소문자·점 없음
    }

    [Fact]
    public void GroupLookup()
    {
        Assert.Equal("image", ExtensionGroups.GroupOf(".PNG")!.Id);
        Assert.Equal("pdf", ExtensionGroups.GroupOf("pdf")!.Id);
        Assert.Null(ExtensionGroups.GroupOf("weird"));
        Assert.Contains("amr", ExtensionGroups.Find("audio")!.Extensions);   // 음성 포함
        Assert.NotNull(ExtensionGroups.Find("video"));
        Assert.Null(ExtensionGroups.Find("nope"));
    }

    // --- 검사 ---

    [Fact]
    public void ValidateAcceptsGoodRule() =>
        Assert.Empty(RuleCheck.Validate(Ext("img", ["png", "jpg"], "a", "b"), Doc("[]"), isNew: true));

    [Fact]
    public void ValidateFindsEachProblem()
    {
        var d = Doc("""[{"id":"other","when":{"kind":"extension","extensions":["png"]},"open":["a"]},{"id":"folders","when":{"kind":"folder"},"open":[]}]""");
        Assert.Contains(RuleCheck.Validate(Ext("x", ["png"], "a"), d, true), p => p is { Kind: RuleProblemKind.ExtensionTaken, Detail: "png", Other: "other" });
        Assert.Contains(RuleCheck.Validate(Ext("x", ["gif"], "ghost"), d, true), p => p is { Kind: RuleProblemKind.UnknownApp, Detail: "ghost" });
        Assert.Contains(RuleCheck.Validate(Ext("other", ["gif"], "a"), d, true), p => p.Kind == RuleProblemKind.IdTaken);
        var tooMany = Ext("x", ["gif"], Enumerable.Repeat("a", PolicyLimits.MaxAppsPerRule + 1).ToArray());
        Assert.Contains(RuleCheck.Validate(tooMany, d, true), p => p.Kind == RuleProblemKind.TooManyApps);
        // 폴더 규칙은 하나만
        Assert.Contains(RuleCheck.Validate(new Rule("f2", RuleCondition.Folder, ["a"]), d, true), p => p is { Kind: RuleProblemKind.KindTaken, Other: "folders" });
        Assert.Empty(RuleCheck.Validate(new Rule("folders", RuleCondition.Folder, ["a"]), d, isNew: false));   // 자기 자신은 괜찮다
    }

    [Fact]
    public void EditingARuleDoesNotConflictWithItself()
    {
        var d = Doc("""[{"id":"img","when":{"kind":"extension","extensions":["png","jpg"]},"open":["a"]}]""");
        Assert.Empty(RuleCheck.Validate(Ext("img", ["png", "jpg", "gif"], "b"), d, isNew: false));
    }

    [Fact]
    public void RuleLimitBlocksNewButNotEditing()
    {
        var rules = string.Join(",", Enumerable.Range(0, PolicyLimits.MaxRules)
            .Select(i => $$"""{"id":"r{{i}}","when":{"kind":"extension","extensions":["x{{i}}"]},"open":["a"]}"""));
        var d = Doc($"[{rules}]");
        Assert.Contains(RuleCheck.Validate(Ext("new", ["new"], "a"), d, isNew: true), p => p.Kind == RuleProblemKind.RuleLimit);
        Assert.Empty(RuleCheck.Validate(Ext("r5", ["x5", "more"], "a"), d, isNew: false));
    }

    // --- 순서 ---

    [Fact]
    public void RankPutsExtensionsFirstAndFileFallbackLast()
    {
        int R(RuleCondition c) => RuleOrder.Rank(c);
        var ext = RuleCondition.Extension(["png"])!;
        Assert.True(R(ext) < R(RuleCondition.Folder) && R(RuleCondition.Folder) == R(RuleCondition.Drive) && R(RuleCondition.Drive) < R(RuleCondition.File));
    }
}
