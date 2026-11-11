using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;
using static JackBean.Domain.Tests.CodeProfiles.CodeProfileTestSupport;

namespace JackBean.Domain.Tests.CodeProfiles;

public class ProfileRulesTests
{
    private const string MinimalType = """
        {
          "version": 1,
          "types": [
            { "id": "a", "label": "A", "priority": 1, "match": { "file": "a.txt" }, "profile": "P" }
          ]
        }
        """;

    private static ProfileRules Ok(string json, ScanConfig? defaults = null)
    {
        var r = ProfileRulesParser.Parse(json, defaults);
        Assert.True(r.Ok, r.Error?.ToString());
        return r.Value!;
    }

    private static CodeError Err(string json)
    {
        var r = ProfileRulesParser.Parse(json);
        Assert.False(r.Ok);
        return r.Error!;
    }

    // --- 내장 기본 규칙 ---

    [Fact]
    public void DefaultRulesLoadAndValidate()
    {
        var rules = DefaultRules();
        Assert.Equal(["esp-idf", "aosp-module", "java-gradle", "java-maven", "cmake", "linux-kernel", "make", "node", "python"], rules.Types.Select(t => t.Id));
        Assert.Equal(3, rules.Scan.MaxDepth);
        Assert.Equal(20000, rules.Scan.MaxFiles);
        Assert.Contains("node_modules", rules.Scan.SkipDirs);
        Assert.Equal(12, rules.Variables["aospRoot"].MaxLevels);
        Assert.Equal([".repo", "build/envsetup.sh"], rules.Variables["aospRoot"].AnyOf);
        Assert.True(Assert.Single(rules.SizeRules).Truncated);
        Assert.Null(rules.Validate());
    }

    [Fact]
    public void DefaultScanMatchesTheBuiltInDefaults()
    {
        Assert.Equal(ScanConfig.Defaults.SkipDirs, DefaultRules().Scan.SkipDirs);
        Assert.Equal(ScanConfig.Defaults.MaxDepth, DefaultRules().Scan.MaxDepth);
    }

    [Fact]
    public void MatcherTreeAndSettingsAreParsed()
    {
        var rules = DefaultRules();
        var aosp = rules.Types.Single(t => t.Id == "aosp-module");
        var all = Assert.IsType<AllMatcher>(aosp.Match);
        Assert.IsType<AnyMatcher>(all.Children[0]);
        Assert.Equal("aospRoot", Assert.IsType<VariableMatcher>(all.Children[1]).Name);
        Assert.Equal(["cmake", "make"], aosp.Suppresses);
        Assert.IsType<JsonArray>(aosp.Settings["clangd.arguments"]);

        var esp = rules.Types.Single(t => t.Id == "esp-idf");
        var contains = Assert.Single(Assert.IsType<AnyMatcher>(esp.Match).Children.OfType<FileContainsMatcher>());
        Assert.Matches(contains.Pattern, "include($ENV{IDF_PATH}/x)");
        Assert.Matches(contains.Pattern, "project.cmake");
        Assert.DoesNotMatch(contains.Pattern, "projectXcmake");
    }

    [Fact]
    public void ProfilesFollowTheDesignDocument()
    {
        var byId = DefaultRules().Types.ToDictionary(t => t.Id);
        Assert.Equal("ESP-IDF", byId["esp-idf"].Profile);
        Assert.Equal("C++ AOSP", byId["aosp-module"].Profile);
        Assert.Equal("Java", byId["java-gradle"].Profile);
        Assert.Equal("Java", byId["java-maven"].Profile);
        Assert.Equal("C++", byId["cmake"].Profile);
        Assert.Equal("Embedded Make", byId["linux-kernel"].Profile);
        Assert.Equal("Embedded Make", byId["make"].Profile);
        Assert.Equal("Web / Python", byId["node"].Profile);
        Assert.Equal("Web / Python", byId["python"].Profile);
    }

    // --- 스키마 오류에는 값의 경로와 줄 번호가 붙는다 ---

    [Fact]
    public void UnknownKeyIsReportedAtThatKey()
    {
        var e = Err("""
            {
              "version": 1,
              "types": [
                {
                  "id": "a",
                  "label": "A",
                  "priority": 1,
                  "match": { "file": "a.txt" },
                  "profile": "P",
                  "extension": ["x"]
                }
              ]
            }
            """);
        Assert.Contains("'extension'", e.Message);
        Assert.Contains("types[0].extension", e.Message);
        Assert.Equal(10, e.Line);
    }

    [Fact]
    public void MissingRequiredKeyPointsAtTheType()
    {
        var e = Err("""
            {
              "version": 1,
              "types": [
                { "id": "a", "label": "A", "match": { "file": "a.txt" }, "profile": "P" }
              ]
            }
            """);
        Assert.Contains("'priority'", e.Message);
        Assert.Equal(4, e.Line);
    }

    [Fact]
    public void WrongValueType()
    {
        var e = Err(MinimalType.Replace("\"priority\": 1", "\"priority\": \"high\""));
        Assert.Contains("priority", e.Message);
        Assert.Equal(4, e.Line);
    }

    [Fact]
    public void BadRegexInFileContains()
    {
        var e = Err("""
            {
              "version": 1,
              "types": [
                { "id": "a", "label": "A", "priority": 1, "profile": "P",
                  "match": { "fileContains": { "glob": "x", "pattern": "(" } } }
              ]
            }
            """);
        Assert.Contains("정규식", e.Message);
        Assert.Equal(5, e.Line);
    }

    [Fact]
    public void MatchWithTwoKeys()
    {
        var e = Err(MinimalType.Replace("{ \"file\": \"a.txt\" }", "{ \"file\": \"a\", \"dir\": \"b\" }"));
        Assert.Contains("정확히 하나", e.Message);
        Assert.Equal(4, e.Line);
    }

    [Fact]
    public void EmptyAllAndAny()
    {
        var e = Err(MinimalType.Replace("{ \"file\": \"a.txt\" }", "{ \"any\": [] }"));
        Assert.Contains("비어", e.Message);
    }

    [Fact]
    public void DuplicateTypeId()
    {
        var e = Err("""
            {
              "version": 1,
              "types": [
                { "id": "a", "label": "A", "priority": 1, "match": { "file": "a" }, "profile": "P" },
                { "id": "a", "label": "B", "priority": 2, "match": { "file": "b" }, "profile": "P" }
              ]
            }
            """);
        Assert.Contains("중복", e.Message);
        Assert.Equal(5, e.Line);
    }

    [Fact]
    public void UnsupportedVersion()
    {
        var e = Err("{\n  \"version\": 2,\n  \"types\": []\n}");
        Assert.Contains("version", e.Message);
        Assert.Equal(2, e.Line);
    }

    [Fact]
    public void SyntaxErrorsAndEmptyFiles()
    {
        var syntax = Err("{\n  \"version\": 1,\n  \"types\": [\n");
        Assert.Contains("JSON 문법 오류", syntax.Message);
        Assert.NotNull(syntax.Line);
        Assert.False(ProfileRulesParser.Parse("{\"version\": 1, \"types\": [").Ok);
        Assert.False(ProfileRulesParser.Parse("").Ok);
        Assert.False(ProfileRulesParser.Parse("[1]").Ok);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowedInRuleFiles()
    {
        var rules = Ok("""
            {
              // 내 규칙
              "version": 1,
              "types": [
                { "id": "a", "label": "A", "priority": 1, "match": { "file": "a.txt", }, "profile": "P", },
              ],
            }
            """);
        Assert.Single(rules.Types);
    }

    [Fact]
    public void TypeAndSizeRuleFieldsAreOptionalWhereTheDesignSaysSo()
    {
        var rules = Ok(MinimalType);
        var t = Assert.Single(rules.Types);
        Assert.Empty(t.Suppresses);
        Assert.Empty(t.Extensions);
        Assert.Empty(t.Notes);
        Assert.Empty(t.Settings);
        Assert.Equal(ScanConfig.Defaults, rules.Scan);
    }

    // --- 참조 검증은 병합한 뒤에 ---

    [Fact]
    public void SuppressesMustNameAnExistingType()
    {
        var rules = Ok("""
            {
              "version": 1,
              "types": [
                { "id": "a", "label": "A", "priority": 1, "match": { "file": "a" }, "suppresses": ["ghost"], "profile": "P" }
              ]
            }
            """);
        Assert.Contains("ghost", rules.Validate()!.Message);
    }

    [Fact]
    public void VariableMustBeDefined()
    {
        var rules = Ok(MinimalType.Replace("{ \"file\": \"a.txt\" }", "{ \"variable\": \"nope\" }"));
        Assert.Contains("nope", rules.Validate()!.Message);
    }

    // --- 사용자 규칙 ---

    [Fact]
    public void SameIdOverridesInPlaceAndNewIdIsAppended()
    {
        var baseRules = DefaultRules();
        var user = Ok("""
            {
              "version": 1,
              "types": [
                { "id": "cmake", "label": "CMake (custom)", "priority": 55, "match": { "file": "CMakeLists.txt" }, "profile": "My C++" },
                { "id": "rust", "label": "Rust", "priority": 60, "match": { "file": "Cargo.toml" }, "profile": "Rust" }
              ]
            }
            """, baseRules.Scan);
        var merged = baseRules.OverriddenBy(user);
        Assert.Equal(10, merged.Types.Count);
        Assert.Equal(4, merged.Types.ToList().FindIndex(t => t.Id == "cmake"));
        Assert.Equal("My C++", merged.Types[4].Profile);
        Assert.Equal("rust", merged.Types[^1].Id);
        Assert.Null(merged.Validate());
    }

    [Fact]
    public void ScanInheritsUnspecifiedFieldsFromTheDefaults()
    {
        var baseRules = DefaultRules();
        var user = Ok("""{ "version": 1, "scan": { "maxDepth": 5 } }""", baseRules.Scan);
        var merged = baseRules.OverriddenBy(user);
        Assert.Equal(5, merged.Scan.MaxDepth);
        Assert.Equal(20000, merged.Scan.MaxFiles);
        Assert.Contains(".git", merged.Scan.SkipDirs);
    }

    [Fact]
    public void UserVariablesAndSizeRulesAreAdded()
    {
        var baseRules = DefaultRules();
        var user = Ok("""
            {
              "version": 1,
              "variables": { "extra": { "findUpward": { "anyOf": ["marker"], "maxLevels": 2 } } },
              "sizeRules": [ { "when": { "truncated": true }, "notes": ["x"] } ]
            }
            """, baseRules.Scan);
        var merged = baseRules.OverriddenBy(user);
        Assert.Contains("aospRoot", merged.Variables.Keys);
        Assert.Contains("extra", merged.Variables.Keys);
        Assert.Equal(2, merged.SizeRules.Count);
    }

    [Fact]
    public void UserRuleSuppressingAnUnknownTypeIsRejectedAfterTheMerge()
    {
        var baseRules = DefaultRules();
        var user = Ok("""
            {
              "version": 1,
              "types": [ { "id": "z", "label": "Z", "priority": 1, "match": { "file": "z" }, "suppresses": ["ghost"], "profile": "P" } ]
            }
            """, baseRules.Scan);
        Assert.Contains("ghost", baseRules.OverriddenBy(user).Validate()!.Message);
    }

    [Fact]
    public void RegexTimeoutIsConfigured()
    {
        var rules = Ok("""
            {
              "version": 1,
              "types": [ { "id": "a", "label": "A", "priority": 1, "profile": "P",
                           "match": { "fileContains": { "glob": "x", "pattern": "a+" } } } ]
            }
            """);
        var contains = Assert.IsType<FileContainsMatcher>(rules.Types[0].Match);
        Assert.Equal(ProfileRulesParser.RegexTimeout, contains.Pattern.MatchTimeout);
    }
}
