using System.Diagnostics;
using System.Text.Json.Nodes;
using JackBean.Domain.Policy;
using JackBean.Services;
using JackBean.Services.Sync;

namespace JackBean.Services.Tests;

/// <summary>진짜 git 으로 시험한다: 빈 bare 저장소를 "GitHub" 로 두고 PC 두 대(A, B)가 번갈아 동기화한다. git 이 없는 PC 에서는 건너뛴다.</summary>
public sealed class PolicySyncTests : IDisposable
{
    private static readonly bool GitAvailable = Run(Path.GetTempPath(), "--version").ok;

    private readonly TempDir _remoteDir = new();
    private readonly List<TempDir> _dirs = [];
    private string Url => _remoteDir.Path;

    public PolicySyncTests()
    {
        if (GitAvailable) Run(_remoteDir.Path, "init", "--bare", "--quiet", "-b", "main");
    }

    public void Dispose()
    {
        foreach (var d in _dirs) ForceDelete(d.Path);
        ForceDelete(_remoteDir.Path);
    }

    private sealed record Pc(PolicyStore Store, PolicySync Sync);

    private Pc NewPc()
    {
        var dir = new TempDir();
        _dirs.Add(dir);
        var store = new PolicyStore(PolicyOs.Windows, dir.Path);
        return new Pc(store, new PolicySync(store, Path.Combine(dir.Path, "sync")));
    }

    private static (bool ok, string output) Run(string dir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode == 0, output);
        }
        catch (Exception) { return (false, ""); }
    }

    private static void ForceDelete(string path)
    {
        // git 의 .git 안 파일은 읽기 전용이라 그대로는 못 지운다.
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(path, true);
        }
        catch (Exception) { /* 임시 폴더라 남아도 큰 문제는 아니다 */ }
    }

    private static RuleEdit Ext(string id, string[] exts, params string[] apps) => new(id, RuleCondition.Extension(exts)!, apps);

    private const string Old = "2026-01-01T00:00:00Z";
    private static DateTime At(string iso) => DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    [Fact]
    public async Task FirstSyncPushesThisPcsPolicyToAnEmptyRepository()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        await a.Store.UpsertRuleAsync(Ext("img", ["png"], "viewer"));

        var result = await a.Sync.SyncAsync(Url);
        Assert.True(result.Ok, result.Error);
        Assert.True(result.FirstSync);
        Assert.True(result.ChangedRemote);

        var verify = new TempDir(); _dirs.Add(verify);
        Assert.True(Run(verify.Path, "clone", "--quiet", Url, "check").ok);
        var pushed = JsonNode.Parse(File.ReadAllText(Path.Combine(verify.Path, "check", "policy.windows.json")))!;
        Assert.NotNull(pushed["apps"]!["viewer"]);
        Assert.Contains(pushed["rules"]!.AsArray(), r => (string?)r!["id"] == "img");
    }

    [Fact]
    public async Task SecondPcReceivesTheFirstPcsAdditionsAndKeepsItsOwn()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        var b = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        await a.Store.UpsertRuleAsync(Ext("img", ["png"], "viewer"));
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);

        await b.Store.UpsertAppAsync(new AppUpsert("player", "player.exe", ["{path}"]));
        await b.Store.UpsertRuleAsync(Ext("music", ["mp3"], "player"));
        var result = await b.Sync.SyncAsync(Url);
        Assert.True(result.Ok, result.Error);
        Assert.True(result.ChangedLocal);    // A 것을 받았고
        Assert.True(result.ChangedRemote);   // B 것을 올렸다

        var docB = await b.Store.LoadAsync();
        Assert.Contains("viewer", docB.Apps.Keys);
        Assert.Contains("player", docB.Apps.Keys);
        Assert.Equal(["img", "music"], docB.Rules.Where(r => r.When.Kind == RuleKind.Extension).Select(r => r.Id).Order());
        Assert.Empty(docB.Warnings);

        Assert.True((await a.Sync.SyncAsync(Url)).Ok);                          // A 도 B 것을 받는다
        Assert.Contains("music", (await a.Store.LoadAsync()).Rules.Select(r => r.Id));
    }

    [Fact]
    public async Task SyncingAgainWithoutChangesDoesNothing()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);

        var again = await a.Sync.SyncAsync(Url);
        Assert.True(again.Ok, again.Error);
        Assert.False(again.ChangedLocal);
        Assert.False(again.ChangedRemote);   // 커밋도 푸시도 하지 않는다
        Assert.False(again.FirstSync);
    }

    [Fact]
    public async Task ConflictingEditsTakeTheMoreRecentOneAndReportIt()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        var b = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]), At(Old));
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);
        Assert.True((await b.Sync.SyncAsync(Url)).Ok);                          // B 도 같은 모습으로 맞춘다

        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer-A.exe", ["{path}"]), At("2026-06-01T00:00:00Z"));
        await b.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer-B.exe", ["{path}"]), At("2026-09-01T00:00:00Z"));   // B 가 더 최근
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);                          // 원격은 아직 옛것이라 A 것이 올라간다

        var result = await b.Sync.SyncAsync(Url);
        Assert.True(result.Ok, result.Error);
        Assert.Contains(result.MergeNotes, n => n is { Kind: MergeNoteKind.ConflictResolved, Id: "viewer", Detail: "local" });
        Assert.Equal("viewer-B.exe", (await b.Store.LoadAsync()).Apps["viewer"].Command);

        Assert.True((await a.Sync.SyncAsync(Url)).Ok);                          // A 도 결국 B 것으로 맞춰진다
        Assert.Equal("viewer-B.exe", (await a.Store.LoadAsync()).Apps["viewer"].Command);
    }

    [Fact]
    public async Task DeletionOnOnePcReachesTheOther()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        var b = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        await a.Store.UpsertRuleAsync(Ext("img", ["png"], "viewer"));
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);
        Assert.True((await b.Sync.SyncAsync(Url)).Ok);
        Assert.Contains("img", (await b.Store.LoadAsync()).Rules.Select(r => r.Id));

        Assert.True((await a.Store.RemoveRuleAsync("img")).Ok);
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);

        var result = await b.Sync.SyncAsync(Url);                               // B 는 그 규칙을 고친 적이 없다
        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain("img", (await b.Store.LoadAsync()).Rules.Select(r => r.Id));
        Assert.Contains(result.MergeNotes, n => n is { Kind: MergeNoteKind.RemovedWithTheOtherSide, Id: "img" });
    }

    [Fact]
    public async Task BrokenRemoteFileStopsTheSyncAndTouchesNothing()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);

        // 누군가 저장소의 정책 파일을 깨뜨렸다.
        var other = new TempDir(); _dirs.Add(other);
        Assert.True(Run(other.Path, "clone", "--quiet", Url, "w").ok);
        var work = Path.Combine(other.Path, "w");
        File.WriteAllText(Path.Combine(work, "policy.windows.json"), "{ 깨진");
        Run(work, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-aqm", "break");
        Assert.True(Run(work, "push", "--quiet", "origin", "HEAD").ok);

        var before = File.ReadAllText(a.Store.FilePath);
        var result = await a.Sync.SyncAsync(Url);
        Assert.False(result.Ok);
        Assert.Contains("JSON", result.Error);
        Assert.Equal(before, File.ReadAllText(a.Store.FilePath));               // 이 PC 파일은 그대로
    }

    [Fact]
    public async Task UnreachableRepositoryFailsWithAReasonAndKeepsTheLocalFile()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        var before = File.ReadAllText(a.Store.FilePath);

        var result = await a.Sync.SyncAsync(Path.Combine(_remoteDir.Path, "does-not-exist"));
        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(before, File.ReadAllText(a.Store.FilePath));
    }

    [Fact]
    public async Task ARejectedPushIsRetriedAfterReceivingAgain()
    {
        if (!GitAvailable) return;
        // 처음 한 번만 푸시를 거절하는 저장소(그 사이 다른 PC 가 먼저 올린 상황을 흉내 낸다).
        var hooks = Path.Combine(_remoteDir.Path, "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-receive");
        File.WriteAllText(hook, string.Join("\n", "#!/bin/sh",
            "if [ ! -f \"$0.done\" ]; then touch \"$0.done\"; echo \"rejected by test\" >&2; exit 1; fi",
            "exit 0", ""));

        var a = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        var result = await a.Sync.SyncAsync(Url);
        Assert.True(result.Ok, result.Error);       // 두 번째 시도에서 올라간다
        Assert.True(result.ChangedRemote);
        Assert.True(File.Exists(hook + ".done"));   // 거절이 실제로 있었다
    }

    [Fact]
    public async Task EmptyAddressIsRejectedWithoutRunningGit()
    {
        var a = NewPc();
        var result = await a.Sync.SyncAsync("  ");
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task SyncResultCannotBeUndoneAsAnEdit()
    {
        if (!GitAvailable) return;
        var a = NewPc();
        var b = NewPc();
        await a.Store.UpsertAppAsync(new AppUpsert("viewer", "viewer.exe", ["{path}"]));
        Assert.True((await a.Sync.SyncAsync(Url)).Ok);
        await b.Store.UpsertAppAsync(new AppUpsert("player", "player.exe", ["{path}"]));
        Assert.True(b.Store.CanUndo);
        Assert.True((await b.Sync.SyncAsync(Url)).Ok);                          // 받은 변경으로 이 PC 파일이 바뀌었다
        Assert.False(b.Store.CanUndo);                                          // 동기화 앞의 편집은 더 되돌리지 못한다(원격과 어긋나므로)
    }
}
