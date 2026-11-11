using JackBean.Services.CodeProfiles;

namespace JackBean.Services.Tests.CodeProfiles;

public class VsCodeCliTests
{
    private const string CodeCmd = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd";

    private static readonly ProcessOutput NotFound = new(1, StdErr: "Profile 'Web / Python' not found.\n");

    private static VsCodeCli Cli(FakeProcessRunner fake, string path = CodeCmd, List<TimeSpan>? waits = null, int timeoutSeconds = 5) =>
        new(fake, path, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(timeoutSeconds), d => { waits?.Add(d); return Task.CompletedTask; });

    // --- version ---

    [Fact]
    public async Task VersionIsTheFirstLineOfDashDashVersion()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--version"], new ProcessOutput(0, StdOut: "1.99.0\nabc123\nx64\n"));
        var r = await Cli(fake).VersionAsync();
        Assert.Equal("1.99.0", r.Value);
        Assert.Equal(CodeCmd, fake.Calls.Single().Executable);
        Assert.Equal(["--version"], fake.Calls.Single().Args);
    }

    [Fact]
    public async Task VersionFailureIsAnErrorWithTheReason()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--version"], new ProcessOutput(1, StdErr: "boom\n"));
        Assert.Contains("boom", (await Cli(fake).VersionAsync()).Error!.Message);
    }

    [Fact]
    public async Task AMissingExecutableIsReportedNotThrown()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--version"], new ProcessOutput(-1, StartError: "code 을(를) 실행하지 못했습니다"));
        Assert.Contains("실행하지 못했습니다", (await Cli(fake).VersionAsync()).Error!.Message);
    }

    // --- list / exists ---

    [Fact]
    public async Task ListExtensionsUsesTheProfileAndLowercasesIds()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "C++ AOSP", "--list-extensions"], new ProcessOutput(0, StdOut: "llvm-vs-code-extensions.vscode-clangd\nMS-Python.python\n\n"));
        var r = await Cli(fake).ListExtensionsAsync("C++ AOSP");
        Assert.Equal(["llvm-vs-code-extensions.vscode-clangd", "ms-python.python"], r.Value);
        Assert.Equal(["--profile", "C++ AOSP", "--list-extensions"], fake.Calls.Single().Args);
    }

    [Fact]
    public async Task ListFailureIsAnError() => Assert.False((await Cli(new FakeProcessRunner()).ListExtensionsAsync("P")).Ok);

    [Fact]
    public async Task ProfileExistsFollowsTheListResult()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Web / Python", "--list-extensions"], NotFound);
        Assert.False(await Cli(fake).ProfileExistsAsync("Web / Python"));

        var ok = new FakeProcessRunner();
        ok.OnArgs(["--profile", "Web / Python", "--list-extensions"], new ProcessOutput(0));
        Assert.True(await Cli(ok).ProfileExistsAsync("Web / Python"));
    }

    // --- open ---

    [Fact]
    public async Task OpenRunsCodeProfileFolder()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        Assert.Null(await Cli(fake).OpenAsync("Java", @"C:\work\app"));
        Assert.Equal(["--profile", "Java", @"C:\work\app"], fake.Calls.Single().Args);
        Assert.NotNull(fake.Calls.Single().Timeout);
    }

    [Fact]
    public async Task ANonZeroExitFromOpenIsAnError()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"], new ProcessOutput(1, StdErr: "no"));
        Assert.Contains("no", (await Cli(fake).OpenAsync("P", @"C:\x"))!.Message);
    }

    // --- 배치 파일(code.cmd) ---

    [Fact]
    public async Task SpacesInThePathAndTheProfileArePassedAsSingleArguments()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        Assert.Null(await Cli(fake).OpenAsync("Web / Python", @"C:\My Work\app"));
        Assert.Equal(CodeCmd, fake.Calls.Single().Executable);
        Assert.Equal(["--profile", "Web / Python", @"C:\My Work\app"], fake.Calls.Single().Args);
    }

    [Theory]
    [InlineData(@"C:\a&calc")]
    [InlineData(@"C:\a|b")]
    [InlineData(@"C:\%PATH%")]
    [InlineData("a\"b")]
    [InlineData(@"C:\a^b")]
    [InlineData("a<b")]
    [InlineData("a`b")]
    [InlineData("a\nb")]
    public async Task CmdMetacharactersAreRefusedForBatchFilesAndNothingRuns(string bad)
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        var cli = Cli(fake);
        Assert.Contains("셸 특수문자", (await cli.OpenAsync("P", bad))!.Message);
        Assert.False((await cli.ListExtensionsAsync(bad)).Ok);
        Assert.Empty(fake.Calls);
    }

    [Theory]
    [InlineData(@"C:\VS\bin\CODE.CMD")]
    [InlineData(@"C:\VS\bin\code.bat")]
    [InlineData("code.cmd")]
    public async Task UppercaseBatAndBareNamesAreBatchFilesToo(string path)
    {
        var fake = new FakeProcessRunner();
        Assert.NotNull(await Cli(fake, path).OpenAsync("P", "a&b"));
    }

    [Fact]
    public async Task ARealExecutableIsNotGuarded()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        Assert.Null(await Cli(fake, @"C:\VS\Code.exe").OpenAsync("P", "a&b"));
    }

    // --- 프로필 생성 ---

    [Fact]
    public async Task CreateProfileOpensTheFolderWithTheProfileThenPolls()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Web / Python", "--list-extensions"], outputs: [NotFound, NotFound, new ProcessOutput(0)]);
        fake.OnArgs(["--profile", "Web / Python", @"C:\work\app"]);
        var waits = new List<TimeSpan>();

        Assert.Null(await Cli(fake, waits: waits).CreateProfileAsync("Web / Python", @"C:\work\app"));

        // 열기 → (확인 실패, 확인 실패, 확인 성공)
        Assert.Equal(["--profile", "Web / Python", @"C:\work\app"], fake.Calls[0].Args);
        Assert.Equal(["--list-extensions", "--list-extensions", "--list-extensions"], fake.Calls.Skip(1).Select(c => c.Args[^1]));
        Assert.Equal(3, waits.Count);
        Assert.All(waits, w => Assert.Equal(TimeSpan.FromSeconds(1), w));
    }

    [Fact]
    public async Task CreateProfileGivesUpAfterTheTimeout()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Web / Python", "--list-extensions"], NotFound);
        fake.OnArgs(["--profile", "Web / Python", @"C:\work\app"]);
        var waits = new List<TimeSpan>();
        var error = await Cli(fake, waits: waits, timeoutSeconds: 3).CreateProfileAsync("Web / Python", @"C:\work\app");
        Assert.Contains("3초", error!.Message);
        Assert.Contains("Web / Python", error.Message);
        Assert.Equal(3, waits.Count);
    }

    [Fact]
    public async Task CreateProfileDoesNotPollWhenOpeningFails()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "Web / Python", @"C:\work\app"], new ProcessOutput(1, StdErr: "cannot start"));
        var waits = new List<TimeSpan>();
        var error = await Cli(fake, waits: waits).CreateProfileAsync("Web / Python", @"C:\work\app");
        Assert.Contains("cannot start", error!.Message);
        Assert.Empty(waits);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task CreateProfileCanBeCancelledWhilePolling()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"], NotFound);
        fake.OnArgs(["--profile", "P", @"C:\x"]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cli(fake).CreateProfileAsync("P", @"C:\x", cts.Token));
    }

    // --- 확장 설치 ---

    private static async Task<List<InstallEvent>> Collect(IAsyncEnumerable<InstallEvent> events)
    {
        var list = new List<InstallEvent>();
        await foreach (var e in events) list.Add(e);
        return list;
    }

    [Fact]
    public async Task InstallSkipsInstalledExtensionsAndStreamsTheRest()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"], new ProcessOutput(0, StdOut: "a.installed\n"));
        fake.OnArgs(["--profile", "P", "--install-extension", "b.new"], events: [new OutputLine("Installing extensions..."), new OutputLine("Extension 'b.new' was successfully installed."), new ProcessExited(0)]);

        var events = await Collect(Cli(fake).InstallExtensionsAsync("P", ["A.Installed", "b.new"]));
        Assert.Equal([typeof(InstallSkipped), typeof(InstallStarted), typeof(InstallLog), typeof(InstallLog), typeof(InstallFinished)], events.Select(e => e.GetType()));
        Assert.Equal("A.Installed", events[0].ExtensionId);
        Assert.True(((InstallFinished)events[^1]).Success);
        Assert.Single(fake.Calls, c => c.Args.Contains("--install-extension"));
    }

    [Fact]
    public async Task AFailedInstallReportsTheExitCodeAndTheNextOneContinues()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"]);
        fake.OnArgs(["--profile", "P", "--install-extension", "bad.ext"], events: [new OutputLine("not found", IsError: true), new ProcessExited(1)]);
        fake.OnArgs(["--profile", "P", "--install-extension", "good.ext"], events: [new ProcessExited(0)]);

        var events = await Collect(Cli(fake).InstallExtensionsAsync("P", ["bad.ext", "good.ext"]));
        var finished = events.OfType<InstallFinished>().ToList();
        Assert.Equal([false, true], finished.Select(f => f.Success));
        Assert.Equal(1, finished[0].ExitCode);
        Assert.True(events.OfType<InstallLog>().Single().IsError);
    }

    [Fact]
    public async Task IfListingFailsEverythingIsAttempted()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile", "P", "--list-extensions"], new ProcessOutput(1));
        fake.OnArgs(["--profile", "P", "--install-extension"], events: [new ProcessExited(0)]);
        var events = await Collect(Cli(fake).InstallExtensionsAsync("P", ["x.y"]));
        Assert.DoesNotContain(events, e => e is InstallSkipped);
        Assert.True(events.OfType<InstallFinished>().Single().Success);
    }

    [Fact]
    public async Task InstallRefusesUnsafeNamesForBatchFilesWithoutRunningAnything()
    {
        var fake = new FakeProcessRunner();
        fake.OnArgs(["--profile"]);
        var events = new List<ProcessEvent>();
        await foreach (var e in Cli(fake).InstallExtensionAsync("P&x", "a.b")) events.Add(e);
        Assert.Equal(-1, ((ProcessExited)events[^1]).ExitCode);
        Assert.Empty(fake.Calls);
    }
}

public class CodeLocatorTests
{
    private static IReadOnlyDictionary<string, string?> Env(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => (string?)p.Value);

    [Fact]
    public void FindsCodeCmdOnPath()
    {
        var found = CodeLocator.Discover(Env(("Path", @"C:\Windows;C:\Tools\VSCode\bin")), p => p == @"C:\Tools\VSCode\bin\code.cmd");
        Assert.Equal(@"C:\Tools\VSCode\bin\code.cmd", found);
    }

    [Fact]
    public void PathEntriesWithTrailingBackslashAreFine()
    {
        var found = CodeLocator.Discover(Env(("Path", @"C:\Tools\VSCode\bin\")), p => p == @"C:\Tools\VSCode\bin\code.cmd");
        Assert.Equal(@"C:\Tools\VSCode\bin\code.cmd", found);
    }

    [Fact]
    public void FallsBackToTheDefaultInstallLocations()
    {
        const string user = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd";
        var env = Env(("Path", @"C:\Windows"), ("LOCALAPPDATA", @"C:\Users\me\AppData\Local"), ("ProgramFiles", @"C:\Program Files"));
        Assert.Equal(user, CodeLocator.Discover(env, p => p == user));

        const string system = @"C:\Program Files\Microsoft VS Code\bin\code.cmd";
        Assert.Equal(system, CodeLocator.Discover(Env(("Path", ""), ("ProgramFiles", @"C:\Program Files")), p => p == system));
    }

    [Fact]
    public void PathBeatsTheDefaultLocations()
    {
        var env = Env(("Path", @"C:\Tools"), ("LOCALAPPDATA", @"C:\Users\me\AppData\Local"));
        var found = CodeLocator.Discover(env, _ => true);
        Assert.Equal(@"C:\Tools\code.cmd", found);
    }

    [Fact]
    public void NothingFoundIsNull() =>
        Assert.Null(CodeLocator.Discover(Env(("Path", @"C:\Windows")), _ => false));
}
