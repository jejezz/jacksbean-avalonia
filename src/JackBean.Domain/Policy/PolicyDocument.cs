// 정책 파일(schema 1)을 읽은 모습과 규칙 판정.
//
//   { "schema": 1, "os": "windows",
//     "apps":  { "<논리 ID>": { "command": "...", "args": ["{path}"] } },
//     "rules": [ { "id": "...", "when": { "kind": "extension", "values": ["png"] }, "open": "<논리 ID>" } ] }
//
// 규칙은 **위에서부터 처음 맞는 것**이 이긴다. `open`이 없거나 null 이면 "물어보기"다.
// 읽을 때는 관대하다: 모양이 틀린 앱·규칙은 건너뛰고 Warnings 에 이유를 남긴다 — 규칙 하나가 틀렸다고 독 전체가 멈추면 안 된다.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JackBean.Domain.Drop;

namespace JackBean.Domain.Policy;

public sealed record AppEntry(string Id, string Command, IReadOnlyList<string> Args)
{
    public AppEntry(string id, string command) : this(id, command, ["{path}"]) { }
}

/// <summary>
/// 규칙 하나의 조건(JSON 의 `when`). extension / hasGit / kind / nameGlob — 어느 OS 에서나 뜻이 같다.
/// </summary>
public sealed class RuleCondition
{
    private RuleCondition(string kind, IReadOnlyList<string>? values = null, string? value = null, string? pattern = null)
    {
        Kind = kind;
        Values = values ?? [];
        Value = value;
        Pattern = pattern;
        if (pattern != null) _glob = GlobToRegex(pattern);
    }

    public string Kind { get; }
    public IReadOnlyList<string> Values { get; }
    public string? Value { get; }
    public string? Pattern { get; }
    private readonly Regex? _glob;

    /// <summary>모양이 틀리면 null.</summary>
    public static RuleCondition? Parse(JsonNode? json)
    {
        if (json is not JsonObject obj) return null;
        var kind = Str(obj["kind"]);
        switch (kind)
        {
            case "extension":
                if (obj["values"] is not JsonArray arr || arr.Count == 0) return null;
                var values = new List<string>();
                foreach (var v in arr)
                {
                    var s = Str(v);
                    if (s == null) return null;
                    var lower = s.ToLowerInvariant();
                    values.Add(lower.StartsWith('.') ? lower[1..] : lower);
                }
                return new RuleCondition("extension", values);
            case "hasGit":
                var raw = obj["value"];
                if (raw != null && !(raw is JsonValue jv && jv.TryGetValue<bool>(out _))) return null;
                return new RuleCondition("hasGit", value: raw == null ? "true" : raw.GetValue<bool>() ? "true" : "false");
            case "kind":
                var k = Str(obj["value"]);
                return k is "drive" or "folder" or "file" ? new RuleCondition("kind", value: k) : null;
            case "nameGlob":
                var p = Str(obj["pattern"]);
                return string.IsNullOrEmpty(p) ? null : new RuleCondition("nameGlob", pattern: p);
            default:
                return null;
        }
    }

    public static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public bool Matches(DroppedItem item) => Kind switch
    {
        "extension" => item.Kind == DropKind.File && Values.Contains(item.Extension),
        "hasGit" => item.Kind == DropKind.Folder && item.HasGit == (Value == "true"),
        "kind" => item.Kind.JsonName() == Value,
        "nameGlob" => _glob!.IsMatch(item.Name),
        _ => false,
    };

    private static Regex GlobToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder("^");
        foreach (var ch in pattern)
            sb.Append(ch switch { '*' => ".*", '?' => ".", _ => Regex.Escape(ch.ToString()) });
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}

/// <summary>
/// 규칙 하나: 조건에 맞으면 <see cref="Choices"/> 의 앱으로 연다. 앱이 1개면 바로 열고, 2개 이상이면 **그 앱들만** 골라서 묻고,
/// 0개면 등록된 앱 전부를 보여 주며 묻는다. 한 규칙에 최대 <see cref="PolicyLimits.MaxAppsPerRule"/>개.
/// </summary>
public sealed record Rule(string Id, RuleCondition When, IReadOnlyList<string> Choices)
{
    public Rule(string id, RuleCondition when, string? open) : this(id, when, open == null ? [] : [open]) { }

    /// <summary>앱이 정확히 1개일 때 그 앱(Flutter 판이 읽는 `open`). 0개나 2개 이상이면 null.</summary>
    public string? Open => Choices.Count == 1 ? Choices[0] : null;
}

public abstract record Decision;

/// <summary>앱으로 연다.</summary>
public sealed record OpenWith(AppEntry App, string RuleId) : Decision;

/// <summary>사용자에게 어떤 앱으로 열지 묻는다(규칙이 "물어보기"이거나 앱이 2개 이상이거나 맞는 규칙이 없다). Candidates = 고를 수 있는 앱(null 이면 등록된 앱 전부).</summary>
public sealed record Ask(string? RuleId = null, IReadOnlyList<AppEntry>? Candidates = null) : Decision;

/// <summary>규칙은 맞았는데 그 앱이 정책의 `apps`에 없다.</summary>
public sealed record MissingApp(string AppId, string RuleId) : Decision;

/// <summary>열 수 없는 항목(없는 경로).</summary>
public sealed record Ignore : Decision;

public sealed class PolicyDocument
{
    public const int SchemaVersion = 1;

    public PolicyDocument(IReadOnlyDictionary<string, AppEntry>? apps = null, IReadOnlyList<Rule>? rules = null, IReadOnlyList<string>? warnings = null, bool isFallback = false)
    {
        IsFallback = isFallback;
        Apps = apps ?? new Dictionary<string, AppEntry>();
        Rules = rules ?? [];
        Warnings = warnings ?? [];
    }

    public IReadOnlyDictionary<string, AppEntry> Apps { get; }
    public IReadOnlyList<Rule> Rules { get; }

    /// <summary>파일 전체를 읽지 못해 기본 정책을 대신 쓰는 중이다(파일은 그대로 있다). 규칙 하나만 건너뛴 경우는 false.</summary>
    public bool IsFallback { get; }

    /// <summary>이 앱을 여는 규칙들(앱을 지우거나 고치기 전에 영향받는 규칙을 알려 주려고).</summary>
    public IReadOnlyList<Rule> RulesUsing(string appId) => Rules.Where(r => r.Choices.Contains(appId)).ToList();

    /// <summary>읽다가 건너뛴 것들의 이유.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>정책 JSON 을 읽는다. 모양이 아예 틀리면 <see cref="FormatException"/>.</summary>
    public static PolicyDocument FromJson(JsonObject json)
    {
        var schema = json["schema"] is JsonValue sv && sv.TryGetValue<int>(out var n) ? n : (int?)null;
        if (schema != SchemaVersion) throw new FormatException($"schema 가 {SchemaVersion} 이 아닙니다: {json["schema"]}");
        var warnings = new List<string>();

        var apps = new Dictionary<string, AppEntry>();
        if (json["apps"] is JsonObject rawApps)
        {
            foreach (var (key, spec) in rawApps)
            {
                var command = spec is JsonObject so ? RuleCondition.Str(so["command"]) : null;
                if (string.IsNullOrEmpty(command)) { warnings.Add($"앱 \"{key}\": command 가 없어 건너뜀"); continue; }
                var args = new List<string> { "{path}" };
                var rawArgs = ((JsonObject)spec!)["args"];
                if (rawArgs != null)
                {
                    if (rawArgs is not JsonArray aa || aa.Any(a => RuleCondition.Str(a) == null))
                    { warnings.Add($"앱 \"{key}\": args 가 문자열 목록이 아니라 건너뜀"); continue; }
                    args = aa.Select(a => RuleCondition.Str(a)!).ToList();
                }
                apps[key] = new AppEntry(key, command, args);
            }
        }

        var rules = new List<Rule>();
        var seen = new HashSet<string>();
        if (json["rules"] is JsonArray rawRules)
        {
            foreach (var raw in rawRules)
            {
                var id = raw is JsonObject ro ? RuleCondition.Str(ro["id"]) : null;
                if (string.IsNullOrEmpty(id)) { warnings.Add("id 없는 규칙을 건너뜀"); continue; }
                if (!seen.Add(id)) { warnings.Add($"규칙 \"{id}\": id 가 겹쳐 뒤의 것을 건너뜀"); continue; }
                var obj = (JsonObject)raw!;
                var when = RuleCondition.Parse(obj["when"]);
                if (when == null) { warnings.Add($"규칙 \"{id}\": 조건(when)을 알 수 없어 건너뜀"); continue; }
                var choices = RuleChoices.Read(obj);
                if (choices == null) { warnings.Add($"규칙 \"{id}\": open/choices 가 올바른 앱 이름이 아니라 건너뜀"); continue; }
                if (choices.Count > PolicyLimits.MaxAppsPerRule)
                {
                    warnings.Add($"규칙 \"{id}\": 앱이 {PolicyLimits.MaxAppsPerRule}개를 넘어 앞 {PolicyLimits.MaxAppsPerRule}개만 씀");
                    choices = choices.Take(PolicyLimits.MaxAppsPerRule).ToList();
                }
                rules.Add(new Rule(id, when, choices));
            }
        }
        // 한도를 넘긴 파일도 지우지 않고 그대로 읽는다(손으로 고친 것일 수 있다). 알리기만 한다.
        if (rules.Count > PolicyLimits.MaxRules) warnings.Add($"규칙이 {rules.Count}개로 한도 {PolicyLimits.MaxRules}개를 넘음");
        return new PolicyDocument(apps, rules, warnings);
    }

    /// <summary>항목 하나를 어떻게 처리할지 정한다.</summary>
    public Decision Decide(DroppedItem item)
    {
        if (item.Kind == DropKind.Missing) return new Ignore();
        foreach (var rule in Rules)
        {
            if (!rule.When.Matches(item)) continue;
            if (rule.Choices.Count == 0) return new Ask(rule.Id);
            var apps = rule.Choices.Where(Apps.ContainsKey).Select(id => Apps[id]).ToList();
            if (apps.Count == 0) return new MissingApp(rule.Choices[0], rule.Id);
            if (rule.Choices.Count == 1) return new OpenWith(apps[0], rule.Id);
            return new Ask(rule.Id, apps);   // 2개 이상: 그 앱들만 고르게 한다(정책에 없는 앱은 뺀다)
        }
        return new Ask();
    }

    /// <summary>
    /// 이 OS의 기본 정책: 카탈로그가 아는 앱만. 드라이브는 파일 관리자로, 파일은 시스템 기본 앱으로 열고,
    /// 폴더는 물어본다. 형제 앱은 설치 경로를 모르므로 일부러 넣지 않는다.
    /// </summary>
    public static JsonObject DefaultJson(PolicyOs os)
    {
        var apps = new JsonObject();
        foreach (var id in new[] { "explorer", "default", "terminal" })
        {
            var spec = KnownApps.Catalog[id].On(os);
            if (spec != null) apps[id] = spec.ToJson();
        }
        return new JsonObject
        {
            ["schema"] = SchemaVersion,
            ["os"] = os.JsonName(),
            ["apps"] = apps,
            ["rules"] = new JsonArray(
                Rule("drives", new JsonObject { ["kind"] = "kind", ["value"] = "drive" }, "explorer"),
                Rule("folders", new JsonObject { ["kind"] = "kind", ["value"] = "folder" }, null),
                Rule("files", new JsonObject { ["kind"] = "kind", ["value"] = "file" }, "default")),
        };

        static JsonObject Rule(string id, JsonObject when, string? open) => new() { ["id"] = id, ["when"] = when, ["open"] = open };
    }
}
