using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;
using JackBean.Services.CodeProfiles;
using static JackBean.Services.Tests.CodeProfiles.CodeProfileFixtures;

namespace JackBean.Services.Tests.CodeProfiles;

public class ProjectSettingsFileTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 5, 7);

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static string SettingsOf(TempDir t) => File.ReadAllText(ProjectSettingsFile.SettingsPath(t.Path));

    private static string[] VsCodeFiles(TempDir t) =>
        Directory.GetFiles(Path.Combine(t.Path, ".vscode")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task CreatesTheVsCodeFolderAndFileWhenMissingWithNoBackup()
    {
        using var t = Create(new() { ["a.txt"] = "" });
        var r = await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"a": 1}"""));
        Assert.True(r.Ok, r.Error?.ToString());
        Assert.True(r.Value!.Written);
        Assert.Null(r.Value.BackupPath);
        Assert.Equal(["settings.json"], VsCodeFiles(t));
        Assert.Equal(1, JsonNode.Parse(SettingsOf(t))!["a"]!.GetValue<int>());
    }

    [Fact]
    public async Task BacksUpTheExistingFileWithATimestampAndMerges()
    {
        using var t = Create(new() { [".vscode/settings.json"] = "{\n  // keep\n  \"a\": 1,\n}\n" });
        var r = await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"b": 2}"""));
        Assert.True(r.Value!.Written);
        Assert.True(r.Value.Diff.CommentsWillBeLost);
        Assert.EndsWith("settings.json.bak.20261111-090507", r.Value.BackupPath);
        Assert.Equal("{\n  // keep\n  \"a\": 1,\n}\n", File.ReadAllText(r.Value.BackupPath!));
        var merged = JsonNode.Parse(SettingsOf(t))!;
        Assert.Equal(1, merged["a"]!.GetValue<int>());
        Assert.Equal(2, merged["b"]!.GetValue<int>());
        Assert.Equal(["settings.json", "settings.json.bak.20261111-090507"], VsCodeFiles(t));
    }

    [Fact]
    public async Task TwoWritesInTheSameSecondDoNotOverwriteTheBackup()
    {
        using var t = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        var file = new ProjectSettingsFile(() => Now);
        await file.ApplyAsync(t.Path, Obj("""{"b": 2}"""));
        var second = await file.ApplyAsync(t.Path, Obj("""{"c": 3}"""));
        Assert.EndsWith("20261111-090507-1", second.Value!.BackupPath);
        Assert.Equal(3, VsCodeFiles(t).Length);
    }

    [Fact]
    public async Task NothingToChangeWritesNothingAndMakesNoBackup()
    {
        using var t = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        var r = await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"a": 1}"""));
        Assert.False(r.Value!.Written);
        Assert.Null(r.Value.BackupPath);
        Assert.Equal(["settings.json"], VsCodeFiles(t));
    }

    [Fact]
    public async Task KeptConflictsAloneDoNotRewriteTheFile()
    {
        using var t = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        var r = await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"a": 2}"""));
        Assert.False(r.Value!.Written);
        Assert.Single(r.Value.Diff.Conflicts);
    }

    [Fact]
    public async Task ChosenConflictsAreReplaced()
    {
        using var t = Create(new() { [".vscode/settings.json"] = """{"a": 1}""" });
        var r = await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"a": 2}"""), new HashSet<string> { "/a" });
        Assert.True(r.Value!.Written);
        Assert.Equal(2, JsonNode.Parse(SettingsOf(t))!["a"]!.GetValue<int>());
    }

    [Fact]
    public async Task AnInvalidExistingFileIsAnErrorAndStaysUntouched()
    {
        using var t = Create(new() { [".vscode/settings.json"] = "{oops" });
        var r = await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"a": 1}"""));
        Assert.False(r.Ok);
        Assert.Equal("{oops", SettingsOf(t));
        Assert.Equal(["settings.json"], VsCodeFiles(t));
    }

    [Fact]
    public async Task NoTempFileIsLeftBehind()
    {
        using var t = Create(new() { [".vscode/settings.json"] = "{}" });
        await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"a": 1}"""));
        Assert.DoesNotContain(VsCodeFiles(t), n => n.EndsWith(".tmp"));
    }

    [Fact]
    public async Task TheFileIsWrittenAsUtf8WithoutABom()
    {
        using var t = Create();
        await new ProjectSettingsFile(() => Now).ApplyAsync(t.Path, Obj("""{"k": "한글"}"""));
        var bytes = File.ReadAllBytes(ProjectSettingsFile.SettingsPath(t.Path));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("한글", SettingsOf(t));
    }

    [Fact]
    public async Task DiffDoesNotWriteAnything()
    {
        using var t = Create(new() { ["a.txt"] = "" });
        var r = await new ProjectSettingsFile(() => Now).DiffAsync(t.Path, Obj("""{"a": 1}"""));
        Assert.Single(r.Value!.Added);
        Assert.False(Directory.Exists(Path.Combine(t.Path, ".vscode")));
    }
}

public class CodeProjectMemoryTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RemembersAFolderAndItsProfile()
    {
        using var t = Create();
        var memory = new CodeProjectMemory(t.Path);
        var folder = Path.Combine(t.Path, "proj");
        Assert.Null(await memory.GetAsync(folder));
        await memory.RememberAsync(folder, "Web / Python", At);
        var record = (await memory.GetAsync(folder))!;
        Assert.Equal("Web / Python", record.Profile);
        Assert.Equal(At, record.AppliedAt);
        Assert.Equal(folder, record.Folder);
    }

    [Fact]
    public async Task ASecondInstanceReadsWhatTheFirstWrote()
    {
        using var t = Create();
        var folder = Path.Combine(t.Path, "proj");
        await new CodeProjectMemory(t.Path).RememberAsync(folder, "Java", At);
        Assert.Equal("Java", (await new CodeProjectMemory(t.Path).GetAsync(folder))!.Profile);
    }

    [Fact]
    public async Task RememberingAgainReplacesTheProfile()
    {
        using var t = Create();
        var memory = new CodeProjectMemory(t.Path);
        var folder = Path.Combine(t.Path, "proj");
        await memory.RememberAsync(folder, "Java", At);
        await memory.RememberAsync(folder, "C++", At.AddMinutes(1));
        Assert.Equal("C++", (await memory.GetAsync(folder))!.Profile);
    }

    [Fact]
    public async Task TrailingSeparatorsAndOnWindowsCaseDoNotMatter()
    {
        using var t = Create();
        var memory = new CodeProjectMemory(t.Path);
        var folder = Path.Combine(t.Path, "Proj");
        await memory.RememberAsync(folder + Path.DirectorySeparatorChar, "Java", At);
        Assert.NotNull(await memory.GetAsync(folder));
        if (OperatingSystem.IsWindows()) Assert.NotNull(await memory.GetAsync(folder.ToUpperInvariant()));
    }

    [Fact]
    public async Task ForgetRemovesAFolder()
    {
        using var t = Create();
        var memory = new CodeProjectMemory(t.Path);
        var folder = Path.Combine(t.Path, "proj");
        await memory.RememberAsync(folder, "Java", At);
        await memory.ForgetAsync(folder);
        Assert.Null(await memory.GetAsync(folder));
    }

    [Fact]
    public async Task ABrokenFileIsMovedAsideAndMemoryStartsEmpty()
    {
        using var t = Create(new() { [CodeProjectMemory.FileName] = "{oops" });
        var memory = new CodeProjectMemory(t.Path);
        Assert.Null(await memory.GetAsync(Path.Combine(t.Path, "proj")));
        Assert.True(File.Exists(memory.FilePath + ".broken"));
        await memory.RememberAsync(Path.Combine(t.Path, "proj"), "Java", At);   // 다음 저장은 새 파일을 만든다
        Assert.Equal("Java", (await memory.GetAsync(Path.Combine(t.Path, "proj")))!.Profile);
    }

    [Fact]
    public async Task OneMalformedEntryDoesNotLoseTheOthers()
    {
        using var t = Create();
        var good = Path.Combine(t.Path, "good");
        var memory = new CodeProjectMemory(t.Path);
        await memory.RememberAsync(good, "Java", At);
        var json = JsonNode.Parse(File.ReadAllText(memory.FilePath))!.AsObject();
        json["projects"]!.AsObject()["bad"] = new JsonObject { ["folder"] = "x" };
        File.WriteAllText(memory.FilePath, json.ToJsonString());
        Assert.Equal("Java", (await memory.GetAsync(good))!.Profile);
    }

    [Fact]
    public async Task ConcurrentRemembersAllEndUpInTheFile()
    {
        using var t = Create();
        var memory = new CodeProjectMemory(t.Path);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => memory.RememberAsync(Path.Combine(t.Path, $"p{i}"), $"P{i}", At)));
        for (var i = 0; i < 10; i++) Assert.Equal($"P{i}", (await memory.GetAsync(Path.Combine(t.Path, $"p{i}")))!.Profile);
    }

    [Fact]
    public async Task NoTempFileIsLeftBehind()
    {
        using var t = Create();
        await new CodeProjectMemory(t.Path).RememberAsync(Path.Combine(t.Path, "p"), "Java", At);
        Assert.DoesNotContain(Directory.GetFiles(t.Path), f => f.EndsWith(".tmp"));
    }
}

public class CodeProfileRulesStoreTests
{
    private const string UserRust = """
        {
          "version": 1,
          "types": [
            { "id": "rust", "label": "Rust", "priority": 60, "match": { "file": "Cargo.toml" }, "profile": "Rust" }
          ]
        }
        """;

    [Fact]
    public async Task NoUserFileMeansTheDefaultRules()
    {
        using var t = Create();
        var r = await new CodeProfileRulesStore(t.Path).LoadAsync();
        Assert.True(r.Ok, r.Error?.ToString());
        Assert.Equal(9, r.Value!.Types.Count);
    }

    [Fact]
    public async Task UserRulesOverrideInPlaceAndNewIdsAreAppended()
    {
        using var t = Create(new()
        {
            [CodeProfileRulesStore.UserFileName] = """
                {
                  "version": 1,
                  "types": [
                    { "id": "cmake", "label": "CMake (custom)", "priority": 55, "match": { "file": "CMakeLists.txt" }, "profile": "My C++" },
                    { "id": "rust", "label": "Rust", "priority": 60, "match": { "file": "Cargo.toml" }, "profile": "Rust" }
                  ]
                }
                """,
        });
        var r = await new CodeProfileRulesStore(t.Path).LoadAsync();
        Assert.True(r.Ok, r.Error?.ToString());
        Assert.Equal(10, r.Value!.Types.Count);
        Assert.Equal("My C++", r.Value.Types[4].Profile);
        Assert.Equal("rust", r.Value.Types[^1].Id);
    }

    [Fact]
    public async Task ScanInheritsUnspecifiedFieldsFromTheDefaults()
    {
        using var t = Create(new() { [CodeProfileRulesStore.UserFileName] = """{ "version": 1, "scan": { "maxDepth": 5 } }""" });
        var r = await new CodeProfileRulesStore(t.Path).LoadAsync();
        Assert.Equal(5, r.Value!.Scan.MaxDepth);
        Assert.Equal(20000, r.Value.Scan.MaxFiles);
        Assert.Contains(".git", r.Value.Scan.SkipDirs);
    }

    [Fact]
    public async Task AnErrorMentionsTheFileAndTheLocationAndIsNotSilentlyReplacedByDefaults()
    {
        using var t = Create(new() { [CodeProfileRulesStore.UserFileName] = "{\n  \"version\": 1,\n  \"types\": [ { \"id\": \"x\" } ]\n}\n" });
        var store = new CodeProfileRulesStore(t.Path);
        var r = await store.LoadAsync();
        Assert.False(r.Ok);
        Assert.Contains(CodeProfileRulesStore.UserFileName, r.Error!.Message);
        Assert.Equal(3, r.Error.Line);
    }

    [Fact]
    public async Task ARuleSuppressingAnUnknownTypeIsRejectedAfterTheMerge()
    {
        using var t = Create(new()
        {
            [CodeProfileRulesStore.UserFileName] = """
                { "version": 1, "types": [ { "id": "z", "label": "Z", "priority": 1, "match": { "file": "z" }, "suppresses": ["ghost"], "profile": "P" } ] }
                """,
        });
        var r = await new CodeProfileRulesStore(t.Path).LoadAsync();
        Assert.False(r.Ok);
        Assert.Contains("ghost", r.Error!.Message);
    }

    [Fact]
    public async Task EditsAreSeenOnTheNextLoad()
    {
        using var t = Create();
        var store = new CodeProfileRulesStore(t.Path);
        Assert.Equal(9, (await store.LoadAsync()).Value!.Types.Count);
        File.WriteAllText(store.UserRulesPath, UserRust);
        Assert.Equal(10, (await store.LoadAsync()).Value!.Types.Count);
    }
}
