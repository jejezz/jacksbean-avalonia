using System.Text;
using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>`fileContains` 가 쓰는 파일 읽기: 앞부분 512KB 만 UTF-8(깨진 바이트는 대체 문자)로 읽는다. 읽을 수 없으면 null.</summary>
public sealed class FileTextReader(string root) : IFileTextReader
{
    public const int MaxReadBytes = 512 * 1024;

    public string? ReadHead(string relativePath)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(root, relativePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[MaxReadBytes];
            var total = 0;
            int n;
            while (total < buffer.Length && (n = stream.Read(buffer, total, buffer.Length - total)) > 0) total += n;
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
