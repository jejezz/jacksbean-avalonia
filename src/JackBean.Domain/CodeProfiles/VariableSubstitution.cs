using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JackBean.Domain.CodeProfiles;

/// <summary>규칙 문자열 안의 `${이름}` 을 분석에서 해석된 변수로 바꾼다.</summary>
public static partial class VariableSubstitution
{
    /// <summary>VS Code 가 직접 해석하는 변수. 치환하지 않고 그대로 둔다(`env:` 로 시작하는 것도).</summary>
    private static readonly HashSet<string> Reserved = ["workspaceFolder", "workspaceFolderBasename", "userHome"];

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();

    private static bool IsReserved(string name) => Reserved.Contains(name) || name.StartsWith("env:", StringComparison.Ordinal);

    /// <summary>
    /// 문자열의 `${이름}` 을 <paramref name="variables"/> 로 바꾼다. 예약 변수는 그대로 두고, 해석할 수 없는 변수의 이름은
    /// <paramref name="unresolved"/> 에 모은다(그 자리에는 원문이 남는다).
    /// </summary>
    public static string Substitute(string text, IReadOnlyDictionary<string, string> variables, ISet<string> unresolved) =>
        VariablePattern().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (IsReserved(name)) return m.Value;
            if (variables.TryGetValue(name, out var value)) return value;
            unresolved.Add(name);
            return m.Value;
        });

    /// <summary>JSON 값에서 문자열만 치환한 **새** 값을 돌려준다(객체·배열은 재귀, 원본은 건드리지 않는다).</summary>
    public static JsonNode? Substitute(JsonNode? node, IReadOnlyDictionary<string, string> variables, ISet<string> unresolved)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var (key, value) in obj) copy[key] = Substitute(value, variables, unresolved);
                return copy;
            }
            case JsonArray arr:
            {
                var copy = new JsonArray();
                foreach (var item in arr) copy.Add(Substitute(item, variables, unresolved));
                return copy;
            }
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create(Substitute(s, variables, unresolved));
            default:
                return node.DeepClone();
        }
    }
}
