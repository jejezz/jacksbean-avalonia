// 떨어뜨린 경로를 분류한다 (드라이브 / 폴더 / 파일). 규칙 엔진의 입력이다. UI 없이 테스트할 수 있다.

namespace JackBean.Domain.Drop;

public enum DropKind { Drive, Folder, File, Missing }

public static class DropKindExtensions
{
    /// <summary>정책 JSON 에서 쓰는 이름(`drive` / `folder` / `file` / `missing`).</summary>
    public static string JsonName(this DropKind kind) => kind.ToString().ToLowerInvariant();
}

public sealed record DroppedItem(string Path, DropKind Kind, string Extension = "", bool HasGit = false)
{
    /// <summary>경로의 마지막 이름. 드라이브(`D:\`)와 루트는 경로 자체를 돌려준다.</summary>
    public string Name
    {
        get
        {
            if (Kind == DropKind.Drive) return Path;
            var parts = Path.Split('\\', '/').Where(s => s.Length > 0).ToArray();
            return parts.Length == 0 ? Path : parts[^1];
        }
    }
}

