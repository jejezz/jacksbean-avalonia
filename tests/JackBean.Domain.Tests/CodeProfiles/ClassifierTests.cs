using JackBean.Domain.CodeProfiles;
using static JackBean.Domain.Tests.CodeProfiles.CodeProfileTestSupport;

namespace JackBean.Domain.Tests.CodeProfiles;

/// <summary>Flutter 판 설계 문서 8.1 의 픽스처를 메모리 스캔 결과로 다시 만든 판정 시험(파일 시스템 없이).</summary>
public class ClassifierTests
{
    private static readonly Dictionary<string, string> EspCmake = new() { ["CMakeLists.txt"] = "include($ENV{IDF_PATH}/tools/cmake/project.cmake)\n" };

    [Fact]
    public void EspIdfWinsAndCmakeIsSuppressed()
    {
        var r = Classify(Snapshot(["CMakeLists.txt", "sdkconfig", "main/main.c"]), contents: EspCmake);
        Assert.Equal(["esp-idf"], Ids(r));
        var evidence = r.Types.Single().Evidence;
        Assert.Contains(evidence, e => e.RuleId == "file");
        Assert.Contains(evidence, e => e.RuleId == "fileContains");
        Assert.Equal("sdkconfig", evidence[0].RelativePath);
    }

    [Fact]
    public void FileContainsNeedsThePatternNotJustTheFile()
    {
        var r = Classify(Snapshot(["CMakeLists.txt"]), contents: new() { ["CMakeLists.txt"] = "project(plain)\n" });
        Assert.Equal(["cmake"], Ids(r));
    }

    [Fact]
    public void UnreadableFilesDoNotMatchFileContains()
    {
        var r = Classify(Snapshot(["CMakeLists.txt"]));   // 읽기가 null 을 돌려준다
        Assert.Equal(["cmake"], Ids(r));
    }

    [Fact]
    public void AospModuleIsDetectedWhenTheRootVariableResolves()
    {
        var r = Classify(Snapshot(["Android.bp"]), variables: new Dictionary<string, string> { ["aospRoot"] = "/src/aosp" });
        Assert.Equal(["aosp-module"], Ids(r));
        Assert.Equal("/src/aosp", r.Variables["aospRoot"]);
        Assert.Contains(r.Types.Single().Evidence, e => e.RuleId == "variable");
    }

    [Fact]
    public void AndroidBpAloneIsNotAnAospModule()
    {
        var r = Classify(Snapshot(["Android.bp"]));
        Assert.Empty(r.Types);
        Assert.Empty(r.Variables);
    }

    [Fact]
    public void GradleProject() =>
        Assert.Equal(["java-gradle"], Ids(Classify(Snapshot(["settings.gradle.kts", "app/build.gradle.kts"]))));

    [Fact]
    public void LinuxKernelWinsAndMakeIsSuppressed() =>
        Assert.Equal(["linux-kernel"], Ids(Classify(Snapshot(["Kconfig", "Makefile"]))));

    [Fact]
    public void WebAndPythonBothDetectedInRulesOrderWithTheSamePriority()
    {
        var r = Classify(Snapshot(["package.json", "requirements.txt"]));
        Assert.Equal(["node", "python"], Ids(r));
        Assert.All(r.Types, t => Assert.Equal(60, t.Priority));
    }

    [Fact]
    public void TypesAreSortedByPriorityHighestFirst() =>
        Assert.Equal(["java-maven", "cmake", "make"], Ids(Classify(Snapshot(["pom.xml", "CMakeLists.txt", "Makefile"]))));

    [Fact]
    public void FilesInsideSkippedDirectoriesAreNotInTheSnapshotSoNothingMatches()
    {
        // 스캔은 node_modules 에 들어가지 않으므로 그 안의 pom.xml 은 스냅샷에 없다. 존재만 알린다.
        var r = Classify(Snapshot([], extraDirs: ["node_modules"], skipped: ["node_modules"]));
        Assert.Empty(r.Types);
        Assert.Equal(["node_modules"], r.Stats.LargeDirsPresent);
        Assert.Equal(0, r.Stats.FileCount);
    }

    [Fact]
    public void DirMatcherSeesSkippedDirectoriesByExistence()
    {
        var rule = new TypeRule("has-out", "Has out", 1, new DirMatcher("out"), [], "P", [], new(), []);
        var rules = new ProfileRules(1, ScanConfig.Defaults, new Dictionary<string, VariableRule>(), [rule], []);
        var r = Classify(Snapshot([], extraDirs: ["out"], skipped: ["out"]), rules: rules);
        Assert.Equal(["has-out"], Ids(r));
        Assert.Equal("out", r.Types.Single().Evidence.Single().RelativePath);
    }

    [Fact]
    public void NestedAllAndAny()
    {
        var rule = new TypeRule("n", "N", 1,
            new AllMatcher([new AnyMatcher([new FileMatcher("a.txt"), new FileMatcher("b.txt")]), new FileMatcher("c.txt")]),
            [], "P", [], new(), []);
        var rules = new ProfileRules(1, ScanConfig.Defaults, new Dictionary<string, VariableRule>(), [rule], []);
        var r = Classify(Snapshot(["a.txt", "c.txt"]), rules: rules);
        Assert.Equal(["n"], Ids(r));
        Assert.Equal(2, r.Types.Single().Evidence.Count);
        Assert.Empty(Classify(Snapshot(["a.txt"]), rules: rules).Types);   // all 은 자식 하나가 실패하면 실패
    }

    [Fact]
    public void TruncatedAndCountAreReported()
    {
        var r = Classify(Snapshot(["Makefile", "a.c", "b.c"], truncated: true));
        Assert.True(r.Stats.Truncated);
        Assert.Equal(3, r.Stats.FileCount);
    }

    [Fact]
    public void RunawayRegexCountsAsNoMatch()
    {
        // 되돌아가기가 폭발하는 사용자 정규식은 시간 제한에 걸려 "맞지 않음"이 된다(판정이 멈추지 않는다).
        var parsed = ProfileRulesParser.Parse("""
            {
              "version": 1,
              "types": [ { "id": "evil", "label": "Evil", "priority": 1, "profile": "P",
                           "match": { "fileContains": { "glob": "x.txt", "pattern": "^(a+)+$" } } } ]
            }
            """);
        var text = new string('a', 60) + "!";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Classify(Snapshot(["x.txt"]), contents: new() { ["x.txt"] = text }, rules: parsed.Value!);
        sw.Stop();
        Assert.Empty(r.Types);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    // --- 근접 경쟁 후보 ---

    [Fact]
    public void CloseCompetitorsAreOtherProfilesWithinTenPriority()
    {
        var rules = DefaultRules();
        var r = Classify(Snapshot(["pom.xml", "package.json", "CMakeLists.txt"]));
        Assert.Equal(["java-maven", "node", "cmake"], Ids(r));
        Assert.Equal(["node"], ProjectClassifier.CloseCompetitors(r, rules).Select(t => t.TypeId));
    }

    [Fact]
    public void SameProfileTypesAreNotCompetitors()
    {
        var r = Classify(Snapshot(["package.json", "requirements.txt"]));
        Assert.Empty(ProjectClassifier.CloseCompetitors(r, DefaultRules()));
    }

    [Fact]
    public void ASingleTypeHasNoCompetitorsAndTheThresholdIsAdjustable()
    {
        var rules = DefaultRules();
        Assert.Empty(ProjectClassifier.CloseCompetitors(Classify(Snapshot(["package.json"])), rules));
        var r = Classify(Snapshot(["pom.xml", "CMakeLists.txt"]));
        Assert.Empty(ProjectClassifier.CloseCompetitors(r, rules));
        Assert.Equal(["cmake"], ProjectClassifier.CloseCompetitors(r, rules, 20).Select(t => t.TypeId));
    }
}
