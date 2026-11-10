using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class RuleModelTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    private static PolicyDocument Doc(string rulesJson, string appsJson = """{"a":{"command":"a.exe"},"b":{"command":"b.exe"},"c":{"command":"c.exe"}}""") =>
        PolicyDocument.FromJson(Json($$"""{"schema":1,"os":"windows","apps":{{appsJson}},"rules":{{rulesJson}}}"""));

    private static DroppedItem File(string name) => PathClassifierFake.File(name);

    // --- choices 읽기·쓰기 ---

    [Fact]
    public void ReadsOpenChoicesAndAskForms()
    {
        Assert.Equal(["a"], RuleChoices.Read(Json("""{"open":"a"}""")));
        Assert.Empty(RuleChoices.Read(Json("""{"open":null}""")) ?? throw new Exception("null"));
        Assert.Empty(RuleChoices.Read(Json("""{}""")) ?? throw new Exception("null"));
        Assert.Equal(["a", "b"], RuleChoices.Read(Json("""{"open":null,"choices":["a","b"]}""")));
        Assert.Equal(["a", "b"], RuleChoices.Read(Json("""{"open":"zzz","choices":["a","b"]}""")));   // choices 가 우선
        Assert.Equal(["a"], RuleChoices.Read(Json("""{"open":"a","choices":[]}""")));                   // 빈 choices 는 없는 것
        Assert.Equal(["a", "b"], RuleChoices.Read(Json("""{"choices":["a","b","a"]}""")));              // 중복은 처음 것만
    }

    [Theory]
    [InlineData("""{"open":5}""")]
    [InlineData("""{"choices":"a"}""")]
    [InlineData("""{"choices":["a",3]}""")]
    [InlineData("""{"choices":["a",""]}""")]
    public void MalformedChoicesAreNull(string rule) => Assert.Null(RuleChoices.Read(Json(rule)));

    [Fact]
    public void WriteUsesOpenForOneAndChoicesForMany()
    {
        var one = Json("""{"id":"x","note":"내 메모","choices":["old","older"]}""");
        RuleChoices.Write(one, ["a"]);
        Assert.Equal("a", (string?)one["open"]);
        Assert.False(one.ContainsKey("choices"));
        Assert.Equal("내 메모", (string?)one["note"]);   // 모르는 키는 그대로

        var many = Json("""{"id":"x"}""");
        RuleChoices.Write(many, ["a", "b", "c"]);
        Assert.Null(many["open"]);
        Assert.Equal(["a", "b", "c"], many["choices"]!.AsArray().Select(n => (string?)n));

        var none = Json("""{"id":"x","open":"a"}""");
        RuleChoices.Write(none, []);
        Assert.Null(none["open"]);
        Assert.False(none.ContainsKey("choices"));
    }

    // --- 정책 읽기 ---

    [Fact]
    public void RuleWithTwoAppsAsksAmongThemOnly()
    {
        var d = Doc("""[{"id":"img","when":{"kind":"extension","values":["png"]},"open":null,"choices":["b","a"]}]""");
        var decision = d.Decide(File("x.png"));
        var ask = Assert.IsType<Ask>(decision);
        Assert.Equal(["b", "a"], ask.Candidates!.Select(x => x.Id));   // 적은 순서 그대로
    }

    [Fact]
    public void RuleWithOneAppOpensDirectlyAndNoneAsksAll()
    {
        var d = Doc("""[{"id":"p","when":{"kind":"extension","values":["pdf"]},"open":"c"},{"id":"any","when":{"kind":"kind","value":"file"},"open":null}]""");
        Assert.Equal("c", Assert.IsType<OpenWith>(d.Decide(File("x.pdf"))).App.Id);
        Assert.Null(Assert.IsType<Ask>(d.Decide(File("x.zip"))).Candidates);   // null = 등록된 앱 전부
    }

    [Fact]
    public void UnknownAppsInChoicesAreDroppedAndAllMissingIsMissingApp()
    {
        var some = Doc("""[{"id":"r","when":{"kind":"kind","value":"file"},"choices":["a","ghost","b"]}]""");
        Assert.Equal(["a", "b"], Assert.IsType<Ask>(some.Decide(File("x.txt"))).Candidates!.Select(x => x.Id));

        var none = Doc("""[{"id":"r","when":{"kind":"kind","value":"file"},"choices":["ghost","phantom"]}]""");
        var missing = Assert.IsType<MissingApp>(none.Decide(File("x.txt")));
        Assert.Equal("ghost", missing.AppId);
    }

    [Fact]
    public void OpenCompatibleWithOldFlutterShape()
    {
        var d = Doc("""[{"id":"r","when":{"kind":"kind","value":"file"},"open":"a"}]""");
        Assert.Equal("a", d.Rules[0].Open);
        Assert.Equal(["a"], d.Rules[0].Choices);
        Assert.Single(d.RulesUsing("a"));
        Assert.Empty(d.RulesUsing("b"));
    }

    [Fact]
    public void MoreThanTenAppsKeepsFirstTenWithWarning()
    {
        var ids = string.Join(",", Enumerable.Range(1, 12).Select(i => $"\"a{i}\""));
        var d = Doc($$"""[{"id":"r","when":{"kind":"kind","value":"file"},"choices":[{{ids}}]}]""");
        Assert.Equal(PolicyLimits.MaxAppsPerRule, d.Rules[0].Choices.Count);
        Assert.Single(d.Warnings);
    }

    [Fact]
    public void MoreThanLimitRulesAreKeptWithOneWarning()
    {
        var rules = string.Join(",", Enumerable.Range(0, PolicyLimits.MaxRules + 3)
            .Select(i => $$"""{"id":"r{{i}}","when":{"kind":"extension","values":["x{{i}}"]},"open":"a"}"""));
        var d = Doc($"[{rules}]");
        Assert.Equal(PolicyLimits.MaxRules + 3, d.Rules.Count);   // 지우지 않는다
        Assert.Single(d.Warnings);
    }

    [Fact]
    public void BadChoicesSkipOnlyThatRule()
    {
        var d = Doc("""[{"id":"bad","when":{"kind":"kind","value":"file"},"choices":[1]},{"id":"ok","when":{"kind":"kind","value":"folder"},"open":"a"}]""");
        Assert.Single(d.Rules);
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

    private static Rule Ext(string id, string[] exts, params string[] apps) =>
        new(id, RuleCondition.Parse(Json($$"""{"kind":"extension","values":[{{string.Join(",", exts.Select(e => $"\"{e}\""))}}]}"""))!, apps);

    [Fact]
    public void ValidateAcceptsGoodRule()
    {
        var d = Doc("[]");
        Assert.Empty(RuleCheck.Validate(Ext("img", ["png", "jpg"], "a", "b"), d, isNew: true));
    }

    [Fact]
    public void ValidateFindsEachProblem()
    {
        var d = Doc("""[{"id":"other","when":{"kind":"extension","values":["png"]},"open":"a"}]""");
        Assert.Contains(RuleCheck.Validate(Ext("x", ["png"], "a"), d, true), p => p is { Kind: RuleProblemKind.ExtensionTaken, Detail: "png", Other: "other" });
        Assert.Contains(RuleCheck.Validate(Ext("x", ["gif"], "ghost"), d, true), p => p is { Kind: RuleProblemKind.UnknownApp, Detail: "ghost" });
        Assert.Contains(RuleCheck.Validate(Ext("other", ["gif"], "a"), d, true), p => p.Kind == RuleProblemKind.IdTaken);
        var tooMany = Ext("x", ["gif"], Enumerable.Range(0, 11).Select(i => "a").ToArray());
        Assert.Contains(RuleCheck.Validate(tooMany, d, true), p => p.Kind == RuleProblemKind.TooManyApps);
    }

    [Fact]
    public void EditingARuleDoesNotConflictWithItself()
    {
        var d = Doc("""[{"id":"img","when":{"kind":"extension","values":["png","jpg"]},"open":"a"}]""");
        Assert.Empty(RuleCheck.Validate(Ext("img", ["png", "jpg", "gif"], "b"), d, isNew: false));
    }

    [Fact]
    public void RuleLimitBlocksNewButNotEditing()
    {
        var rules = string.Join(",", Enumerable.Range(0, PolicyLimits.MaxRules)
            .Select(i => $$"""{"id":"r{{i}}","when":{"kind":"extension","values":["x{{i}}"]},"open":"a"}"""));
        var d = Doc($"[{rules}]");
        Assert.Contains(RuleCheck.Validate(Ext("new", ["new"], "a"), d, isNew: true), p => p.Kind == RuleProblemKind.RuleLimit);
        Assert.Empty(RuleCheck.Validate(Ext("r5", ["x5", "more"], "a"), d, isNew: false));
    }

    [Fact]
    public void FindsConflictsInHandEditedPolicy()
    {
        var d = Doc("""[{"id":"one","when":{"kind":"extension","values":["png"]},"open":"a"},{"id":"two","when":{"kind":"extension","values":["png","gif"]},"open":"b"}]""");
        var conflicts = RuleCheck.FindExtensionConflicts(d.Rules);
        Assert.Single(conflicts);
        Assert.Equal("one", conflicts[0].Other);
    }

    // --- 순서 ---

    [Fact]
    public void RankPutsSpecificFirstAndFallbackFileLast()
    {
        int R(string json) => RuleOrder.Rank(RuleCondition.Parse(Json(json))!);
        var glob = R("""{"kind":"nameGlob","pattern":"*.test.js"}""");
        var ext = R("""{"kind":"extension","values":["png"]}""");
        var git = R("""{"kind":"hasGit"}""");
        var folder = R("""{"kind":"kind","value":"folder"}""");
        var drive = R("""{"kind":"kind","value":"drive"}""");
        var file = R("""{"kind":"kind","value":"file"}""");
        Assert.True(glob < ext && ext < git && git < folder && folder == drive && drive < file);
    }
}

/// <summary>시험용 파일 항목(디스크를 만들지 않는다).</summary>
internal static class PathClassifierFake
{
    public static DroppedItem File(string name) => new(Path.Combine(Path.GetTempPath(), name), DropKind.File, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
}
