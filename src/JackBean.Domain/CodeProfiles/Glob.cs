using System.Text;
using System.Text.RegularExpressions;

namespace JackBean.Domain.CodeProfiles;

/// <summary>
/// 루트 기준 상대 경로(구분자 `/`)에 대한 최소 glob.
/// 지원: `*`(`/` 제외 임의 문자) · `?`(한 글자) · `**/`(0개 이상의 디렉터리) · `**`(`/` 포함 임의 문자). 대소문자를 구분한다.
/// 중괄호·문자 클래스는 지원하지 않는다.
/// </summary>
public sealed class Glob
{
    private readonly Regex _regex;

    public Glob(string pattern)
    {
        Pattern = pattern;
        _regex = new Regex(ToRegexPattern(pattern), RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    public string Pattern { get; }

    public bool Matches(string relativePath) => _regex.IsMatch(relativePath);

    private static string ToRegexPattern(string glob)
    {
        var sb = new StringBuilder("^");
        var i = 0;
        while (i < glob.Length)
        {
            var c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    if (i + 2 < glob.Length && glob[i + 2] == '/')
                    {
                        sb.Append("(?:.*/)?");
                        i += 3;
                    }
                    else
                    {
                        sb.Append(".*");
                        i += 2;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                    i++;
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }
        return sb.Append('$').ToString();
    }
}
