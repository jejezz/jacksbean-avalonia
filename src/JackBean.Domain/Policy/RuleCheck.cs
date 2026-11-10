namespace JackBean.Domain.Policy;

public enum RuleProblemKind
{
    /// <summary>정책의 규칙이 한도(255)에 닿아 새 규칙을 더할 수 없다.</summary>
    RuleLimit,
    /// <summary>규칙 하나에 앱이 한도(10)를 넘게 연결되었다.</summary>
    TooManyApps,
    /// <summary>정책의 `apps` 에 없는 앱을 가리킨다. Detail = 앱 ID.</summary>
    UnknownApp,
    /// <summary>다른 규칙에도 있는 확장자다. Detail = 확장자, Other = 그 규칙 ID.</summary>
    ExtensionTaken,
    /// <summary>드라이브·폴더·그 밖의 파일은 규칙이 하나씩만 있다. Other = 이미 있는 규칙 ID.</summary>
    KindTaken,
    /// <summary>새 규칙의 ID 가 이미 있다.</summary>
    IdTaken,
}

public sealed record RuleProblem(RuleProblemKind Kind, string? Detail = null, string? Other = null);

/// <summary>
/// 규칙을 만들거나 고칠 때의 검사(편집 창이 화면에 보이고, 서비스가 저장 전에 한 번 더 한다).
/// 읽을 때는 관대하다 — 여기 규칙은 **편집할 때만** 적용한다.
/// </summary>
public static class RuleCheck
{
    /// <param name="candidate">저장하려는 규칙.</param>
    /// <param name="policy">지금 정책(후보 자신을 포함해도 된다 — 같은 ID 는 자기 자신으로 보고 건너뛴다).</param>
    /// <param name="isNew">새 규칙이면 ID 중복과 한도를 본다.</param>
    public static IReadOnlyList<RuleProblem> Validate(Rule candidate, PolicyDocument policy, bool isNew)
    {
        var problems = new List<RuleProblem>();
        if (isNew)
        {
            if (policy.Rules.Any(r => r.Id == candidate.Id)) problems.Add(new(RuleProblemKind.IdTaken, candidate.Id));
            else if (policy.Rules.Count >= PolicyLimits.MaxRules) problems.Add(new(RuleProblemKind.RuleLimit));
        }

        if (candidate.Choices.Count > PolicyLimits.MaxAppsPerRule) problems.Add(new(RuleProblemKind.TooManyApps));
        foreach (var id in candidate.Choices)
            if (!policy.Apps.ContainsKey(id)) problems.Add(new(RuleProblemKind.UnknownApp, id));

        if (candidate.When.Kind == RuleKind.Extension)
        {
            foreach (var ext in candidate.When.Extensions)
            {
                var other = policy.Rules.FirstOrDefault(r => r.Id != candidate.Id && r.When.Kind == RuleKind.Extension && r.When.Extensions.Contains(ext));
                if (other != null) problems.Add(new(RuleProblemKind.ExtensionTaken, ext, other.Id));
            }
        }
        else
        {
            var other = policy.Rules.FirstOrDefault(r => r.Id != candidate.Id && r.When.Kind == candidate.When.Kind);
            if (other != null) problems.Add(new(RuleProblemKind.KindTaken, candidate.When.Kind.ToString(), other.Id));
        }
        return problems;
    }
}

/// <summary>
/// 규칙의 자리: **구체적인 것을 위에**. 저장할 때 이 순서로 정렬한다(사용자는 순서를 신경 쓰지 않는다).
/// 확장자 → 드라이브·폴더 → 그 밖의 파일. 확장자 규칙끼리는 겹치지 않고 드라이브·폴더·파일도 하나씩이라 같은 자리끼리의 순서는 상관없다.
/// </summary>
public static class RuleOrder
{
    public static int Rank(RuleCondition when) => when.Kind switch
    {
        RuleKind.Extension => 0,
        RuleKind.Drive or RuleKind.Folder => 1,
        _ => 2,
    };
}
