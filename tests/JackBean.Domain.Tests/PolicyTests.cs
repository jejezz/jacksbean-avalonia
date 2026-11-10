using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class PolicyDocumentTests
{
    static DroppedItem File(string name) { var dot = name.LastIndexOf('.'); return new($"/x/{name}", DropKind.File, dot > 0 ? name[(dot + 1)..].ToLowerInvariant() : ""); }
    static DroppedItem Folder(string name) => new($"/x/{name}", DropKind.Folder);
    static readonly DroppedItem Drive = new(@"D:\", DropKind.Drive);

    static PolicyDocument Doc(string rulesJson, string? appsJson = null) => PolicyDocument.FromJson(JsonNode.Parse($$"""
        {"schema":2,"os":"windows",
         "apps": {{appsJson ?? """{"viewer":{"command":"viewer.exe","args":["{path}"]},"taskbench":{"command":"tb.exe","args":["--open","{path}"]},"explorer":{"command":"explorer.exe"}}"""}},
         "rules": {{rulesJson}} }
        """)!.AsObject());

    static string? App(Decision d) => d is OpenWith o ? o.App.Id : null;

    [Fact]
    public void ExtensionIsCaseInsensitiveAndOnlyForFiles()
    {
        var d = Doc("""[{"id":"img","when":{"kind":"extension","extensions":["PNG",".jpg"]},"open":["viewer"]}]""");
        Assert.Equal("viewer", App(d.Decide(File("a.png"))));
        Assert.Equal("viewer", App(d.Decide(File("A.JPG"))));
        Assert.IsType<Ask>(d.Decide(File("a.txt")));
        Assert.IsType<Ask>(d.Decide(Folder("png")));
    }

    [Fact]
    public void KindRulesMatchTheirTarget()
    {
        var d = Doc("""[{"id":"d","when":{"kind":"drive"},"open":["explorer"]},{"id":"f","when":{"kind":"folder"},"open":["taskbench"]},{"id":"x","when":{"kind":"file"},"open":["viewer"]}]""");
        Assert.Equal("explorer", App(d.Decide(Drive)));
        Assert.Equal("taskbench", App(d.Decide(Folder("repo"))));   // Git 이든 아니든 폴더는 폴더
        Assert.Equal("viewer", App(d.Decide(File("a.zip"))));
    }

    [Fact]
    public void FirstMatchWinsAndEmptyOpenAsks()
    {
        var d = Doc("""[{"id":"pdf","when":{"kind":"extension","extensions":["pdf"]},"open":["taskbench"]},{"id":"all","when":{"kind":"file"},"open":["viewer"]},{"id":"ask","when":{"kind":"folder"},"open":[]}]""");
        Assert.Equal("taskbench", App(d.Decide(File("a.pdf"))));
        Assert.Equal("viewer", App(d.Decide(File("a.txt"))));
        var empty = Assert.IsType<Ask>(d.Decide(Folder("docs")));
        Assert.Equal("ask", empty.RuleId);
        Assert.Empty(empty.Candidates!);                            // 앱을 연결하지 않은 규칙 — 고를 앱이 없다(다른 대상의 앱을 섞어 보여 주지 않는다)
        var none = Assert.IsType<Ask>(d.Decide(Drive));            // 어느 규칙에도 안 맞는다
        Assert.Null(none.RuleId);
        Assert.Empty(none.Candidates!);
    }

    [Fact]
    public void MissingAppAndIgnore()
    {
        var d = Doc("""[{"id":"pdf","when":{"kind":"extension","extensions":["pdf"]},"open":["daylight_pdf"]}]""");
        Assert.Equal("daylight_pdf", Assert.IsType<MissingApp>(d.Decide(File("a.pdf"))).AppId);
        Assert.IsType<Ignore>(d.Decide(new DroppedItem("/nope", DropKind.Missing)));
    }

    [Fact]
    public void LenientReading()
    {
        var d = Doc("""
            [{"when":{"kind":"folder"},"open":["ok"]},
             {"id":"badwhen","when":{"kind":"unknown"},"open":["ok"]},
             {"id":"badopen","when":{"kind":"folder"},"open":5},
             {"id":"good","when":{"kind":"folder"},"open":["ok"]},
             {"id":"good","when":{"kind":"folder"},"open":["ok"]},
             "not a map"]
            """, """{"ok":{"command":"ok.exe"},"nocmd":{"args":["{path}"]},"badargs":{"command":"x","args":"oops"}}""");
        Assert.Equal(["ok"], d.Apps.Keys);
        Assert.Equal(["{path}"], d.Apps["ok"].Args);
        Assert.Equal(["good"], d.Rules.Select(r => r.Id));
        Assert.Equal(7, d.Warnings.Count);
    }

    [Fact]
    public void OldSchemaThrows()
    {
        Assert.Throws<FormatException>(() => PolicyDocument.FromJson(new JsonObject { ["schema"] = 1 }));
        Assert.Throws<FormatException>(() => PolicyDocument.FromJson(new JsonObject { ["schema"] = 3 }));
    }

    [Fact]
    public void DefaultPolicies()
    {
        foreach (var os in Enum.GetValues<PolicyOs>())
        {
            var d = PolicyDocument.FromJson(PolicyDocument.DefaultJson(os));
            Assert.Empty(d.Warnings);
            Assert.Equal("explorer", App(d.Decide(Drive)));
            Assert.Equal("default", App(d.Decide(File("a.txt"))));
            // 폴더는 파일 관리자와 터미널 중에서 고른다(터미널이 없는 OS 는 파일 관리자 하나라 바로 연다). 어느 쪽이든 폴더와 관련된 앱뿐이다.
            var folder = d.Decide(Folder("a"));
            if (d.Apps.ContainsKey("terminal"))
            {
                var ask = Assert.IsType<Ask>(folder);
                Assert.Equal("folders", ask.RuleId);
                Assert.Equal(["explorer", "terminal"], ask.Candidates!.Select(a => a.Id));
            }
            else Assert.Equal("explorer", App(folder));
        }
        Assert.False(PolicyDocument.DefaultJson(PolicyOs.Linux)["apps"]!.AsObject().ContainsKey("terminal"));
        Assert.True(PolicyDocument.DefaultJson(PolicyOs.Windows)["apps"]!.AsObject().ContainsKey("terminal"));
    }
}
