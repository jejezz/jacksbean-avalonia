using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;
using static JackBean.Domain.Tests.CodeProfiles.CodeProfileTestSupport;

namespace JackBean.Domain.Tests.CodeProfiles;

public class RecommenderTests
{
    private static readonly ProfileRules Rules = DefaultRules();

    private static Recommendation Recommend(AnalysisResult analysis, ProfileRules? rules = null)
    {
        var r = Recommender.Recommend(analysis, rules ?? Rules);
        Assert.True(r.Ok, r.Error?.ToString());
        return r.Value!;
    }

    private static Recommendation RecommendFiles(IEnumerable<string> files, Dictionary<string, string>? contents = null, Dictionary<string, string>? variables = null) =>
        Recommend(Classify(Snapshot(files), variables, contents));

    private static AnalysisResult Analysis(string[] typeIds, bool truncated = false, Dictionary<string, string>? variables = null) =>
        new(typeIds.Select(id => new DetectedType(id, id, Rules.Types.Single(t => t.Id == id).Priority, [])).ToList(),
            new TreeStats(1, truncated, []),
            variables ?? new Dictionary<string, string>(),
            DateTimeOffset.UnixEpoch);

    // --- 8.1 픽스처의 추천 스냅샷 ---

    [Fact]
    public void EspIdfSnapshot()
    {
        var r = RecommendFiles(["CMakeLists.txt", "sdkconfig", "main/main.c"], new() { ["CMakeLists.txt"] = "include($ENV{IDF_PATH}/x/project.cmake)\n" });
        Assert.Equal("ESP-IDF", r.ProfileName);
        Assert.Equal(["espressif.esp-idf-extension", "llvm-vs-code-extensions.vscode-clangd"], r.Extensions);
        Assert.Empty(r.OptionalExtensions);
        Assert.True(JsonNode.DeepEquals(Obj("""
            {
              "files.watcherExclude": { "**/build/**": true, "**/managed_components/**": true },
              "search.exclude": { "**/build": true, "**/managed_components": true }
            }
            """), r.SettingsPatch));
        Assert.Equal(["idf.port 등 보드별 설정은 프로젝트 settings.json 에 직접 추가하세요."], r.Notes);
    }

    [Fact]
    public void AospModuleSubstitutesAospRootIntoClangdArgsAndNotes()
    {
        var r = RecommendFiles(["Android.bp"], variables: new() { ["aospRoot"] = "/src/aosp" });
        Assert.Equal("C++ AOSP", r.ProfileName);
        Assert.Equal(["llvm-vs-code-extensions.vscode-clangd"], r.Extensions);
        var args = r.SettingsPatch["clangd.arguments"]!.AsArray();
        Assert.Equal("--compile-commands-dir=/src/aosp/out/soong/development/ide/compdb", args[0]!.GetValue<string>());
        Assert.False(r.SettingsPatch["search.followSymlinks"]!.GetValue<bool>());
        var note = Assert.Single(r.Notes);
        Assert.StartsWith("compdb 생성: cd /src/aosp && source build/envsetup.sh", note);
        Assert.DoesNotContain("${aospRoot}", note);
    }

    [Fact]
    public void GradleProject()
    {
        var r = RecommendFiles(["settings.gradle.kts", "app/build.gradle.kts"]);
        Assert.Equal("Java", r.ProfileName);
        Assert.Equal(["vscjava.vscode-java-pack", "vscjava.vscode-gradle"], r.Extensions);
        Assert.Equal("interactive", r.SettingsPatch["java.configuration.updateBuildConfiguration"]!.GetValue<string>());
    }

    [Fact]
    public void KernelUsesTheEmbeddedMakeProfile()
    {
        var r = RecommendFiles(["Kconfig", "Makefile"]);
        Assert.Equal("Embedded Make", r.ProfileName);
        Assert.Contains("gen_compile_commands.py", Assert.Single(r.Notes));
    }

    [Fact]
    public void WebAndPythonMergeExtensionsAndKeepBothSidesOfSharedSettings()
    {
        var r = RecommendFiles(["package.json", "requirements.txt"]);
        Assert.Equal("Web / Python", r.ProfileName);
        Assert.Equal(["dbaeumer.vscode-eslint", "esbenp.prettier-vscode", "ms-python.python"], r.Extensions);
        Assert.Empty(r.OptionalExtensions);
        Assert.Equal(["eslint.run", "files.watcherExclude", "search.exclude", "python.analysis.diagnosticMode", "python.analysis.exclude"],
            r.SettingsPatch.Select(kv => kv.Key));
        var watcher = r.SettingsPatch["files.watcherExclude"]!.AsObject().Select(kv => kv.Key).ToList();
        Assert.Equal(["**/node_modules/**", "**/dist/**", "**/.venv/**", "**/__pycache__/**"], watcher);
    }

    // --- 프로필·확장 규칙 ---

    [Fact]
    public void ADifferentProfileTypeGoesToOptionalExtensions()
    {
        var r = Recommend(Analysis(["java-maven", "cmake"]));
        Assert.Equal("Java", r.ProfileName);
        Assert.Equal(["vscjava.vscode-java-pack"], r.Extensions);
        Assert.Equal(["llvm-vs-code-extensions.vscode-clangd", "ms-vscode.cmake-tools"], r.OptionalExtensions);
    }

    [Fact]
    public void AnOptionalExtensionAlreadyInTheMainListIsDropped()
    {
        var r = Recommend(Analysis(["node", "cmake", "make"]));
        Assert.Equal("Web / Python", r.ProfileName);
        Assert.Equal(r.OptionalExtensions.Count, r.OptionalExtensions.Distinct().Count());
        Assert.DoesNotContain(r.OptionalExtensions, e => r.Extensions.Contains(e));
    }

    [Fact]
    public void SettingsOfAllDetectedTypesAreMergedPrimaryWinsConflicts()
    {
        var r = Recommend(Analysis(["java-maven", "cmake"]));
        Assert.True(r.SettingsPatch.ContainsKey("java.import.exclusions"));
        Assert.True(r.SettingsPatch.ContainsKey("cmake.configureOnOpen"));
    }

    [Fact]
    public void NoDetectedTypeIsAnError() =>
        Assert.False(Recommender.Recommend(Analysis([]), Rules).Ok);

    [Fact]
    public void ADetectedTypeMissingFromTheRulesIsAnError()
    {
        var a = new AnalysisResult([new DetectedType("ghost", "g", 1, [])], new TreeStats(0, false, []), new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        Assert.False(Recommender.Recommend(a, Rules).Ok);
    }

    [Fact]
    public void ChoosingAnotherPrimaryTypeRecommendsAgainWithItFirst()
    {
        var a = Analysis(["java-maven", "node"]);
        Assert.Equal("Web / Python", Assert.IsType<CodeResult<Recommendation>>(Recommender.RecommendWithPrimary(a, Rules, "node")).Value!.ProfileName);
        Assert.Equal("Java", Recommender.RecommendWithPrimary(a, Rules, "java-maven").Value!.ProfileName);
        Assert.Equal("Java", Recommender.RecommendWithPrimary(a, Rules, "unknown").Value!.ProfileName);   // 없는 id 면 그대로
        Assert.Equal(["java-maven", "node"], a.Types.Select(t => t.TypeId));   // 입력은 바뀌지 않는다
    }

    // --- sizeRules ---

    [Fact]
    public void SizeRulesApplyOnlyWhenTheTreeWasTruncated()
    {
        var small = Recommend(Analysis(["make"]));
        Assert.False(small.SettingsPatch.ContainsKey("search.followSymlinks"));

        var big = Recommend(Analysis(["make"], truncated: true));
        Assert.False(big.SettingsPatch["search.followSymlinks"]!.GetValue<bool>());
        Assert.Contains("트리가 매우 큽니다. 전체 대신 작업 모듈 폴더만 여는 것을 권장합니다.", big.Notes);
    }

    // --- 변수 치환 ---

    private static readonly Dictionary<string, string> Vars = new() { ["aospRoot"] = "/src/aosp" };

    private static string Sub(string text, IReadOnlyDictionary<string, string>? vars = null, ISet<string>? missing = null) =>
        VariableSubstitution.Substitute(text, vars ?? Vars, missing ?? new HashSet<string>());

    [Fact]
    public void ResolvedVariable()
    {
        Assert.Equal("/src/aosp/out", Sub("${aospRoot}/out"));
        Assert.Equal("a /src/aosp b /src/aosp", Sub("a ${aospRoot} b ${aospRoot}"));
    }

    [Fact]
    public void SubstitutionRecursesIntoListsAndObjectsAndLeavesNonStringsAlone()
    {
        var input = Obj("""{"k": ["${aospRoot}", 1, true, null], "n": {"p": "x/${aospRoot}"}}""");
        var output = (JsonObject)VariableSubstitution.Substitute(input, Vars, new HashSet<string>())!;
        Assert.True(JsonNode.DeepEquals(Obj("""{"k": ["/src/aosp", 1, true, null], "n": {"p": "x//src/aosp"}}"""), output));
        Assert.Equal("${aospRoot}", input["k"]![0]!.GetValue<string>());   // 원본은 그대로
    }

    [Fact]
    public void ReservedVariablesArePreserved()
    {
        const string text = "${workspaceFolder}/build:${workspaceFolderBasename}:${userHome}:${env:HOME}";
        var missing = new HashSet<string>();
        Assert.Equal(text, Sub(text, new Dictionary<string, string>(), missing));
        Assert.Empty(missing);
    }

    [Fact]
    public void UnresolvedVariablesAreReportedAndLeftInPlace()
    {
        var missing = new HashSet<string>();
        Assert.Equal("${nope}/x", Sub("${nope}/x", Vars, missing));
        Assert.Equal(["nope"], missing);
    }

    [Fact]
    public void AnUnresolvedVariableDropsThatSettingAndAddsAWarning()
    {
        var rule = new TypeRule("t", "T", 1, new FileMatcher("x"), [], "P", [],
            Obj("""{"good": "${known}", "bad": ["--dir=${missing}/x"], "keep": "${workspaceFolder}/b"}"""),
            ["run in ${missing}", "ok ${known}"]);
        var rules = new ProfileRules(1, ScanConfig.Defaults, new Dictionary<string, VariableRule>(), [rule], []);
        var a = new AnalysisResult([new DetectedType("t", "T", 1, [])], new TreeStats(0, false, []),
            new Dictionary<string, string> { ["known"] = "K" }, DateTimeOffset.UnixEpoch);

        var r = Recommend(a, rules);
        Assert.True(JsonNode.DeepEquals(Obj("""{"good": "K", "keep": "${workspaceFolder}/b"}"""), r.SettingsPatch));
        Assert.Equal("ok K", r.Notes[0]);
        Assert.Equal(2, r.Notes.Count(n => n.Contains("${missing}")));
        Assert.Contains(r.Notes, n => n.Contains("'bad'"));
    }
}
