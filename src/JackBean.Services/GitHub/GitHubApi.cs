using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Services.GitHub;

/// <summary>GitHub 와의 통신이 실패했다. <see cref="Unauthorized"/> = 토큰이 거절됐다(다시 로그인), <see cref="Offline"/> = 네트워크에 닿지 못했다.</summary>
public class GitHubException(string message, int? statusCode = null, bool offline = false) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public bool Unauthorized => StatusCode == 401;
    public bool Offline { get; } = offline;
}

/// <summary>파일을 올리려는 사이에 다른 곳에서 먼저 바꿨다(sha 가 어긋남) — 다시 받아서 합쳐야 한다.</summary>
public sealed class GitHubConflictException(string message) : GitHubException(message, 409);

public sealed record GitHubUser(string Login, long Id, string? Name);

/// <summary>앱이 쓰는 저장소에 붙이는 GitHub 토픽 — 새 PC 에서 로그인하면 이 표식으로 저장소를 바로 제안한다.</summary>
public sealed record GitHubRepo(string FullName, bool IsPrivate, string? Description, IReadOnlyList<string> Topics)
{
    public const string AppTopic = "jacksbean";

    public bool IsAppRepo => Topics.Contains(AppTopic);
    public string Name => FullName.Split('/')[^1];
}

/// <summary>저장소의 파일 하나: 글과, 올릴 때 "내가 본 것이 이것"이라고 알리는 sha.</summary>
public sealed record RemoteFile(string Text, string Sha);

/// <summary>
/// 필요한 GitHub REST 호출만: 사용자, 저장소 목록·만들기, 토픽, 파일 읽기·쓰기(Contents API). git 이 없어도 된다.
/// 파일을 쓸 때는 읽은 sha 를 함께 보내서, 그 사이 다른 곳에서 바꿨으면 <see cref="GitHubConflictException"/> 으로 알려 준다(낙관적 잠금).
/// </summary>
public sealed class GitHubApi : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <param name="handler">시험에서 가짜 서버를 끼워 넣는다.</param>
    public GitHubApi(string token, HttpMessageHandler? handler = null, string baseUrl = "https://api.github.com")
    {
        _http = handler == null ? new HttpClient { Timeout = Timeout } : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("JacksBean");
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public void Dispose() => _http.Dispose();

    public async Task<GitHubUser> UserAsync(CancellationToken ct = default)
    {
        var j = (await SendAsync(HttpMethod.Get, "/user", null, ct)).Body as JsonObject ?? throw new GitHubException("GitHub 응답을 읽지 못했습니다");
        return new GitHubUser((string)j["login"]!, (long)j["id"]!, (string?)j["name"]);
    }

    /// <summary>내가 쓸 수 있는 저장소를 최근 수정순으로(최대 <paramref name="maxPages"/>×100개).</summary>
    public async Task<IReadOnlyList<GitHubRepo>> ReposAsync(int maxPages = 5, CancellationToken ct = default)
    {
        var all = new List<GitHubRepo>();
        for (var page = 1; page <= maxPages; page++)
        {
            var list = (await SendAsync(HttpMethod.Get, $"/user/repos?per_page=100&page={page}&sort=updated&affiliation=owner,collaborator,organization_member", null, ct)).Body as JsonArray
                       ?? throw new GitHubException("GitHub 응답을 읽지 못했습니다");
            all.AddRange(list.OfType<JsonObject>().Select(ToRepo));
            if (list.Count < 100) break;
        }
        return all;
    }

    /// <summary>
    /// 새 저장소를 만든다. 비어 있지 않게(`auto_init`) 만든다 — 빈 저장소는 파일을 올릴 첫 브랜치가 없어서 API 로 올리기 어렵다.
    /// </summary>
    public async Task<GitHubRepo> CreateRepoAsync(string name, bool isPrivate = true, string? description = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["name"] = name, ["private"] = isPrivate, ["auto_init"] = true };
        if (description != null) body["description"] = description;
        var j = (await SendAsync(HttpMethod.Post, "/user/repos", body, ct)).Body as JsonObject ?? throw new GitHubException("GitHub 응답을 읽지 못했습니다");
        return ToRepo(j);
    }

    /// <summary>저장소에 <see cref="GitHubRepo.AppTopic"/> 표식을 붙인다(이미 있으면 그대로). 토픽 설정은 전체를 교체하므로 기존 것을 읽어 합친다.</summary>
    public async Task EnsureTopicAsync(string fullName, CancellationToken ct = default)
    {
        var j = (await SendAsync(HttpMethod.Get, $"/repos/{fullName}/topics", null, ct)).Body as JsonObject;
        var names = (j?["names"] as JsonArray)?.Select(n => (string)n!).ToList() ?? [];
        if (names.Contains(GitHubRepo.AppTopic)) return;
        names.Add(GitHubRepo.AppTopic);
        await SendAsync(HttpMethod.Put, $"/repos/{fullName}/topics", new JsonObject { ["names"] = new JsonArray(names.Select(n => (JsonNode?)n).ToArray()) }, ct);
    }

    /// <summary>저장소 루트의 파일 하나. 없으면 null.</summary>
    public async Task<RemoteFile?> GetFileAsync(string fullName, string path, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"/repos/{fullName}/contents/{Uri.EscapeDataString(path)}", null, ct, allowNotFound: true);
        if (response.Body is not JsonObject j) return null;
        var content = (string?)j["content"] ?? "";
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "").Replace("\r", "")));
        return new RemoteFile(text, (string)j["sha"]!);
    }

    /// <summary>
    /// 파일을 올린다. <paramref name="sha"/> = 읽을 때 받은 sha(새 파일이면 null). 그 사이 다른 곳에서 바꿨으면 <see cref="GitHubConflictException"/>.
    /// 올린 뒤의 sha 를 돌려준다.
    /// </summary>
    public async Task<string> PutFileAsync(string fullName, string path, string text, string? sha, string message, CancellationToken ct = default)
    {
        var body = new JsonObject { ["message"] = message, ["content"] = Convert.ToBase64String(new UTF8Encoding(false).GetBytes(text)) };
        if (sha != null) body["sha"] = sha;
        var j = (await SendAsync(HttpMethod.Put, $"/repos/{fullName}/contents/{Uri.EscapeDataString(path)}", body, ct, conflictStatuses: [409, 422])).Body as JsonObject;
        return (string?)j?["content"]?["sha"] ?? throw new GitHubException("GitHub 응답에 sha 가 없습니다");
    }

    private static GitHubRepo ToRepo(JsonObject j) => new(
        (string)j["full_name"]!, (bool?)j["private"] ?? true, (string?)j["description"],
        (j["topics"] as JsonArray)?.Select(t => (string)t!).ToList() ?? []);

    private sealed record Response(JsonNode? Body);

    private async Task<Response> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct, bool allowNotFound = false, int[]? conflictStatuses = null)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage res;
        try { res = await _http.SendAsync(request, ct); }
        catch (HttpRequestException e) { throw new GitHubException($"네트워크에 연결하지 못했습니다: {e.Message}", offline: true); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new GitHubException("GitHub 의 응답이 늦어 중단했습니다", offline: true); }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            if (text.Length > 0) { try { json = JsonNode.Parse(text); } catch (JsonException) { /* 본문이 JSON 이 아니면 메시지는 상태 코드로 */ } }
            if ((int)res.StatusCode is >= 200 and < 300) return new Response(json);
            if (allowNotFound && res.StatusCode == HttpStatusCode.NotFound) return new Response(null);

            var message = $"GitHub {(int)res.StatusCode}";
            if (json is JsonObject o && (string?)o["message"] is { } m) message += $": {m}";
            if (conflictStatuses?.Contains((int)res.StatusCode) == true) throw new GitHubConflictException(message);
            throw new GitHubException(message, (int)res.StatusCode);
        }
    }
}
