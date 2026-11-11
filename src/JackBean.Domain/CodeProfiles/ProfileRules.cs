using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JackBean.Domain.CodeProfiles;

/// <summary>스캔 상한과 건너뛸 디렉터리.</summary>
public sealed record ScanConfig(int MaxDepth, int MaxFiles, IReadOnlyList<string> SkipDirs)
{
    public static ScanConfig Defaults { get; } = new(
        3,
        20000,
        [".git", ".repo", "out", "build", "node_modules", "prebuilts", ".venv", "venv", "__pycache__", ".gradle", ".idea", "managed_components", ".cache", "dist", "target"]);
}

/// <summary>매처 트리. `all`/`any` 로 중첩할 수 있다.</summary>
public abstract record Matcher;

public sealed record AllMatcher(IReadOnlyList<Matcher> Children) : Matcher;

public sealed record AnyMatcher(IReadOnlyList<Matcher> Children) : Matcher;

public sealed record FileMatcher(string Glob) : Matcher;

public sealed record DirMatcher(string Glob) : Matcher;

public sealed record FileContainsMatcher(string Glob, Regex Pattern) : Matcher;

/// <summary>변수가 해석되면 참.</summary>
public sealed record VariableMatcher(string Name) : Matcher;

/// <summary>변수 정의: 프로젝트 경로에서 부모 방향으로 <see cref="AnyOf"/> 중 하나를 찾는다(최대 <see cref="MaxLevels"/>단계).</summary>
public sealed record VariableRule(IReadOnlyList<string> AnyOf, int MaxLevels);

public sealed record TypeRule(
    string Id,
    string Label,
    int Priority,
    Matcher Match,
    IReadOnlyList<string> Suppresses,
    string Profile,
    IReadOnlyList<string> Extensions,
    JsonObject Settings,
    IReadOnlyList<string> Notes);

/// <summary>트리 규모에 따른 추가 패치(유형과 무관). 지금 조건은 `truncated` 하나다.</summary>
public sealed record SizeRule(bool Truncated, JsonObject Settings, IReadOnlyList<string> Notes);

/// <summary>프로젝트 유형 판정과 프로필 추천에 쓰는 규칙 전체.</summary>
public sealed record ProfileRules(
    int Version,
    ScanConfig Scan,
    IReadOnlyDictionary<string, VariableRule> Variables,
    IReadOnlyList<TypeRule> Types,
    IReadOnlyList<SizeRule> SizeRules)
{
    /// <summary>
    /// 사용자 규칙을 이 규칙 위에 얹는다: 같은 `id` 의 유형은 제자리에서 바꾸고 새 `id` 는 뒤에 더한다.
    /// 변수는 이름 단위로 덮어쓰고 sizeRules 는 이어 붙인다. scan 은 사용자 쪽(파서가 빠진 필드를 이쪽 값으로 채운다)을 쓴다.
    /// </summary>
    public ProfileRules OverriddenBy(ProfileRules user)
    {
        var types = Types.ToList();
        foreach (var t in user.Types)
        {
            var i = types.FindIndex(x => x.Id == t.Id);
            if (i >= 0) types[i] = t;
            else types.Add(t);
        }
        var variables = Variables.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var kv in user.Variables) variables[kv.Key] = kv.Value;
        return new ProfileRules(Version, user.Scan, variables, types, [.. SizeRules, .. user.SizeRules]);
    }

    /// <summary>유형 사이의 참조(`suppresses`, `variable`)가 모두 존재하는지 본다. 문제가 없으면 null.</summary>
    public CodeError? Validate()
    {
        var ids = Types.Select(t => t.Id).ToHashSet();
        foreach (var t in Types)
        {
            foreach (var s in t.Suppresses)
                if (!ids.Contains(s)) return new CodeError($"유형 '{t.Id}'의 suppresses 에 없는 유형 '{s}'가 있습니다");
            foreach (var name in VariableNames(t.Match))
                if (!Variables.ContainsKey(name)) return new CodeError($"유형 '{t.Id}'가 정의되지 않은 변수 '{name}'를 참조합니다");
        }
        return null;
    }

    private static IEnumerable<string> VariableNames(Matcher m)
    {
        switch (m)
        {
            case AllMatcher a:
                foreach (var c in a.Children)
                    foreach (var n in VariableNames(c)) yield return n;
                break;
            case AnyMatcher a:
                foreach (var c in a.Children)
                    foreach (var n in VariableNames(c)) yield return n;
                break;
            case VariableMatcher v:
                yield return v.Name;
                break;
        }
    }
}
