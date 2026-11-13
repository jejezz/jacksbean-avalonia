// From daylight-commander-avalonia @ 783c8db
using System.Text.RegularExpressions;

namespace JackBean.Services.Updates;

/// <summary>대화상자에 보일 변경 항목들. <see cref="More"/>는 개수 제한으로 뺀 항목 수.</summary>
public sealed partial record ReleaseNotesSummary(IReadOnlyList<string> Items, int More)
{
    public static readonly ReleaseNotesSummary Empty = new([], 0);

    /// <summary>
    /// GitHub 릴리스 노트(마크다운)에서 사용자에게 보일 변경 항목만 뽑는다 (application-release-templates의
    /// <c>releaseNoteHighlights</c>와 같은 규칙):
    /// <list type="bullet">
    /// <item>첫 구분선(<c>---</c>)이나 <c>**Full Changelog**</c> 아래(설치 안내 등)와 "New Contributors" 절은 버린다.</item>
    /// <item>목록 항목(<c>* </c>, <c>- </c>)만 모은다. 목록이 하나도 없으면 제목·표가 아닌 문단 줄을 쓴다.</item>
    /// <item>끝의 <c>by @사람 in https://…</c>를 지운다.</item>
    /// <item>Conventional Commits 접두어를 떼고, 사용자와 상관없는 종류(chore·ci·build·docs·test·style·refactor)는 뺀다.</item>
    /// <item>링크·굵게·코드 같은 인라인 마크다운 기호를 지운다.</item>
    /// </list>
    /// </summary>
    public static ReleaseNotesSummary From(string? markdown, int max = 6)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return Empty;
        var bullets = new List<string>();
        var paragraphs = new List<string>();
        var skipSection = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (HorizontalRule().IsMatch(line) || line.TrimStart().StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase)) break;
            var heading = Heading().Match(line);
            if (heading.Success)
            {
                skipSection = heading.Groups[1].Value.Trim().Equals("New Contributors", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (skipSection || line.Trim().Length == 0) continue;

            var bullet = Bullet().Match(line);
            if (bullet.Success)
            {
                if (Clean(bullet.Groups[1].Value) is { } item) bullets.Add(item);
            }
            else if (!line.TrimStart().StartsWith('|') && !line.TrimStart().StartsWith('>') && Clean(line) is { } text)
            {
                paragraphs.Add(text);
            }
        }

        var items = (bullets.Count > 0 ? bullets : paragraphs).Distinct().ToList();
        return items.Count <= max ? new(items, 0) : new(items[..max], items.Count - max);
    }

    static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
        { "chore", "ci", "build", "docs", "doc", "test", "tests", "style", "refactor" };

    /// <summary>항목 하나를 다듬는다. 사용자에게 보이지 않을 항목이면 null.</summary>
    static string? Clean(string text)
    {
        var s = Attribution().Replace(text.Trim(), "");
        if (FirstContribution().IsMatch(s)) return null;
        var prefix = ConventionalPrefix().Match(s);
        if (prefix.Success)
        {
            if (Hidden.Contains(prefix.Groups[1].Value)) return null;
            s = s[prefix.Length..];
        }
        s = Link().Replace(s, "$1");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        s = Whitespace().Replace(s, " ").Trim();
        return s.Length == 0 ? null : s;
    }

    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\s+by\s+@[\w.\-\[\]]+\s+in\s+\S+\s*$")]
    private static partial Regex Attribution();

    [GeneratedRegex(@"^@\S+\s+made their first contribution", RegexOptions.IgnoreCase)]
    private static partial Regex FirstContribution();

    [GeneratedRegex(@"^([A-Za-z]+)(\([^)]*\))?!?:\s*")]
    private static partial Regex ConventionalPrefix();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
