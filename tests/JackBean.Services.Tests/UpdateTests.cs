using System.Net;
using System.Security.Cryptography;
using System.Text;
using JackBean.Domain;
using JackBean.Services.Updates;

namespace JackBean.Services.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("", "latest", "1", "1.2", "1.2.3.4", "v", "1.2.x", "1.2.3-")]
    public void InvalidTextIsNotAVersion(params string[] texts)
    {
        foreach (var t in texts) Assert.Null(SemanticVersion.TryParse(t));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData(" 1.2.3 ", "1.2.3")]
    [InlineData("1.2.3+4", "1.2.3")]
    [InlineData("1.2.3-rc.1+9", "1.2.3-rc.1")]
    public void ParsesAndNormalizes(string text, string expected) => Assert.Equal(expected, SemanticVersion.TryParse(text)!.ToString());

    // 서버(introduce-public-repos)의 test/vectors/version.json과 같은 값.
    [Theory]
    [InlineData("0.2.0", "0.2.1", -1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("1.2.3", "v1.2.3", 0)]
    [InlineData("1.2.3+4", "1.2.3+9", 0)]
    [InlineData("1.0.0-beta.2", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-rc", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1+5", 0)]
    [InlineData("1.0.0-rc.3", "1.0.0-rc.10", -1)]
    public void Compares(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(SemanticVersion.TryParse(a)!.CompareTo(SemanticVersion.TryParse(b)!)));
}

public sealed class UpdatePolicyTests
{
    [Fact]
    public void DueUntilCheckedThenAfterInterval()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        var policy = new UpdatePolicy(new MemoryUpdateStateStore(), now: () => now);
        Assert.True(policy.IsDue());
        policy.MarkChecked();
        Assert.False(policy.IsDue());
        now += TimeSpan.FromHours(25);
        Assert.True(policy.IsDue());
    }

    [Fact]
    public void ClockSetBackCountsAsDue()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000);
        var policy = new UpdatePolicy(new MemoryUpdateStateStore(), now: () => now);
        policy.MarkChecked();
        now -= TimeSpan.FromDays(2);
        Assert.True(policy.IsDue());
    }

    [Fact]
    public void SkipRemembersOnlyThatVersion()
    {
        var policy = new UpdatePolicy(new MemoryUpdateStateStore());
        policy.Skip("1.1.0");
        Assert.True(policy.IsSkipped("1.1.0"));
        Assert.False(policy.IsSkipped("1.2.0"));
    }

    [Fact]
    public void FileStoreRoundTripsAndSurvivesGarbage()
    {
        var dir = Directory.CreateTempSubdirectory("upd_state_").FullName;
        try
        {
            var file = Path.Combine(dir, "sub", "update-state.json");
            var store = new FileUpdateStateStore(file);
            Assert.Null(store.Read("a"));
            store.Write("a", "1");
            store.Write("b", "2");
            var again = new FileUpdateStateStore(file);
            Assert.Equal("1", again.Read("a"));
            Assert.Equal("2", again.Read("b"));
            File.WriteAllText(file, "{not json");
            Assert.Null(again.Read("a"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public sealed class UpdateClientTests : IDisposable
{
    static readonly byte[] Payload = Encoding.UTF8.GetBytes("installer-bytes");
    static readonly string Sha = Convert.ToHexStringLower(SHA256.HashData(Payload));
    static readonly UpdatePlatform Win = new("windows", "x64");

    readonly string _temp = Directory.CreateTempSubdirectory("upd_client_").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    static string Available(string sha = "", long size = 15, string name = "JackBean-1.0.0-windows-x64-setup.exe",
        string url = "https://github.com/jejezz/x/releases/download/v1.0.0/setup.exe", string extra = "") =>
        $$"""{"updateAvailable":true,"latest":"1.0.0","tag":"v1.0.0","notes":"n","releaseUrl":"https://github.com/jejezz/x/releases/tag/v1.0.0","asset":{"name":"{{name}}","url":"{{url}}","size":{{size}},"sha256":"{{(sha.Length == 0 ? Sha : sha)}}"}{{extra}} }""";

    UpdateClient Client(Handler h, string version = "1.0.0-rc.3", IUpdateInstaller? installer = null) =>
        new(new Uri("https://server.example/repos"), "daylight-commander-avalonia", version, Win, new HttpClient(h), installer, tempRoot: _temp);

    [Fact]
    public async Task CheckSendsAppVersionOsAndArch()
    {
        var h = new Handler(_ => Json("""{"updateAvailable":false,"latest":"1.0.0-rc.3"}"""));
        var result = await Client(h).CheckAsync();
        Assert.IsType<UpdateCheckResult.UpToDate>(result);
        Assert.Equal("/repos/api/update", h.Requests[0].AbsolutePath);
        Assert.Equal("?app=daylight-commander-avalonia&version=1.0.0-rc.3&os=windows&arch=x64", h.Requests[0].Query);
    }

    [Fact]
    public async Task AvailableUpdateCarriesVerifiedAssetInfo()
    {
        var result = await Client(new Handler(_ => Json(Available()))).CheckAsync();
        var info = Assert.IsType<UpdateCheckResult.Available>(result).Info;
        Assert.Equal("1.0.0", info.LatestVersion);
        Assert.Equal("1.0.0-rc.3", info.CurrentVersion);
        Assert.Equal(Sha, info.Asset.Sha256);
    }

    [Fact]
    public async Task ServerNeverMovesTheAppBackwards()
    {
        var result = await Client(new Handler(_ => Json(Available())), version: "1.0.1").CheckAsync();
        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    [Fact]
    public async Task MissingChecksumIsUnavailableNotInstallable()
    {
        var json = """{"updateAvailable":true,"latest":"1.0.0","reason":"no-checksum","releaseUrl":"https://github.com/x/y/releases/tag/v1.0.0","asset":{"name":"a.exe","url":"https://github.com/x/y/a.exe","size":5}}""";
        var u = Assert.IsType<UpdateCheckResult.Unavailable>(await Client(new Handler(_ => Json(json))).CheckAsync());
        Assert.Equal(UnavailableReason.NoChecksum, u.Reason);
        Assert.NotNull(u.ReleaseUrl);
    }

    [Fact]
    public async Task NoAssetForPlatformIsUnavailable()
    {
        var json = """{"updateAvailable":true,"latest":"1.0.0","asset":null,"reason":"no-asset-for-platform"}""";
        var u = Assert.IsType<UpdateCheckResult.Unavailable>(await Client(new Handler(_ => Json(json))).CheckAsync());
        Assert.Equal(UnavailableReason.NoAssetForPlatform, u.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, UpdateErrorKind.UnknownApp)]
    [InlineData(HttpStatusCode.InternalServerError, UpdateErrorKind.Server)]
    public async Task HttpErrorsBecomeFailedResults(HttpStatusCode code, UpdateErrorKind kind)
    {
        var failed = Assert.IsType<UpdateCheckResult.Failed>(await Client(new Handler(_ => Json("{}", code))).CheckAsync());
        Assert.Equal(kind, failed.Error.Kind);
    }

    [Fact]
    public async Task GarbageAndNetworkFailuresDoNotThrow()
    {
        var bad = Assert.IsType<UpdateCheckResult.Failed>(await Client(new Handler(_ => Json("not json"))).CheckAsync());
        Assert.Equal(UpdateErrorKind.BadResponse, bad.Error.Kind);
        var down = Assert.IsType<UpdateCheckResult.Failed>(await Client(new Handler(_ => throw new HttpRequestException("down"))).CheckAsync());
        Assert.Equal(UpdateErrorKind.Network, down.Error.Kind);
    }

    [Fact]
    public void PlainHttpServerIsRefusedExceptLoopback()
    {
        Assert.Throws<UpdateException>(() => new UpdateClient(new Uri("http://server.example/repos"), "app", "1.0.0", Win));
        using var loopback = new UpdateClient(new Uri("http://127.0.0.1:8080/repos"), "app", "1.0.0", Win);
    }

    [Fact]
    public async Task AutomaticCheckIsQuietWhenNotDueSkippedOrFailed()
    {
        var store = new MemoryUpdateStateStore();
        var policy = new UpdatePolicy(store);
        var client = Client(new Handler(_ => Json(Available())));
        Assert.IsType<UpdateCheckResult.Available>(await client.CheckAutomaticallyAsync(policy));
        Assert.Null(await client.CheckAutomaticallyAsync(policy)); // 하루 1회

        var policy2 = new UpdatePolicy(new MemoryUpdateStateStore());
        policy2.Skip("1.0.0");
        Assert.Null(await client.CheckAutomaticallyAsync(policy2));

        var failing = Client(new Handler(_ => throw new HttpRequestException()));
        var policy3 = new UpdatePolicy(new MemoryUpdateStateStore());
        Assert.Null(await failing.CheckAutomaticallyAsync(policy3));
        Assert.True(policy3.IsDue()); // 실패는 '확인함'으로 기록하지 않는다
    }

    [Fact]
    public async Task DownloadVerifiesAndReportsProgress()
    {
        var client = Client(new Handler(r => r.RequestUri!.Host == "github.com" ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) } : Json(Available())));
        var info = Assert.IsType<UpdateCheckResult.Available>(await client.CheckAsync()).Info;
        long last = 0;
        var done = await client.DownloadAsync(info, (got, _) => last = got);
        Assert.Equal(Payload.Length, last);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(done.File));
        client.Discard(done);
        Assert.False(Directory.Exists(done.Directory));
    }

    [Theory]
    [InlineData(100, UpdateErrorKind.SizeMismatch)] // 알린 것보다 작다
    [InlineData(5, UpdateErrorKind.SizeMismatch)] // 알린 것보다 크다
    public async Task DownloadRejectsWrongSize(long announced, UpdateErrorKind kind)
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }));
        var info = Info(size: announced);
        var e = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(info));
        Assert.Equal(kind, e.Kind);
        Assert.Empty(Directory.GetDirectories(_temp)); // 실패하면 임시 폴더를 남기지 않는다
    }

    [Fact]
    public async Task DownloadRejectsChecksumMismatch()
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }));
        var e = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(Info(sha: new string('0', 64))));
        Assert.Equal(UpdateErrorKind.ChecksumMismatch, e.Kind);
        Assert.Empty(Directory.GetDirectories(_temp));
    }

    [Theory]
    [InlineData("https://evil.example/setup.exe", "setup.exe")]
    [InlineData("https://github.com/x/setup.exe", "../setup.exe")]
    [InlineData("https://github.com/x/setup.exe", ".hidden")]
    public async Task DownloadRefusesUnsafeLocationOrName(string url, string name)
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }));
        await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(Info(url: url, name: name)));
    }

    [Fact]
    public async Task CancelledDownloadReportsCancelled()
    {
        var client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var e = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(Info(), ct: cts.Token));
        Assert.Equal(UpdateErrorKind.Cancelled, e.Kind);
    }

    [Fact]
    public void CleanUpRemovesOnlyOldUpdaterFolders()
    {
        var old = Directory.CreateDirectory(Path.Combine(_temp, "app_updater_old"));
        old.LastWriteTimeUtc = DateTime.UtcNow - TimeSpan.FromDays(3);
        var fresh = Directory.CreateDirectory(Path.Combine(_temp, "app_updater_fresh"));
        var other = Directory.CreateDirectory(Path.Combine(_temp, "unrelated"));
        other.LastWriteTimeUtc = DateTime.UtcNow - TimeSpan.FromDays(3);
        Client(new Handler(_ => Json("{}"))).CleanUpOldDownloads();
        Assert.False(old.Exists is true && Directory.Exists(old.FullName));
        Assert.True(Directory.Exists(fresh.FullName));
        Assert.True(Directory.Exists(other.FullName));
    }

    static UpdateInfo Info(long size = 15, string? sha = null, string url = "https://github.com/x/y/setup.exe", string name = "setup.exe") =>
        new("1.0.0-rc.3", "1.0.0", "v1.0.0", "", new UpdateAsset(name, new Uri(url), size, sha ?? Sha), null, null);
}

public sealed class UpdateInstallerTests
{
    sealed class FakeRunner(Func<string, IReadOnlyList<string>, int>? run = null) : ICommandRunner
    {
        public List<(string Exe, string[] Args)> Ran { get; } = [];
        public List<(string Exe, string[] Args)> Started { get; } = [];

        public int Run(string executable, IReadOnlyList<string> arguments)
        {
            Ran.Add((executable, [.. arguments]));
            return run?.Invoke(executable, arguments) ?? 0;
        }

        public void StartDetached(string executable, IReadOnlyList<string> arguments) => Started.Add((executable, [.. arguments]));
    }

    static DownloadedUpdate Downloaded(string dir, string name) =>
        new(new UpdateInfo("1.0.0-rc.3", "1.0.0", "v1.0.0", "", new UpdateAsset(name, new Uri("https://github.com/x"), 1, new string('a', 64)), null, null),
            Path.Combine(dir, name));

    [Fact]
    public void MacosOpensTheDmgAndLeavesTheAppRunning()
    {
        var runner = new FakeRunner();
        var outcome = new MacosInstaller(runner).Install(Downloaded("/tmp/x", "a.dmg"));
        Assert.Equal(InstallOutcome.OpenedForUser, outcome);
        Assert.Equal("open", runner.Ran[0].Exe);
        Assert.Equal([Path.Combine("/tmp/x", "a.dmg")], runner.Ran[0].Args); // 구분자는 OS마다 다르다 (Windows는 \)
    }

    [Fact]
    public void MacosFailureIsReported()
    {
        var e = Assert.Throws<UpdateException>(() => new MacosInstaller(new FakeRunner((_, _) => 1)).Install(Downloaded("/tmp/x", "a.dmg")));
        Assert.Equal(UpdateErrorKind.InstallFailed, e.Kind);
    }

    [Fact]
    public void WindowsOpensSetupThroughExplorer()
    {
        var runner = new FakeRunner();
        var outcome = new WindowsInstaller(runner).Install(Downloaded(@"C:\t", "a-setup.exe"));
        Assert.Equal(InstallOutcome.InstallerStarted, outcome);
        Assert.Equal("explorer.exe", runner.Started[0].Exe);
    }

    [Fact]
    public void WindowsRefusesAZipInsteadOfSilentlyOpeningIt()
    {
        var runner = new FakeRunner();
        var e = Assert.Throws<UpdateException>(() => new WindowsInstaller(runner).Install(Downloaded(@"C:\t", "a.zip")));
        Assert.Equal(UpdateErrorKind.InstallFailed, e.Kind);
        Assert.Empty(runner.Started);
    }

    [Fact]
    public void LinuxChecksArchiveBeforeExitThenHandsOverToDetachedHelper()
    {
        var dir = Directory.CreateTempSubdirectory("upd_linux_").FullName;
        try
        {
            // tar를 실제로 돌리지 않고, 풀린 결과를 가짜 러너가 만들어 준다.
            var runner = new FakeRunner((exe, args) =>
            {
                var top = Directory.CreateDirectory(Path.Combine(args[3], "App"));
                File.WriteAllText(Path.Combine(top.FullName, "install.sh"), "#!/bin/sh");
                Directory.CreateDirectory(Path.Combine(top.FullName, "share", "applications"));
                File.WriteAllText(Path.Combine(top.FullName, "share", "applications", "art.zoomon.test.desktop"), "");
                return 0;
            });
            var outcome = new LinuxInstaller(runner, currentPid: 4242).Install(Downloaded(dir, "a.tar.gz"));
            Assert.Equal(InstallOutcome.InstallerStarted, outcome);
            Assert.Equal("tar", runner.Ran[0].Exe);
            var (exe, args) = runner.Started[0];
            Assert.Equal("sh", exe);
            Assert.Equal("4242", args[3]);
            Assert.EndsWith("install.sh", args[4]);
            Assert.Equal("art.zoomon.test", args[5]); // 앱 ID는 .desktop 파일 이름에서
            Assert.Equal(dir, args[6]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LinuxRejectsAnArchiveWithoutInstallSh()
    {
        var dir = Directory.CreateTempSubdirectory("upd_linux_").FullName;
        try
        {
            var runner = new FakeRunner((_, args) => { Directory.CreateDirectory(Path.Combine(args[3], "App")); return 0; });
            var e = Assert.Throws<UpdateException>(() => new LinuxInstaller(runner).Install(Downloaded(dir, "a.tar.gz")));
            Assert.Equal(UpdateErrorKind.InstallFailed, e.Kind);
            Assert.Empty(runner.Started);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
