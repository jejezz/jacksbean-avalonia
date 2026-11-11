namespace JackBean.Domain.CodeProfiles;

public static class ProjectClassifier
{
    /// <summary>
    /// 훑은 결과와 해석된 변수로 프로젝트 유형을 판정한다.
    /// 모든 유형의 `match` 를 평가해 맞은 것을 모으고, 맞은 유형들의 `suppresses` 에 든 유형을 한꺼번에 뺀 뒤
    /// priority 내림차순으로 정렬한다(같으면 규칙에 적힌 순서).
    /// </summary>
    public static AnalysisResult Classify(
        ScanSnapshot snapshot,
        IReadOnlyDictionary<string, string> variables,
        ProfileRules rules,
        IFileTextReader reader,
        DateTimeOffset now)
    {
        var ctx = new MatchContext(snapshot, variables, reader);
        var matched = new List<(TypeRule Rule, List<Evidence> Evidence)>();
        foreach (var type in rules.Types)
        {
            var evidence = MatcherEvaluator.Evaluate(type.Match, ctx);
            if (evidence != null) matched.Add((type, evidence));
        }

        // suppresses 는 맞은 유형 전체를 기준으로 계산한 뒤 한꺼번에 제거한다.
        var suppressed = matched.SelectMany(m => m.Rule.Suppresses).ToHashSet();
        var order = rules.Types.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var detected = matched
            .Where(m => !suppressed.Contains(m.Rule.Id))
            .OrderByDescending(m => m.Rule.Priority)
            .ThenBy(m => order[m.Rule.Id])
            .Select(m => new DetectedType(m.Rule.Id, m.Rule.Label, m.Rule.Priority, m.Evidence))
            .ToList();

        return new AnalysisResult(
            detected,
            new TreeStats(snapshot.Files.Count, snapshot.Truncated, snapshot.SkippedDirNames),
            variables,
            now);
    }

    /// <summary>
    /// 주 유형과 priority 차이가 <paramref name="threshold"/> 이하이면서 **다른 프로필**을 쓰는 후보.
    /// 판정은 항상 최고 priority 를 자동 선택하고, 이런 후보가 있으면 화면이 "근접 경쟁 후보"로 알리기만 한다.
    /// 같은 프로필을 쓰는 유형은 확장이 합쳐지므로 경쟁이 아니다.
    /// </summary>
    public static IReadOnlyList<DetectedType> CloseCompetitors(AnalysisResult analysis, ProfileRules rules, int threshold = 10)
    {
        if (analysis.Types.Count < 2) return [];
        var profileOf = rules.Types.ToDictionary(t => t.Id, t => t.Profile);
        var primary = analysis.Types[0];
        profileOf.TryGetValue(primary.TypeId, out var primaryProfile);
        return analysis.Types
            .Skip(1)
            .Where(t => primary.Priority - t.Priority <= threshold && (!profileOf.TryGetValue(t.TypeId, out var p) || p != primaryProfile))
            .ToList();
    }
}
