using System.Text.RegularExpressions;

namespace JackBean.Services.Text;

public readonly record struct TextMatch(int Index, int Length);

/// <summary>에디터의 찾기·바꾸기 로직 (화면과 무관한 순수 함수). 화면이 결과 위치를 선택하고 바꾼 본문을 문서에 적용한다.</summary>
public static class TextSearch
{
    /// <summary>
    /// 본문에서 <paramref name="query"/>와 맞는 곳을 모두 찾는다. 빈 검색어, 잘못된 정규식, 길이 0인 정규식 결과는 건너뛴다
    /// (결과 없음으로 보인다).
    /// </summary>
    public static IReadOnlyList<TextMatch> FindAll(string text, string query, bool matchCase, bool regex)
    {
        if (query.Length == 0) return [];
        var found = new List<TextMatch>();
        if (regex)
        {
            Regex re;
            try { re = Create(query, matchCase); }
            catch (ArgumentException) { return []; }
            foreach (Match m in re.Matches(text))
                if (m.Length > 0) found.Add(new TextMatch(m.Index, m.Length));
            return found;
        }
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var i = text.IndexOf(query, comparison); i >= 0; i = text.IndexOf(query, i + query.Length, comparison))
            found.Add(new TextMatch(i, query.Length));
        return found;
    }

    /// <summary>정규식이 문법적으로 맞는가 (찾기 칸에 오류를 표시하는 데 쓴다).</summary>
    public static bool IsValidRegex(string query)
    {
        try { _ = new Regex(query); return true; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// <paramref name="from"/> 위치에서 다음(<paramref name="forward"/>) 또는 이전 결과의 번호. 끝에 닿으면 반대편 끝으로 돌아간다.
    /// 결과가 없으면 -1.
    /// </summary>
    public static int IndexOfNext(IReadOnlyList<TextMatch> matches, int from, bool forward)
    {
        if (matches.Count == 0) return -1;
        if (forward)
        {
            for (var i = 0; i < matches.Count; i++)
                if (matches[i].Index >= from) return i;
            return 0;
        }
        for (var i = matches.Count - 1; i >= 0; i--)
            if (matches[i].Index + matches[i].Length <= from) return i;
        return matches.Count - 1;
    }

    /// <summary>모두 바꾼 본문. 정규식이면 <c>$1</c> 같은 묶음 참조가 통한다. 잘못된 정규식이면 본문 그대로.</summary>
    public static string ReplaceAll(string text, string query, string replacement, bool matchCase, bool regex)
    {
        if (query.Length == 0) return text;
        if (regex)
        {
            try { return Create(query, matchCase).Replace(text, replacement); }
            catch (ArgumentException) { return text; }
        }
        return text.Replace(query, replacement, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 결과 하나(<paramref name="match"/>)를 바꾼 뒤의 글. 정규식이면 묶음 참조를 풀어 준다.
    /// 문서에서 그 구간만 바꾸는 데 쓴다.
    /// </summary>
    public static string ReplacementFor(string text, TextMatch match, string query, string replacement, bool matchCase, bool regex)
    {
        if (!regex) return replacement;
        try
        {
            var m = Create(query, matchCase).Match(text, match.Index);
            return m.Success && m.Index == match.Index ? m.Result(replacement) : replacement;
        }
        catch (ArgumentException)
        {
            return replacement;
        }
    }

    static Regex Create(string query, bool matchCase) =>
        new(query, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
