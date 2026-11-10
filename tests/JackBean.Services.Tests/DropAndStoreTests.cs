using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.Services.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("jackbean_").FullName;
    public void Dispose() => Directory.Delete(Path, true);
}

public class PathClassifierTests
{
    [Theory]
    [InlineData("D:")] [InlineData(@"D:\")] [InlineData("d:/")] [InlineData(@"Z:\\")]
    public void WindowsDriveRootIsDrive(string p) => Assert.Equal(DropKind.Drive, PathClassifier.Classify(p, windows: true).Kind);

    [Fact]
    public void WindowsFolderIsNotDrive() => Assert.NotEqual(DropKind.Drive, PathClassifier.Classify(@"D:\Users", windows: true).Kind);

    [Theory]
    [InlineData("/")] [InlineData("/Volumes/USB")] [InlineData("/Volumes/USB/")]
    public void MacVolumesAreDrives(string p) => Assert.Equal(DropKind.Drive, PathClassifier.Classify(p, windows: false).Kind);

    [Fact]
    public void FolderInsideVolumeIsNotDrive() => Assert.NotEqual(DropKind.Drive, PathClassifier.Classify("/Volumes/USB/photos", windows: false).Kind);

    [Fact]
    public void FolderGitDetection()
    {
        using var t = new TempDir();
        var plain = Directory.CreateDirectory(Path.Combine(t.Path, "plain")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(t.Path, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var wt = Directory.CreateDirectory(Path.Combine(t.Path, "wt")).FullName;
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: ../repo/.git");
        Assert.False(PathClassifier.Classify(plain).HasGit);
        Assert.True(PathClassifier.Classify(repo).HasGit);
        Assert.True(PathClassifier.Classify(wt).HasGit); // 워크트리는 .git 이 파일이다
        Assert.Equal(DropKind.Folder, PathClassifier.Classify(plain).Kind);
    }

    [Theory]
    [InlineData("Photo.PNG", "png")] [InlineData("archive.tar.gz", "gz")] [InlineData(".gitignore", "")]
    [InlineData("README", "")] [InlineData("end.", "")]
    public void ExtensionRules(string name, string ext)
    {
        using var t = new TempDir();
        var f = Path.Combine(t.Path, name);
        File.WriteAllText(f, "x");
        var item = PathClassifier.Classify(f);
        Assert.Equal(DropKind.File, item.Kind);
        Assert.Equal(ext, item.Extension);
    }

    [Fact]
    public void MissingPath() => Assert.Equal(DropKind.Missing, PathClassifier.Classify("/definitely/not/here-xyz").Kind);

    [Fact]
    public void NameAndCommonKind()
    {
        Assert.Equal(@"D:\", PathClassifier.Classify(@"D:\", windows: true).Name);
        Assert.Equal("b.txt", new DroppedItem(@"C:\a\b.txt", DropKind.File).Name);
        Assert.Equal("b", new DroppedItem("/a/b/", DropKind.Folder).Name);
        var f = new DroppedItem("a", DropKind.File);
        var d = new DroppedItem("b", DropKind.Folder);
        Assert.Equal(DropKind.File, PathClassifier.CommonKind([f, f]));
        Assert.Null(PathClassifier.CommonKind([f, d]));
        Assert.Null(PathClassifier.CommonKind([]));
    }
}

public class StoreAndRouterTests
{
    static PolicyStore Store(TempDir t) => new(PolicyOs.Windows, t.Path);

    [Fact]
    public async Task CreatesDefaultFileAndReloadsEveryTime()
    {
        using var t = new TempDir();
        var s = Store(t);
        var d = await s.LoadAsync();
        Assert.True(File.Exists(s.FilePath));
        Assert.Contains("drives", d.Rules.Select(r => r.Id));
        var edited = File.ReadAllText(s.FilePath).Replace("\"explorer\"", "\"explorer\"");
        File.WriteAllText(s.FilePath, edited.Replace("\"id\": \"files\"", "\"id\": \"files2\""));
        Assert.Contains("files2", (await s.LoadAsync()).Rules.Select(r => r.Id));
    }

    [Fact]
    public async Task BrokenFileFallsBackWithoutOverwriting()
    {
        using var t = new TempDir();
        var s = Store(t);
        File.WriteAllText(s.FilePath, "{ not json");
        var d = await s.LoadAsync();
        Assert.Single(d.Warnings);
        Assert.Equal("{ not json", File.ReadAllText(s.FilePath));
        var save = await s.UpsertRulesAsync([new("x", new JsonObject { ["kind"] = "hasGit" }, "explorer")]);
        Assert.False(save.Ok);
        Assert.Equal("{ not json", File.ReadAllText(s.FilePath));
    }

    [Fact]
    public async Task UpsertInsertsBeforeAskRuleKeepsUnknownKeysAndReplacesInPlace()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        var json = JsonNode.Parse(File.ReadAllText(s.FilePath))!.AsObject();
        json["custom"] = "keep me";
        File.WriteAllText(s.FilePath, json.ToJsonString());

        var save = await s.UpsertRulesAsync([new("auto-git-folder", new JsonObject { ["kind"] = "hasGit" }, "terminal", "folders")]);
        Assert.True(save.Ok);
        var after = JsonNode.Parse(File.ReadAllText(s.FilePath))!.AsObject();
        Assert.Equal("keep me", (string?)after["custom"]);
        Assert.Equal(["drives", "auto-git-folder", "folders", "files"], after["rules"]!.AsArray().Select(x => (string?)x!["id"]));

        await s.UpsertRulesAsync([new("auto-git-folder", new JsonObject { ["kind"] = "hasGit" }, "explorer", "folders")]);
        after = JsonNode.Parse(File.ReadAllText(s.FilePath))!.AsObject();
        Assert.Equal(4, after["rules"]!.AsArray().Count);
        Assert.Equal("explorer", (string?)after["rules"]![1]!["open"]);
        Assert.False(File.Exists(s.FilePath + ".tmp"));
    }

    [Fact]
    public void LauncherExpandsPathWithoutShellInterpretation()
    {
        var args = AppLauncher.ExpandArgs(["--open", "{path}", "x={path}"], "C:\\a b\\&c \"q\".txt");
        Assert.Equal(["--open", "C:\\a b\\&c \"q\".txt", "x=C:\\a b\\&c \"q\".txt"], args);
    }

    [Fact]
    public async Task RoutesOpenAskAndRemembersOnlyWhenAllOpened()
    {
        using var t = new TempDir();
        var calls = new List<(string, IReadOnlyList<string>)>();
        var fail = false;
        var launcher = new AppLauncher((c, a) => { calls.Add((c, a)); return fail ? throw new InvalidOperationException("boom") : Task.CompletedTask; });
        var router = new DropRouter(Store(t), launcher);

        var dir = Directory.CreateDirectory(Path.Combine(t.Path, "my repo")).FullName;
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        var file = Path.Combine(t.Path, "a b.txt");
        File.WriteAllText(file, "x");

        var plan = await router.RouteAsync([PathClassifier.Classify(dir), PathClassifier.Classify(file)]);
        Assert.Single(plan.AskOutcomes);            // 폴더는 물어본다
        Assert.IsType<Opened>(plan.Outcomes[1]);    // 파일은 기본 앱
        Assert.Equal(file, calls[0].Item2[0]);

        var term = plan.Policy.Apps["terminal"];
        fail = true;
        var bad = await router.ChooseAsync(term, plan.AskOutcomes, remember: true);
        Assert.Single(bad.Failures);
        Assert.Empty(bad.SavedRules);               // 실패하면 저장하지 않는다

        fail = false;
        var ok = await router.ChooseAsync(term, plan.AskOutcomes, remember: true);
        Assert.Equal("auto-git-folder", Assert.Single(ok.SavedRules).Id);
        var again = await router.RouteAsync([PathClassifier.Classify(dir)]);
        Assert.IsType<Opened>(Assert.Single(again.Outcomes));
    }

    [Fact]
    public void SuggestionsPerKind()
    {
        DroppedItem I(DropKind k, string ext = "", bool git = false) => new("p", k, ext, git);
        var s = RuleSuggester.Suggest([new(I(DropKind.File, "zip"), "folders"), new(I(DropKind.File, "zip")), new(I(DropKind.File)),
            new(I(DropKind.Folder, git: true)), new(I(DropKind.Folder)), new(I(DropKind.Drive))]);
        Assert.Equal(["auto-ext-zip", "auto-git-folder", "auto-plain-folder", "auto-drive"], s.Select(x => x.Id));
        Assert.Equal("folders", s[0].BeforeRuleId);
    }
}

public class PolicyResetTests
{
    static PolicyStore Store(TempDir t) => new(PolicyOs.Windows, t.Path);

    [Fact]
    public async Task BrokenFileIsFallbackAndResetRestoresDefaultsKeepingBackup()
    {
        using var t = new TempDir();
        var store = Store(t);
        await File.WriteAllTextAsync(store.FilePath, "{ 깨진 json");

        var broken = await store.LoadAsync();
        Assert.True(broken.IsFallback);
        Assert.NotEmpty(broken.Warnings);

        var result = await store.ResetToDefaultsAsync(new DateTime(2026, 11, 10, 15, 30, 12));
        Assert.True(result.Ok);
        Assert.EndsWith(".broken-20261110-153012", result.BackupPath);
        Assert.Equal("{ 깨진 json", await File.ReadAllTextAsync(result.BackupPath!));   // 이전 내용은 남는다

        var fixedDoc = await store.LoadAsync();
        Assert.False(fixedDoc.IsFallback);
        Assert.Empty(fixedDoc.Warnings);
    }

    [Fact]
    public async Task ResetWithoutFileJustCreatesIt()
    {
        using var t = new TempDir();
        var store = Store(t);
        var result = await store.ResetToDefaultsAsync();
        Assert.True(result.Ok);
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task SkippedRuleWarnsButIsNotFallback()
    {
        using var t = new TempDir();
        var store = Store(t);
        await store.LoadAsync(); // 기본 정책 생성
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.FilePath))!.AsObject();
        ((JsonArray)json["rules"]!).Add(new JsonObject { ["id"] = "깨진-규칙" });   // when/open 이 없는 규칙
        await File.WriteAllTextAsync(store.FilePath, json.ToJsonString());

        var doc = await store.LoadAsync();
        Assert.NotEmpty(doc.Warnings);
        Assert.False(doc.IsFallback);   // 파일 전체는 읽었다 — 기본 정책으로 덮는 복구를 권하지 않는다
    }
}
