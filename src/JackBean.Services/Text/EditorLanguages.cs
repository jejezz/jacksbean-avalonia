namespace JackBean.Services.Text;

/// <summary>
/// 파일 이름 → 구문 강조에 쓸 확장자 (원본 <c>editor_languages.dart</c>의 표). 강조 엔진(TextMate)이 아는 확장자로 바꿔 돌려준다.
/// 강조를 하지 않는 파일(<c>csv/tsv/log</c> 등)은 null.
/// </summary>
public static class EditorLanguages
{
    /// <summary>전용 문법이 없어 비슷한 문법으로 대신하는 확장자.</summary>
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["toml"] = ".ini",
        ["env"] = ".properties",
        ["bat"] = ".bat",
        ["cmd"] = ".bat",
        ["ps1"] = ".ps1",
        ["psm1"] = ".ps1",
        ["html"] = ".html",
        ["htm"] = ".html",
        ["axaml"] = ".xml",
        ["xaml"] = ".xml",
        ["csproj"] = ".xml",
        ["props"] = ".xml",
        ["targets"] = ".xml",
        ["slnx"] = ".xml",
        ["svg"] = ".xml",
        ["plist"] = ".xml",
        ["gradle"] = ".groovy",
        ["kt"] = ".kt",
        ["yml"] = ".yaml",
    };

    /// <summary>강조 없이 보여 주는 확장자.</summary>
    static readonly HashSet<string> Plain = new(StringComparer.OrdinalIgnoreCase) { "txt", "csv", "tsv", "log", "srt", "vtt", "lock" };

    /// <summary>확장자 없는 이름 중 문법이 정해진 것.</summary>
    static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = ".dockerfile",
        ["makefile"] = ".mk",
        ["gnumakefile"] = ".mk",
        [".gitignore"] = ".ini",
        [".gitattributes"] = ".ini",
        [".editorconfig"] = ".ini",
        [".dockerignore"] = ".ini",
    };

    /// <summary>마크다운 문서인가 (에디터에 미리보기 버튼이 나온다).</summary>
    public static bool IsMarkdown(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd";

    public static string? ExtensionFor(string fileName)
    {
        if (Names.TryGetValue(fileName, out var byName)) return byName;
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot == fileName.Length - 1) return null;
        var ext = fileName[(dot + 1)..];
        if (Plain.Contains(ext)) return null;
        return Aliases.TryGetValue(ext, out var alias) ? alias : "." + ext.ToLowerInvariant();
    }
}
