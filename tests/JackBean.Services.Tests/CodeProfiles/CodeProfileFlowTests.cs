using JackBean.Domain.CodeProfiles;
using JackBean.Services.CodeProfiles;
using static JackBean.Services.Tests.CodeProfiles.CodeProfileFixtures;

namespace JackBean.Services.Tests.CodeProfiles;

public class ProfileNamesTests
{
    [Theory]
    [InlineData("Java")]
    [InlineData("Web / Python")]
    [InlineData("  C++ 개발  ")]
    public void OrdinaryNamesAreFine(string name) => Assert.Null(ProfileNames.Validate(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsEmpty(string? name) => Assert.Equal(ProfileNameProblem.Empty, ProfileNames.Validate(name));

    [Theory]
    [InlineData("a&b")]
    [InlineData("a|b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a^b")]
    [InlineData("a%b")]
    [InlineData("a\"b")]
    [InlineData("a`b")]
    [InlineData("a\nb")]
    public void ShellCharactersAreRejected(string name) => Assert.Equal(ProfileNameProblem.UnsafeCharacter, ProfileNames.Validate(name));

    [Fact]
    public void ATooLongNameIsRejected()
    {
        Assert.Null(ProfileNames.Validate(new string('a', ProfileNames.MaxLength)));
        Assert.Equal(ProfileNameProblem.TooLong, ProfileNames.Validate(new string('a', ProfileNames.MaxLength + 1)));
    }
}

public class CodeProfileFlowTests
{
    private static readonly ProcessOutput NotFound = new(1, StdErr: "Profile not found.");

    private static CodeProfileFlow Flow(FakeProcessRunner fake, string dataDir, string folder) =>
        new(new CodeProfileService(
            new VsCodeCli(fake, @"C:\VS\bin\code.cmd", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), _ => Task.CompletedTask),
            new ProjectAnalyzer(), new CodeProfileRulesStore(dataDir), new ProjectSettingsFile(), new CodeProjectMemory(dataDir)), folder);

    private static async Task<CodeProfileFlow> Analyzed(FakeProcessRunner fake, TempDir data, TempDir project)
    {
        var flow = Flow(fake, data.Path, project.Path);
        await flow.AnalyzeAsync();
        return flow;
    }

    [Fact]
    public async Task AnalyzingFillsTheRecommendationAndSelectsTheDefaultExtensions()
    {
        using var project = Create(new() { ["pom.xml"] = "", ["package.json"] = "{}" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);

        Assert.Null(flow.Error);
        Assert.Equal(FlowStep.Analysis, flow.Step);
        Assert.Equal("java-maven", flow.PrimaryTypeId);
        Assert.Equal("Java", flow.ProfileName);
        Assert.All(flow.Extensions.Where(e => !e.Optional), e => Assert.True(e.Selected));
        Assert.All(flow.Extensions.Where(e => e.Optional), e => Assert.False(e.Selected));   // 보조 유형의 확장은 기본 선택 안 함
        Assert.Contains(flow.Extensions, e => e.Optional);
        Assert.False(flow.IsBusy);
    }

    [Fact]
    public async Task AFolderWithNoTypeCannotContinue()
    {
        using var project = Create(new() { ["a.txt"] = "" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);
        Assert.Null(flow.Recommendation);
        Assert.False(flow.GoToRecommendation());
        Assert.False(flow.CanContinueToDiff);
    }

    [Fact]
    public async Task AMissingFolderIsAnAnalysisError()
    {
        using var data = Create();
        var flow = Flow(new FakeProcessRunner(), data.Path, Path.Combine(data.Path, "nope"));
        await flow.AnalyzeAsync();
        Assert.NotNull(flow.Error);
        Assert.Null(flow.Analysis);
    }

    [Fact]
    public async Task ChangingThePrimaryTypeRecommendsAgainAndDropsManualEdits()
    {
        using var project = Create(new() { ["pom.xml"] = "", ["package.json"] = "{}" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);
        flow.SetProfileName("Mine");

        Assert.True(flow.SelectPrimary("node"));
        Assert.Equal("node", flow.PrimaryTypeId);
        Assert.Equal("Web / Python", flow.ProfileName);
        Assert.False(flow.SelectPrimary("node"));   // 같은 유형이면 아무것도 안 한다
        Assert.False(flow.SelectPrimary("no-such-type"));
        Assert.Null(flow.Error);   // 모르는 유형은 조용히 무시
    }

    [Fact]
    public async Task ProfileNameAndExtensionEditsAreValidatedAndKept()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);
        flow.GoToRecommendation();
        Assert.Equal(FlowStep.Recommendation, flow.Step);

        flow.SetProfileName("a&b");
        Assert.Equal(ProfileNameProblem.UnsafeCharacter, flow.NameProblem);
        Assert.False(flow.CanContinueToDiff);
        flow.SetProfileName("  ");
        Assert.Equal(ProfileNameProblem.Empty, flow.NameProblem);
        flow.SetProfileName("My Java");
        Assert.True(flow.CanContinueToDiff);

        var first = flow.Extensions[0].Id;
        flow.SetExtension(first, false);
        Assert.DoesNotContain(first, flow.SelectedExtensions);
        flow.SetExtension(first, true);
        Assert.Contains(first, flow.SelectedExtensions);
        flow.SetExtension("not.listed", true);   // 모르는 확장은 무시
        Assert.DoesNotContain("not.listed", flow.SelectedExtensions);
    }

    [Fact]
    public async Task GoingBackToTheAnalysisKeepsTheEdits()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);
        flow.GoToRecommendation();
        flow.SetProfileName("Keep me");
        flow.BackToAnalysis();
        Assert.Equal(FlowStep.Analysis, flow.Step);
        Assert.Equal("Keep me", flow.ProfileName);
        Assert.NotNull(flow.Analysis);
    }

    [Fact]
    public async Task TheDiffStepShowsConflictsAndTogglingReplacesRecomputes()
    {
        using var project = Create(new() { ["pom.xml"] = "", [".vscode/settings.json"] = """{"java.configuration.updateBuildConfiguration": "disabled"}""" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);
        flow.GoToRecommendation();

        Assert.True(await flow.GoToDiffAsync());
        Assert.Equal(FlowStep.Diff, flow.Step);
        var conflict = Assert.Single(flow.Diff!.Conflicts.Take(1)).Pointer;
        Assert.False(flow.Diff.Conflicts[0].Replaced);

        await flow.ToggleReplaceAsync(conflict);
        Assert.Contains(conflict, flow.ReplaceConflicts);
        Assert.True(flow.Diff!.Conflicts.Single(c => c.Pointer == conflict).Replaced);

        await flow.ToggleReplaceAsync(conflict);
        Assert.DoesNotContain(conflict, flow.ReplaceConflicts);

        await flow.SetAllReplaceAsync(true);
        Assert.All(flow.Diff!.Conflicts, c => Assert.True(c.Replaced));
        await flow.SetAllReplaceAsync(false);
        Assert.Empty(flow.ReplaceConflicts);

        await flow.ToggleReplaceAsync("/not/a/conflict");   // 충돌이 아닌 포인터는 무시
        Assert.Empty(flow.ReplaceConflicts);
    }

    [Fact]
    public async Task ABrokenSettingsFileKeepsTheFlowOnTheRecommendationStepWithAnError()
    {
        using var project = Create(new() { ["pom.xml"] = "", [".vscode/settings.json"] = "{oops" });
        using var data = Create();
        var flow = await Analyzed(new FakeProcessRunner(), data, project);
        flow.GoToRecommendation();
        Assert.False(await flow.GoToDiffAsync());
        Assert.Equal(FlowStep.Recommendation, flow.Step);
        Assert.NotNull(flow.Error);
        flow.BackToRecommendation();
        Assert.Null(flow.Error);
    }

    [Fact]
    public async Task ApplyingRunsTheStepsReportsStatusAndEndsInDone()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);   // 프로필이 있고 확장이 이미 설치돼 있다고 답한다
        var flow = await Analyzed(fake, data, project);
        flow.GoToRecommendation();
        await flow.GoToDiffAsync();

        var states = new List<(FlowStep Step, bool Busy, bool CanClose)>();
        flow.Changed += () => states.Add((flow.Step, flow.IsBusy, flow.CanClose));
        await flow.ApplyAsync();

        Assert.Equal(FlowStep.Done, flow.Step);
        Assert.Null(flow.FailedStep);
        Assert.All(flow.Status.Values, v => Assert.Equal(StepStatus.Done, v));
        Assert.True(flow.SettingsWritten);
        Assert.Contains(flow.Log, l => l.Event == LogEvent.ProfileExists);
        Assert.Contains(states, s => s.Step == FlowStep.Apply && s.Busy && !s.CanClose);   // 진행 중에는 닫지 못한다
        Assert.True(flow.CanClose);
        Assert.True(File.Exists(ProjectSettingsFile.SettingsPath(project.Path)));
    }

    [Fact]
    public async Task OnlyTheSelectedExtensionsAreInstalled()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Java", "--list-extensions"]);
        fake.OnArgs(["--profile", "Java", "--install-extension"], events: [new ProcessExited(0)]);
        var flow = await Analyzed(fake, data, project);
        foreach (var e in flow.Extensions.ToList()) flow.SetExtension(e.Id, false);
        flow.SetExtension(flow.Extensions[0].Id, true);

        await flow.ApplyAsync();

        var installs = fake.Calls.Where(c => c.Args.Contains("--install-extension")).ToList();
        Assert.Single(installs);
        Assert.Contains(flow.Extensions[0].Id, installs[0].Args);
    }

    [Fact]
    public async Task AFailedInstallStaysOnTheApplyStepAndRetryContinues()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Java", "--list-extensions"]);
        fake.OnArgs(["--profile", "Java", "--install-extension"],
            outputs: [new ProcessOutput(1, StdErr: "boom"), new ProcessOutput(0)]);   // 첫 설치는 실패, 다음 호출부터 성공
        var flow = await Analyzed(fake, data, project);
        foreach (var e in flow.Extensions.ToList()) flow.SetExtension(e.Id, false);
        flow.SetExtension(flow.Extensions[0].Id, true);

        await flow.ApplyAsync();
        Assert.Equal(FlowStep.Apply, flow.Step);
        Assert.Equal(ApplyStep.Extensions, flow.FailedStep);
        Assert.Equal(StepStatus.Failed, flow.Status[ApplyStep.Extensions]);
        Assert.Equal(StepStatus.Pending, flow.Status[ApplyStep.Settings]);   // 설정은 건드리지 않았다
        Assert.NotNull(flow.Error);
        Assert.Contains(flow.Log, l => l.Event == LogEvent.ExtensionFailed);
        Assert.Contains(flow.Log, l => l.Event == LogEvent.Failure);
        Assert.False(Directory.Exists(Path.Combine(project.Path, ".vscode")));
        Assert.True(flow.CanClose);   // 실패하면 닫을 수 있다

        await flow.ApplyAsync();   // 재시도
        Assert.Equal(FlowStep.Done, flow.Step);
        Assert.Null(flow.FailedStep);
        Assert.Null(flow.Error);
        Assert.DoesNotContain(flow.Log, l => l.Event == LogEvent.ExtensionFailed);   // 로그는 새로 시작한다
    }

    [Fact]
    public async Task ApplyDoesNothingWhileBusyOrWithABadName()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        var flow = await Analyzed(fake, data, project);
        flow.SetProfileName("a&b");
        await flow.ApplyAsync();
        Assert.Empty(fake.Calls);
        Assert.NotEqual(FlowStep.Apply, flow.Step);
    }

    [Fact]
    public async Task ACancelledApplyEndsAsAFailureOfTheRunningStep()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Java", "--list-extensions"]);
        var flow = await Analyzed(fake, data, project);
        flow.Changed += () => { if (flow.IsBusy && flow.Status.GetValueOrDefault(ApplyStep.Extensions) == StepStatus.Running) flow.Cancel(); };

        await flow.ApplyAsync();

        Assert.Equal(FlowStep.Apply, flow.Step);
        Assert.NotNull(flow.FailedStep);
        Assert.Contains(flow.Log, l => l.Event == LogEvent.Cancelled);
        Assert.True(flow.CanClose);
    }

    [Fact]
    public async Task OpeningReportsAFailureAndNeverThrows()
    {
        using var project = Create(new() { ["pom.xml"] = "" });
        using var data = Create();
        var fake = new FakeProcessRunner();   // 응답이 없으면 127 로 실패한다
        var flow = await Analyzed(fake, data, project);
        Assert.False(await flow.OpenAsync());
        Assert.NotNull(flow.Error);

        fake.OnArgs(["--profile", "Java"]);
        Assert.True(await flow.OpenAsync());
        Assert.Null(flow.Error);
    }
}
