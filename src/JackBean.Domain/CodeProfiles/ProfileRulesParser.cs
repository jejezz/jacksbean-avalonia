using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JackBean.Domain.CodeProfiles;

/// <summary>
/// 규칙 JSON(주석·끝 쉼표 허용)을 <see cref="ProfileRules"/> 로 바꾼다. 구조만 검사한다 — 유형 사이의 참조는
/// 병합 뒤에 <see cref="ProfileRules.Validate"/> 로 본다. 오류에는 값의 경로(`types[2].priority`)와 줄 번호가 붙는다.
///
/// 스키마(`version` `scan` `variables` `types` `sizeRules`)는 Flutter 판 rules.yaml 과 같다:
/// <code>
/// { "version": 1,
///   "scan": { "maxDepth": 3, "maxFiles": 20000, "skipDirs": [".git"] },
///   "variables": { "aospRoot": { "findUpward": { "anyOf": [".repo"], "maxLevels": 12 } } },
///   "types": [ { "id": "esp-idf", "label": "ESP-IDF", "priority": 100,
///                "match": { "any": [ { "file": "sdkconfig" } ] },
///                "suppresses": ["cmake"], "profile": "ESP-IDF", "extensions": ["..."], "settings": { }, "notes": ["..."] } ],
///   "sizeRules": [ { "when": { "truncated": true }, "settings": { }, "notes": ["..."] } ] }
/// </code>
/// 매처는 `all` `any`(목록) · `file` `dir`(glob) · `fileContains`({glob, pattern}) · `variable`(이름) 중 정확히 하나의 키를 가진다.
/// </summary>
public static class ProfileRulesParser
{
    /// <summary>사용자 규칙의 정규식이 폭주(되돌아가기 폭발)해도 판정이 멈추지 않게 하는 제한.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <param name="defaults">`scan` 에서 빠진 필드를 채울 값. 사용자 규칙은 기본 규칙의 scan 을 넘겨 일부 필드만 바꿀 수 있다.</param>
    public static CodeResult<ProfileRules> Parse(string text, ScanConfig? defaults = null)
    {
        var doc = Jsonc.Parse(text);
        if (!doc.Ok) return CodeResult<ProfileRules>.Fail(doc.Error!);
        if (doc.Value!.Value == null) return CodeResult<ProfileRules>.Fail("규칙이 비어 있습니다");

        try
        {
            return CodeResult<ProfileRules>.Success(ParseRoot(doc.Value.Value, defaults ?? ScanConfig.Defaults));
        }
        catch (FormatError e)
        {
            var message = e.Path.Length == 0 ? e.Message : $"{e.Path}: {e.Message}";
            return CodeResult<ProfileRules>.Fail(message, JsonPositions.Build(text).LineOf(e.Path));
        }
    }

    private sealed class FormatError(string path, string message) : Exception(message)
    {
        public string Path { get; } = path;
    }

    private static ProfileRules ParseRoot(JsonNode root, ScanConfig defaults)
    {
        var obj = Obj(root, "", "최상위");
        CheckKeys(obj, "", "version", "scan", "variables", "types", "sizeRules");

        if (Int(Required(obj, "version", ""), "version", "version") != 1)
            throw new FormatError("version", "지원하지 않는 version 입니다(1만 지원)");

        var types = new List<TypeRule>();
        var seen = new HashSet<string>();
        if (obj["types"] is { } typesNode)
        {
            var arr = Arr(typesNode, "types", "types");
            for (var i = 0; i < arr.Count; i++)
            {
                var type = ParseType(arr[i], Index("types", i));
                if (!seen.Add(type.Id)) throw new FormatError(Index("types", i), $"유형 id '{type.Id}'가 중복됩니다");
                types.Add(type);
            }
        }

        var sizeRules = new List<SizeRule>();
        if (obj["sizeRules"] is { } sizeNode)
        {
            var arr = Arr(sizeNode, "sizeRules", "sizeRules");
            for (var i = 0; i < arr.Count; i++) sizeRules.Add(ParseSizeRule(arr[i], Index("sizeRules", i)));
        }

        return new ProfileRules(
            1,
            obj["scan"] is { } scan ? ParseScan(scan, defaults) : defaults,
            obj["variables"] is { } vars ? ParseVariables(vars) : new Dictionary<string, VariableRule>(),
            types,
            sizeRules);
    }

    private static ScanConfig ParseScan(JsonNode node, ScanConfig defaults)
    {
        var obj = Obj(node, "scan", "scan");
        CheckKeys(obj, "scan", "maxDepth", "maxFiles", "skipDirs");
        var maxDepth = obj["maxDepth"] is { } d ? Int(d, "scan.maxDepth", "maxDepth") : defaults.MaxDepth;
        var maxFiles = obj["maxFiles"] is { } f ? Int(f, "scan.maxFiles", "maxFiles") : defaults.MaxFiles;
        if (maxDepth < 0) throw new FormatError("scan.maxDepth", "maxDepth 는 0 이상이어야 합니다");
        if (maxFiles < 1) throw new FormatError("scan.maxFiles", "maxFiles 는 1 이상이어야 합니다");
        return new ScanConfig(maxDepth, maxFiles, obj["skipDirs"] is { } s ? Strings(s, "scan.skipDirs", "skipDirs") : defaults.SkipDirs);
    }

    private static Dictionary<string, VariableRule> ParseVariables(JsonNode node)
    {
        var result = new Dictionary<string, VariableRule>();
        foreach (var (name, value) in Obj(node, "variables", "variables"))
        {
            var path = Key("variables", name);
            var body = Obj(value, path, $"변수 '{name}'");
            CheckKeys(body, path, "findUpward");
            var upPath = Key(path, "findUpward");
            var up = Obj(Required(body, "findUpward", path), upPath, "findUpward");
            CheckKeys(up, upPath, "anyOf", "maxLevels");
            var anyOf = Strings(Required(up, "anyOf", upPath), Key(upPath, "anyOf"), "anyOf");
            if (anyOf.Count == 0) throw new FormatError(Key(upPath, "anyOf"), "anyOf 는 비어 있을 수 없습니다");
            var maxLevels = up["maxLevels"] is { } l ? Int(l, Key(upPath, "maxLevels"), "maxLevels") : 12;
            if (maxLevels < 1) throw new FormatError(Key(upPath, "maxLevels"), "maxLevels 는 1 이상이어야 합니다");
            result[name] = new VariableRule(anyOf, maxLevels);
        }
        return result;
    }

    private static TypeRule ParseType(JsonNode? node, string path)
    {
        var obj = Obj(node, path, "유형");
        CheckKeys(obj, path, "id", "label", "priority", "match", "suppresses", "profile", "extensions", "settings", "notes");
        return new TypeRule(
            Str(Required(obj, "id", path), Key(path, "id"), "id"),
            Str(Required(obj, "label", path), Key(path, "label"), "label"),
            Int(Required(obj, "priority", path), Key(path, "priority"), "priority"),
            ParseMatcher(Required(obj, "match", path), Key(path, "match")),
            obj["suppresses"] is { } s ? Strings(s, Key(path, "suppresses"), "suppresses") : [],
            Str(Required(obj, "profile", path), Key(path, "profile"), "profile"),
            obj["extensions"] is { } e ? Strings(e, Key(path, "extensions"), "extensions") : [],
            obj["settings"] is { } st ? Settings(st, Key(path, "settings")) : new JsonObject(),
            obj["notes"] is { } n ? Strings(n, Key(path, "notes"), "notes") : []);
    }

    private static SizeRule ParseSizeRule(JsonNode? node, string path)
    {
        var obj = Obj(node, path, "sizeRules 항목");
        CheckKeys(obj, path, "when", "settings", "notes");
        var whenPath = Key(path, "when");
        var when = Obj(Required(obj, "when", path), whenPath, "when");
        CheckKeys(when, whenPath, "truncated");
        var truncatedPath = Key(whenPath, "truncated");
        if (Required(when, "truncated", whenPath) is not JsonValue v || !v.TryGetValue<bool>(out var truncated))
            throw new FormatError(truncatedPath, "truncated 는 true/false 여야 합니다");
        return new SizeRule(
            truncated,
            obj["settings"] is { } st ? Settings(st, Key(path, "settings")) : new JsonObject(),
            obj["notes"] is { } n ? Strings(n, Key(path, "notes"), "notes") : []);
    }

    private static Matcher ParseMatcher(JsonNode node, string path)
    {
        var obj = Obj(node, path, "match");
        if (obj.Count != 1)
            throw new FormatError(path, "match 에는 all/any/file/dir/fileContains/variable 중 정확히 하나의 키가 있어야 합니다");
        var (key, value) = obj.Single();
        var valuePath = Key(path, key);
        switch (key)
        {
            case "all" or "any":
                var items = Arr(value, valuePath, key);
                if (items.Count == 0) throw new FormatError(valuePath, $"{key} 는 비어 있을 수 없습니다");
                var children = new List<Matcher>();
                for (var i = 0; i < items.Count; i++)
                    children.Add(ParseMatcher(items[i] ?? throw new FormatError(Index(valuePath, i), "match 가 비어 있습니다"), Index(valuePath, i)));
                return key == "all" ? new AllMatcher(children) : new AnyMatcher(children);
            case "file":
                return new FileMatcher(Str(value, valuePath, "file"));
            case "dir":
                return new DirMatcher(Str(value, valuePath, "dir"));
            case "variable":
                return new VariableMatcher(Str(value, valuePath, "variable"));
            case "fileContains":
                var body = Obj(value, valuePath, "fileContains");
                CheckKeys(body, valuePath, "glob", "pattern");
                var patternPath = Key(valuePath, "pattern");
                var pattern = Str(Required(body, "pattern", valuePath), patternPath, "pattern");
                Regex regex;
                try
                {
                    regex = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
                }
                catch (ArgumentException e)
                {
                    throw new FormatError(patternPath, $"정규식이 올바르지 않습니다: {e.Message}");
                }
                return new FileContainsMatcher(Str(Required(body, "glob", valuePath), Key(valuePath, "glob"), "glob"), regex);
            default:
                throw new FormatError(valuePath, $"알 수 없는 매처 '{key}' 입니다");
        }
    }

    private static string Key(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string Index(string path, int i) => $"{path}[{i}]";

    private static JsonObject Obj(JsonNode? node, string path, string what) =>
        node as JsonObject ?? throw new FormatError(path, $"{what}은(는) 키-값 목록이어야 합니다");

    private static JsonArray Arr(JsonNode? node, string path, string what) =>
        node as JsonArray ?? throw new FormatError(path, $"{what}은(는) 목록이어야 합니다");

    private static string Str(JsonNode? node, string path, string what) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0
            ? s
            : throw new FormatError(path, $"{what}은(는) 비어 있지 않은 문자열이어야 합니다");

    private static int Int(JsonNode? node, string path, string what) =>
        node is JsonValue v && v.TryGetValue<int>(out var i) ? i : throw new FormatError(path, $"{what}은(는) 정수여야 합니다");

    private static List<string> Strings(JsonNode? node, string path, string what)
    {
        var arr = Arr(node, path, what);
        var list = new List<string>();
        for (var i = 0; i < arr.Count; i++) list.Add(Str(arr[i], Index(path, i), $"{what}의 항목"));
        return list;
    }

    private static JsonNode Required(JsonObject obj, string key, string path) =>
        obj[key] ?? throw new FormatError(path, $"필수 키 '{key}'가 없습니다");

    private static void CheckKeys(JsonObject obj, string path, params string[] allowed)
    {
        foreach (var (key, _) in obj)
            if (!allowed.Contains(key))
                throw new FormatError(Key(path, key), $"알 수 없는 키 '{key}' 입니다(허용: {string.Join(", ", allowed)})");
    }

    private static JsonObject Settings(JsonNode node, string path) => (JsonObject)Obj(node, path, "settings").DeepClone();
}
