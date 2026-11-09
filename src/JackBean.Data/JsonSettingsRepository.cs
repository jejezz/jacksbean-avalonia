using System.Text.Json;
using System.Text.Json.Serialization;
using JackBean.Domain.Settings;

namespace JackBean.Data;

/// <summary>설정 JSON의 소스 생성 컨텍스트. 리플렉션이 없어서 NativeAOT에서 동작한다.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never, // 기본값이어도 키를 적는다 (Kotlin 판의 encodeDefaults)
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)] // 모르는 키는 무시
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// 설정을 JSON 파일 하나에 저장한다.
/// <list type="bullet">
/// <item>읽을 수 없는 파일(깨짐)은 <c>.broken</c>으로 옮겨 두고 기본값으로 시작한다. 다음 저장이 그 파일을 덮어쓰기 전에 남기는 것.</item>
/// <item>쓸 때는 임시 파일에 쓴 뒤 바꿔치기해서 도중에 꺼져도 반쯤 쓴 파일이 남지 않는다.</item>
/// <item>모르는 키는 무시하고, 모르는 값은 기본값으로 읽는다 (다른 버전이 쓴 파일).</item>
/// </list>
/// </summary>
public sealed class JsonSettingsRepository(string file) : ISettingsRepository
{
    public AppSettings Load()
    {
        if (!File.Exists(file)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(file), SettingsJsonContext.Default.AppSettings);
            return (settings ?? new AppSettings()).Normalized();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Move(file, file + ".broken", overwrite: true); } catch (Exception) { /* 못 옮겨도 기본값으로 시작한다 */ }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 설정을 못 저장한다고 앱을 멈출 수는 없다. 다음 저장 때 다시 시도한다.
        }
    }

    /// <summary>OS별 사용자 설정 위치 아래의 <c>&lt;이름&gt;/settings.json</c>.</summary>
    public static string DefaultFile(string appFileName)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string baseDir;
        if (OperatingSystem.IsWindows())
            baseDir = Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } a ? a : Path.Combine(home, "AppData", "Roaming");
        else if (OperatingSystem.IsMacOS())
            baseDir = Path.Combine(home, "Library", "Application Support");
        else
            baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
        return Path.Combine(baseDir, appFileName, "settings.json");
    }
}

/// <summary>메모리에만 두는 저장소 (테스트용).</summary>
public sealed class InMemorySettingsRepository(AppSettings? initial = null) : ISettingsRepository
{
    public AppSettings Stored { get; private set; } = initial ?? new AppSettings();

    public AppSettings Load() => Stored;

    public void Save(AppSettings settings) => Stored = settings;
}
