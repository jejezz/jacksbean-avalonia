using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;
using JackBean.Services.CodeProfiles;
using static JackBean.Services.Tests.CodeProfiles.CodeProfileFixtures;

namespace JackBean.Services.Tests.CodeProfiles;

public class CodeProfileServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly ProcessOutput NotFound = new(1, StdErr: "Profile not found.");

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static CodeProfileService Service(FakeProcessRunner fake, string dataDir) => new(
        new VsCodeCli(fake, @"C:\VS\bin\code.cmd", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), _ => Task.CompletedTask),
        new ProjectAnalyzer(),
        new CodeProfileRulesStore(dataDir),
        new ProjectSettingsFile(),
        new CodeProjectMemory(dataDir),
        () => Now);

    private static async Task<List<ApplyEvent>> Collect(IAsyncEnumerable<ApplyEvent> events)
    {
        var list = new List<ApplyEvent>();
        await foreach (var e in events) list.Add(e);
        return list;
    }

    // --- 분석 ---

    [Fact]
    public async Task AnalyzeReturnsTheAnalysisARecommendationAndCompetitors()
    {
        using var project = Create(new() { ["pom.xml"] = "<project/>", ["package.json"] = "{}" });
        using var data = Create();
        var r = await Service(new FakeProcessRunner(), data.Path).AnalyzeAsync(project.Path);
        Assert.True(r.Ok, r.Error?.ToString());
        var a = r.Value!;
        Assert.Equal(["java-maven", "node"], Ids(a.Analysis));
        Assert.Equal("Java", a.Recommendation!.ProfileName);
        Assert.Equal(["node"], a.Competitors.Select(c => c.TypeId));
    }

    [Fact]
    public async Task AFolderWithNoDetectedTypeHasNoRecommendation()
    {
        using var project = Create(new() { ["readme.txt"] = "" });
        using var data = Create();
        var a = (await Service(new FakeProcessRunner(), data.Path).AnalyzeAsync(project.Path)).Value!;
        Assert.Empty(a.Analysis.Types);
        Assert.Null(a.Recommendation);
    }

    [Fact]
    public async Task AMissingFolderIsAnError()
    {
        using var data = Create();
        Assert.False((await Service(new FakeProcessRunner(), data.Path).AnalyzeAsync(Path.Combine(data.Path, "nope"))).Ok);
    }

    [Fact]
    public async Task BadUserRulesAreAnErrorNotSilentlyIgnored()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create(new() { [CodeProfileRulesStore.UserFileName] = "{oops" });
        var r = await Service(new FakeProcessRunner(), data.Path).AnalyzeAsync(project.Path);
        Assert.False(r.Ok);
        Assert.Contains(CodeProfileRulesStore.UserFileName, r.Error!.Message);
    }

    [Fact]
    public async Task UserRulesTakePartInTheAnalysis()
    {
        using var project = Create(new() { ["Cargo.toml"] = "" });
        using var data = Create(new()
        {
            [CodeProfileRulesStore.UserFileName] = """
                { "version": 1, "types": [ { "id": "rust", "label": "Rust", "priority": 60, "match": { "file": "Cargo.toml" }, "profile": "Rust", "extensions": ["rust-lang.rust-analyzer"] } ] }
                """,
        });
        var a = (await Service(new FakeProcessRunner(), data.Path).AnalyzeAsync(project.Path)).Value!;
        Assert.Equal("Rust", a.Recommendation!.ProfileName);
        Assert.Equal(["rust-lang.rust-analyzer"], a.Recommendation.Extensions);
    }

    [Fact]
    public async Task ChoosingAnotherPrimaryTypeRecommendsAgain()
    {
        using var project = Create(new() { ["pom.xml"] = "", ["package.json"] = "{}" });
        using var data = Create();
        var a = (await Service(new FakeProcessRunner(), data.Path).AnalyzeAsync(project.Path)).Value!;
        var r = CodeProfileService.RecommendWithPrimary(a, "node");
        Assert.Equal("Web / Python", r.Value!.ProfileName);
        Assert.Contains("vscjava.vscode-java-pack", r.Value.OptionalExtensions);
        Assert.Equal("Java", a.Recommendation!.ProfileName);   // 분석 결과는 그대로
    }

    // --- diff ---

    [Fact]
    public async Task DiffDoesNotWriteAnything()
    {
        using var project = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        using var data = Create();
        var r = await Service(new FakeProcessRunner(), data.Path).DiffAsync(project.Path, Obj("""{"a": 2, "b": 1}"""));
        Assert.Single(r.Value!.Conflicts);
        Assert.Single(r.Value.Added);
        Assert.Equal("""{"a": 1}""", File.ReadAllText(ProjectSettingsFile.SettingsPath(project.Path)));
    }

    // --- 적용 ---

    [Fact]
    public async Task ApplyWithAnExistingProfileInstallsMissingExtensionsThenWritesSettingsAndRemembers()
    {
        using var project = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Java", "--list-extensions"], new ProcessOutput(0, StdOut: "old.ext\n"));
        fake.OnArgs(["--profile", "Java", "--install-extension", "new.ext"], events: [new OutputLine("Installing"), new ProcessExited(0)]);

        var service = Service(fake, data.Path);
        var events = await Collect(service.ApplyAsync(new ApplyPlan(project.Path, "Java", ["old.ext", "new.ext"], Obj("""{"b": 2}"""))));

        Assert.Equal(
            [typeof(StepStarted), typeof(ProfileExists), typeof(StepFinished),
             typeof(StepStarted), typeof(ExtensionSkipped), typeof(ExtensionStarted), typeof(ExtensionOutput), typeof(ExtensionFinished), typeof(StepFinished),
             typeof(StepStarted), typeof(StepFinished)],
            events.Select(e => e.GetType()));
        var last = (StepFinished)events[^1];
        Assert.Equal(ApplyStep.Settings, last.Step);
        Assert.True(last.Written);
        Assert.NotNull(last.BackupPath);
        var written = JsonNode.Parse(File.ReadAllText(ProjectSettingsFile.SettingsPath(project.Path)))!;
        Assert.Equal(1, written["a"]!.GetValue<int>());
        Assert.Equal(2, written["b"]!.GetValue<int>());
        Assert.Single(fake.Calls, c => c.Args.Contains("--install-extension"));
        Assert.Equal("Java", (await service.Memory.GetAsync(project.Path))!.Profile);
    }

    [Fact]
    public async Task AMissingProfileIsCreatedByOpeningTheFolderBeforeAnythingIsInstalled()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Web / Python", "--list-extensions"], outputs: [NotFound, NotFound, new ProcessOutput(0)]);
        fake.OnArgs(["--profile", "Web / Python", project.Path]);
        fake.OnArgs(["--profile", "Web / Python", "--install-extension", "a.b"], events: [new ProcessExited(0)]);

        var events = await Collect(Service(fake, data.Path).ApplyAsync(new ApplyPlan(project.Path, "Web / Python", ["a.b"], Obj("""{"x": 1}"""))));

        Assert.Single(events.OfType<ProfileCreating>());
        Assert.Single(events.OfType<ProfileCreated>());
        Assert.Empty(events.OfType<ProfileExists>());
        Assert.Empty(events.OfType<StepFailed>());
        var order = fake.Calls.Select(c => string.Join(' ', c.Args.Skip(2))).ToList();
        Assert.Equal("--list-extensions", order[0]);
        Assert.Equal(project.Path, order[1]);   // 폴더를 그 프로필로 열어서 만든다
        Assert.True(order.IndexOf("--install-extension a.b") > 1);
        Assert.True(File.Exists(ProjectSettingsFile.SettingsPath(project.Path)));
    }

    [Fact]
    public async Task IfTheProfileCannotBeCreatedNothingElseRunsAndNothingIsRemembered()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"], NotFound);
        fake.OnArgs(["--profile", "P", project.Path]);

        var service = Service(fake, data.Path);
        var events = await Collect(service.ApplyAsync(new ApplyPlan(project.Path, "P", ["a.b"], Obj("""{"x": 1}"""))));

        var failed = (StepFailed)events[^1];
        Assert.Equal(ApplyStep.Profile, failed.Step);
        Assert.Contains("P", failed.Message);
        Assert.DoesNotContain(fake.Calls, c => c.Args.Contains("--install-extension"));
        Assert.False(Directory.Exists(Path.Combine(project.Path, ".vscode")));
        Assert.Null(await service.Memory.GetAsync(project.Path));
    }

    [Fact]
    public async Task AFailedInstallStopsBeforeTheSettingsAreTouched()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"]);
        fake.OnArgs(["--profile", "P", "--install-extension", "bad.ext"], events: [new OutputLine("nope", IsError: true), new ProcessExited(1)]);

        var service = Service(fake, data.Path);
        var events = await Collect(service.ApplyAsync(new ApplyPlan(project.Path, "P", ["bad.ext"], Obj("""{"x": 1}"""))));

        var failed = (StepFailed)events[^1];
        Assert.Equal(ApplyStep.Extensions, failed.Step);
        Assert.Equal("bad.ext", failed.Message);
        Assert.Equal([ApplyStep.Profile, ApplyStep.Extensions], events.OfType<StepStarted>().Select(e => e.Step));
        Assert.False(Directory.Exists(Path.Combine(project.Path, ".vscode")));
        Assert.Null(await service.Memory.GetAsync(project.Path));
    }

    [Fact]
    public async Task ASettingsFailureIsAFailedSettingsStep()
    {
        using var project = Create(new() { [".vscode/settings.json"] = "{oops" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        var events = await Collect(Service(fake, data.Path).ApplyAsync(new ApplyPlan(project.Path, "P", [], Obj("""{"b": 2}"""))));
        Assert.Equal(ApplyStep.Settings, ((StepFailed)events[^1]).Step);
    }

    [Fact]
    public async Task WithNoExtensionsOnlyTheProfileAndSettingsSteps()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        var events = await Collect(Service(fake, data.Path).ApplyAsync(new ApplyPlan(project.Path, "P", [], Obj("""{"b": 2}"""))));
        Assert.Empty(events.OfType<StepFailed>());
        Assert.Empty(events.OfType<ExtensionStarted>());
        Assert.True(File.Exists(ProjectSettingsFile.SettingsPath(project.Path)));
    }

    [Fact]
    public async Task AProfileNameWithShellCharactersFailsAtTheProfileStep()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var events = await Collect(Service(new FakeProcessRunner(), data.Path).ApplyAsync(new ApplyPlan(project.Path, "a&b", [], new JsonObject())));
        Assert.Equal(ApplyStep.Profile, ((StepFailed)events[^1]).Step);
    }

    [Fact]
    public async Task ReapplyingSkipsWhatIsAlreadyDone()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"], new ProcessOutput(0, StdOut: "a.b\n"));
        var service = Service(fake, data.Path);
        var plan = new ApplyPlan(project.Path, "P", ["a.b"], Obj("""{"x": 1}"""));
        await Collect(service.ApplyAsync(plan));
        var second = await Collect(service.ApplyAsync(plan));
        Assert.Single(second.OfType<ProfileExists>());
        Assert.Single(second.OfType<ExtensionSkipped>());
        Assert.False(((StepFinished)second[^1]).Written);   // 바뀐 것이 없어 다시 쓰지 않는다
    }

    [Fact]
    public async Task ReplacingChosenConflictsIsPassedThrough()
    {
        using var project = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        var plan = new ApplyPlan(project.Path, "P", [], Obj("""{"a": 2}"""), new HashSet<string> { "/a" });
        await Collect(Service(fake, data.Path).ApplyAsync(plan));
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(ProjectSettingsFile.SettingsPath(project.Path)))!["a"]!.GetValue<int>());
    }

    // --- 열기 ---

    [Fact]
    public async Task OpenRunsCodeWithTheProfileAndFolder()
    {
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        Assert.Null(await Service(fake, data.Path).OpenAsync(@"C:\work\app", "Java"));
        Assert.Equal(["--profile", "Java", @"C:\work\app"], fake.Calls.Single().Args);
    }
}
