using JackBean.Domain.Policy;
using JackBean.Domain.Drop;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Services;

/// <summary>규칙 편집 폼이 만든 규칙 하나. <paramref name="Choices"/> 는 앱 ID 들(0~10개, 순서가 목록 순서).</summary>
public sealed record RuleEdit(string Id, RuleCondition When, IReadOnlyList<string> Choices);

/// <summary>정책에 넣거나 고칠 앱 하나(편집 폼이 만든다).</summary>
public sealed record AppUpsert(string Id, string Command, IReadOnlyList<string> Args);

/// <summary>앱을 지울 때 그 앱을 여는 규칙을 어떻게 할지.</summary>
public enum ReferencedRules { Refuse, MakeAsk }

public sealed record SaveResult(string? Error, IReadOnlyList<RuleProblem>? Problems = null)
{
    public bool Ok => Error == null;
    public static SaveResult Success { get; } = new((string?)null);
}

public sealed record ResetResult(string? Error, string? BackupPath)
{
    public bool Ok => Error == null;
}

/// <summary>이 OS의 정책 파일(`policy.&lt;os&gt;.json`)을 읽고, 없으면 기본 정책으로 만든다.</summary>
public sealed class PolicyStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public PolicyStore(PolicyOs os, string? directory = null)
    {
        Os = os;
        Directory = directory ?? DataDirectory(os);
    }

    public PolicyOs Os { get; }
    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, $"policy.{Os.JsonName()}.json");

    /// <summary>
    /// 사용자 데이터 폴더: Windows `%APPDATA%\JacksBean`, macOS `~/Library/Application Support/JacksBean`,
    /// Linux `${XDG_CONFIG_HOME:-~/.config}/JacksBean`.
    /// </summary>
    public static string DataDirectory(PolicyOs os, IReadOnlyDictionary<string, string?>? env = null)
    {
        string? Get(string k) => env != null ? env.GetValueOrDefault(k) : Environment.GetEnvironmentVariable(k);
        var home = Get("HOME") ?? Get("USERPROFILE") ?? ".";
        return os switch
        {
            PolicyOs.Windows => Path.Combine(Get("APPDATA") ?? Path.Combine(home, "AppData", "Roaming"), "JacksBean"),
            PolicyOs.Macos => Path.Combine(home, "Library", "Application Support", "JacksBean"),
            _ => Path.Combine(Get("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "JacksBean"),
        };
    }

    /// <summary>
    /// 매번 파일을 다시 읽는다 — 손으로 고쳐도 바로 반영된다. 파일이 없으면 기본 정책을 쓰고 돌려준다.
    /// 깨져 있으면 기본 정책을 돌려주되 파일은 덮어쓰지 않는다(Warnings 에 이유).
    /// </summary>
    public async Task<PolicyDocument> LoadAsync()
    {
        var defaults = PolicyDocument.DefaultJson(Os);
        if (!File.Exists(FilePath))
        {
            System.IO.Directory.CreateDirectory(Directory);
            await File.WriteAllTextAsync(FilePath, Serialize(defaults));
            return PolicyDocument.FromJson(defaults);
        }
        try
        {
            var decoded = ParseObject(await File.ReadAllTextAsync(FilePath));
            var os = RuleCondition.Str(decoded["os"]);
            if (os != Os.JsonName()) throw new FormatException($"os 가 {Os.JsonName()} 이 아닙니다: {os}");
            return PolicyDocument.FromJson(decoded);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            var fb = PolicyDocument.FromJson(defaults);
            return new PolicyDocument(fb.Apps, fb.Rules, [$"{FilePath}: {e.Message} — 기본 정책을 씁니다(파일은 그대로 둠)"], isFallback: true, os: Os);
        }
    }

    /// <summary>
    /// 정책 파일을 기본 정책으로 다시 만든다. 지금 파일은 지우지 않고 같은 폴더에 `.broken-날짜시각` 으로 옮겨 둔다
    /// (손으로 고친 내용을 되찾을 수 있게). 파일이 없으면 그냥 만든다. 돌려주는 값의 <see cref="ResetResult.BackupPath"/> 가 옮긴 곳.
    /// </summary>
    public async Task<ResetResult> ResetToDefaultsAsync(DateTime? now = null)
    {
        try
        {
            string? backup = null;
            if (File.Exists(FilePath))
            {
                backup = $"{FilePath}.broken-{(now ?? DateTime.Now):yyyyMMdd-HHmmss}";
                File.Move(FilePath, backup, overwrite: true);
            }
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, Serialize(PolicyDocument.DefaultJson(Os)));
            File.Move(tmp, FilePath, overwrite: true);
            _previousText = _lastWrittenText = null;
            return new ResetResult(null, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ResetResult(e.Message, null);
        }
    }

    /// <summary>
    /// "항상 이렇게 열기": 제안한 규칙들을 <paramref name="appId"/> 로 열도록 저장한다. 규칙은 대상마다 하나씩이라
    /// 드라이브·폴더는 이미 있는 그 규칙의 앱을 바꾸고(없으면 `drives`/`folders` 를 만들고), 확장자는 다른 규칙에서 빼서
    /// 그 확장자만의 규칙(`auto-ext-확장자`)으로 옮긴다 — 개별 지정이 우선이고, 같은 확장자가 두 규칙에 겹치지 않는다.
    /// 규칙이 비게 되면 지운다. 저장하면 규칙들은 구체적인 것이 위로 정렬된다.
    /// </summary>
    public Task<SaveResult> RememberAsync(IReadOnlyList<RuleSuggestion> suggestions, string appId, DateTime? now = null) => MutateAsync(decoded =>
    {
        var rules = new List<JsonNode?>();
        if (decoded["rules"] is JsonArray existing) foreach (var r in existing) rules.Add(r?.DeepClone());
        var stamp = Stamp(now);
        var objects = rules.OfType<JsonObject>().ToList();
        JsonObject? Of(RuleKind kind) => objects.FirstOrDefault(o => RuleCondition.Parse(o["when"])?.Kind == kind);
        string FreeId(string wanted)
        {
            if (!objects.Any(o => RuleCondition.Str(o["id"]) == wanted)) return wanted;
            for (var i = 2; ; i++) if (!objects.Any(o => RuleCondition.Str(o["id"]) == $"{wanted}-{i}")) return $"{wanted}-{i}";
        }
        void Upsert(JsonObject? rule, string newId, RuleCondition when)
        {
            if (rule == null)
            {
                rule = new JsonObject { ["id"] = FreeId(newId), ["when"] = when.ToJson() };
                rules.Insert(0, rule);
                objects.Add(rule);
            }
            RuleChoices.Write(rule, [appId]);
            rule["updated"] = stamp;
        }

        foreach (var s in suggestions)
        {
            switch (s.Kind)
            {
                case SuggestionKind.Drive: Upsert(Of(RuleKind.Drive), "drives", RuleCondition.Drive); break;
                case SuggestionKind.Folder: Upsert(Of(RuleKind.Folder), "folders", RuleCondition.Folder); break;
                case SuggestionKind.Extension:
                    var ext = ExtensionGroups.Normalize(s.Arg);
                    if (ext.Length == 0) break;
                    // 다른 확장자 규칙에서 이 확장자를 뺀다(비면 그 규칙을 지운다).
                    foreach (var o in objects.ToList())
                    {
                        if (RuleCondition.Str(o["id"]) == $"auto-ext-{ext}") continue;
                        var when = RuleCondition.Parse(o["when"]);
                        if (when is not { Kind: RuleKind.Extension } || !when.Extensions.Contains(ext)) continue;
                        var left = RuleCondition.Extension(when.Extensions.Where(e => e != ext));
                        if (left == null) { rules.Remove(o); objects.Remove(o); }
                        else { o["when"] = left.ToJson(); o["updated"] = stamp; }
                    }
                    var own = objects.FirstOrDefault(o => RuleCondition.Str(o["id"]) == $"auto-ext-{ext}");
                    Upsert(own, $"auto-ext-{ext}", RuleCondition.Extension([ext])!);
                    break;
            }
        }
        if (rules.Count > PolicyLimits.MaxRules) return new SaveResult($"규칙은 {PolicyLimits.MaxRules}개까지입니다");
        decoded["rules"] = new JsonArray(SortByRank(rules).ToArray());
        return null;
    });

    /// <summary>
    /// 앱을 넣거나 고친다. 있는 앱이면 command·args 만 바꾸고 그 앱의 나머지 키(사용자가 적은 것)는 그대로 둔다.
    /// ID 는 규칙이 가리키는 이름이라 여기서 바꾸지 않는다.
    /// </summary>
    public Task<SaveResult> UpsertAppAsync(AppUpsert app) => MutateAsync(decoded =>
    {
        if (decoded["apps"] is not null and not JsonObject) return new SaveResult("정책 파일의 apps 가 객체가 아닙니다");
        var apps = decoded["apps"] as JsonObject;
        if (apps is null) decoded["apps"] = apps = new JsonObject();
        if (apps[app.Id] is not JsonObject node) apps[app.Id] = node = new JsonObject();
        node["command"] = app.Command;
        node["args"] = new JsonArray(app.Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
        return null;
    });

    /// <summary>
    /// 앱을 지운다. 그 앱을 여는 규칙이 있으면 <paramref name="referenced"/> 에 따라 막거나(<see cref="ReferencedRules.Refuse"/>),
    /// 그 규칙들을 "물어보기"(`open: null`)로 바꾼다 — 규칙은 지우지 않는다.
    /// </summary>
    public Task<SaveResult> RemoveAppAsync(string id, ReferencedRules referenced = ReferencedRules.Refuse, DateTime? now = null) => MutateAsync(decoded =>
    {
        if (decoded["apps"] is not JsonObject apps || !apps.ContainsKey(id)) return new SaveResult($"앱 {id} 이(가) 정책에 없습니다");
        var users = (decoded["rules"] as JsonArray ?? [])
            .OfType<JsonObject>().Where(r => RuleChoices.Read(r)?.Contains(id) == true).ToList();
        if (users.Count > 0)
        {
            if (referenced == ReferencedRules.Refuse)
                return new SaveResult($"규칙 {users.Count}개가 이 앱을 씁니다: {string.Join(", ", users.Select(r => RuleCondition.Str(r["id"])))}");
            var stamp = Stamp(now);
            foreach (var r in users)
            {
                // 그 규칙의 앱 목록에서만 뺀다: 목록에 다른 앱이 남으면 그 앱(들)로, 마지막 앱이었으면 "물어보기".
                RuleChoices.Write(r, RuleChoices.Read(r)!.Where(c => c != id).ToList());
                r["updated"] = stamp;
            }
        }
        apps.Remove(id);
        return null;
    });

    /// <summary>
    /// 규칙 하나를 넣거나 고친다(앱 1~10개 = `open`/`choices`). 저장 전에 <see cref="RuleCheck"/> 로 검사한다 — 한도(255)·앱 수(10)·
    /// 없는 앱·겹치는 확장자 — 그리고 통과하면 규칙들을 <see cref="RuleOrder"/> 순서(구체적인 것이 위)로 정렬해 쓴다.
    /// 그 규칙의 `open`/`choices`/`when`/`updated` 말고 사용자가 적은 키는 그대로 둔다.
    /// </summary>
    public Task<SaveResult> UpsertRuleAsync(RuleEdit edit, DateTime? now = null) => MutateAsync(decoded =>
    {
        var doc = PolicyDocument.FromJson(decoded);
        var isNew = doc.Rules.All(r => r.Id != edit.Id);
        var problems = RuleCheck.Validate(new Rule(edit.Id, edit.When, edit.Choices), doc, isNew);
        if (problems.Count > 0) return new SaveResult(string.Join("; ", problems.Select(Describe)), problems);

        var rules = new List<JsonNode?>();
        if (decoded["rules"] is JsonArray existing) foreach (var r in existing) rules.Add(r?.DeepClone());
        var stamp = Stamp(now);
        var at = rules.FindIndex(r => r is JsonObject o && RuleCondition.Str(o["id"]) == edit.Id);
        JsonObject rule;
        if (at >= 0) rule = (JsonObject)rules[at]!;
        else rules.Insert(0, rule = new JsonObject { ["id"] = edit.Id });
        rule["when"] = edit.When.ToJson();
        RuleChoices.Write(rule, edit.Choices);
        rule["updated"] = stamp;
        decoded["rules"] = new JsonArray(SortByRank(rules).ToArray());
        return null;
    });

    /// <summary>규칙 하나를 지운다. 그 규칙의 앱은 건드리지 않는다.</summary>
    public Task<SaveResult> RemoveRuleAsync(string id) => MutateAsync(decoded =>
    {
        if (decoded["rules"] is not JsonArray rules) return new SaveResult($"규칙 {id} 이(가) 정책에 없습니다");
        var at = rules.ToList().FindIndex(r => r is JsonObject o && RuleCondition.Str(o["id"]) == id);
        if (at < 0) return new SaveResult($"규칙 {id} 이(가) 정책에 없습니다");
        rules.RemoveAt(at);
        return null;
    });

    /// <summary>구체적인 규칙이 위로 오게 안정 정렬한다(같은 자리 규칙들은 원래 순서 그대로). 조건을 못 읽는 규칙은 일반 규칙 자리.</summary>
    private static List<JsonNode?> SortByRank(List<JsonNode?> rules) => rules
        .Select((node, index) => (node, index, rank: node is JsonObject o && RuleCondition.Parse(o["when"]) is { } w ? RuleOrder.Rank(w) : 3))
        .OrderBy(x => x.rank).ThenBy(x => x.index).Select(x => x.node).ToList();

    private static string Describe(RuleProblem p) => p.Kind switch
    {
        RuleProblemKind.RuleLimit => $"규칙은 {PolicyLimits.MaxRules}개까지입니다",
        RuleProblemKind.TooManyApps => $"규칙 하나에 앱은 {PolicyLimits.MaxAppsPerRule}개까지입니다",
        RuleProblemKind.UnknownApp => $"정책에 없는 앱입니다: {p.Detail}",
        RuleProblemKind.KindTaken => $"{p.Detail} 규칙은 하나만 둘 수 있습니다(이미 규칙 {p.Other} 가 있습니다)",
        RuleProblemKind.ExtensionTaken => $"확장자 {p.Detail} 은(는) 규칙 {p.Other} 에도 있습니다",
        RuleProblemKind.IdTaken => $"이미 있는 규칙 이름입니다: {p.Detail}",
        _ => p.Kind.ToString(),
    };

    private static string Stamp(DateTime? now) => (now ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

    /// <summary>
    /// 파일을 읽어 <paramref name="edit"/> 로 고치고 임시 파일에 쓴 뒤 바꿔 끼운다. 파일이 깨져 있거나 다른 OS 것이면 아무것도 쓰지 않는다.
    /// <paramref name="edit"/> 가 SaveResult 를 돌려주면 그것을 결과로 하고 쓰지 않는다(null 이면 저장).
    /// </summary>
    // 실행 취소 한 단계: 마지막 변경 전의 파일 내용과, 그 변경으로 우리가 쓴 내용. 앱을 끌 때까지만 기억한다.
    private string? _previousText;
    private string? _lastWrittenText;

    /// <summary>방금 한 변경(앱·규칙 저장·삭제, 항상 이렇게 열기)을 되돌릴 수 있다.</summary>
    public bool CanUndo => _previousText != null && _lastWrittenText != null;

    /// <summary>
    /// 마지막 변경을 되돌린다. 그 뒤 파일을 손으로 고쳤으면(우리가 쓴 내용과 다르면) 그 수정을 덮어쓰지 않도록 거절한다.
    /// 되돌린 뒤 다시 부르면 방금 되돌린 변경이 돌아온다(다시 실행).
    /// </summary>
    public async Task<SaveResult> UndoAsync()
    {
        if (!CanUndo) return new SaveResult("되돌릴 변경이 없습니다");
        try
        {
            var current = File.Exists(FilePath) ? await File.ReadAllTextAsync(FilePath) : null;
            if (current != _lastWrittenText) return new SaveResult("정책 파일이 그 사이에 바뀌어 되돌릴 수 없습니다");
            var restore = _previousText!;
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, restore);
            File.Move(tmp, FilePath, overwrite: true);
            _previousText = current;
            _lastWrittenText = restore;
            return SaveResult.Success;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SaveResult($"정책 파일을 쓰지 못했습니다: {e.Message}");
        }
    }

    private async Task<SaveResult> MutateAsync(Func<JsonObject, SaveResult?> edit)
    {
        try
        {
            if (!File.Exists(FilePath)) await LoadAsync();
            var before = await File.ReadAllTextAsync(FilePath);
            var decoded = ParseObject(before);
            if (RuleCondition.Str(decoded["os"]) != Os.JsonName())
                return new SaveResult($"정책 파일의 os 가 {Os.JsonName()} 이 아닙니다: {decoded["os"]}");
            if (!(decoded["schema"] is JsonValue sv && sv.TryGetValue<int>(out var sch) && sch == PolicyDocument.SchemaVersion))
                return new SaveResult($"정책 파일의 schema 가 {PolicyDocument.SchemaVersion} 이 아닙니다: {decoded["schema"]}");
            if (decoded["rules"] is { } rr && rr is not JsonArray) return new SaveResult("정책 파일의 rules 가 목록이 아닙니다");

            if (edit(decoded) is { } failure) return failure;

            var after = Serialize(decoded);
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, after);
            File.Move(tmp, FilePath, overwrite: true);
            _previousText = before;
            _lastWrittenText = after;
            return SaveResult.Success;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return new SaveResult($"정책 파일을 읽지 못했습니다: {e.Message}");
        }
        catch (IOException e)
        {
            return new SaveResult($"정책 파일을 쓰지 못했습니다: {e.Message}");
        }
    }

    private static JsonObject ParseObject(string text) =>
        JsonNode.Parse(text) as JsonObject ?? throw new FormatException("최상위가 객체가 아닙니다");

    private static string Serialize(JsonObject o) => o.ToJsonString(Indented) + "\n";
}
