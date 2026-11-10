using JackBean.Domain.Policy;
using JackBean.Domain.Drop;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Services;

/// <summary>정책에 넣거나 고칠 규칙 하나. <see cref="BeforeRuleId"/> 규칙 바로 위에 넣는다(없으면 맨 위).</summary>
public sealed record RuleUpsert(string Id, JsonObject When, string Open, string? BeforeRuleId = null);

public sealed record SaveResult(string? Error)
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
    /// Linux `${XDG_CONFIG_HOME:-~/.config}/JacksBean`. 기존 Flutter 앱과 같은 폴더라 정책 파일을 그대로 이어 쓴다.
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
            return new PolicyDocument(fb.Apps, fb.Rules, [$"{FilePath}: {e.Message} — 기본 정책을 씁니다(파일은 그대로 둠)"], isFallback: true);
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
            return new ResetResult(null, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ResetResult(e.Message, null);
        }
    }

    /// <summary>
    /// 규칙을 넣거나 고친다. 파일의 나머지(사용자가 적은 앱·규칙·모르는 키)는 그대로 둔다.
    /// 같은 id 가 있으면 그 자리에서 when/open 만 바꾼다. 파일이 깨져 있으면 아무것도 쓰지 않는다.
    /// 임시 파일에 쓰고 바꿔 끼운다.
    /// </summary>
    public async Task<SaveResult> UpsertRulesAsync(IReadOnlyList<RuleUpsert> upserts, DateTime? now = null)
    {
        try
        {
            if (!File.Exists(FilePath)) await LoadAsync();
            var decoded = ParseObject(await File.ReadAllTextAsync(FilePath));
            if (RuleCondition.Str(decoded["os"]) != Os.JsonName())
                return new SaveResult($"정책 파일의 os 가 {Os.JsonName()} 이 아닙니다: {decoded["os"]}");
            if (!(decoded["schema"] is JsonValue sv && sv.TryGetValue<int>(out var sch) && sch == 1))
                return new SaveResult($"정책 파일의 schema 가 1 이 아닙니다: {decoded["schema"]}");
            if (decoded["rules"] is { } rr && rr is not JsonArray) return new SaveResult("정책 파일의 rules 가 목록이 아닙니다");

            var rules = new List<JsonNode?>();
            if (decoded["rules"] is JsonArray existing) foreach (var r in existing) rules.Add(r?.DeepClone());
            var stamp = (now ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
            int IndexOf(string? id) => id == null ? -1 : rules.FindIndex(r => r is JsonObject o && RuleCondition.Str(o["id"]) == id);

            foreach (var u in upserts)
            {
                var at = IndexOf(u.Id);
                if (at >= 0)
                {
                    var rule = (JsonObject)rules[at]!;
                    rule["when"] = u.When.DeepClone();
                    rule["open"] = u.Open;
                    rule["updated"] = stamp;
                }
                else
                {
                    var before = IndexOf(u.BeforeRuleId);
                    rules.Insert(before >= 0 ? before : 0, new JsonObject
                    {
                        ["id"] = u.Id, ["when"] = u.When.DeepClone(), ["open"] = u.Open, ["updated"] = stamp,
                    });
                }
            }
            decoded["rules"] = new JsonArray(rules.ToArray());

            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, Serialize(decoded));
            File.Move(tmp, FilePath, overwrite: true);
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
