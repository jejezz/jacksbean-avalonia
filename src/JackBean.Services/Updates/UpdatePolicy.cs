using System.Text.Json;

namespace JackBean.Services.Updates;

/// <summary>자동으로 언제 확인할지, 사용자가 어느 버전을 거절했는지.</summary>
public sealed class UpdatePolicy(IUpdateStateStore store, TimeSpan? interval = null, Func<DateTimeOffset>? now = null)
{
    public const string LastCheckKey = "update.lastCheckMs";
    public const string SkippedVersionKey = "update.skippedVersion";

    readonly TimeSpan _interval = interval ?? TimeSpan.FromHours(24);
    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>간격 안에 성공한 자동 확인이 없으면 true. 시계를 뒤로 돌렸으면 "영영 안 함"이 아니라 확인한다.</summary>
    public bool IsDue()
    {
        if (!long.TryParse(store.Read(LastCheckKey), out var last)) return true;
        var elapsed = _now().ToUnixTimeMilliseconds() - last;
        return elapsed < 0 || elapsed >= _interval.TotalMilliseconds;
    }

    public void MarkChecked() => store.Write(LastCheckKey, _now().ToUnixTimeMilliseconds().ToString());

    public bool IsSkipped(string version) => store.Read(SkippedVersionKey) == version;

    /// <summary>"이 버전 건너뛰기" — 더 새로운 버전이 나오면 다시 알린다.</summary>
    public void Skip(string version) => store.Write(SkippedVersionKey, version);
}

public sealed class MemoryUpdateStateStore : IUpdateStateStore
{
    readonly Dictionary<string, string> _values = [];
    public string? Read(string key) => _values.GetValueOrDefault(key);
    public void Write(string key, string value) => _values[key] = value;
}

/// <summary>설정 폴더의 작은 JSON 파일 하나 (<c>update-state.json</c>). 읽기·쓰기 실패는 조용히 넘긴다.</summary>
public sealed class FileUpdateStateStore(string file) : IUpdateStateStore
{
    public string? Read(string key) => Load().GetValueOrDefault(key);

    public void Write(string key, string value)
    {
        var all = Load();
        all[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            using (var stream = File.Create(temp))
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                foreach (var (k, v) in all) json.WriteString(k, v);
                json.WriteEndObject();
            }
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 못 저장해도 앱을 멈추지 않는다. 다음 확인 때 다시 시도.
        }
    }

    Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(file)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) result[p.Name] = p.Value.GetString()!;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // 깨졌으면 비어 있는 것으로 본다.
        }
        return result;
    }

    /// <summary>설정 파일(<c>settings.json</c>)과 같은 폴더의 <c>update-state.json</c>.</summary>
    public static string BesideSettings(string settingsFile) => Path.Combine(Path.GetDirectoryName(settingsFile)!, "update-state.json");
}
