using System.Text.Json.Nodes;

namespace JackBean.Domain.Policy;

/// <summary>정책의 한도. 읽을 때는 관대하다(넘어도 지우지 않고 경고만) — 한도는 편집할 때 막는다.</summary>
public static class PolicyLimits
{
    /// <summary>정책 전체의 규칙 개수(규칙 한 줄 = 1, '이미지' 묶음은 확장자가 몇 개든 1).</summary>
    public const int MaxRules = 255;

    /// <summary>규칙 하나에 연결하는 앱 개수.</summary>
    public const int MaxAppsPerRule = 10;
}

/// <summary>
/// 규칙 JSON 의 `open` / `choices` 를 앱 목록 하나로 읽고 쓴다.
/// <list type="bullet">
/// <item>앱 1개: `"open": "a"` (Flutter 판과 같은 모양, `choices` 없음)</item>
/// <item>앱 2개 이상: `"open": null` + `"choices": ["a","b"]` — Flutter 판은 모르는 키를 무시하므로 거기서는 "물어보기"(전체 앱)로 동작한다</item>
/// <item>앱 0개: `"open": null` — 등록된 앱 전부를 보여 주며 묻는다</item>
/// </list>
/// `choices` 가 있고 비어 있지 않으면 `open` 보다 우선한다(손으로 둘 다 적은 경우).
/// </summary>
public static class RuleChoices
{
    /// <summary>모양이 틀리면 null. 같은 앱이 두 번 적혀 있으면 처음 것만 남긴다.</summary>
    public static IReadOnlyList<string>? Read(JsonObject rule)
    {
        var choicesNode = rule["choices"];
        if (choicesNode != null)
        {
            if (choicesNode is not JsonArray arr) return null;
            var ids = new List<string>();
            foreach (var n in arr)
            {
                var id = RuleCondition.Str(n);
                if (string.IsNullOrEmpty(id)) return null;
                if (!ids.Contains(id)) ids.Add(id);
            }
            if (ids.Count > 0) return ids;
        }
        var openNode = rule["open"];
        if (openNode == null) return [];
        var open = RuleCondition.Str(openNode);
        return open == null ? null : [open];
    }

    /// <summary>`open`/`choices` 를 위 규칙대로 쓴다. 규칙의 나머지 키는 건드리지 않는다.</summary>
    public static void Write(JsonObject rule, IReadOnlyList<string> choices)
    {
        rule["open"] = choices.Count == 1 ? choices[0] : null;
        if (choices.Count >= 2) rule["choices"] = new JsonArray(choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
        else rule.Remove("choices");
    }
}
