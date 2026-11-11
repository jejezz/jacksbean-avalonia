using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

public static class VariableResolver
{
    /// <summary>규칙의 `variables` 를 모두 해석한다. 해석되지 않은 변수는 결과에 없다.</summary>
    public static IReadOnlyDictionary<string, string> Resolve(string root, IReadOnlyDictionary<string, VariableRule> rules)
    {
        var result = new Dictionary<string, string>();
        foreach (var (name, rule) in rules)
        {
            var value = FindUpward(root, rule);
            if (value != null) result[name] = value;
        }
        return result;
    }

    /// <summary>
    /// <paramref name="root"/> 에서 시작해 부모 방향으로 최대 <see cref="VariableRule.MaxLevels"/> 단계 올라가며
    /// anyOf 중 하나가 있는 **가장 가까운** 디렉터리의 경로를 돌려준다(루트 자신도 본다).
    /// </summary>
    private static string? FindUpward(string root, VariableRule rule)
    {
        var dir = new DirectoryInfo(root);
        for (var level = 0; level <= rule.MaxLevels && dir != null; level++, dir = dir.Parent)
        {
            foreach (var name in rule.AnyOf)
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return dir.FullName;
            }
        }
        return null;
    }
}
