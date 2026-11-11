using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Services.CodeProfiles;

/// <summary>이 PC 에서 폴더에 적용해 둔 VS Code 프로필 기록.</summary>
public sealed record CodeProjectRecord(string Folder, string Profile, DateTimeOffset AppliedAt);

/// <summary>
/// 폴더 → 적용한 프로필을 **이 PC 전용 파일**(`code-profile-projects.json`)에 기억한다(경로는 PC 마다 달라서 동기화 대상이 아니다).
/// 다음에 같은 폴더를 드롭하면 분석·확인 없이 바로 그 프로필로 열 수 있다. 읽을 때는 관대하고(모양이 틀린 항목은 건너뜀),
/// 파일이 깨졌으면 `.broken` 으로 옮기고 비어 있는 것으로 시작한다. 쓸 때는 임시 파일에 쓴 뒤 교체한다.
/// </summary>
public sealed class CodeProjectMemory(string directory)
{
    public const string FileName = "code-profile-projects.json";
    private const int Schema = 1;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string FilePath => Path.Combine(directory, FileName);

    public async Task<CodeProjectRecord?> GetAsync(string folder)
    {
        await _gate.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            return projects.TryGetValue(Key(folder), out var record) ? record : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RememberAsync(string folder, string profile, DateTimeOffset appliedAt)
    {
        await _gate.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            projects[Key(folder)] = new CodeProjectRecord(Normalize(folder), profile, appliedAt);
            await SaveAsync(projects);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ForgetAsync(string folder)
    {
        await _gate.WaitAsync();
        try
        {
            var projects = await LoadAsync();
            if (projects.Remove(Key(folder))) await SaveAsync(projects);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    /// <summary>Windows 는 경로의 대소문자를 구분하지 않는다.</summary>
    private static string Key(string folder) => OperatingSystem.IsWindows() ? Normalize(folder).ToLowerInvariant() : Normalize(folder);

    private async Task<Dictionary<string, CodeProjectRecord>> LoadAsync()
    {
        var result = new Dictionary<string, CodeProjectRecord>();
        if (!File.Exists(FilePath)) return result;
        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(FilePath)) as JsonObject;
            if (root?["projects"] is not JsonObject projects) return result;
            foreach (var (_, value) in projects)
            {
                if (value is not JsonObject o
                    || o["folder"] is not JsonValue f || !f.TryGetValue<string>(out var folder)
                    || o["profile"] is not JsonValue p || !p.TryGetValue<string>(out var profile)
                    || o["appliedAt"] is not JsonValue a || !a.TryGetValue<string>(out var at)
                    || !DateTimeOffset.TryParse(at, out var appliedAt)) continue;
                result[Key(folder)] = new CodeProjectRecord(folder, profile, appliedAt);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Move(FilePath, FilePath + ".broken", overwrite: true); } catch (Exception) { /* 못 옮겨도 비어 있는 것으로 시작한다 */ }
            result.Clear();
        }
        return result;
    }

    private async Task SaveAsync(Dictionary<string, CodeProjectRecord> projects)
    {
        var obj = new JsonObject();
        foreach (var (key, r) in projects)
            obj[key] = new JsonObject { ["folder"] = r.Folder, ["profile"] = r.Profile, ["appliedAt"] = r.AppliedAt.ToString("O") };
        var root = new JsonObject { ["schema"] = Schema, ["projects"] = obj };

        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, root.ToJsonString(Indented) + "\n");
        File.Move(temp, FilePath, overwrite: true);
    }
}
