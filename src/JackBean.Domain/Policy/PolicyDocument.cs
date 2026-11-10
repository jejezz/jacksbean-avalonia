// 정책 파일(schema 2)을 읽은 모습과 규칙 판정.
//
//   { "schema": 2, "os": "windows",
//     "apps":  { "<앱 ID>": { "command": "...", "args": ["{path}"] } },
//     "rules": [ { "id": "...", "when": { "kind": "extension", "extensions": ["png", "jpg"] }, "open": ["<앱 ID>", ...] } ] }
//
// 대상(`when.kind`)은 네 가지다: drive · folder · file(그 밖의 모든 파일 = 기본 앱) · extension(확장자 묶음).
// 규칙은 **위에서부터 처음 맞는 것**이 이긴다(저장할 때 구체적인 것이 위로 정렬된다 — RuleOrder). `open` 은 앱 0~10개:
// 1개면 바로 열고, 2개 이상이면 **그 앱들만** 골라서 묻는다. 0개면 아직 앱을 연결하지 않은 것이라 열 수 없다(규칙 편집으로 안내).
// 읽을 때는 관대하다: 모양이 틀린 앱·규칙은 건너뛰고 Warnings 에 이유를 남긴다 — 규칙 하나가 틀렸다고 독 전체가 멈추면 안 된다.

using System.Text.Json;
using System.Text.Json.Nodes;
using JackBean.Domain.Drop;

namespace JackBean.Domain.Policy;

public sealed record AppEntry(string Id, string Command, IReadOnlyList<string> Args)
{
    public AppEntry(string id, string command) : this(id, command, ["{path}"]) { }
}

/// <summary>규칙의 대상 종류.</summary>
public enum RuleKind { Drive, Folder, File, Extension }

/// <summary>규칙 하나의 조건(JSON 의 `when`): 대상 종류, 확장자 규칙이면 확장자 목록.</summary>
public sealed class RuleCondition
{
    private RuleCondition(RuleKind kind, IReadOnlyList<string>? extensions = null)
    {
        Kind = kind;
        Extensions = extensions ?? [];
    }

    public RuleKind Kind { get; }

    /// <summary>확장자 규칙의 확장자(소문자, 점 없음, 중복 없음). 다른 종류는 비어 있다.</summary>
    public IReadOnlyList<string> Extensions { get; }

    public static RuleCondition Drive { get; } = new(RuleKind.Drive);
    public static RuleCondition Folder { get; } = new(RuleKind.Folder);
    public static RuleCondition File { get; } = new(RuleKind.File);

    /// <summary>확장자 규칙. `.PNG` 도 `png` 로 고르고 중복은 버린다. 확장자가 하나도 없으면 null.</summary>
    public static RuleCondition? Extension(IEnumerable<string> extensions)
    {
        var list = extensions.Select(ExtensionGroups.Normalize).Where(e => e.Length > 0).Distinct().ToList();
        return list.Count == 0 ? null : new RuleCondition(RuleKind.Extension, list);
    }

    private static string KindName(RuleKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>모양이 틀리면 null.</summary>
    public static RuleCondition? Parse(JsonNode? json)
    {
        if (json is not JsonObject obj) return null;
        switch (Str(obj["kind"]))
        {
            case "drive": return Drive;
            case "folder": return Folder;
            case "file": return File;
            case "extension":
                if (obj["extensions"] is not JsonArray arr) return null;
                var values = new List<string>();
                foreach (var v in arr)
                {
                    var s = Str(v);
                    if (s == null) return null;
                    values.Add(s);
                }
                return Extension(values);
            default:
                return null;
        }
    }

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["kind"] = KindName(Kind) };
        if (Kind == RuleKind.Extension) json["extensions"] = new JsonArray(Extensions.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray());
        return json;
    }

    public static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public bool Matches(DroppedItem item) => Kind switch
    {
        RuleKind.Drive => item.Kind == DropKind.Drive,
        RuleKind.Folder => item.Kind == DropKind.Folder,
        RuleKind.File => item.Kind == DropKind.File,
        RuleKind.Extension => item.Kind == DropKind.File && Extensions.Contains(item.Extension),
        _ => false,
    };
}

/// <summary>
/// 규칙 하나: 조건에 맞으면 <see cref="Choices"/> 의 앱으로 연다. 앱이 1개면 바로 열고, 2개 이상이면 **그 앱들만** 골라서 묻는다.
/// 0개면 연결된 앱이 없어 열 수 없다 — 다른 대상(폴더 …)의 앱을 섞어 보여 주지 않는다. 한 규칙에 최대 <see cref="PolicyLimits.MaxAppsPerRule"/>개.
/// </summary>
public sealed record Rule(string Id, RuleCondition When, IReadOnlyList<string> Choices);

public abstract record Decision;

/// <summary>앱으로 연다.</summary>
public sealed record OpenWith(AppEntry App, string RuleId) : Decision;

/// <summary>사용자에게 어떤 앱으로 열지 묻는다(앱이 2개 이상이다). Candidates = 고를 수 있는 앱 — 그 규칙에 연결된 앱뿐이다. 비어 있으면(규칙에 앱이 없거나 맞는 규칙이 없다) 고를 앱이 없다.</summary>
public sealed record Ask(string? RuleId = null, IReadOnlyList<AppEntry>? Candidates = null) : Decision;

/// <summary>규칙은 맞았는데 그 앱이 정책의 `apps`에 없다.</summary>
public sealed record MissingApp(string AppId, string RuleId) : Decision;

/// <summary>열 수 없는 항목(없는 경로).</summary>
public sealed record Ignore : Decision;

public sealed class PolicyDocument
{
    public const int SchemaVersion = 2;

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

    /// <summary>정책 JSON 을 읽는다. 모양이 아예 틀리면(schema 가 다르면) <see cref="FormatException"/>.</summary>
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
                if (choices == null) { warnings.Add($"규칙 \"{id}\": open 이 앱 이름의 목록이 아니라 건너뜀"); continue; }
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
            if (rule.Choices.Count == 0) return new Ask(rule.Id, []);   // 연결된 앱이 없다
            var apps = rule.Choices.Where(Apps.ContainsKey).Select(id => Apps[id]).ToList();
            if (apps.Count == 0) return new MissingApp(rule.Choices[0], rule.Id);
            if (rule.Choices.Count == 1) return new OpenWith(apps[0], rule.Id);
            return new Ask(rule.Id, apps);   // 2개 이상: 그 앱들만 고르게 한다(정책에 없는 앱은 뺀다)
        }
        return new Ask(null, []);
    }

    /// <summary>
    /// 이 OS의 기본 정책: 카탈로그가 아는 앱만. 드라이브는 파일 관리자로, 파일은 시스템 기본 앱으로 열고,
    /// 폴더는 파일 관리자와 터미널 중에서 고르게 한다(터미널이 없는 OS 는 파일 관리자 하나). 설치 경로를 모르는 앱은 일부러 넣지 않는다.
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
                Rule("drives", RuleCondition.Drive, Present("explorer")),
                Rule("folders", RuleCondition.Folder, Present("explorer", "terminal")),
                Rule("files", RuleCondition.File, Present("default"))),
        };

        string[] Present(params string[] ids) => ids.Where(apps.ContainsKey).ToArray();

        static JsonObject Rule(string id, RuleCondition when, params string[] open)
        {
            var rule = new JsonObject { ["id"] = id, ["when"] = when.ToJson() };
            RuleChoices.Write(rule, open);
            return rule;
        }
    }
}
