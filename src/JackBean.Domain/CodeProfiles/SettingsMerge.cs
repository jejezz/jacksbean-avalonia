using System.Text.Json.Nodes;

namespace JackBean.Domain.CodeProfiles;

public enum ChangeKind { Added, Unchanged, Conflict }

/// <summary>병합에서 값 하나에 일어난 일.</summary>
/// <param name="Pointer">JSON Pointer(RFC 6901). 설정 키에 `.` 과 `/` 가 흔해서 경로를 이 형식으로 식별한다. 예: `/files.watcherExclude/**~1build~1**` (`/` 는 `~1`, `~` 는 `~0`).</param>
/// <param name="Existing">기존 값(없으면 null).</param>
/// <param name="Patch">패치 값. 목록에 항목이 더해진 경우에는 새로 더해진 항목들.</param>
/// <param name="Replaced">충돌에서 패치 값으로 교체했는지.</param>
public sealed record SettingChange(string Pointer, ChangeKind Kind, JsonNode? Existing = null, JsonNode? Patch = null, bool Replaced = false);

public sealed record MergeResult(JsonObject Merged, IReadOnlyList<SettingChange> Changes);

public static class JsonPointer
{
    public static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");

    /// <summary>`/a.b/c~1d` → `["a.b", "c/d"]`.</summary>
    public static IReadOnlyList<string> Segments(string pointer) =>
        pointer is "" or "/" ? [] : pointer[1..].Split('/').Select(s => s.Replace("~1", "/").Replace("~0", "~")).ToList();
}

/// <summary>
/// 설정 병합(설계 문서 표 4.2):
/// <list type="bullet">
/// <item>Map + Map: 키 단위 재귀 병합</item>
/// <item>List + List: 기존 순서를 유지하고 없는 항목만 뒤에 추가</item>
/// <item>그 밖: **기존 값 유지**(충돌로 보고). 같은 값이면 변경 없음</item>
/// <item>기존에 없음: 패치 값 추가</item>
/// </list>
/// 충돌 항목의 pointer 가 <c>replaceConflicts</c> 에 있으면 그 항목만 패치 값으로 교체한다. 입력은 건드리지 않고 새 값을 돌려준다.
/// </summary>
public static class SettingsMerge
{
    public static MergeResult Merge(JsonObject existing, JsonObject patch, IReadOnlySet<string>? replaceConflicts = null)
    {
        var changes = new List<SettingChange>();
        var merged = MergeMap(existing, patch, "", replaceConflicts ?? new HashSet<string>(), changes);
        return new MergeResult(merged, changes);
    }

    private static JsonObject MergeMap(JsonObject existing, JsonObject patch, string pointer, IReadOnlySet<string> replace, List<SettingChange> changes)
    {
        // 기존 키 순서를 보존하고, 새 키는 패치 순서대로 뒤에 붙는다.
        var output = new JsonObject();
        foreach (var (key, value) in existing) output[key] = value?.DeepClone();

        foreach (var (key, value) in patch)
        {
            var childPointer = $"{pointer}/{JsonPointer.Escape(key)}";
            if (!existing.ContainsKey(key))
            {
                output[key] = value?.DeepClone();
                changes.Add(new SettingChange(childPointer, ChangeKind.Added, Patch: value?.DeepClone()));
            }
            else
            {
                output[key] = MergeValue(existing[key], value, childPointer, replace, changes);
            }
        }
        return output;
    }

    private static JsonNode? MergeValue(JsonNode? existing, JsonNode? patch, string pointer, IReadOnlySet<string> replace, List<SettingChange> changes)
    {
        if (existing is JsonObject eo && patch is JsonObject po) return MergeMap(eo, po, pointer, replace, changes);

        if (existing is JsonArray ea && patch is JsonArray pa)
        {
            var output = new JsonArray();
            foreach (var item in ea) output.Add(item?.DeepClone());
            var added = new JsonArray();
            foreach (var item in pa)
            {
                if (output.Any(x => JsonNode.DeepEquals(x, item))) continue;
                output.Add(item?.DeepClone());
                added.Add(item?.DeepClone());
            }
            changes.Add(added.Count == 0
                ? new SettingChange(pointer, ChangeKind.Unchanged, Existing: ea.DeepClone())
                : new SettingChange(pointer, ChangeKind.Added, Existing: ea.DeepClone(), Patch: added));
            return output;
        }

        if (JsonNode.DeepEquals(existing, patch))
        {
            changes.Add(new SettingChange(pointer, ChangeKind.Unchanged, Existing: existing?.DeepClone()));
            return existing?.DeepClone();
        }

        var replaced = replace.Contains(pointer);
        changes.Add(new SettingChange(pointer, ChangeKind.Conflict, existing?.DeepClone(), patch?.DeepClone(), replaced));
        return (replaced ? patch : existing)?.DeepClone();
    }
}
