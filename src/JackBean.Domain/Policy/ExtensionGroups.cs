namespace JackBean.Domain.Policy;

/// <summary>
/// 미리 정의한 확장자 묶음(이미지·음악·영상 …). 규칙 편집 창에서 묶음을 고르면 확장자가 채워진다.
/// 저장은 **확장자 목록**이라 이 정의가 나중에 바뀌어도 이미 저장한 규칙은 그대로다. 이름(이미지 …)은 화면이 언어별로 붙인다.
/// 한 확장자는 한 묶음에만 속한다(시험으로 고정) — 묶음 둘을 함께 골라도 확장자가 겹치지 않는다.
/// </summary>
public sealed record ExtensionGroup(string Id, IReadOnlyList<string> Extensions);

public static class ExtensionGroups
{
    public static IReadOnlyList<ExtensionGroup> All { get; } =
    [
        new("image", ["png", "jpg", "jpeg", "gif", "webp", "bmp", "tif", "tiff", "svg", "ico", "heic", "heif", "avif"]),
        new("audio", ["mp3", "wav", "flac", "aac", "m4a", "ogg", "oga", "opus", "wma", "aif", "aiff", "mid", "midi", "amr"]),   // 음성 포함
        new("video", ["mp4", "m4v", "mkv", "mov", "avi", "wmv", "flv", "webm", "mpg", "mpeg", "3gp", "m2ts"]),
        new("pdf", ["pdf"]),
        new("document", ["doc", "docx", "hwp", "hwpx", "odt", "rtf", "pages"]),
        new("spreadsheet", ["xls", "xlsx", "xlsm", "csv", "ods", "numbers"]),
        new("presentation", ["ppt", "pptx", "pps", "ppsx", "odp", "key"]),
        new("archive", ["zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz", "zst"]),
        new("text", ["txt", "md", "log", "ini", "cfg", "conf"]),
        new("code", ["json", "xml", "yaml", "yml", "toml", "html", "htm", "css", "js", "mjs", "ts", "tsx", "jsx", "py", "cs", "java",
                     "c", "cpp", "h", "hpp", "go", "rs", "rb", "php", "sh", "ps1", "sql"]),
    ];

    public static ExtensionGroup? Find(string id) => All.FirstOrDefault(g => g.Id == id);

    /// <summary>이 확장자(점·대소문자 무관)가 속한 묶음. 어느 묶음에도 없으면 null.</summary>
    public static ExtensionGroup? GroupOf(string extension)
    {
        var e = Normalize(extension);
        return All.FirstOrDefault(g => g.Extensions.Contains(e));
    }

    /// <summary>`.PNG` → `png`.</summary>
    public static string Normalize(string extension)
    {
        var e = extension.Trim().ToLowerInvariant();
        return e.StartsWith('.') ? e[1..] : e;
    }
}
