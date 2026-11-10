using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JackBean.Domain.Policy;

namespace JackBean.Services.Sync;

public sealed record SyncResult(
    bool Ok, string? Error = null,
    bool ChangedLocal = false, bool ChangedRemote = false, bool FirstSync = false,
    IReadOnlyList<MergeNote>? Notes = null)
{
    public IReadOnlyList<MergeNote> MergeNotes => Notes ?? [];
}

/// <summary>
/// 정책 파일(이 PC 의 OS 것 `policy.&lt;os&gt;.json`)을 GitHub 저장소와 동기화한다. 앱이 GitHub 에 로그인하지 않고
/// **컴퓨터에 설치된 `git` 명령**을 실행한다 — 로그인은 사용자가 이미 쓰는 git 설정(자격 증명 관리자, SSH 키)이 처리하므로 앱은 토큰을 다루지 않는다.
/// <para>
/// 흐름: 저장소를 데이터 폴더(`sync/repo`)에 내려받아 최신으로 맞추고 → 원격 파일·이 PC 파일·마지막 동기화 때의 모습(`sync/base.&lt;os&gt;.json`)을
/// <see cref="PolicyMerger"/> 로 합치고 → 이 PC 파일을 고치고 → 달라졌으면 저장소 파일을 고쳐 커밋·푸시 → 합친 모습을 새 기준으로 저장.
/// 동시에 다른 PC 가 먼저 올렸으면(푸시 거절) 다시 받아 합친다(최대 3번). 합치기 전에 어느 쪽 파일이 깨져 있으면 아무것도 쓰지 않고 멈춘다.
/// </para>
/// </summary>
public sealed class PolicySync
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(120);
    private const int MaxAttempts = 3;

    private readonly PolicyStore _store;
    private readonly string _workDirectory;

    /// <param name="workDirectory">저장소 사본과 기준 파일을 둘 폴더(보통 데이터 폴더 아래 `sync`).</param>
    public PolicySync(PolicyStore store, string workDirectory)
    {
        _store = store;
        _workDirectory = workDirectory;
    }

    private string RepoDir => Path.Combine(_workDirectory, "repo");
    private string FileName => Path.GetFileName(_store.FilePath);
    private string BaseFile => Path.Combine(_workDirectory, $"base.{_store.Os.JsonName()}.json");

    public async Task<SyncResult> SyncAsync(string repositoryUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl)) return new SyncResult(false, "저장소 주소가 비어 있습니다");
        try
        {
            System.IO.Directory.CreateDirectory(_workDirectory);
            if (!await GitAvailableAsync(ct)) return new SyncResult(false, "git 을 찾을 수 없습니다. git 을 설치하고 PATH 에 있는지 확인하세요");

            var error = await EnsureCloneAsync(repositoryUrl.Trim(), ct);
            if (error != null) return new SyncResult(false, error);

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var step = await TryOnceAsync(ct);
                if (step.Retry && attempt < MaxAttempts) continue;
                return step.Result;
            }
            return new SyncResult(false, "다른 PC 가 계속 먼저 올려서 동기화하지 못했습니다. 잠시 뒤에 다시 해 주세요");
        }
        catch (OperationCanceledException) { return new SyncResult(false, "동기화가 시간 안에 끝나지 않았습니다"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return new SyncResult(false, e.Message);
        }
    }

    private sealed record Step(SyncResult Result, bool Retry = false);

    private async Task<Step> TryOnceAsync(CancellationToken ct)
    {
        // 1) 저장소를 원격 최신으로 맞춘다(우리가 올리다 만 커밋이 남아 있어도 버린다 — 합치기는 파일 내용만 본다).
        var branch = (await Git(ct, "symbolic-ref", "--short", "HEAD")).Out.Trim();
        var fetch = await Git(ct, "fetch", "--quiet", "origin");
        if (!fetch.Ok) return Fail(fetch, "저장소에서 받아 오지 못했습니다");
        var remoteHasBranch = branch.Length > 0 && (await Git(ct, "rev-parse", "--verify", "--quiet", $"origin/{branch}")).Ok;
        if (remoteHasBranch)
        {
            var reset = await Git(ct, "reset", "--hard", $"origin/{branch}");
            if (!reset.Ok) return Fail(reset, "저장소를 최신으로 맞추지 못했습니다");
        }
        else if ((await Git(ct, "rev-parse", "--verify", "--quiet", "HEAD")).Ok)
        {
            // 원격은 아직 비어 있는데 사본에는 올리다 만 커밋이 남아 있다(푸시가 거절된 뒤 다시 하는 경우) — 원격에 없는 것이므로 버린다.
            await Git(ct, "update-ref", "-d", "HEAD");
            var stale = Path.Combine(RepoDir, FileName);
            if (File.Exists(stale)) File.Delete(stale);
        }

        // 2) 세 가지를 읽는다: 원격 파일, 이 PC 파일, 마지막 동기화 때의 모습.
        var repoFile = Path.Combine(RepoDir, FileName);
        JsonObject? remote = null;
        if (File.Exists(repoFile))
        {
            remote = ParseOrNull(await File.ReadAllTextAsync(repoFile, ct));
            if (remote == null) return new Step(new SyncResult(false, $"저장소의 {FileName} 이 올바른 JSON 이 아닙니다 — 고친 뒤에 다시 해 주세요"));
            if (!SchemaOk(remote)) return new Step(new SyncResult(false, $"저장소의 {FileName} 의 형식(schema)이 이 앱과 다릅니다"));
        }

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

        // 5) 원격이 달라졌으면 올린다.
        if (changedRemote)
        {
            await File.WriteAllTextAsync(repoFile, PolicyStore.SerializePolicy(merge.Merged), new UTF8Encoding(false), ct);
            var add = await Git(ct, "add", "--", FileName);
            if (!add.Ok) return Fail(add, "저장소에 파일을 담지 못했습니다");
            var commit = await Git(ct, [.. await IdentityArgsAsync(ct), "commit", "--quiet", "-m", $"policy: sync from {Environment.MachineName}"]);
            if (!commit.Ok) return Fail(commit, "저장소에 기록하지 못했습니다");
            var push = await Git(ct, "push", "--quiet", "origin", "HEAD");
            if (!push.Ok)
            {
                var text = push.Err + push.Out;
                // 그 사이 다른 PC 가 먼저 올렸다 — 다시 받아서 합친다.
                if (text.Contains("rejected", StringComparison.OrdinalIgnoreCase) || text.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("fetch first", StringComparison.OrdinalIgnoreCase))
                    return new Step(new SyncResult(false, "저장소가 그 사이에 바뀌었습니다"), Retry: true);
                return Fail(push, "저장소에 올리지 못했습니다");
            }
        }

        // 6) 합친 모습을 다음 동기화의 기준으로 삼는다.
        await File.WriteAllTextAsync(BaseFile, PolicyStore.SerializePolicy(merge.Merged), new UTF8Encoding(false), ct);
        return new Step(new SyncResult(true, ChangedLocal: changedLocal, ChangedRemote: changedRemote,
            FirstSync: baseJson == null, Notes: merge.Notes));
    }

    private static Step Fail(GitResult r, string what) => new(new SyncResult(false, $"{what}: {FirstLine(r.Err.Length > 0 ? r.Err : r.Out)}"));

    private static string FirstLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static JsonObject? ParseOrNull(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static bool SchemaOk(JsonObject json) => json["schema"] is JsonValue v && v.TryGetValue<int>(out var n) && n == PolicyDocument.SchemaVersion;

    // --- 저장소 준비 ---

    private async Task<string?> EnsureCloneAsync(string url, CancellationToken ct)
    {
        if (!System.IO.Directory.Exists(Path.Combine(RepoDir, ".git")))
        {
            if (System.IO.Directory.Exists(RepoDir)) System.IO.Directory.Delete(RepoDir, recursive: true);   // 반쯤 만들어진 것
            var clone = await GitIn(ct, _workDirectory, "clone", "--quiet", url, RepoDir);
            return clone.Ok ? null : $"저장소를 내려받지 못했습니다: {FirstLine(clone.Err.Length > 0 ? clone.Err : clone.Out)}";
        }
        var current = (await Git(ct, "remote", "get-url", "origin")).Out.Trim();
        if (current != url)
        {
            var set = await Git(ct, "remote", "set-url", "origin", url);
            if (!set.Ok) return $"저장소 주소를 바꾸지 못했습니다: {FirstLine(set.Err)}";
        }
        return null;
    }

    /// <summary>git 에 사용자 이름·이메일이 없으면 커밋이 실패한다 — 없을 때만 앱 이름으로 채운다.</summary>
    private async Task<string[]> IdentityArgsAsync(CancellationToken ct)
    {
        var name = (await Git(ct, "config", "user.name")).Out.Trim();
        var email = (await Git(ct, "config", "user.email")).Out.Trim();
        return name.Length > 0 && email.Length > 0 ? [] : ["-c", "user.name=Jack's Bean", "-c", "user.email=jacksbean@users.noreply.github.com"];
    }

    // --- git 실행 ---

    private sealed record GitResult(int ExitCode, string Out, string Err) { public bool Ok => ExitCode == 0; }

    /// <summary>저장소 사본 안에서 git 을 실행한다.</summary>
    private Task<GitResult> Git(CancellationToken ct, params string[] args) => GitIn(ct, RepoDir, args);

    private static async Task<bool> GitAvailableAsync(CancellationToken ct)
    {
        try { return (await GitIn(ct, Path.GetTempPath(), "--version")).Ok; }
        catch (Win32Exception) { return false; }
    }

    /// <summary>지정한 폴더에서 git 을 실행한다(시간 제한 안에서, 암호를 묻지 않고).</summary>
    private static async Task<GitResult> GitIn(CancellationToken ct, string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";            // 암호를 물어보며 멈추지 않고 실패하게 한다
        psi.Environment["LC_ALL"] = "C";                          // 오류 문구를 영어로 고정해서 "rejected" 같은 말을 알아본다
        if (Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") == null) psi.Environment["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git 을 시작하지 못했습니다");
        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(GitTimeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new GitResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* 이미 끝났다 */ }
            throw;
        }
    }
}
