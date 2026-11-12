using System.Text.Json;
using System.Text.Json.Nodes;
using JackBean.Domain.Drop;
using JackBean.Domain.Recent;

namespace JackBean.Services.Recent;

/// <summary>
/// 최근 작업을 **이 PC 전용 파일**(`recent.json`, 설정과 같은 폴더)에 남긴다 — 경로는 PC 마다 달라서 동기화하지 않는다.
/// 경로와 앱 ID 만 적는다. 읽을 때는 관대하고(모양이 틀린 항목은 건너뜀), 파일이 깨졌으면 `.broken` 으로 옮기고 비어 있는 것으로 시작한다.
/// 쓸 때는 임시 파일에 쓴 뒤 교체한다. 처음 읽은 뒤로는 메모리의 목록을 원본으로 쓴다(앱은 하나만 뜬다).
/// </summary>
public sealed class RecentStore(string directory, bool? ignoreCase = null)
{
    public const string FileName = "recent.json";
    private const int Schema = 1;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _ignoreCase = ignoreCase ?? OperatingSystem.IsWindows();
    private IReadOnlyList<RecentEntry>? _entries;

    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>목록이 바뀌었다(어느 스레드에서든 불린다).</summary>
    public event Action? Changed;

    /// <summary>최근 것부터.</summary>
    public async Task<IReadOnlyList<RecentEntry>> ListAsync()
    {
        await _gate.WaitAsync();
        try { return await EnsureLoadedAsync(); }
        finally { _gate.Release(); }
    }

    /// <summary>작업들을 맨 앞에 더한다(같은 작업은 한 줄로, 최대 <see cref="RecentHistory.MaxEntries"/>개).</summary>
    public Task AddAsync(IEnumerable<RecentEntry> entries) => MutateAsync(list =>
    {
        // 앞의 것이 맨 위에 오도록 거꾸로 더한다.
        foreach (var e in entries.Reverse()) list = RecentHistory.Add(list, e, _ignoreCase);
        return list;
    });

    public Task RemoveAsync(RecentEntry entry) => MutateAsync(list => list.Where(e => !e.SameWork(entry, _ignoreCase)).ToList());

    public Task ClearAsync() => MutateAsync(_ => []);

    private async Task MutateAsync(Func<IReadOnlyList<RecentEntry>, IReadOnlyList<RecentEntry>> change)
    {
        await _gate.WaitAsync();
        try
        {
            var before = await EnsureLoadedAsync();
            var after = change(before);
            if (after.Count == 0 && before.Count == 0 && !File.Exists(FilePath)) return;   // 지울 것이 없다 — 빈 파일을 만들지 않는다
            _entries = after;
            await SaveAsync(after);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private async Task<IReadOnlyList<RecentEntry>> EnsureLoadedAsync() => _entries ??= await LoadAsync();

    private async Task<IReadOnlyList<RecentEntry>> LoadAsync()
    {
        var result = new List<RecentEntry>();
        if (!File.Exists(FilePath)) return result;
        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(FilePath)) as JsonObject;
            if (root?["entries"] is not JsonArray entries) return result;
            foreach (var node in entries)
            {
                if (node is not JsonObject o
                    || o["paths"] is not JsonArray ps
                    || o["app"] is not JsonValue a || !a.TryGetValue<string>(out var app) || app.Length == 0
                    || o["at"] is not JsonValue t || !t.TryGetValue<string>(out var at) || !DateTimeOffset.TryParse(at, out var when)) continue;
                var paths = ps.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>().Where(s => s.Length > 0).ToList();
                if (paths.Count == 0) continue;
                DropKind? kind = o["kind"] is JsonValue k && k.TryGetValue<string>(out var ks) && Enum.TryParse<DropKind>(ks, ignoreCase: true, out var parsed) ? parsed : null;
                result.Add(new RecentEntry(paths, kind, app, when));
                if (result.Count == RecentHistory.MaxEntries) break;
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            try { File.Move(FilePath, FilePath + ".broken", overwrite: true); } catch (Exception) { /* 못 옮겨도 비어 있는 것으로 시작한다 */ }
            result.Clear();
        }
        return result;
    }

    private async Task SaveAsync(IReadOnlyList<RecentEntry> entries)
    {
        var array = new JsonArray();
        foreach (var e in entries)
        {
            var o = new JsonObject { ["paths"] = new JsonArray(e.Paths.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) };
            if (e.Kind is { } k) o["kind"] = k.JsonName();
            o["app"] = e.AppId;
            o["at"] = e.At.ToString("O");
            array.Add(o);
        }
        var root = new JsonObject { ["schema"] = Schema, ["entries"] = array };
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, root.ToJsonString(Indented) + "\n");
        File.Move(temp, FilePath, overwrite: true);
    }
}
