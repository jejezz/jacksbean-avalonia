using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using JackBean.Domain.Policy;
using JackBean.Domain.Settings;
using JackBean.Services.GitHub;
using JackBean.Services.Sync;

namespace JackBean.Services.Tests;

/// <summary>GitHub 의 일부를 흉내 내는 가짜 서버(메모리). 사용자·저장소·토픽·Contents API(sha 로 낙관적 잠금)와 Device Flow.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string ValidToken = "tok-valid";

    public sealed record Repo(string FullName, bool IsPrivate, List<string> Topics);

    public Dictionary<string, (string Text, string Sha)> Files { get; } = [];   // "소유자/이름:경로" → 내용
    public List<Repo> Repos { get; } = [];
    public List<string> Requests { get; } = [];
    public Func<HttpRequestMessage, string, HttpResponseMessage?>? Override { get; set; }
    public Action<string>? BeforePut { get; set; }   // 올리기 직전에(그 사이 다른 PC 가 먼저 올린 상황을 만들려고)
    public bool Offline { get; set; }
    private int _sha;

    // Device Flow
    public Queue<JsonObject> TokenResponses { get; } = new();

    public string NextSha() => $"sha{++_sha:x4}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Offline) throw new HttpRequestException("offline");
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method.Method;
        Requests.Add($"{method} {path}");
        if (Override?.Invoke(request, body) is { } custom) return custom;

        // Device Flow(github.com)
        if (path == "/login/device/code") return Json(new JsonObject { ["device_code"] = "dc", ["user_code"] = "ABCD-1234", ["verification_uri"] = "https://github.com/login/device", ["interval"] = 1, ["expires_in"] = 900 });
        if (path == "/login/oauth/access_token") return Json(TokenResponses.Count > 0 ? TokenResponses.Dequeue() : new JsonObject { ["error"] = "authorization_pending" });

        // 인증
        var auth = request.Headers.Authorization?.Parameter;
        if (auth != ValidToken) return Json(new JsonObject { ["message"] = "Bad credentials" }, HttpStatusCode.Unauthorized);

        if (path == "/user" && method == "GET") return Json(new JsonObject { ["login"] = "octo", ["id"] = 42, ["name"] = "Octo Cat" });
        if (path == "/user/repos" && method == "GET")
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["page"] ?? "1");
            return Json(new JsonArray(Repos.Skip((page - 1) * 100).Take(100).Select(RepoJson).ToArray()));
        }
        if (path == "/user/repos" && method == "POST")
        {
            var name = (string)JsonNode.Parse(body)!["name"]!;
            if (Repos.Any(r => r.FullName == $"octo/{name}")) return Json(new JsonObject { ["message"] = "Repository creation failed.", ["errors"] = new JsonArray(new JsonObject { ["message"] = "name already exists on this account" }) }, HttpStatusCode.UnprocessableEntity);
            var repo = new Repo($"octo/{name}", (bool)JsonNode.Parse(body)!["private"]!, []);
            Repos.Add(repo);
            return Json(RepoJson(repo), HttpStatusCode.Created);
        }

        var parts = path.Trim('/').Split('/');   // repos/{o}/{r}/...
        if (parts.Length >= 4 && parts[0] == "repos")
        {
            var full = $"{parts[1]}/{parts[2]}";
            var repo = Repos.FirstOrDefault(r => r.FullName == full);
            if (repo == null) return Json(new JsonObject { ["message"] = "Not Found" }, HttpStatusCode.NotFound);
            if (parts[3] == "topics")
            {
                if (method == "GET") return Json(new JsonObject { ["names"] = new JsonArray(repo.Topics.Select(t => (JsonNode?)t).ToArray()) });
                repo.Topics.Clear();
                repo.Topics.AddRange(((JsonArray)JsonNode.Parse(body)!["names"]!).Select(n => (string)n!));
                return Json(new JsonObject { ["names"] = new JsonArray(repo.Topics.Select(t => (JsonNode?)t).ToArray()) });
            }
            if (parts[3] == "contents" && parts.Length >= 5)
            {
                var key = $"{full}:{Uri.UnescapeDataString(parts[4])}";
                if (method == "GET")
                    return Files.TryGetValue(key, out var f)
                        ? Json(new JsonObject { ["sha"] = f.Sha, ["encoding"] = "base64", ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(f.Text)).Replace("=", "=\n") })
                        : Json(new JsonObject { ["message"] = "Not Found" }, HttpStatusCode.NotFound);
                if (method == "PUT")
                {
                    BeforePut?.Invoke(key);
                    var j = JsonNode.Parse(body)!;
                    var sentSha = (string?)j["sha"];
                    var exists = Files.TryGetValue(key, out var current);
                    if (exists && sentSha == null) return Json(new JsonObject { ["message"] = "Invalid request. \"sha\" wasn't supplied." }, HttpStatusCode.UnprocessableEntity);
                    if (exists && sentSha != current.Sha) return Json(new JsonObject { ["message"] = $"{parts[4]} does not match {sentSha}" }, HttpStatusCode.Conflict);
                    var sha = NextSha();
                    Files[key] = (Encoding.UTF8.GetString(Convert.FromBase64String((string)j["content"]!)), sha);
                    return Json(new JsonObject { ["content"] = new JsonObject { ["sha"] = sha } }, exists ? HttpStatusCode.OK : HttpStatusCode.Created);
                }
            }
        }
        return Json(new JsonObject { ["message"] = "Not Found" }, HttpStatusCode.NotFound);
    }

    private static JsonObject RepoJson(Repo r) => new()
    {
        ["full_name"] = r.FullName, ["private"] = r.IsPrivate, ["description"] = null,
        ["topics"] = new JsonArray(r.Topics.Select(t => (JsonNode?)t).ToArray()),
    };

    public static HttpResponseMessage Json(JsonNode body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

public class GitHubApiTests
{
    [Fact]
    public async Task UserAndReposAndCreate()
    {
        var gh = new FakeGitHub();
        using var api = new GitHubApi(FakeGitHub.ValidToken, gh);
        var user = await api.UserAsync();
        Assert.Equal("octo", user.Login);
        Assert.Equal(42, user.Id);

        gh.Repos.AddRange(Enumerable.Range(0, 130).Select(i => new FakeGitHub.Repo($"octo/r{i}", true, [])));   // 100개씩 두 쪽
        Assert.Equal(130, (await api.ReposAsync()).Count);

        var created = await api.CreateRepoAsync("jacksbean-policy");
        Assert.Equal("octo/jacksbean-policy", created.FullName);
        Assert.True(created.IsPrivate);
        var exists = await Assert.ThrowsAsync<GitHubException>(() => api.CreateRepoAsync("jacksbean-policy"));
        Assert.Equal(422, exists.StatusCode);
    }

    [Fact]
    public async Task TopicIsAddedOnceAndKeepsOthers()
    {
        var gh = new FakeGitHub();
        gh.Repos.Add(new FakeGitHub.Repo("octo/p", true, ["other"]));
        using var api = new GitHubApi(FakeGitHub.ValidToken, gh);
        await api.EnsureTopicAsync("octo/p");
        await api.EnsureTopicAsync("octo/p");
        Assert.Equal(["other", GitHubRepo.AppTopic], gh.Repos[0].Topics);
        Assert.Equal(1, gh.Requests.Count(r => r.StartsWith("PUT") && r.EndsWith("/topics")));   // 두 번째는 쓰지 않는다
    }

    [Fact]
    public async Task FileRoundTripWithUnicodeAndOptimisticLocking()
    {
        var gh = new FakeGitHub();
        gh.Repos.Add(new FakeGitHub.Repo("octo/p", true, []));
        using var api = new GitHubApi(FakeGitHub.ValidToken, gh);
        Assert.Null(await api.GetFileAsync("octo/p", "policy.windows.json"));

        var sha1 = await api.PutFileAsync("octo/p", "policy.windows.json", "{ \"앱\": \"한글 ✓\" }", null, "first");
        var read = await api.GetFileAsync("octo/p", "policy.windows.json");
        Assert.Equal("{ \"앱\": \"한글 ✓\" }", read!.Text);
        Assert.Equal(sha1, read.Sha);

        var sha2 = await api.PutFileAsync("octo/p", "policy.windows.json", "{}", sha1, "second");
        Assert.NotEqual(sha1, sha2);
        await Assert.ThrowsAsync<GitHubConflictException>(() => api.PutFileAsync("octo/p", "policy.windows.json", "{ }", sha1, "stale"));     // 옛 sha
        await Assert.ThrowsAsync<GitHubConflictException>(() => api.PutFileAsync("octo/p", "policy.windows.json", "{ }", null, "no sha"));   // sha 없이 덮어쓰기
    }

    [Fact]
    public async Task BadTokenIsUnauthorizedAndNetworkFailureIsOffline()
    {
        var gh = new FakeGitHub();
        using var bad = new GitHubApi("nope", gh);
        var unauthorized = await Assert.ThrowsAsync<GitHubException>(() => bad.UserAsync());
        Assert.True(unauthorized.Unauthorized);

        gh.Offline = true;
        using var api = new GitHubApi(FakeGitHub.ValidToken, gh);
        var offline = await Assert.ThrowsAsync<GitHubException>(() => api.UserAsync());
        Assert.True(offline.Offline);
    }
}

public class DeviceFlowTests
{
    private static JsonObject Pending => new() { ["error"] = "authorization_pending" };

    [Fact]
    public async Task ShowsTheCodeThenWaitsForApproval()
    {
        var gh = new FakeGitHub();
        var waits = new List<TimeSpan>();
        using var flow = new DeviceFlow("client", gh, delay: (t, _) => { waits.Add(t); return Task.CompletedTask; });
        var code = await flow.StartAsync();
        Assert.Equal("ABCD-1234", code.UserCode);
        Assert.Equal("https://github.com/login/device", code.VerificationUri);

        gh.TokenResponses.Enqueue(Pending);
        gh.TokenResponses.Enqueue(new JsonObject { ["error"] = "slow_down", ["interval"] = 10 });   // 더 천천히 물어보라고 한다
        gh.TokenResponses.Enqueue(new JsonObject { ["access_token"] = "gho_abc" });
        Assert.Equal("gho_abc", await flow.AwaitTokenAsync(code));
        Assert.Equal([1, 1, 10], waits.Select(w => (int)w.TotalSeconds));   // slow_down 뒤에는 간격이 늘어난다
    }

    [Theory]
    [InlineData("expired_token", "만료")]
    [InlineData("access_denied", "거절")]
    public async Task ExpiredOrDeniedStopsWithAReason(string error, string expected)
    {
        var gh = new FakeGitHub();
        using var flow = new DeviceFlow("client", gh, delay: (_, _) => Task.CompletedTask);
        var code = await flow.StartAsync();
        gh.TokenResponses.Enqueue(new JsonObject { ["error"] = error });
        var e = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.AwaitTokenAsync(code));
        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public async Task CancellingStopsWaiting()
    {
        var gh = new FakeGitHub();
        using var cts = new CancellationTokenSource();
        using var flow = new DeviceFlow("client", gh, delay: (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        var code = await flow.StartAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.AwaitTokenAsync(code, cts.Token));
    }

    [Fact]
    public async Task ClientErrorsFromGitHubAreReported()
    {
        var gh = new FakeGitHub { Override = (r, _) => r.RequestUri!.AbsolutePath == "/login/device/code" ? FakeGitHub.Json(new JsonObject { ["error"] = "device_flow_disabled", ["error_description"] = "Device Flow must be explicitly enabled" }) : null };
        using var flow = new DeviceFlow("client", gh);
        var e = await Assert.ThrowsAsync<DeviceFlowException>(() => flow.StartAsync());
        Assert.Contains("Device Flow", e.Message);
    }
}

public class TokenStoreTests
{
    [Fact]
    public void MemoryStoreRoundTrip()
    {
        var store = new MemoryTokenStore();
        Assert.False(store.IsPersistent);
        Assert.Null(store.Read());
        store.Write("t");
        Assert.Equal("t", store.Read());
        store.Delete();
        Assert.Null(store.Read());
    }

    [Fact]
    public void WindowsCredentialManagerRoundTripWithAnIsolatedName()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = TokenStores.Create("JacksBeanTest-" + Guid.NewGuid().ToString("N"));   // 진짜 로그인 토큰과 겹치지 않는 이름
        try
        {
            Assert.True(store.IsPersistent);
            Assert.Null(store.Read());
            store.Write("ghp_한글토큰_123");
            Assert.Equal("ghp_한글토큰_123", store.Read());
            store.Write("second");                       // 덮어쓴다
            Assert.Equal("second", store.Read());
            store.Delete();
            Assert.Null(store.Read());
            store.Delete();                              // 이미 없어도 괜찮다
        }
        finally { store.Delete(); }
    }
}

internal sealed class MemorySettingsRepository : ISettingsRepository
{
    public AppSettings Saved { get; private set; } = new();
    public AppSettings Load() => Saved;
    public void Save(AppSettings settings) => Saved = settings;
}

public sealed class GitHubSyncServiceTests : IDisposable
{
    private readonly FakeGitHub _gh = new();
    private readonly List<TempDir> _dirs = [];
    private readonly List<SettingsController> _settings = [];

    public GitHubSyncServiceTests() => _gh.Repos.Add(new FakeGitHub.Repo("octo/policy", true, []));

    public void Dispose()
    {
        foreach (var s in _settings) s.Dispose();
        foreach (var d in _dirs) d.Dispose();
    }

    private sealed record Pc(PolicyStore Store, GitHubSyncService Service, SettingsController Settings, MemoryTokenStore Tokens);

    private Pc NewPc(bool signedIn = true, bool connected = true)
    {
        var dir = new TempDir();
        _dirs.Add(dir);
        var store = new PolicyStore(PolicyOs.Windows, dir.Path);
        var settings = new SettingsController(new MemorySettingsRepository());
        _settings.Add(settings);
        var tokens = new MemoryTokenStore();
        if (signedIn) { tokens.Write(FakeGitHub.ValidToken); settings.Update(s => s with { SyncLogin = "octo" }); }
        if (connected) settings.Update(s => s with { SyncRepo = "octo/policy" });
        var service = new GitHubSyncService(settings, tokens, store, Path.Combine(dir.Path, "sync"), token => new GitHubApi(token, _gh));
        return new Pc(store, service, settings, tokens);
    }

    private static RuleEdit Ext(string id, string[] exts, params string[] apps) => new(id, RuleCondition.Extension(exts)!, apps);
    private const string Policy = "octo/policy:policy.windows.json";

    // --- 로그인 ---

    [Fact]
    public async Task SignInStoresTheTokenOnlyInTheSecureStoreAndTheNameInSettings()
    {
        var pc = NewPc(signedIn: false);
        Assert.False(pc.Service.LoggedIn);
        var user = await pc.Service.SignInAsync("  " + FakeGitHub.ValidToken + " ");
        Assert.Equal("octo", user.Login);
        Assert.True(pc.Service.LoggedIn);
        Assert.Equal(FakeGitHub.ValidToken, pc.Tokens.Read());
        Assert.Equal("octo", pc.Settings.Value.SyncLogin);
        Assert.DoesNotContain(FakeGitHub.ValidToken, System.Text.Json.JsonSerializer.Serialize(pc.Settings.Value.SyncLogin));   // 설정에는 이름만
    }

    [Fact]
    public async Task BadTokenIsNotStored()
    {
        var pc = NewPc(signedIn: false);
        var e = await Assert.ThrowsAsync<GitHubException>(() => pc.Service.SignInAsync("wrong"));
        Assert.True(e.Unauthorized);
        Assert.Null(pc.Tokens.Read());
        Assert.False(pc.Service.LoggedIn);
        await Assert.ThrowsAsync<GitHubException>(() => pc.Service.SignInAsync("   "));
    }

    [Fact]
    public void SignOutRemovesTheTokenButKeepsTheRepository()
    {
        var pc = NewPc();
        Assert.True(pc.Service.CanSync);
        pc.Service.SignOut();
        Assert.False(pc.Service.LoggedIn);
        Assert.Null(pc.Tokens.Read());
        Assert.Equal("octo/policy", pc.Service.Repo);
        Assert.False(pc.Service.CanSync);
    }

    // --- 저장소 ---

    [Fact]
    public async Task RepositoryListShowsTheAppsOwnRepositoriesFirstAndConnectMarksTheTopic()
    {
        var pc = NewPc(connected: false);
        _gh.Repos.Add(new FakeGitHub.Repo("octo/mine", true, [GitHubRepo.AppTopic]));
        var repos = await pc.Service.ReposAsync();
        Assert.Equal("octo/mine", repos[0].FullName);   // 토픽 표식이 있는 저장소가 맨 위

        await pc.Service.ConnectAsync(repos.First(r => r.FullName == "octo/policy"));
        Assert.Equal("octo/policy", pc.Service.Repo);
        Assert.Contains(GitHubRepo.AppTopic, _gh.Repos.First(r => r.FullName == "octo/policy").Topics);

        var created = await pc.Service.CreateRepoAsync("jacksbean-policy");
        Assert.Equal("octo/jacksbean-policy", created.FullName);
    }

    // --- 동기화 ---

    [Fact]
    public async Task SyncNeedsLoginAndRepository()
    {
        Assert.False((await NewPc(signedIn: false).Service.SyncAsync()).Ok);
        var noRepo = await NewPc(connected: false).Service.SyncAsync();
        Assert.False(noRepo.Ok);
    }

    [Fact]
    public async Task TwoPcsExchangeChangesThroughTheRepository()
    {
        var a = NewPc();
        var b = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        await a.Store.UpsertRuleAsync(Ext("img", ["png"], "viewer"));

        var first = await a.Service.SyncAsync();
        Assert.True(first.Ok, first.Error);
        Assert.True(first.FirstSync);
        Assert.True(first.ChangedRemote);
        Assert.Contains("\"viewer\"", _gh.Files[Policy].Text);                // GitHub 에 올라갔다

        await b.Store.UpsertAppAsync(new AppUpsert("player", "player.exe", ["{path}"]));
        await b.Store.UpsertRuleAsync(Ext("music", ["mp3"], "player"));
        var second = await b.Service.SyncAsync();
        Assert.True(second.Ok, second.Error);
        Assert.True(second.ChangedLocal);                                      // A 것을 받았고
        Assert.True(second.ChangedRemote);                                     // B 것을 올렸다
        var docB = await b.Store.LoadAsync();
        Assert.Contains("viewer", docB.Apps.Keys);
        Assert.Contains("player", docB.Apps.Keys);
        Assert.Empty(docB.Warnings);

        Assert.True((await a.Service.SyncAsync()).Ok);                         // A 도 B 것을 받는다
        Assert.Contains("music", (await a.Store.LoadAsync()).Rules.Select(r => r.Id));

        var again = await a.Service.SyncAsync();                               // 바뀐 것이 없으면 올리지 않는다
        Assert.True(again.Ok);
        Assert.False(again.ChangedLocal);
        Assert.False(again.ChangedRemote);
    }

    [Fact]
    public async Task ConflictTakesTheMoreRecentEditAndDeletionPropagates()
    {
        var a = NewPc();
        var b = NewPc();
        var old = DateTime.Parse("2026-01-01T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]), old);
        await a.Store.UpsertRuleAsync(Ext("img", ["qqq"], "viewer"));
        Assert.True((await a.Service.SyncAsync()).Ok);
        Assert.True((await b.Service.SyncAsync()).Ok);

        // 충돌: 같은 앱을 양쪽에서 고쳤고 B 가 더 최근이다.
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer-A.exe", ["{path}"]), old.AddMonths(5));
        await b.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer-B.exe", ["{path}"]), old.AddMonths(8));
        Assert.True((await a.Service.SyncAsync()).Ok);
        var conflict = await b.Service.SyncAsync();
        Assert.True(conflict.Ok, conflict.Error);
        Assert.Contains(conflict.MergeNotes, n => n is { Kind: MergeNoteKind.ConflictResolved, Id: "viewer" });
        Assert.Equal("viewer-B.exe", (await b.Store.LoadAsync()).Apps["viewer"].Command);
        Assert.True((await a.Service.SyncAsync()).Ok);
        Assert.Equal("viewer-B.exe", (await a.Store.LoadAsync()).Apps["viewer"].Command);

        // 삭제: A 가 규칙을 지웠고 B 는 안 고쳤다.
        Assert.True((await a.Store.RemoveRuleAsync("img")).Ok);
        Assert.True((await a.Service.SyncAsync()).Ok);
        var removed = await b.Service.SyncAsync();
        Assert.True(removed.Ok, removed.Error);
        Assert.DoesNotContain("img", (await b.Store.LoadAsync()).Rules.Select(r => r.Id));
    }

    [Fact]
    public async Task WhenAnotherPcUploadsFirstTheSyncReadsAgainAndMerges()
    {
        var a = NewPc();
        var b = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        Assert.True((await a.Service.SyncAsync()).Ok);

        await b.Store.UpsertAppAsync(new AppUpsert("player", "player.exe", ["{path}"]));
        // B 가 읽은 뒤, 올리기 직전에 A 가 먼저 올린다 → B 의 첫 올리기는 sha 가 어긋난다.
        var injected = false;
        _gh.BeforePut = key =>
        {
            if (injected) return;
            injected = true;
            var sneaky = JsonNode.Parse(_gh.Files[key].Text)!.AsObject();
            sneaky["apps"]!["sneaky"] = new JsonObject { ["command"] = "sneaky.exe", ["args"] = new JsonArray("{path}") };
            _gh.Files[key] = (PolicyStore.SerializePolicy(sneaky), _gh.NextSha());
        };
        var result = await b.Service.SyncAsync();
        Assert.True(result.Ok, result.Error);
        Assert.True(injected);
        var docB = await b.Store.LoadAsync();
        Assert.Contains("sneaky", docB.Apps.Keys);    // 그 사이 올라온 것도 합쳤다
        Assert.Contains("player", docB.Apps.Keys);
        Assert.Contains("sneaky", _gh.Files[Policy].Text);
        Assert.Contains("player", _gh.Files[Policy].Text);
    }

    [Fact]
    public async Task RejectedTokenAsksToSignInAgainAndOfflineIsReported()
    {
        var pc = NewPc();
        await pc.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        var before = File.ReadAllText(pc.Store.FilePath);

        pc.Tokens.Write("revoked");
        var revoked = new GitHubSyncService(pc.Settings, pc.Tokens, pc.Store, Path.Combine(_dirs[0].Path, "sync"), t => new GitHubApi(t, _gh));
        var result = await revoked.SyncAsync();
        Assert.False(result.Ok);
        Assert.True(result.AuthFailed);
        Assert.True(revoked.NeedsReauth);

        _gh.Offline = true;
        var offline = await NewPc().Service.SyncAsync();
        Assert.False(offline.Ok);
        Assert.True(offline.Offline);
        Assert.Equal(before, File.ReadAllText(pc.Store.FilePath));             // 실패하면 이 PC 파일은 그대로
    }

    [Fact]
    public async Task BrokenRemoteFileStopsWithoutTouchingTheLocalFile()
    {
        var pc = NewPc();
        await pc.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        _gh.Files[Policy] = ("{ 깨진", _gh.NextSha());
        var before = File.ReadAllText(pc.Store.FilePath);

        var result = await pc.Service.SyncAsync();
        Assert.False(result.Ok);
        Assert.Contains("JSON", result.Error);
        Assert.Equal(before, File.ReadAllText(pc.Store.FilePath));
        Assert.Equal("{ 깨진", _gh.Files[Policy].Text);                         // 원격도 건드리지 않았다

        _gh.Files[Policy] = ("""{"schema":1,"os":"windows"}""", _gh.NextSha());
        Assert.Contains("schema", (await pc.Service.SyncAsync()).Error);       // 다른 형식도 막는다
    }
}
