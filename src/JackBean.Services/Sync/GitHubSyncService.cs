using JackBean.Services.GitHub;

namespace JackBean.Services.Sync;

/// <summary>
/// GitHub 로그인·저장소 연결·동기화를 한곳에서 관리한다. 토큰은 OS 보안 저장소(<see cref="ITokenStore"/>)에만 있고 설정 파일에는
/// 로그인 이름(`sync_login`)과 연결한 저장소 이름(`sync_repo`)만 남는다. UI 와 무관해서 가짜 서버로 시험한다.
/// </summary>
public sealed class GitHubSyncService
{
    private readonly SettingsController _settings;
    private readonly ITokenStore _tokens;
    private readonly PolicyStore _store;
    private readonly string _workDirectory;
    private readonly Func<string, GitHubApi> _apiFactory;
    private string? _token;

    public GitHubSyncService(SettingsController settings, ITokenStore tokens, PolicyStore store, string workDirectory, Func<string, GitHubApi>? apiFactory = null)
    {
        _settings = settings;
        _tokens = tokens;
        _store = store;
        _workDirectory = workDirectory;
        _apiFactory = apiFactory ?? (token => new GitHubApi(token));
        _token = tokens.Read();
    }

    public string? Login => _settings.Value.SyncLogin;
    public string? Repo => _settings.Value.SyncRepo;
    public bool LoggedIn => _token != null && Login != null;

    /// <summary>로그인과 저장소 연결이 모두 되어 동기화할 수 있다.</summary>
    public bool CanSync => LoggedIn && !string.IsNullOrWhiteSpace(Repo);

    /// <summary>토큰이 거절되어 다시 로그인해야 한다.</summary>
    public bool NeedsReauth { get; private set; }

    /// <summary>토큰이 다음 실행에도 남는다(OS 보안 저장소). false 면 이번 실행에서만 로그인 상태다.</summary>
    public bool TokenPersistent => _tokens.IsPersistent;

    /// <summary>토큰으로 로그인한다(Device Flow 로 받은 토큰이든 직접 만든 토큰이든). 사용자를 확인한 뒤에야 저장한다.</summary>
    public async Task<GitHubUser> SignInAsync(string token, CancellationToken ct = default)
    {
        token = token.Trim();
        if (token.Length == 0) throw new GitHubException("토큰이 비어 있습니다");
        using var api = _apiFactory(token);
        var user = await api.UserAsync(ct);
        _tokens.Write(token);
        _token = token;
        NeedsReauth = false;
        _settings.Update(s => s with { SyncLogin = user.Login });
        return user;
    }

    /// <summary>로그아웃: 토큰을 지운다. 연결한 저장소 이름은 그대로 둔다(다시 로그인하면 이어서 쓴다).</summary>
    public void SignOut()
    {
        _tokens.Delete();
        _token = null;
        _settings.Update(s => s with { SyncLogin = null });
    }

    private GitHubApi Api() => _apiFactory(_token ?? throw new GitHubException("GitHub 에 로그인하지 않았습니다", 401));

    public async Task<IReadOnlyList<GitHubRepo>> ReposAsync(CancellationToken ct = default)
    {
        using var api = Api();
        var repos = await Guard(() => api.ReposAsync(ct: ct));
        return repos.OrderByDescending(r => r.IsAppRepo).ToList();   // 이 앱이 쓰던 저장소를 맨 위에
    }

    public async Task<GitHubRepo> CreateRepoAsync(string name, CancellationToken ct = default)
    {
        using var api = Api();
        return await Guard(() => api.CreateRepoAsync(name, isPrivate: true, "Jack’s Bean policy sync", ct));
    }

    /// <summary>이 저장소를 정책 동기화에 쓴다. 토픽 표식(다른 PC 에서 바로 제안하려는 편의)은 못 붙여도 연결은 유효하다.</summary>
    public async Task ConnectAsync(GitHubRepo repo, CancellationToken ct = default)
    {
        _settings.Update(s => s with { SyncRepo = repo.FullName });
        try { using var api = Api(); await api.EnsureTopicAsync(repo.FullName, ct); }
        catch (GitHubException) { /* 권한이 없거나 네트워크가 끊겼다 — 표식은 편의 기능이라 무시한다 */ }
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!LoggedIn) return new SyncResult(false, "GitHub 에 로그인하지 않았습니다", AuthFailed: true);
        if (string.IsNullOrWhiteSpace(Repo)) return new SyncResult(false, "동기화할 저장소를 고르지 않았습니다");
        using var api = Api();
        var remote = new GitHubPolicyRemote(api, Repo!, Path.GetFileName(_store.FilePath));
        var result = await new PolicySync(_store, _workDirectory, remote).SyncAsync(ct);
        if (result.AuthFailed) NeedsReauth = true;
        return result;
    }

    private async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (GitHubException e) when (e.Unauthorized) { NeedsReauth = true; throw; }
    }
}
