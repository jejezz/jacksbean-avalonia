using System.Text;

namespace JackBean.Services.Text;

/// <summary>
/// 읽어 들인 텍스트 파일 (원본 <c>EditorTextFile</c>). 저장할 때 BOM과 줄바꿈을 그대로 되돌리려고 함께 들고 있다.
/// <paramref name="Text"/>는 읽은 그대로의 본문이다 (줄바꿈 정규화 전).
/// </summary>
/// <param name="LineBreak"><c>\n</c> 또는 <c>\r\n</c>. 섞여 있으면 많은 쪽.</param>
/// <param name="Binary">
/// 바이너리이거나 UTF-8이 아니라서 바이트를 글자 하나씩(Latin-1) 그대로 보여 주는 파일.
/// 줄바꿈 정규화와 BOM 처리가 바이트를 바꿀 수 있어 읽기 전용으로만 연다.
/// </param>
public sealed record EditorTextFile(string Text, bool HasBom, string LineBreak, DateTime Modified, long Length, bool Binary);

public enum EditorLoadFailure { TooLarge }

public sealed record EditorLoadResult(EditorTextFile? File, EditorLoadFailure? Failure)
{
    public static EditorLoadResult Ok(EditorTextFile file) => new(file, null);
    public static EditorLoadResult Failed(EditorLoadFailure failure) => new(null, failure);
}

/// <summary>내장 에디터(F4)가 텍스트 파일을 읽고 쓰는 규칙 (원본 <c>text_file.dart</c>).</summary>
public static class TextFiles
{
    /// <summary>에디터가 한 번에 열 수 있는 파일 크기 상한.</summary>
    public const int EditorMaxBytes = 5 * 1024 * 1024;

    /// <summary>앞 8KB 안에 NUL 바이트가 있으면 바이너리로 본다 (읽기 전용으로 연다).</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var end = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < end; i++)
            if (bytes[i] == 0) return true;
        return false;
    }

    /// <summary>본문에서 줄바꿈 종류를 센다. CRLF가 LF 단독보다 많으면 <c>\r\n</c>.</summary>
    public static string DetectLineBreak(string text)
    {
        int crlf = 0, lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++; else lf++;
        }
        return crlf > lf ? "\r\n" : "\n";
    }

    /// <summary>CRLF·CR을 모두 LF로 맞춘다 (편집 화면은 LF만 쓰고, 저장할 때 원래 줄바꿈으로 되돌린다).</summary>
    public static string NormalizeLineBreaks(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>LF로 정규화된 본문을 <paramref name="lineBreak"/>로 이어 붙인다.</summary>
    public static string ApplyLineBreak(string text, string lineBreak) =>
        lineBreak == "\n" ? text : text.Replace("\n", lineBreak);

    public static EditorLoadResult Decode(byte[] bytes, DateTime modified)
    {
        if (bytes.Length > EditorMaxBytes) return EditorLoadResult.Failed(EditorLoadFailure.TooLarge);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var binary = LooksBinary(bytes);
        string text;
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            text = strict.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            // UTF-8이 아니면 바이트 값 그대로(0x00–0xFF → 글자 하나) 보여 준다.
            text = Encoding.Latin1.GetString(bytes);
            binary = true;
        }
        return EditorLoadResult.Ok(new EditorTextFile(
            text, HasBom: hasBom && !binary, DetectLineBreak(text), modified, bytes.Length, binary));
    }

    public static async Task<EditorLoadResult> LoadAsync(string path, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        if (info.Length > EditorMaxBytes) return EditorLoadResult.Failed(EditorLoadFailure.TooLarge);
        return Decode(await File.ReadAllBytesAsync(path, ct), info.LastWriteTimeUtc);
    }

    /// <summary>저장할 바이트. <paramref name="text"/>는 이미 원래 줄바꿈으로 이어 붙인 본문이다.</summary>
    public static byte[] Encode(string text, bool hasBom)
    {
        var body = new UTF8Encoding(false).GetBytes(text);
        return hasBom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>열었을 때와 디스크의 수정 시각·크기가 달라졌는가. 파일이 없으면 false (새로 쓰면 되므로).</summary>
    public static bool ChangedOnDisk(string path, EditorTextFile opened)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return false;
        return info.LastWriteTimeUtc != opened.Modified || info.Length != opened.Length;
    }

    /// <summary>
    /// <paramref name="path"/>에 제자리로 쓰고(권한 비트를 보존하려고 임시 파일 교체 대신) 새 기준값(수정 시각·크기)을 가진
    /// <see cref="EditorTextFile"/>을 돌려준다. <paramref name="text"/>는 LF로 정규화된 편집 본문이다.
    /// </summary>
    public static async Task<EditorTextFile> SaveAsync(string path, string text, EditorTextFile opened, CancellationToken ct = default)
    {
        var bytes = Encode(ApplyLineBreak(text, opened.LineBreak), opened.HasBom);
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
        }
        var info = new FileInfo(path);
        return opened with { Text = text, Modified = info.LastWriteTimeUtc, Length = info.Length };
    }
}
