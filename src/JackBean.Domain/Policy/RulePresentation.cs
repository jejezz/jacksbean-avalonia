namespace JackBean.Domain.Policy;

/// <summary>홈 창이 규칙 한 줄을 어떻게 보여 줄지(화면과 무관한 부분): 아이콘 이름, 제목 종류, 확장자 칩.</summary>
public sealed record RuleLabel(string Icon, string? GroupId, string RuleId, RuleKind Kind, IReadOnlyList<string> Extensions)
{
    /// <summary>제목이 확장자 묶음의 이름(이미지·PDF …)인가. 아니면 규칙 id 를 그대로 쓴다.</summary>
    public bool HasGroupName => GroupId != null;
}

public static class RulePresentation
{
    /// <summary>묶음 id → 아이콘 이름(`scripts/gen-phosphor-icons.py` 의 ICONS 키).</summary>
    private static readonly Dictionary<string, string> GroupIcons = new()
    {
        ["image"] = "image", ["audio"] = "audio", ["video"] = "video", ["pdf"] = "pdf", ["document"] = "file-doc",
        ["spreadsheet"] = "file-xls", ["presentation"] = "file-ppt", ["archive"] = "file-zip", ["text"] = "file-text", ["code"] = "code",
    };

    public static RuleLabel Label(Rule rule)
    {
        switch (rule.When.Kind)
        {
            case RuleKind.Drive: return new("drive", null, rule.Id, RuleKind.Drive, []);
            case RuleKind.Folder: return new("folder", null, rule.Id, RuleKind.Folder, []);
            case RuleKind.File: return new("file", null, rule.Id, RuleKind.File, []);
        }
        var exts = rule.When.Extensions;
        // 제목은 규칙 id 가 묶음 id 와 같을 때만 묶음 이름을 쓴다(저장 때 확장자가 묶음과 똑같으면 id 를 묶음 이름으로 정한다).
        var named = ExtensionGroups.Find(rule.Id) ?? ExtensionGroups.Find(rule.Id.TrimEnd((char)115));   // 기본 정책의 `images` 처럼 복수형 id 도 묶음 이름으로 본다
        // 아이콘은 확장자가 모두 한 묶음에 속하면 그 묶음의 것, 아니면 일반 파일.
        var groups = exts.Select(e => ExtensionGroups.GroupOf(e)?.Id).Distinct().ToList();
        var iconGroup = named?.Id ?? (groups.Count == 1 && groups[0] != null ? groups[0] : null);
        var icon = iconGroup != null && GroupIcons.TryGetValue(iconGroup, out var i) ? i : "file";
        return new(icon, named?.Id, rule.Id, RuleKind.Extension, exts);
    }

    /// <summary>확장자 칩: 앞 <paramref name="max"/>개와 나머지 개수(0 이면 전부 보였다).</summary>
    public static (IReadOnlyList<string> Shown, int More) Chips(IReadOnlyList<string> extensions, int max)
        => (extensions.Take(max).ToList(), Math.Max(0, extensions.Count - max));
}
