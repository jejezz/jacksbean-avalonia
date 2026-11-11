using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>
/// 폴더를 너비 우선으로 훑는다.
/// <list type="bullet">
/// <item>심볼릭 링크·정션은 따라가지 않고 무시한다(순환 방지).</item>
/// <item>깊이 <see cref="ScanConfig.MaxDepth"/> 보다 깊은 디렉터리에는 들어가지 않는다 — 파일 경로에 든 디렉터리 단계 수가 maxDepth 이하인 파일만 스캔한다.</item>
/// <item>skipDirs 에는 들어가지 않지만, 있다는 사실(디렉터리 이름)은 남긴다.</item>
/// <item>너비 우선이라 파일 수 상한에 걸려도 루트에 가까운 표식 파일부터 확보된다.</item>
/// <item>읽을 수 없는 디렉터리는 건너뛴다.</item>
/// </list>
/// </summary>
public static class TreeScanner
{
    public static ScanSnapshot Scan(string root, ScanConfig config)
    {
        var skip = config.SkipDirs.ToHashSet(StringComparer.Ordinal);
        var files = new List<string>();
        var dirs = new List<string>();
        var skipped = new SortedSet<string>(StringComparer.Ordinal);
        var truncated = false;

        var level = new List<string> { "" };
        for (var depth = 0; depth <= config.MaxDepth && level.Count > 0 && !truncated; depth++)
        {
            var next = new List<string>();
            foreach (var rel in level)
            {
                var entries = ListEntries(rel.Length == 0 ? root : Path.Combine(root, rel));
                if (entries == null) continue;
                foreach (var entry in entries)
                {
                    if (entry.LinkTarget != null) continue;
                    var childRel = rel.Length == 0 ? entry.Name : $"{rel}/{entry.Name}";
                    if (entry is DirectoryInfo)
                    {
                        dirs.Add(childRel);
                        if (skip.Contains(entry.Name)) skipped.Add(entry.Name);
                        else next.Add(childRel);
                    }
                    else
                    {
                        if (files.Count >= config.MaxFiles)
                        {
                            truncated = true;
                            break;
                        }
                        files.Add(childRel);
                    }
                }
                if (truncated) break;
            }
            level = next;
        }

        return new ScanSnapshot(root, files, dirs, skipped.ToList(), truncated);
    }

    private static List<FileSystemInfo>? ListEntries(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
