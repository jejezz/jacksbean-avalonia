using JackBean.Domain.Policy;
using JackBean.Domain.Drop;
using System.Text.RegularExpressions;

namespace JackBean.Services;

/// <summary>경로 하나를 분류한다 (파일 시스템을 읽는다).</summary>
public static partial class PathClassifier
{
    [GeneratedRegex(@"^[A-Za-z]:[\\/]*$")]
    private static partial Regex WindowsDriveRoot();

    [GeneratedRegex(@"^/Volumes/[^/]+/?$")]
    private static partial Regex MacVolume();

    /// <summary>
    /// 경로 하나를 분류한다. <paramref name="windows"/>는 테스트에서 OS 를 흉내 낼 때만 쓴다.
    /// 드라이브: Windows 는 `D:` / `D:\`, macOS 는 `/` 와 `/Volumes/&lt;이름&gt;`.
    /// 리눅스 마운트 지점은 배포판마다 달라 아직 모른다 — 폴더로 본다.
    /// </summary>
    public static DroppedItem Classify(string path, bool? windows = null)
    {
        var isWindows = windows ?? OperatingSystem.IsWindows();
        var isDrive = isWindows ? WindowsDriveRoot().IsMatch(path) : path == "/" || MacVolume().IsMatch(path);
        // 연결이 끊긴 드라이브(빈 카드 리더 등)도 드라이브로 본다 — 무엇으로 열지는 규칙이 정한다.
        if (isDrive) return new DroppedItem(path, DropKind.Drive);

        if (Directory.Exists(path))
        {
            var git = Path.Combine(path, ".git");
            return new DroppedItem(path, DropKind.Folder, HasGit: Directory.Exists(git) || File.Exists(git));
        }
        if (!File.Exists(path)) return new DroppedItem(path, DropKind.Missing);

        var baseName = path.Split('\\', '/')[^1];
        var dot = baseName.LastIndexOf('.');
        var ext = dot > 0 && dot < baseName.Length - 1 ? baseName[(dot + 1)..].ToLowerInvariant() : "";
        return new DroppedItem(path, DropKind.File, ext);
    }

    /// <summary>여러 개를 떨어뜨렸을 때의 요약: 모두 같은 종류면 그 종류, 아니면 null(섞임).</summary>
    public static DropKind? CommonKind(IReadOnlyList<DroppedItem> items)
    {
        if (items.Count == 0) return null;
        var first = items[0].Kind;
        return items.All(i => i.Kind == first) ? first : null;
    }
}
