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
        Assert.Equal("ask", Assert.IsType<Ask>(d.Decide(Folder("docs"))).RuleId);
        Assert.Null(Assert.IsType<Ask>(d.Decide(Drive)).RuleId);   // 어느 규칙에도 안 맞으면 규칙 없이 묻는다
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
            Assert.Equal("folders", Assert.IsType<Ask>(d.Decide(Folder("a"))).RuleId);
            Assert.Equal("default", App(d.Decide(File("a.txt"))));
        }
        Assert.False(PolicyDocument.DefaultJson(PolicyOs.Linux)["apps"]!.AsObject().ContainsKey("terminal"));
        Assert.True(PolicyDocument.DefaultJson(PolicyOs.Windows)["apps"]!.AsObject().ContainsKey("terminal"));
    }
}
