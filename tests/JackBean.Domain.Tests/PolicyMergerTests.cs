using System.Text.Json.Nodes;
using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class PolicyMergerTests
{
    private static JsonObject P(string apps, string rules) =>
        JsonNode.Parse($$"""{"schema":2,"os":"windows","apps":{{{apps}}},"rules":[{{rules}}]}""")!.AsObject();

    private static string App(string id, string command, string? updated = null) =>
        $$"""
        "{{id}}":{"command":"{{command}}","args":["{path}"]{{(updated == null ? "" : $",\"updated\":\"{updated}\"")}}}
        """;

    private static string ExtRule(string id, string[] exts, string[] apps, string? updated = null, string extra = "") =>
        $$"""
        {"id":"{{id}}","when":{"kind":"extension","extensions":[{{string.Join(",", exts.Select(e => $"\"{e}\""))}}]},"open":[{{string.Join(",", apps.Select(a => $"\"{a}\""))}}]{{(updated == null ? "" : $",\"updated\":\"{updated}\"")}}{{extra}}}
        """;

    private static string KindRule(string id, string kind, string[] apps, string? updated = null) =>
        $$"""
        {"id":"{{id}}","when":{"kind":"{{kind}}"},"open":[{{string.Join(",", apps.Select(a => $"\"{a}\""))}}]{{(updated == null ? "" : $",\"updated\":\"{updated}\"")}}}
        """;

    private const string Old = "2026-01-01T00:00:00Z", Mid = "2026-06-01T00:00:00Z", New = "2026-11-01T00:00:00Z";

    private static string[] RuleIds(MergeResult r) => r.Merged["rules"]!.AsArray().Select(n => (string)n!["id"]!).ToArray();
    private static string Command(MergeResult r, string id) => (string)r.Merged["apps"]![id]!["command"]!;

    [Fact]
    public void OneSideChangedTakesThatSide()
    {
        var b = P(App("a", "a-old.exe", Old), "");
        var localEdited = P(App("a", "a-local.exe", Mid), "");
        var remoteEdited = P(App("a", "a-remote.exe", Mid), "");

        Assert.Equal("a-local.exe", Command(PolicyMerger.Merge(b, localEdited, b), "a"));      // 이 PC 만 고침
        Assert.Equal("a-remote.exe", Command(PolicyMerger.Merge(b, b, remoteEdited), "a"));    // GitHub 만 고침
        Assert.Empty(PolicyMerger.Merge(b, localEdited, b).Notes);
    }

    [Fact]
    public void BothChangedTheSameWayIsNotAConflict()
    {
        var b = P(App("a", "a.exe", Old), "");
        var both = P(App("a", "same.exe", Mid), "");
        var r = PolicyMerger.Merge(b, both, P(App("a", "same.exe", Mid), ""));
        Assert.Equal("same.exe", Command(r, "a"));
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void ConflictGoesToTheMoreRecentEditAndTiesGoToThisPc()
    {
        var b = P(App("a", "a.exe", Old), "");
        var localNewer = PolicyMerger.Merge(b, P(App("a", "local.exe", New), ""), P(App("a", "remote.exe", Mid), ""));
        Assert.Equal("local.exe", Command(localNewer, "a"));
        Assert.Contains(localNewer.Notes, n => n is { Kind: MergeNoteKind.ConflictResolved, Id: "a", Detail: "local" });

        var remoteNewer = PolicyMerger.Merge(b, P(App("a", "local.exe", Mid), ""), P(App("a", "remote.exe", New), ""));
        Assert.Equal("remote.exe", Command(remoteNewer, "a"));
        Assert.Contains(remoteNewer.Notes, n => n is { Kind: MergeNoteKind.ConflictResolved, Detail: "remote" });

        var tie = PolicyMerger.Merge(b, P(App("a", "local.exe", Mid), ""), P(App("a", "remote.exe", Mid), ""));
        Assert.Equal("local.exe", Command(tie, "a"));
        var noStamps = PolicyMerger.Merge(b, P(App("a", "local.exe"), ""), P(App("a", "remote.exe"), ""));   // updated 가 없으면 이 PC
        Assert.Equal("local.exe", Command(noStamps, "a"));
    }

    [Fact]
    public void ItemsAddedOnOneSideAreKeptFromBothSides()
    {
        var b = P(App("a", "a.exe"), "");
        var local = P(App("a", "a.exe") + "," + App("l", "l.exe", Mid), ExtRule("local-rule", ["png"], ["l"], Mid));
        var remote = P(App("a", "a.exe") + "," + App("r", "r.exe", Mid), ExtRule("remote-rule", ["pdf"], ["r"], Mid));
        var m = PolicyMerger.Merge(b, local, remote);
        Assert.Equal(["a", "l", "r"], m.Merged["apps"]!.AsObject().Select(kv => kv.Key));
        Assert.Equal(["local-rule", "remote-rule"], RuleIds(m));
        Assert.Empty(m.Notes);
    }

    [Fact]
    public void DeletionFollowsTheOtherSideOnlyIfThisSideDidNotEditItAfterwards()
    {
        var rule = ExtRule("pdf", ["pdf"], ["a"], Old);
        var apps = App("a", "a.exe");
        var b = P(apps, rule);

        // 상대가 지웠고 이 PC 는 그대로 → 지운다
        var removed = PolicyMerger.Merge(b, b, P(apps, ""));
        Assert.Empty(RuleIds(removed));
        Assert.Contains(removed.Notes, n => n is { Kind: MergeNoteKind.RemovedWithTheOtherSide, Id: "pdf", Detail: "remote" });

        // 상대가 지웠지만 이 PC 가 그 뒤에 고쳤다 → 남긴다
        var edited = P(apps, ExtRule("pdf", ["pdf", "djvu"], ["a"], New));
        var kept = PolicyMerger.Merge(b, edited, P(apps, ""));
        Assert.Equal(["pdf"], RuleIds(kept));
        Assert.Contains(kept.Notes, n => n is { Kind: MergeNoteKind.KeptEditedOverDelete, Detail: "local" });

        // 반대 방향: 이 PC 가 지웠다
        Assert.Empty(RuleIds(PolicyMerger.Merge(b, P(apps, ""), b)));
        var remoteEdited = PolicyMerger.Merge(b, P(apps, ""), edited);
        Assert.Equal(["pdf"], RuleIds(remoteEdited));
        Assert.Contains(remoteEdited.Notes, n => n is { Kind: MergeNoteKind.KeptEditedOverDelete, Detail: "remote" });
    }

    [Fact]
    public void FirstSyncWithoutBaseIsAUnionAndNeverDeletes()
    {
        var local = P(App("a", "a.exe") + "," + App("l", "l.exe"), ExtRule("only-local", ["png"], ["l"]));
        var remote = P(App("a", "a.exe") + "," + App("r", "r.exe"), ExtRule("only-remote", ["pdf"], ["r"]));
        var m = PolicyMerger.Merge(null, local, remote);
        Assert.Equal(["a", "l", "r"], m.Merged["apps"]!.AsObject().Select(kv => kv.Key));
        Assert.Equal(["only-local", "only-remote"], RuleIds(m));
    }

    [Fact]
    public void RulesThatPointAtADeletedAppLoseThatApp()
    {
        var b = P(App("a", "a.exe") + "," + App("x", "x.exe"), ExtRule("r", ["png"], ["a", "x"], Old));
        var local = P(App("a", "a.exe") + "," + App("x", "x.exe"), ExtRule("r", ["png"], ["a", "x"], New, ",\"memo\":\"keep\""));   // 이 PC: 규칙을 고침
        var remote = P(App("a", "a.exe"), ExtRule("r", ["png"], ["a", "x"], Old));                                                 // GitHub: 앱 x 를 지움
        var m = PolicyMerger.Merge(b, local, remote);
        Assert.False(m.Merged["apps"]!.AsObject().ContainsKey("x"));
        Assert.Equal(["a"], m.Merged["rules"]![0]!["open"]!.AsArray().Select(n => (string)n!));
        Assert.Equal("keep", (string)m.Merged["rules"]![0]!["memo"]!);                      // 모르는 키는 이긴 쪽 항목 그대로
        Assert.Contains(m.Notes, n => n.Kind == MergeNoteKind.Repaired && n.Id == "r");
    }

    [Fact]
    public void SameExtensionInTwoRulesGoesToTheMoreRecentRule()
    {
        var apps = App("a", "a.exe") + "," + App("b", "b.exe");
        var local = P(apps, ExtRule("img-local", ["png", "gif"], ["a"], Mid));
        var remote = P(apps, ExtRule("img-remote", ["png"], ["b"], New));
        var m = PolicyMerger.Merge(null, local, remote);

        var rules = m.Merged["rules"]!.AsArray();
        var remoteRule = rules.First(r => (string)r!["id"]! == "img-remote")!;
        var localRule = rules.First(r => (string)r!["id"]! == "img-local")!;
        Assert.Equal(["png"], remoteRule["when"]!["extensions"]!.AsArray().Select(n => (string)n!));
        Assert.Equal(["gif"], localRule["when"]!["extensions"]!.AsArray().Select(n => (string)n!));   // png 는 더 최근에 고친 쪽이 가진다
        Assert.Contains(m.Notes, n => n.Kind == MergeNoteKind.Repaired);

        // 규칙이 비면 지운다
        var emptied = PolicyMerger.Merge(null, P(apps, ExtRule("old", ["png"], ["a"], Mid)), P(apps, ExtRule("new", ["png"], ["b"], New)));
        Assert.Equal(["new"], RuleIds(emptied));
    }

    [Fact]
    public void OnlyOneRulePerKindSurvives()
    {
        var apps = App("a", "a.exe");
        var local = P(apps, KindRule("folders", "folder", ["a"], Mid));
        var remote = P(apps, KindRule("my-folders", "folder", ["a"], New));
        var m = PolicyMerger.Merge(null, local, remote);
        Assert.Equal(["my-folders"], RuleIds(m));   // 더 최근에 고친 쪽
    }

    [Fact]
    public void ResultIsSortedMostSpecificFirst()
    {
        var apps = App("a", "a.exe");
        var local = P(apps, KindRule("files", "file", ["a"]) + "," + KindRule("folders", "folder", ["a"]));
        var remote = P(apps, ExtRule("pdf", ["pdf"], ["a"], Mid));
        var m = PolicyMerger.Merge(null, local, remote);
        Assert.Equal(["pdf", "folders", "files"], RuleIds(m));
    }

    [Fact]
    public void MergingIdenticalSidesChangesNothing()
    {
        var p = P(App("a", "a.exe", Mid) + "," + App("b", "b.exe"), ExtRule("pdf", ["pdf"], ["a", "b"], Mid) + "," + KindRule("folders", "folder", ["a"]) + "," + KindRule("files", "file", ["b"]));
        var m = PolicyMerger.Merge(p, p, p);
        Assert.True(JsonNode.DeepEquals(p, m.Merged));
        Assert.Empty(m.Notes);
        Assert.True(JsonNode.DeepEquals(m.Merged, PolicyMerger.Merge(m.Merged, m.Merged, m.Merged).Merged));   // 다시 합쳐도 같다
    }

    [Fact]
    public void MergedPolicyIsAlwaysReadableWithoutWarnings()
    {
        var apps = App("a", "a.exe") + "," + App("b", "b.exe");
        var local = P(apps, ExtRule("x", ["png", "jpg"], ["a", "ghost"], Mid) + "," + KindRule("f1", "folder", ["a"], Mid));
        var remote = P(apps, ExtRule("y", ["png"], ["b"], New) + "," + KindRule("f2", "folder", ["b"], New));
        var doc = PolicyDocument.FromJson(PolicyMerger.Merge(null, local, remote).Merged);
        Assert.Empty(doc.Warnings);
        Assert.Empty(RuleCheckConflicts(doc));
    }

    private static IEnumerable<string> RuleCheckConflicts(PolicyDocument doc) =>
        doc.Rules.Where(r => r.When.Kind == RuleKind.Extension).SelectMany(r => r.When.Extensions).GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key);

    [Fact]
    public void RulesPointingAtBuiltInToolsAreNotRepairedAway()
    {
        // 내장 도구는 파일의 apps 에 없어도 있는 앱이다 — 합칠 때 그 앱을 가리키는 규칙에서 빼지 않는다.
        var b = P("", "");
        var r = PolicyMerger.Merge(b, b, P("", ExtRule("txt", ["txt"], ["text-editor"], Mid)));
        Assert.Contains("txt", RuleIds(r));
        Assert.Equal(["text-editor"], r.Merged["rules"]!.AsArray()[0]!["open"]!.AsArray().Select(n => (string)n!).ToArray());
        Assert.DoesNotContain(r.Notes, n => n.Kind == MergeNoteKind.Repaired);
    }
}
