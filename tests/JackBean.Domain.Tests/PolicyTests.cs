using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class PolicyDocumentTests
{
    static DroppedItem File(string name) { var dot = name.LastIndexOf('.'); return new($"/x/{name}", DropKind.File, dot > 0 ? name[(dot + 1)..].ToLowerInvariant() : ""); }
    static DroppedItem Folder(string name, bool git = false) => new($"/x/{name}", DropKind.Folder, HasGit: git);
    static readonly DroppedItem Drive = new(@"D:\", DropKind.Drive);

    static PolicyDocument Doc(string rulesJson, string? appsJson = null) => PolicyDocument.FromJson(JsonNode.Parse($$"""
        {"schema":1,"os":"windows",
         "apps": {{appsJson ?? """{"viewer":{"command":"viewer.exe","args":["{path}"]},"taskbench":{"command":"tb.exe","args":["--open","{path}"]},"explorer":{"command":"explorer.exe"}}"""}},
         "rules": {{rulesJson}} }
        """)!.AsObject());

    static string? App(Decision d) => d is OpenWith o ? o.App.Id : null;

    [Fact]
    public void ExtensionIsCaseInsensitiveAndOnlyForFiles()
    {
        var d = Doc("""[{"id":"img","when":{"kind":"extension","values":["PNG",".jpg"]},"open":"viewer"}]""");
        Assert.Equal("viewer", App(d.Decide(File("a.png"))));
        Assert.Equal("viewer", App(d.Decide(File("A.JPG"))));
        Assert.IsType<Ask>(d.Decide(File("a.txt")));
        Assert.IsType<Ask>(d.Decide(Folder("png")));
    }

    [Fact]
    public void HasGit()
    {
        var d = Doc("""[{"id":"git","when":{"kind":"hasGit"},"open":"taskbench"},{"id":"plain","when":{"kind":"hasGit","value":false},"open":"explorer"}]""");
        Assert.Equal("taskbench", App(d.Decide(Folder("repo", true))));
        Assert.Equal("explorer", App(d.Decide(Folder("docs"))));
        Assert.IsType<Ask>(d.Decide(File("a.txt")));
        Assert.IsType<Ask>(d.Decide(Drive));
    }

    [Fact]
    public void KindAndGlob()
    {
        var d = Doc("""[{"id":"d","when":{"kind":"kind","value":"drive"},"open":"explorer"},{"id":"log","when":{"kind":"nameGlob","pattern":"a.b*"},"open":"viewer"},{"id":"q","when":{"kind":"nameGlob","pattern":"img-?.png"},"open":"explorer"}]""");
        Assert.Equal("explorer", App(d.Decide(Drive)));
        Assert.Equal("viewer", App(d.Decide(File("A.B.log"))));
        Assert.IsType<Ask>(d.Decide(File("aXb1"))); // . 은 아무 글자가 아니다
        Assert.Equal("explorer", App(d.Decide(File("img-1.png"))));
        Assert.IsType<Ask>(d.Decide(File("img-12.png")));
    }

    [Fact]
    public void FirstMatchWinsAndNullOpenAsks()
    {
        var d = Doc("""[{"id":"git","when":{"kind":"hasGit"},"open":"taskbench"},{"id":"ask","when":{"kind":"kind","value":"folder"},"open":null}]""");
        Assert.Equal("taskbench", App(d.Decide(Folder("repo", true))));
        Assert.Equal("ask", Assert.IsType<Ask>(d.Decide(Folder("docs"))).RuleId);
        Assert.Null(Assert.IsType<Ask>(d.Decide(File("a.txt"))).RuleId);
    }

    [Fact]
    public void MissingAppAndIgnore()
    {
        var d = Doc("""[{"id":"pdf","when":{"kind":"extension","values":["pdf"]},"open":"daylight_pdf"}]""");
        Assert.Equal("daylight_pdf", Assert.IsType<MissingApp>(d.Decide(File("a.pdf"))).AppId);
        Assert.IsType<Ignore>(d.Decide(new DroppedItem("/nope", DropKind.Missing)));
    }

    [Fact]
    public void LenientReading()
    {
        var d = Doc("""
            [{"when":{"kind":"hasGit"},"open":"ok"},
             {"id":"badwhen","when":{"kind":"unknown"},"open":"ok"},
             {"id":"badopen","when":{"kind":"hasGit"},"open":5},
             {"id":"good","when":{"kind":"hasGit"},"open":"ok"},
             {"id":"good","when":{"kind":"hasGit"},"open":"ok"},
             "not a map"]
            """, """{"ok":{"command":"ok.exe"},"nocmd":{"args":["{path}"]},"badargs":{"command":"x","args":"oops"}}""");
        Assert.Equal(["ok"], d.Apps.Keys);
        Assert.Equal(["{path}"], d.Apps["ok"].Args);
        Assert.Equal(["good"], d.Rules.Select(r => r.Id));
        Assert.Equal(7, d.Warnings.Count);
    }

    [Fact]
    public void WrongSchemaThrows() => Assert.Throws<FormatException>(() => PolicyDocument.FromJson(new JsonObject { ["schema"] = 2 }));

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

public class PolicyPorterTests
{
    [Fact]
    public void PortsDefaultWindowsToMac()
    {
        var r = PolicyPorter.Port(PolicyDocument.DefaultJson(PolicyOs.Windows), PolicyOs.Macos);
        var d = PolicyDocument.FromJson(r.Policy);
        Assert.Equal("open", d.Apps["explorer"].Command);
        Assert.Equal("macos", (string?)r.Policy["os"]);
    }

    [Fact]
    public void SkipsUnknownAppsOsSpecificConditionsAndKeepsExisting()
    {
        var src = JsonNode.Parse("""
            {"schema":1,"os":"windows","apps":{"explorer":{"command":"explorer.exe"},"taskbench":{"command":"tb.exe"}},
             "rules":[{"id":"drv","when":{"kind":"driveLetter","value":"D"},"open":"explorer"},
                      {"id":"tb","when":{"kind":"hasGit"},"open":"taskbench"},
                      {"id":"img","when":{"kind":"extension","values":["png"]},"open":"explorer"}]}
            """)!.AsObject();
        var existing = JsonNode.Parse("""{"schema":1,"os":"macos","apps":{"explorer":{"command":"custom"}},"rules":[]}""")!.AsObject();
        var r = PolicyPorter.Port(src, PolicyOs.Macos, existing);
        Assert.Equal("custom", (string?)r.Policy["apps"]!["explorer"]!["command"]);
        Assert.Contains(r.Notes, n => n.Subject == "app taskbench" && n.Action == PortAction.Skipped);
        Assert.Contains(r.Notes, n => n.Subject == "rule drv" && n.Action == PortAction.Skipped);
        Assert.Contains(r.Notes, n => n.Subject == "rule tb" && n.Action == PortAction.Skipped);
        Assert.Equal(["img"], r.Policy["rules"]!.AsArray().Select(x => (string?)x!["id"]));
    }

    [Fact]
    public void ExistingOsMismatchThrows()
    {
        var w = PolicyDocument.DefaultJson(PolicyOs.Windows);
        Assert.Throws<FormatException>(() => PolicyPorter.Port(w, PolicyOs.Macos, PolicyDocument.DefaultJson(PolicyOs.Linux)));
    }
}

