namespace JackBean.Services.CodeProfiles;

/// <summary>
/// VS Code 실행 파일(`code`)을 찾는다(Windows 전용). 순서: PATH 의 `code.cmd` · `code.exe` · `code.bat` → 기본 설치 위치
/// (`%LOCALAPPDATA%\Programs\Microsoft VS Code\bin\code.cmd`, `%ProgramFiles%\Microsoft VS Code\bin\code.cmd`). 못 찾으면 null.
/// 환경과 파일 시스템은 시험에서 바꿀 수 있도록 인자로 받는다.
/// </summary>
public static class CodeLocator
{
    private static readonly string[] ExecutableNames = ["code.cmd", "code.exe", "code.bat"];

    /// <summary>못 찾았을 때 PATH 탐색에 맡기는 이름(Windows 는 확장자가 필요하다).</summary>
    public const string FallbackCommand = "code.cmd";

    public static string? Discover(IReadOnlyDictionary<string, string?>? environment = null, Func<string, bool>? fileExists = null)
    {
        string? Get(string key) => environment != null ? environment.GetValueOrDefault(key) : Environment.GetEnvironmentVariable(key);
        var exists = fileExists ?? File.Exists;

        var path = Get("Path") ?? Get("PATH");
        if (path != null)
        {
            foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
                foreach (var name in ExecutableNames)
                {
                    var candidate = dir.TrimEnd('\\') + "\\" + name;
                    if (exists(candidate)) return candidate;
                }
        }

        var defaults = new List<string>();
        if (Get("LOCALAPPDATA") is { Length: > 0 } local) defaults.Add($@"{local}\Programs\Microsoft VS Code\bin\code.cmd");
        if (Get("ProgramFiles") is { Length: > 0 } programFiles) defaults.Add($@"{programFiles}\Microsoft VS Code\bin\code.cmd");
        return defaults.FirstOrDefault(exists);
    }
}
