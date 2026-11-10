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
/// 규칙 JSON 의 `open`(앱 ID 목록)을 읽고 쓴다. 0개 = 등록된 앱 전부를 보여 주며 묻기, 1개 = 바로 열기, 2개 이상 = 그 앱들만 묻기.
/// </summary>
public static class RuleChoices
{
    /// <summary>`open` 이 없으면 빈 목록. 목록이 아니거나 앱 이름이 아닌 것이 섞여 있으면 null. 같은 앱이 두 번 적혀 있으면 처음 것만 남긴다.</summary>
    public static IReadOnlyList<string>? Read(JsonObject rule)
    {
        var node = rule["open"];
        if (node == null) return [];
        if (node is not JsonArray arr) return null;
        var ids = new List<string>();
        foreach (var n in arr)
        {
            var id = RuleCondition.Str(n);
            if (string.IsNullOrEmpty(id)) return null;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>`open` 을 쓴다. 규칙의 나머지 키는 건드리지 않는다.</summary>
    public static void Write(JsonObject rule, IReadOnlyList<string> choices) =>
        rule["open"] = new JsonArray(choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
}
