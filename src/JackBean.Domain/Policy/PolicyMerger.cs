using System.Globalization;
using System.Text.Json.Nodes;

namespace JackBean.Domain.Policy;

public enum MergeItemKind { App, Rule }

public enum MergeNoteKind
{
    /// <summary>상대 쪽에서 지운 것을 따라 지웠다(이 쪽은 마지막 동기화 뒤로 안 고쳤다).</summary>
    RemovedWithTheOtherSide,
    /// <summary>상대 쪽에서 지웠지만 이 쪽에서 그 뒤에 고쳤으므로 남겼다.</summary>
    KeptEditedOverDelete,
    /// <summary>같은 항목을 양쪽에서 다르게 고쳐서 더 최근에 고친 쪽을 썼다. Detail = 이긴 쪽(`local`/`remote`).</summary>
    ConflictResolved,
    /// <summary>합치고 보니 어긋나서 고쳤다(없는 앱을 가리키는 규칙, 겹치는 확장자, 같은 대상의 규칙이 둘 …). Detail = 무엇을.</summary>
    Repaired,
}

public sealed record MergeNote(MergeItemKind Item, string Id, MergeNoteKind Kind, string? Detail = null);

public sealed record MergeResult(JsonObject Merged, IReadOnlyList<MergeNote> Notes);

/// <summary>
/// 두 정책(이 PC 것과 GitHub 것)을 **앱·규칙 단위**로 합친다(3-way: 마지막으로 동기화한 모습 `base` 와 비교).
/// 항목마다: 한쪽만 바뀌었으면 바뀐 쪽, 양쪽이 같게 바뀌었으면 그대로, 양쪽이 다르게 바뀌었으면 `updated` 가 더 최근인 쪽(같으면 이 PC).
/// 한쪽에만 있는 항목은 새로 생긴 것이면 합치고, `base` 에 있었다면 상대가 지운 것이라 따라 지운다(이쪽이 그 뒤에 고쳤으면 남긴다).
/// `base` 가 없으면(처음 동기화) 지운 것을 알 수 없어서 양쪽을 합집합으로 본다. 합친 뒤에는 편집 때와 같은 규칙(확장자는 한 규칙에만,
/// 드라이브·폴더·파일 규칙은 하나씩, 없는 앱은 규칙에서 빼기)으로 어긋난 것을 고치고 규칙을 구체적인 것이 위로 정렬한다.
/// 사용자가 적은 모르는 키는 이긴 쪽의 항목 그대로 보존한다.
/// </summary>
public static class PolicyMerger
{
    public static MergeResult Merge(JsonObject? @base, JsonObject local, JsonObject remote)
    {
        var notes = new List<MergeNote>();

        var apps = MergeItems(MergeItemKind.App, AppItems(@base), AppItems(local), AppItems(remote), notes);
        var rules = MergeItems(MergeItemKind.Rule, RuleItems(@base), RuleItems(local), RuleItems(remote), notes);

        Repair(apps, rules, notes);

        var merged = (JsonObject)local.DeepClone();
        var appsObject = new JsonObject();
        foreach (var (id, node) in apps) appsObject[id] = node;
        merged["apps"] = appsObject;
        merged["rules"] = new JsonArray(Sorted(rules.Select(r => r.Node)).ToArray());
        return new MergeResult(merged, notes);
    }

    // --- 항목 꺼내기 ---

    private static List<(string Id, JsonNode Node)> AppItems(JsonObject? policy) =>
        policy?["apps"] is JsonObject apps ? apps.Where(kv => kv.Value != null).Select(kv => (kv.Key, kv.Value!.DeepClone())).ToList() : [];

    private static List<(string Id, JsonNode Node)> RuleItems(JsonObject? policy) =>
        policy?["rules"] is JsonArray rules
            ? rules.OfType<JsonObject>().Select(r => (Id: RuleCondition.Str(r["id"]), Node: (JsonNode)r.DeepClone())).Where(x => !string.IsNullOrEmpty(x.Id)).Select(x => (x.Id!, x.Node)).ToList()
            : [];

    private static DateTimeOffset Updated(JsonNode? node) =>
        node is JsonObject o && RuleCondition.Str(o["updated"]) is { } s
        && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.MinValue;

    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    // --- 항목마다 합치기 ---

    private static List<(string Id, JsonNode Node)> MergeItems(MergeItemKind kind,
        List<(string Id, JsonNode Node)> baseItems, List<(string Id, JsonNode Node)> local, List<(string Id, JsonNode Node)> remote, List<MergeNote> notes)
    {
        var b = baseItems.ToDictionary(x => x.Id, x => x.Node);
        var l = local.ToDictionary(x => x.Id, x => x.Node);
        var r = remote.ToDictionary(x => x.Id, x => x.Node);
        var hasBase = baseItems.Count > 0 || b.Count > 0;

        var order = local.Select(x => x.Id).Concat(remote.Select(x => x.Id).Where(id => !l.ContainsKey(id))).ToList();   // 이 PC 순서 먼저, 상대에만 있는 것은 뒤에
        var result = new List<(string Id, JsonNode Node)>();
        foreach (var id in order)
        {
            var hasL = l.TryGetValue(id, out var ln);
            var hasR = r.TryGetValue(id, out var rn);
            var hasB = b.TryGetValue(id, out var bn);
            JsonNode? pick;

            if (hasL && hasR)
            {
                if (Same(ln, rn)) pick = ln;
                else if (hasB && Same(ln, bn)) pick = rn;               // 이 쪽은 그대로, 상대가 고쳤다
                else if (hasB && Same(rn, bn)) pick = ln;               // 상대는 그대로, 이 쪽이 고쳤다
                else
                {
                    var remoteWins = Updated(rn) > Updated(ln);          // 같으면 이 PC
                    pick = remoteWins ? rn : ln;
                    notes.Add(new MergeNote(kind, id, MergeNoteKind.ConflictResolved, remoteWins ? "remote" : "local"));
                }
            }
            else if (hasL)
            {
                // 상대에는 없다
                if (!hasBase || !hasB) pick = ln;                        // 이 쪽에서 새로 만들었다(또는 처음 동기화라 지운 것인지 모른다)
                else if (Same(ln, bn)) { pick = null; notes.Add(new MergeNote(kind, id, MergeNoteKind.RemovedWithTheOtherSide, "remote")); }   // 상대가 지웠고 이 쪽은 안 고쳤다
                else { pick = ln; notes.Add(new MergeNote(kind, id, MergeNoteKind.KeptEditedOverDelete, "local")); }
            }
            else
            {
                // 이 쪽에는 없다
                if (!hasBase || !hasB) pick = rn;                        // 상대가 새로 만들었다
                else if (Same(rn, bn)) { pick = null; notes.Add(new MergeNote(kind, id, MergeNoteKind.RemovedWithTheOtherSide, "local")); }   // 이 쪽이 지웠고 상대는 안 고쳤다
                else { pick = rn; notes.Add(new MergeNote(kind, id, MergeNoteKind.KeptEditedOverDelete, "remote")); }
            }
            if (pick != null) result.Add((id, pick.DeepClone()));
        }
        return result;
    }

    // --- 합친 뒤 어긋난 것 고치기 ---

    private static void Repair(List<(string Id, JsonNode Node)> apps, List<(string Id, JsonNode Node)> rules, List<MergeNote> notes)
    {
        // 1) 같은 대상(드라이브·폴더·그 밖의 파일)의 규칙은 하나만 — 더 최근에 고친 것(같으면 앞의 것)을 남긴다.
        foreach (var kind in new[] { RuleKind.Drive, RuleKind.Folder, RuleKind.File })
        {
            var same = rules.Where(x => WhenOf(x.Node)?.Kind == kind).ToList();
            if (same.Count < 2) continue;
            var keep = same.OrderByDescending(x => Updated(x.Node)).First();
            foreach (var extra in same.Where(x => x.Id != keep.Id))
            {
                rules.Remove(extra);
                notes.Add(new MergeNote(MergeItemKind.Rule, extra.Id, MergeNoteKind.Repaired, $"{kind}: {keep.Id}"));
            }
        }

        // 2) 확장자는 한 규칙에만 — 더 최근에 고친 규칙이 가진다.
        var byRecent = rules.Where(x => WhenOf(x.Node)?.Kind == RuleKind.Extension).OrderByDescending(x => Updated(x.Node)).ToList();
        var owner = new Dictionary<string, string>();
        foreach (var (id, node) in byRecent)
        {
            var when = WhenOf(node)!;
            var keepExts = new List<string>();
            foreach (var ext in when.Extensions)
            {
                if (owner.TryAdd(ext, id)) keepExts.Add(ext);
                else notes.Add(new MergeNote(MergeItemKind.Rule, id, MergeNoteKind.Repaired, $"ext {ext}: {owner[ext]}"));
            }
            if (keepExts.Count == when.Extensions.Count) continue;
            var index = rules.FindIndex(x => x.Id == id);
            if (keepExts.Count == 0) rules.RemoveAt(index);
            else ((JsonObject)node)["when"] = RuleCondition.Extension(keepExts)!.ToJson();
        }

        // 3) 없는 앱을 가리키는 규칙은 그 앱을 뺀다.
        var appIds = apps.Select(a => a.Id).Concat(KnownApps.BuiltInTools).ToHashSet();   // 내장 도구는 파일에 없어도 있는 앱이다
        foreach (var (id, node) in rules)
        {
            if (node is not JsonObject rule || RuleChoices.Read(rule) is not { } choices) continue;
            var present = choices.Where(appIds.Contains).ToList();
            if (present.Count == choices.Count) continue;
            RuleChoices.Write(rule, present);
            notes.Add(new MergeNote(MergeItemKind.Rule, id, MergeNoteKind.Repaired, "app: " + string.Join(",", choices.Except(present))));
        }
    }

    private static RuleCondition? WhenOf(JsonNode node) => node is JsonObject o ? RuleCondition.Parse(o["when"]) : null;

    /// <summary>구체적인 규칙이 위로(안정 정렬) — 편집 창이 저장할 때와 같은 순서다.</summary>
    private static IEnumerable<JsonNode?> Sorted(IEnumerable<JsonNode> rules) => rules
        .Select((node, index) => (node, index, rank: node is JsonObject o && RuleCondition.Parse(o["when"]) is { } w ? RuleOrder.Rank(w) : 3))
        .OrderBy(x => x.rank).ThenBy(x => x.index).Select(x => (JsonNode?)x.node);
}
