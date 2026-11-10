using System.IO.Compression;

namespace JackBean.Services.Archive;

public enum ArchiveErrorKind
{
    /// <summary>zip 이름이 비었거나 쓸 수 없는 글자가 있다.</summary>
    BadName,
    /// <summary>이미 있는 파일이라 덮어쓰지 않고 멈췄다(Detail = 이름).</summary>
    AlreadyExists,
    /// <summary>zip 항목이 풀 폴더 밖으로 빠져나가려 한다(Detail = 항목 이름).</summary>
    UnsafePath,
    /// <summary>담을 것이 없다(없어진 경로뿐).</summary>
    NothingToAdd,
}

/// <summary>압축·풀기가 사용자에게 알릴 수 있는 이유로 멈췄다. 그 밖의 입출력 오류는 IOException 등 그대로 던진다.</summary>
public sealed class ArchiveException(ArchiveErrorKind kind, string detail = "") : Exception($"{kind}: {detail}")
{
    public ArchiveErrorKind Kind { get; } = kind;
    public string Detail { get; } = detail;
}

/// <summary>zip 안의 내용 요약(풀기 창이 보여 준다).</summary>
public sealed record ArchiveListing(IReadOnlyList<string> Entries, long TotalSize, int FileCount, int FolderCount);

/// <summary>
/// 압축(zip 만들기)과 풀기. zip 만 지원한다 — 표준 라이브러리(System.IO.Compression)만 쓰므로 NativeAOT 에서 안전하고 앱이 커지지 않는다.
/// 형제 앱 DaylightCommander 의 ArchiveService 를 가져왔다(오류는 파일 관리자의 FileError 대신 <see cref="ArchiveException"/>).
/// </summary>
public sealed class ArchiveService
{
    public static bool IsZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>기본 압축 파일 이름: 항목이 하나면 "이름.zip", 여럿이면 "archive.zip".</summary>
    public static string DefaultZipName(IReadOnlyList<string> sourcePaths) =>
        sourcePaths.Count == 1 ? Path.GetFileName(sourcePaths[0].TrimEnd('/', '\\')) + ".zip" : "archive.zip";

    /// <summary>풀 폴더 이름: zip 이름에서 확장자를 뗀 것.</summary>
    public static string ExtractFolderName(string zipName) => Path.GetFileNameWithoutExtension(zipName);

    /// <summary>zip 이름 검사: 비었거나 쓸 수 없는 글자(\ / : * ? " &lt; &gt; |, 제어 문자)가 있으면 <see cref="ArchiveErrorKind.BadName"/>.</summary>
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") throw new ArchiveException(ArchiveErrorKind.BadName, name);
        foreach (var ch in name)
            if ("\\/:*?\"<>|".Contains(ch) || ch < 32) throw new ArchiveException(ArchiveErrorKind.BadName, name);
    }

    /// <summary>zip 안을 훑어 항목 이름(앞 <paramref name="maxEntries"/>개)과 개수·크기를 알려 준다. 깨진 zip 이면 InvalidDataException.</summary>
    public Task<ArchiveListing> InspectAsync(string zipPath, int maxEntries = 500, CancellationToken ct = default) => Task.Run(() =>
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = new List<string>();
        long total = 0;
        int files = 0, folders = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var isFolder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (isFolder) folders++; else { files++; total += entry.Length; }
            if (names.Count < maxEntries) names.Add(entry.FullName);
        }
        return new ArchiveListing(names, total, files, folders);
    }, ct);

    /// <summary>
    /// <paramref name="sources"/>(파일·폴더)를 <paramref name="zipPath"/> 하나로 묶는다. 폴더는 폴더 이름부터 담는다.
    /// 이미 있는 파일이면 <see cref="ArchiveErrorKind.AlreadyExists"/>. 실패하면 만들다 만 zip 은 지운다.
    /// </summary>
    public Task CompressAsync(IReadOnlyList<string> sources, string zipPath, CancellationToken ct = default) => Task.Run(() =>
    {
        var zipName = Path.GetFileName(zipPath);
        ValidateName(zipName);
        if (File.Exists(zipPath) || Directory.Exists(zipPath)) throw new ArchiveException(ArchiveErrorKind.AlreadyExists, zipName);
        if (!sources.Any(s => File.Exists(s) || Directory.Exists(s))) throw new ArchiveException(ArchiveErrorKind.NothingToAdd);

        try
        {
            using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            var zipFull = Path.GetFullPath(zipPath);
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(source.TrimEnd('/', '\\'));
                if (Directory.Exists(source)) AddDirectory(zip, source, name, zipFull, ct);
                else if (File.Exists(source)) AddFile(zip, source, name, zipFull);
            }
        }
        catch
        {
            try { File.Delete(zipPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }, ct);

    static void AddDirectory(ZipArchive zip, string dir, string entryName, string zipFull, CancellationToken ct)
    {
        var children = Directory.EnumerateFileSystemEntries(dir).ToList();
        if (children.Count == 0) zip.CreateEntry(entryName + "/"); // 빈 폴더도 남긴다
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            var childName = entryName + "/" + Path.GetFileName(child);
            if (Directory.Exists(child))
            {
                if (new DirectoryInfo(child).LinkTarget is not null) continue; // 링크를 따라 들어가 무한 반복하지 않는다
                AddDirectory(zip, child, childName, zipFull, ct);
            }
            else AddFile(zip, child, childName, zipFull);
        }
    }

    static void AddFile(ZipArchive zip, string file, string entryName, string zipFull)
    {
        if (string.Equals(Path.GetFullPath(file), zipFull, StringComparison.OrdinalIgnoreCase)) return; // 만드는 zip 자신
        zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
    }

    /// <summary>
    /// <paramref name="zipPath"/> 를 <paramref name="destinationDir"/> 에 푼다(없으면 만든다). 이미 있는 파일은 덮어쓰지 않고
    /// <see cref="ArchiveErrorKind.AlreadyExists"/> 로 멈추며, 폴더 밖으로 빠져나가는 항목은 <see cref="ArchiveErrorKind.UnsafePath"/> 로 거절한다.
    /// 풀린 폴더 경로를 돌려준다.
    /// </summary>
    public Task<string> ExtractAsync(string zipPath, string destinationDir, CancellationToken ct = default) => Task.Run(() =>
    {
        var full = Path.GetFullPath(destinationDir);
        var root = full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(full, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal) && target != full)
                throw new ArchiveException(ArchiveErrorKind.UnsafePath, entry.FullName); // 밖으로 빠져나가는 경로
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) throw new ArchiveException(ArchiveErrorKind.AlreadyExists, entry.FullName);
            entry.ExtractToFile(target);
        }
        Directory.CreateDirectory(full); // 빈 zip 이어도 폴더는 만든다
        return full;
    }, ct);
}
