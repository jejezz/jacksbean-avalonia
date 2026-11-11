using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Domain.CodeProfiles;

/// <summary>기존 설정 파일에 패치를 적용했을 때의 결과(쓰지 않는 dry-run).</summary>
/// <param name="MergedJson">적용 뒤 파일 내용(2칸 들여쓰기, 줄바꿈 `\n`, 끝에 줄바꿈).</param>
/// <param name="CommentsWillBeLost">기존 파일에 주석이 있어서, 다시 쓰면 사라진다.</param>
/// <param name="ExistingFile">기존 settings.json 이 있었는지.</param>
public sealed record SettingsDiff(IReadOnlyList<SettingChange> Changes, string MergedJson, bool CommentsWillBeLost, bool ExistingFile)
{
    public IReadOnlyList<SettingChange> Added => Changes.Where(c => c.Kind == ChangeKind.Added).ToList();

    public IReadOnlyList<SettingChange> Unchanged => Changes.Where(c => c.Kind == ChangeKind.Unchanged).ToList();

    public IReadOnlyList<SettingChange> Conflicts => Changes.Where(c => c.Kind == ChangeKind.Conflict).ToList();

    /// <summary>파일에 실제로 써야 할 변화가 있는지(추가되었거나 교체된 충돌이 있는지). 유지된 충돌만 있으면 쓰지 않는다.</summary>
    public bool HasChanges => Changes.Any(c => c.Kind == ChangeKind.Added || (c.Kind == ChangeKind.Conflict && c.Replaced));
}

public static class SettingsDiffer
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// <paramref name="existingText"/>(null 이면 새 파일)에 <paramref name="patch"/> 를 병합한 diff 를 만든다. 파일은 건드리지 않는다.
    /// 기존 파일이 비어 있으면 `{}` 로 본다. 문법 오류이거나 최상위가 객체가 아니면 실패.
    /// </summary>
    public static CodeResult<SettingsDiff> Compute(string? existingText, JsonObject patch, IReadOnlySet<string>? replaceConflicts = null)
    {
        var existing = new JsonObject();
        var hadComments = false;
        if (existingText != null)
        {
            var doc = Jsonc.Parse(existingText);
            if (!doc.Ok) return CodeResult<SettingsDiff>.Fail(doc.Error!);
            hadComments = doc.Value!.HadComments;
            switch (doc.Value.Value)
            {
                case null:
                    break;
                case JsonObject obj:
                    existing = obj;
                    break;
                default:
                    return CodeResult<SettingsDiff>.Fail("settings.json 의 최상위 값이 객체({})가 아닙니다");
            }
        }

        var merged = SettingsMerge.Merge(existing, patch, replaceConflicts);
        return CodeResult<SettingsDiff>.Success(new SettingsDiff(
            merged.Changes,
            merged.Merged.ToJsonString(Indented) + "\n",
            hadComments,
            existingText != null));
    }
}
