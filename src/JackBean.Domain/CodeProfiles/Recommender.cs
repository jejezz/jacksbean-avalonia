using System.Text.Json.Nodes;

namespace JackBean.Domain.CodeProfiles;

public static class Recommender
{
    /// <summary>
    /// 분석 결과로 프로필·확장·설정 패치를 추천한다(설계 문서 3.3).
    /// <list type="number">
    /// <item>최상위 유형(주 유형)의 profile 이 추천 프로필이다.</item>
    /// <item>같은 profile 을 쓰는 유형의 확장은 `Extensions` 로 합치고, 다른 profile 의 확장은 `OptionalExtensions` 로 보낸다.</item>
    /// <item>settings 는 주 유형 → 나머지 유형 → sizeRules(트리가 잘렸을 때만) 순으로 병합한다. 충돌하면 먼저 온 쪽이 이긴다.</item>
    /// <item>`${변수}` 를 치환한다. 해석되지 않은 변수가 든 설정 항목은 빼고 notes 에 경고를 남긴다.</item>
    /// </list>
    /// 판정된 유형이 없으면 실패한다.
    /// </summary>
    public static CodeResult<Recommendation> Recommend(AnalysisResult analysis, ProfileRules rules)
    {
        if (analysis.Types.Count == 0) return CodeResult<Recommendation>.Fail("판정된 프로젝트 유형이 없어 추천할 수 없습니다");

        var byId = rules.Types.ToDictionary(t => t.Id);
        var detected = new List<TypeRule>();
        foreach (var d in analysis.Types)
        {
            if (!byId.TryGetValue(d.TypeId, out var rule))
                return CodeResult<Recommendation>.Fail($"분석 결과의 유형 '{d.TypeId}'가 현재 규칙에 없습니다. 다시 분석하세요");
            detected.Add(rule);
        }

        var primary = detected[0];
        var extensions = detected.Where(t => t.Profile == primary.Profile).SelectMany(t => t.Extensions).Distinct().ToList();
        var optional = detected.Where(t => t.Profile != primary.Profile).SelectMany(t => t.Extensions).Distinct()
            .Where(e => !extensions.Contains(e)).ToList();

        var sizeRules = rules.SizeRules.Where(s => s.Truncated == analysis.Stats.Truncated).ToList();

        var settings = new JsonObject();
        foreach (var patch in detected.Select(t => t.Settings).Concat(sizeRules.Select(s => s.Settings)))
            settings = SettingsMerge.Merge(settings, patch).Merged;

        var notes = detected.SelectMany(t => t.Notes).Concat(sizeRules.SelectMany(s => s.Notes)).Distinct().ToList();

        var warnings = new List<string>();
        var resolvedSettings = new JsonObject();
        foreach (var (key, value) in settings)
        {
            var missing = new HashSet<string>();
            var resolved = VariableSubstitution.Substitute(value, analysis.Variables, missing);
            if (missing.Count == 0) resolvedSettings[key] = resolved;
            else warnings.Add($"설정 '{key}'은(는) 해석되지 않은 변수 {Names(missing)}가 있어 제외했습니다");
        }

        var resolvedNotes = new List<string>();
        foreach (var note in notes)
        {
            var missing = new HashSet<string>();
            var text = VariableSubstitution.Substitute(note, analysis.Variables, missing);
            if (missing.Count == 0) resolvedNotes.Add(text);
            else warnings.Add($"안내 문구에 해석되지 않은 변수 {Names(missing)}가 있어 제외했습니다: {note}");
        }

        return CodeResult<Recommendation>.Success(new Recommendation(primary.Profile, extensions, optional, resolvedSettings, [.. resolvedNotes, .. warnings]));
    }

    /// <summary>
    /// <paramref name="primaryTypeId"/> 유형을 주 유형으로 삼아 다시 추천한다(화면에서 사용자가 주 유형을 바꿀 때).
    /// 아직 분석 결과에 없는 id 면 주 유형은 그대로다.
    /// </summary>
    public static CodeResult<Recommendation> RecommendWithPrimary(AnalysisResult analysis, ProfileRules rules, string primaryTypeId)
    {
        var types = analysis.Types.ToList();
        var i = types.FindIndex(t => t.TypeId == primaryTypeId);
        if (i > 0)
        {
            var chosen = types[i];
            types.RemoveAt(i);
            types.Insert(0, chosen);
        }
        return Recommend(analysis with { Types = types }, rules);
    }

    private static string Names(IEnumerable<string> names) => string.Join(", ", names.Order().Select(n => "${" + n + "}"));
}
