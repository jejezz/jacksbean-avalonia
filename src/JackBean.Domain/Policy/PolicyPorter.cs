// 정책 파일을 다른 OS용으로 옮긴다. 원칙: **아는 정보만 바꾸고, 모르는 것은 건너뛴다.** 건너뛴 것은 이유와 함께 보고한다.
//
// - apps: 카탈로그가 아는 앱만 대상 OS 실행 방법으로 바꾼다.
// - rules: 어느 OS에서나 뜻이 같은 조건만 옮긴다. 대상에 없는 앱을 여는 규칙은 건너뛴다(카탈로그가 알면 앱도 더한다).
// - 대상 파일에 이미 있는 항목(같은 앱 ID, 같은 규칙 id)은 그대로 둔다(overwrite 일 때만 덮어쓴다).
// - 모르는 최상위 키는 옮기지 않는다.

using System.Text.Json.Nodes;

namespace JackBean.Domain.Policy;

public enum PortAction { Converted, Kept, Skipped }

public sealed record PortNote(PortAction Action, string Subject, string Reason)
{
    public override string ToString() => $"{Action.ToString().ToLowerInvariant(),-9} {Subject} — {Reason}";
}

public sealed record PortResult(JsonObject Policy, IReadOnlyList<PortNote> Notes)
{
    public IEnumerable<PortNote> Of(PortAction action) => Notes.Where(n => n.Action == action);
}

public static class PolicyPorter
{
    /// <summary>어느 OS에서나 같은 뜻인 조건 종류.</summary>
    public static readonly IReadOnlySet<string> PortableWhenKinds = new HashSet<string> { "extension", "hasGit", "kind", "nameGlob" };

    public static PortResult Port(JsonObject source, PolicyOs target, JsonObject? existing = null, bool overwrite = false)
    {
        var sourceOs = ReadOs(source, "source");
        if (existing != null)
        {
            var existingOs = ReadOs(existing, "existing");
            if (existingOs != target)
                throw new FormatException($"existing 정책의 os({existingOs.JsonName()})가 대상({target.JsonName()})과 다릅니다.");
        }

        var notes = new List<PortNote>();
        var apps = new JsonObject();
        if (existing?["apps"] is JsonObject ea) foreach (var (k, v) in ea) apps[k] = v?.DeepClone();
        var rules = new List<JsonObject>();
        if (existing?["rules"] is JsonArray er) foreach (var r in er) if (r is JsonObject ro) rules.Add((JsonObject)ro.DeepClone());

        var sameOs = sourceOs == target;
        var t = target.JsonName();

        if (source["apps"] is JsonObject sa)
        {
            foreach (var (id, value) in sa)
            {
                var subject = $"app {id}";
                if (apps.ContainsKey(id) && !overwrite) { notes.Add(new(PortAction.Kept, subject, "대상에 이미 있어 그대로 둠")); continue; }
                if (sameOs) { apps[id] = value?.DeepClone(); notes.Add(new(PortAction.Converted, subject, "같은 OS라 그대로 복사")); continue; }
                if (!KnownApps.Catalog.TryGetValue(id, out var known))
                { notes.Add(new(PortAction.Skipped, subject, $"카탈로그에 없는 앱 — {t}에서의 실행 방법을 모름")); continue; }
                var spec = known.On(target);
                if (spec == null) { notes.Add(new(PortAction.Skipped, subject, $"{t}에서의 실행 방법을 모름")); continue; }
                apps[id] = spec.ToJson();
                notes.Add(new(PortAction.Converted, subject, $"{sourceOs.JsonName()} → {t} 실행 방법으로 바꿈"));
            }
        }

        var index = new Dictionary<string, int>();
        for (var i = 0; i < rules.Count; i++) if (RuleId(rules[i]) is { } rid) index[rid] = i;

        if (source["rules"] is JsonArray sr)
        {
            foreach (var raw in sr)
            {
                if (raw is not JsonObject rule || RuleId(rule) is not { } id)
                { notes.Add(new(PortAction.Skipped, "rule (id 없음)", "id가 없는 규칙은 병합할 수 없음")); continue; }
                var subject = $"rule {id}";
                var has = index.TryGetValue(id, out var at);
                if (has && !overwrite) { notes.Add(new(PortAction.Kept, subject, "대상에 이미 있어 그대로 둠")); continue; }
                if (!sameOs)
                {
                    var kind = RuleCondition.Str((rule["when"] as JsonObject)?["kind"]);
                    if (kind == null || !PortableWhenKinds.Contains(kind))
                    { notes.Add(new(PortAction.Skipped, subject, $"조건 \"{kind}\"은 OS마다 뜻이 달라 옮기지 않음")); continue; }
                    var open = RuleCondition.Str(rule["open"]);
                    if (open != null && !apps.ContainsKey(open))
                    {
                        var spec = KnownApps.Catalog.TryGetValue(open, out var ka) ? ka.On(target) : null;
                        if (spec == null)
                        { notes.Add(new(PortAction.Skipped, subject, $"여는 앱 \"{open}\"이 {t}에 없음 (앱을 추가한 뒤 다시 포팅)")); continue; }
                        apps[open] = spec.ToJson();
                        notes.Add(new(PortAction.Converted, $"app {open}", $"규칙 \"{id}\"이 써서 카탈로그의 {t} 실행 방법으로 더함"));
                    }
                }
                var clone = (JsonObject)rule.DeepClone();
                if (has) rules[at] = clone; else { index[id] = rules.Count; rules.Add(clone); }
                notes.Add(new(PortAction.Converted, subject, sameOs ? "같은 OS라 그대로 복사" : $"{t}에서도 같은 뜻이라 옮김"));
            }
        }

        var policy = new JsonObject
        {
            ["schema"] = PolicyDocument.SchemaVersion,
            ["os"] = t,
            ["apps"] = apps,
            ["rules"] = new JsonArray(rules.Select(r => (JsonNode?)r).ToArray()),
        };
        return new PortResult(policy, notes);
    }

    private static string? RuleId(JsonObject rule) => RuleCondition.Str(rule["id"]);

    private static PolicyOs ReadOs(JsonObject policy, string label)
    {
        var schema = policy["schema"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : (int?)null;
        if (schema != PolicyDocument.SchemaVersion)
            throw new FormatException($"{label} 정책의 schema가 {PolicyDocument.SchemaVersion}이 아닙니다: {policy["schema"]}");
        return PolicyOsExtensions.Parse(RuleCondition.Str(policy["os"]))
            ?? throw new FormatException($"{label} 정책의 os가 windows/macos/linux 중 하나가 아닙니다: {policy["os"]}");
    }
}
