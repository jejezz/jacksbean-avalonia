using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JackBean.Domain.Policy;
using JackBean.Services.GitHub;

namespace JackBean.Services.Sync;

public sealed record SyncResult(
    bool Ok, string? Error = null,
    bool ChangedLocal = false, bool ChangedRemote = false, bool FirstSync = false,
    IReadOnlyList<MergeNote>? Notes = null,
    bool AuthFailed = false, bool Offline = false)
{
    public IReadOnlyList<MergeNote> MergeNotes => Notes ?? [];
}

/// <summary>동기화가 오가는 원격의 정책 파일 하나. 읽을 때의 version(sha)을 올릴 때 알려서, 그 사이 바뀌었는지 가린다.</summary>
public interface IPolicyRemote
{
    /// <summary>원격의 파일. 아직 없으면 null.</summary>
    Task<RemoteFile?> GetAsync(CancellationToken ct);

    /// <summary>올린다. <paramref name="sha"/> = 읽을 때 받은 것(새 파일이면 null). 그 사이 바뀌었으면 <see cref="GitHubConflictException"/>. 올린 뒤의 sha 를 돌려준다.</summary>
    Task<string> PutAsync(string text, string? sha, string message, CancellationToken ct);
}

/// <summary>GitHub 저장소 루트의 `policy.&lt;os&gt;.json` (Contents API).</summary>
public sealed class GitHubPolicyRemote(GitHubApi api, string repoFullName, string path) : IPolicyRemote
{
    public Task<RemoteFile?> GetAsync(CancellationToken ct) => api.GetFileAsync(repoFullName, path, ct);
    public Task<string> PutAsync(string text, string? sha, string message, CancellationToken ct) => api.PutFileAsync(repoFullName, path, text, sha, message, ct);
}

/// <summary>
/// 정책 파일(이 PC 의 OS 것 `policy.&lt;os&gt;.json`)을 원격(GitHub 저장소)과 동기화한다. git 이 필요 없다 — GitHub REST(Contents API)로
/// 파일 하나를 읽고 쓴다.
/// <para>
/// 흐름: 원격 파일을 읽고(sha 함께) → 이 PC 파일·마지막 동기화 때의 모습(`sync/base.&lt;os&gt;.json`)과 <see cref="PolicyMerger"/> 로 합치고 →
/// 이 PC 파일을 고치고 → 달라졌으면 원격에 올린다(읽은 sha 와 함께; 그 사이 다른 PC 가 먼저 올렸으면 어긋남 → 다시 읽어 합친다, 최대 3번) →
/// 합친 모습을 새 기준으로 저장. 어느 쪽 파일이 깨져 있으면 아무것도 쓰지 않고 멈춘다.
/// </para>
/// </summary>
public sealed class PolicySync
{
    private const int MaxAttempts = 3;

    private readonly PolicyStore _store;
    private readonly string _workDirectory;
    private readonly IPolicyRemote _remote;

    /// <param name="workDirectory">기준 파일을 둘 폴더(보통 데이터 폴더 아래 `sync`).</param>
    public PolicySync(PolicyStore store, string workDirectory, IPolicyRemote remote)
    {
        _store = store;
        _workDirectory = workDirectory;
        _remote = remote;
    }

    private string BaseFile => Path.Combine(_workDirectory, $"base.{_store.Os.JsonName()}.json");

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_workDirectory);
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var step = await TryOnceAsync(ct);
                if (step.Retry && attempt < MaxAttempts) continue;
                return step.Retry ? new SyncResult(false, "다른 PC 가 계속 먼저 올려서 동기화하지 못했습니다. 잠시 뒤에 다시 해 주세요") : step.Result;
            }
            return new SyncResult(false, "동기화하지 못했습니다");
        }
        catch (GitHubException e) { return new SyncResult(false, e.Message, AuthFailed: e.Unauthorized, Offline: e.Offline); }
        catch (OperationCanceledException) { return new SyncResult(false, "동기화가 취소되었습니다"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException) { return new SyncResult(false, e.Message); }
    }

    private sealed record Step(SyncResult Result, bool Retry = false);

    private async Task<Step> TryOnceAsync(CancellationToken ct)
    {
        // 1) 원격 파일을 읽는다(없으면 이 PC 것을 처음 올리는 것이다).
        var remoteFile = await _remote.GetAsync(ct);
        JsonObject? remote = null;
        if (remoteFile != null)
        {
            remote = ParseOrNull(remoteFile.Text);
            if (remote == null) return new Step(new SyncResult(false, "원격의 정책 파일이 올바른 JSON 이 아닙니다 — 고친 뒤에 다시 해 주세요"));
            if (!SchemaOk(remote)) return new Step(new SyncResult(false, "원격의 정책 파일의 형식(schema)이 이 앱과 다릅니다"));
        }

        // 2) 이 PC 파일과 마지막 동기화 때의 모습.
        var doc = await _store.LoadAsync();
        if (doc.IsFallback) return new Step(new SyncResult(false, "이 PC 의 정책 파일을 읽지 못해 동기화하지 않았습니다(홈 창에서 정책 파일을 확인하세요)"));
        var local = ParseOrNull(await File.ReadAllTextAsync(_store.FilePath, ct));
        if (local == null) return new Step(new SyncResult(false, "이 PC 의 정책 파일을 읽지 못했습니다"));
        var baseJson = File.Exists(BaseFile) ? ParseOrNull(await File.ReadAllTextAsync(BaseFile, ct)) : null;

        // 3) 합친다. 원격에 파일이 아직 없으면 이 PC 것이 곧 합친 모습이다.
        var merge = remote == null ? new MergeResult(local, []) : PolicyMerger.Merge(baseJson, local, remote);
        var changedLocal = !JsonNode.DeepEquals(merge.Merged, local);
        var changedRemote = remote == null || !JsonNode.DeepEquals(merge.Merged, remote);

        // 4) 이 PC 파일을 고친다.
        if (changedLocal)
        {
            var replaced = await _store.ReplaceAsync(merge.Merged);
            if (!replaced.Ok) return new Step(new SyncResult(false, replaced.Error));
        }

        // 5) 원격이 달라졌으면 올린다. 그 사이 다른 PC 가 먼저 올렸으면(sha 어긋남) 처음부터 다시.
        if (changedRemote)
        {
            try { await _remote.PutAsync(PolicyStore.SerializePolicy(merge.Merged), remoteFile?.Sha, $"policy: sync from {Environment.MachineName}", ct); }
            catch (GitHubConflictException) { return new Step(new SyncResult(false, "원격이 그 사이에 바뀌었습니다"), Retry: true); }
        }

        // 6) 합친 모습을 다음 동기화의 기준으로 삼는다.
        await File.WriteAllTextAsync(BaseFile, PolicyStore.SerializePolicy(merge.Merged), new UTF8Encoding(false), ct);
        return new Step(new SyncResult(true, ChangedLocal: changedLocal, ChangedRemote: changedRemote, FirstSync: baseJson == null, Notes: merge.Notes));
    }

    private static JsonObject? ParseOrNull(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static bool SchemaOk(JsonObject json) => json["schema"] is JsonValue v && v.TryGetValue<int>(out var n) && n == PolicyDocument.SchemaVersion;
}
