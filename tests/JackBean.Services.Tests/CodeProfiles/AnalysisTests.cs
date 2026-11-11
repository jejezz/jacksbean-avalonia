using JackBean.Domain.CodeProfiles;
using JackBean.Services.CodeProfiles;
using static JackBean.Services.Tests.CodeProfiles.CodeProfileFixtures;

namespace JackBean.Services.Tests.CodeProfiles;

/// <summary>Flutter 판 설계 문서 8.1 의 픽스처를 **실제 폴더**로 재현한 분석 시험.</summary>
public class ProjectAnalyzerTests
{
    [Fact]
    public void EspIdfWinsAndCmakeIsSuppressed()
    {
        using var t = Create(new()
        {
            ["CMakeLists.txt"] = "include($ENV{IDF_PATH}/tools/cmake/project.cmake)\n",
            ["sdkconfig"] = "CONFIG_X=y\n",
            ["main/main.c"] = "int main(){}\n",
        });
        var r = Analyze(t.Path);
        Assert.Equal(["esp-idf"], Ids(r));
        Assert.Contains(r.Types.Single().Evidence, e => e.RuleId == "fileContains" && e.RelativePath == "CMakeLists.txt");
        Assert.Equal("sdkconfig", r.Types.Single().Evidence[0].RelativePath);
    }

    [Fact]
    public void AospModuleIsDetectedUnderARepoRootAndAospRootIsResolved()
    {
        using var t = Create(new()
        {
            ["aosp/.repo/"] = "",
            ["aosp/build/envsetup.sh"] = "# envsetup\n",
            ["aosp/vendor/x/hal/Android.bp"] = "cc_binary {}\n",
        });
        var hal = Path.Combine(t.Path, "aosp", "vendor", "x", "hal");
        var r = Analyze(hal);
        Assert.Equal(["aosp-module"], Ids(r));
        Assert.Equal(Path.Combine(t.Path, "aosp"), r.Variables["aospRoot"], ignoreCase: true);
        Assert.Contains(r.Types.Single().Evidence, e => e.RuleId == "variable");
    }

    [Fact]
    public void AndroidBpAloneIsNotAnAospModule()
    {
        using var t = Create(new() { ["Android.bp"] = "cc_binary {}\n" });
        var r = Analyze(t.Path);
        Assert.Empty(r.Types);
        Assert.Empty(r.Variables);
    }

    [Fact]
    public void GradleProject()
    {
        using var t = Create(new() { ["settings.gradle.kts"] = "rootProject.name = \"x\"\n", ["app/build.gradle.kts"] = "plugins {}\n" });
        Assert.Equal(["java-gradle"], Ids(Analyze(t.Path)));
    }

    [Fact]
    public void LinuxKernelWinsAndMakeIsSuppressed()
    {
        using var t = Create(new() { ["Kconfig"] = "", ["Makefile"] = "all:\n" });
        Assert.Equal(["linux-kernel"], Ids(Analyze(t.Path)));
    }

    [Fact]
    public void WebAndPythonInRulesOrder()
    {
        using var t = Create(new() { ["package.json"] = "{}", ["requirements.txt"] = "" });
        var r = Analyze(t.Path);
        Assert.Equal(["node", "python"], Ids(r));
        Assert.All(r.Types, x => Assert.Equal(60, x.Priority));
    }

    [Fact]
    public void BigTreeIsTruncatedAtMaxFilesButRootMarkersAreStillFound()
    {
        var files = new Dictionary<string, string> { ["Makefile"] = "all:\n" };
        for (var i = 0; i < 30; i++) files[$"src/f{i}.c"] = "";
        using var t = Create(files);
        var rules = DefaultRules() with { Scan = new ScanConfig(3, 10, []) };
        var r = Analyze(t.Path, rules);
        Assert.True(r.Stats.Truncated);
        Assert.Equal(10, r.Stats.FileCount);
        Assert.Equal(["make"], Ids(r));
    }

    [Fact]
    public void ExactlyMaxFilesIsNotTruncated()
    {
        using var t = Create(Enumerable.Range(0, 5).ToDictionary(i => $"f{i}.c", _ => ""));
        var r = Analyze(t.Path, DefaultRules() with { Scan = new ScanConfig(3, 5, []) });
        Assert.False(r.Stats.Truncated);
        Assert.Equal(5, r.Stats.FileCount);
    }

    [Fact]
    public void FilesInsideSkipDirsAreIgnoredButTheDirectoryIsReportedAsPresent()
    {
        using var t = Create(new() { ["node_modules/pkg/pom.xml"] = "<project/>" });
        var r = Analyze(t.Path);
        Assert.Empty(r.Types);
        Assert.Equal(0, r.Stats.FileCount);
        Assert.Equal(["node_modules"], r.Stats.LargeDirsPresent);
    }

    [Fact]
    public void FilesDeeperThanMaxDepthAreNotScanned()
    {
        using var t = Create(new() { ["a/b/c/d/pom.xml"] = "", ["a/b/c/pom.xml"] = "" });
        Assert.Equal(1, Analyze(t.Path, DefaultRules() with { Scan = new ScanConfig(3, 100, []) }).Stats.FileCount);
        var shallow = Analyze(t.Path, DefaultRules() with { Scan = new ScanConfig(2, 100, []) });
        Assert.Equal(0, shallow.Stats.FileCount);
        Assert.Empty(shallow.Types);
    }

    [Fact]
    public void ADirMatcherSeesSkipDirsByExistence()
    {
        using var t = Create(new() { ["out/soong/x.txt"] = "" });
        var rule = new TypeRule("has-out", "Has out", 1, new DirMatcher("out"), [], "P", [], new(), []);
        var rules = DefaultRules() with { Types = [rule] };
        var r = Analyze(t.Path, rules);
        Assert.Equal(["has-out"], Ids(r));
        Assert.Equal("out", r.Types.Single().Evidence.Single().RelativePath);
        Assert.Equal(0, r.Stats.FileCount);
    }

    [Fact]
    public void SymlinkLoopsTerminateWithoutFollowingLinks()
    {
        using var t = Create(new() { ["Makefile"] = "all:\n", ["a/x.c"] = "" });
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(t.Path, "a", "loop"), t.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;   // 이 PC 에서는 심볼릭 링크를 만들 수 없다(Windows 는 개발자 모드·권한이 필요) — 건너뛴다
        }
        var r = Analyze(t.Path);
        Assert.Equal(2, r.Stats.FileCount);
        Assert.Equal(["make"], Ids(r));
    }

    [Fact]
    public void MissingFolderAndFilePathsAreErrors()
    {
        using var t = Create(new() { ["f.txt"] = "" });
        var analyzer = new ProjectAnalyzer();
        Assert.False(analyzer.Analyze(Path.Combine(t.Path, "nope"), DefaultRules()).Ok);
        Assert.False(analyzer.Analyze(Path.Combine(t.Path, "f.txt"), DefaultRules()).Ok);
    }

    [Fact]
    public async Task AsyncAnalysisMatchesTheSynchronousOne()
    {
        using var t = Create(new() { ["package.json"] = "{}" });
        var viaTask = await new ProjectAnalyzer().AnalyzeAsync(t.Path, DefaultRules());
        Assert.True(viaTask.Ok);
        Assert.Equal(Ids(Analyze(t.Path)), Ids(viaTask.Value!));
    }

    [Fact]
    public void TrailingSeparatorsOnThePathAreFine()
    {
        using var t = Create(new() { ["package.json"] = "{}" });
        Assert.Equal(["node"], Ids(Analyze(t.Path + Path.DirectorySeparatorChar)));
    }
}

public class FileTextReaderTests
{
    [Fact]
    public void ReadsTheFileHeadAsUtf8()
    {
        using var t = Create(new() { ["a.txt"] = "한글 text" });
        Assert.Equal("한글 text", new FileTextReader(t.Path).ReadHead("a.txt"));
    }

    [Fact]
    public void OnlyTheFirstChunkIsRead()
    {
        using var t = Create(new() { ["big.txt"] = new string('x', FileTextReader.MaxReadBytes + 1000) });
        Assert.Equal(FileTextReader.MaxReadBytes, new FileTextReader(t.Path).ReadHead("big.txt")!.Length);
    }

    [Fact]
    public void MissingFilesAreNull()
    {
        using var t = Create();
        Assert.Null(new FileTextReader(t.Path).ReadHead("nope.txt"));
    }

    [Fact]
    public void FilesOpenInAnotherProcessCanStillBeRead()
    {
        using var t = Create(new() { ["a.txt"] = "hello" });
        using var writer = new FileStream(Path.Combine(t.Path, "a.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("hello", new FileTextReader(t.Path).ReadHead("a.txt"));
    }
}

public class VariableResolverTests
{
    private static readonly IReadOnlyDictionary<string, VariableRule> Rules = new Dictionary<string, VariableRule>
    {
        ["root"] = new([".repo", "build/envsetup.sh"], 3),
    };

    [Fact]
    public void FindsTheNearestAncestorThatHasAMarker()
    {
        using var t = Create(new() { ["top/.repo/"] = "", ["top/mid/.repo/"] = "", ["top/mid/leaf/x.txt"] = "" });
        var leaf = Path.Combine(t.Path, "top", "mid", "leaf");
        Assert.Equal(Path.Combine(t.Path, "top", "mid"), VariableResolver.Resolve(leaf, Rules)["root"]);
    }

    [Fact]
    public void TheStartFolderItselfCounts()
    {
        using var t = Create(new() { ["a/build/envsetup.sh"] = "" });
        var a = Path.Combine(t.Path, "a");
        Assert.Equal(a, VariableResolver.Resolve(a, Rules)["root"]);
    }

    [Fact]
    public void GivesUpAfterMaxLevels()
    {
        using var t = Create(new() { [".repo/"] = "", ["a/b/c/d/e/x.txt"] = "" });
        var deep = Path.Combine(t.Path, "a", "b", "c", "d", "e");   // 루트까지 5단계 > maxLevels 3
        Assert.Empty(VariableResolver.Resolve(deep, Rules));
    }

    [Fact]
    public void NothingFoundMeansNoEntry()
    {
        using var t = Create(new() { ["a.txt"] = "" });
        Assert.Empty(VariableResolver.Resolve(t.Path, new Dictionary<string, VariableRule> { ["x"] = new(["definitely-not-here-xyz"], 2) }));
    }
}
