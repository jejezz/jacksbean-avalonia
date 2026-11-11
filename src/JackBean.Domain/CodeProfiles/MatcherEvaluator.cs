using System.Text.RegularExpressions;

namespace JackBean.Domain.CodeProfiles;

/// <summary>`fileContains` 가 파일 내용을 읽는 통로. 도메인은 IO 를 하지 않으므로 서비스가 구현한다(앞부분 일정 크기만, 읽을 수 없으면 null).</summary>
public interface IFileTextReader
{
    string? ReadHead(string relativePath);
}

/// <summary>매처 평가에 필요한 입력.</summary>
public sealed class MatchContext(ScanSnapshot snapshot, IReadOnlyDictionary<string, string> variables, IFileTextReader reader)
{
    private readonly Dictionary<string, Glob> _globs = [];

    public ScanSnapshot Snapshot { get; } = snapshot;

    public IReadOnlyDictionary<string, string> Variables { get; } = variables;

    public IFileTextReader Reader { get; } = reader;

    public Glob GlobOf(string pattern)
    {
        if (!_globs.TryGetValue(pattern, out var glob)) _globs[pattern] = glob = new Glob(pattern);
        return glob;
    }
}

public static class MatcherEvaluator
{
    /// <summary>`fileContains` 가 한 규칙에서 내용을 확인하는 최대 파일 수.</summary>
    public const int MaxContainsCandidates = 50;

    /// <summary>
    /// 매처가 맞으면 근거 목록을, 맞지 않으면 null 을 돌려준다.
    /// `all` 은 모든 자식의 근거를, `any` 는 맞은 자식들의 근거를 모두 합친다.
    /// </summary>
    public static List<Evidence>? Evaluate(Matcher matcher, MatchContext ctx)
    {
        switch (matcher)
        {
            case AllMatcher all:
            {
                var evidence = new List<Evidence>();
                foreach (var child in all.Children)
                {
                    var e = Evaluate(child, ctx);
                    if (e == null) return null;
                    evidence.AddRange(e);
                }
                return evidence;
            }
            case AnyMatcher any:
            {
                var evidence = new List<Evidence>();
                var matched = false;
                foreach (var child in any.Children)
                {
                    var e = Evaluate(child, ctx);
                    if (e == null) continue;
                    matched = true;
                    evidence.AddRange(e);
                }
                return matched ? evidence : null;
            }
            case FileMatcher file:
            {
                var glob = ctx.GlobOf(file.Glob);
                var hit = ctx.Snapshot.Files.FirstOrDefault(glob.Matches);
                return hit == null ? null : [new Evidence("file", $"{hit} 파일 발견", hit)];
            }
            case DirMatcher dir:
            {
                var glob = ctx.GlobOf(dir.Glob);
                var hit = ctx.Snapshot.Dirs.FirstOrDefault(glob.Matches);
                return hit == null ? null : [new Evidence("dir", $"{hit} 디렉터리 발견", hit)];
            }
            case FileContainsMatcher contains:
            {
                var glob = ctx.GlobOf(contains.Glob);
                foreach (var f in ctx.Snapshot.Files.Where(glob.Matches).Take(MaxContainsCandidates))
                {
                    if (HasMatch(contains.Pattern, ctx.Reader.ReadHead(f)))
                        return [new Evidence("fileContains", $"{f} 에서 패턴 {contains.Pattern} 발견", f)];
                }
                return null;
            }
            case VariableMatcher variable:
                return ctx.Variables.TryGetValue(variable.Name, out var value)
                    ? [new Evidence("variable", $"변수 {variable.Name} 해석됨: {value}")]
                    : null;
            default:
                return null;
        }
    }

    private static bool HasMatch(Regex pattern, string? text)
    {
        if (text == null) return false;
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;   // 폭주하는 사용자 정규식은 "맞지 않음"으로 본다.
        }
    }
}
