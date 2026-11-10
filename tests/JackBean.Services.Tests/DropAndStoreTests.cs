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
    public void FolderIsAFolderWhetherOrNotItIsAGitRepository()
    {
        using var t = new TempDir();
        var plain = Directory.CreateDirectory(Path.Combine(t.Path, "plain")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(t.Path, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Assert.Equal(DropKind.Folder, PathClassifier.Classify(plain).Kind);
        Assert.Equal(DropKind.Folder, PathClassifier.Classify(repo).Kind);   // 저장소인지는 앱이 판단한다
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
        var save = await s.RememberAsync([new(SuggestionKind.Folder)], "explorer");
        Assert.False(save.Ok);
        Assert.Equal("{ not json", File.ReadAllText(s.FilePath));
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
        Assert.Equal(SuggestionKind.Folder, Assert.Single(ok.SavedRules).Kind);
        Assert.Equal(3, (await Store(t).LoadAsync()).Rules.Count);   // 기본 folders 규칙을 고칠 뿐 새 규칙이 쌓이지 않는다
        var again = await router.RouteAsync([PathClassifier.Classify(dir)]);
        Assert.IsType<Opened>(Assert.Single(again.Outcomes));
    }

    [Fact]
    public void SuggestionsPerKind()
    {
        DroppedItem I(DropKind k, string ext = "") => new("p", k, ext);
        var s = RuleSuggester.Suggest([new(I(DropKind.File, "zip")), new(I(DropKind.File, "zip")), new(I(DropKind.File)),
            new(I(DropKind.Folder)), new(I(DropKind.Folder)), new(I(DropKind.Drive))]);
        Assert.Equal([new(SuggestionKind.Extension, "zip"), new(SuggestionKind.Folder), new(SuggestionKind.Drive)], s);   // 같은 조건은 한 번, 확장자 없는 파일은 제안 없음
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
