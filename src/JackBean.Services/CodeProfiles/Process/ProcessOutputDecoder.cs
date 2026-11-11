using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace JackBean.Services.CodeProfiles;

/// <summary>
/// 프로세스 출력 바이트를 문자열로 바꾼다. 대부분의 도구(`code` 등 Node 프로그램)는 UTF-8 로 쓰지만, Windows 의 `cmd` 는 오류 메시지를
/// 시스템 코드 페이지(예: 한국어 cp949)로 쓴다. UTF-8 로 읽다 깨지면 그 코드 페이지로 다시 읽어, 한글 오류 메시지가 깨져 보이지 않게 한다.
/// </summary>
public static partial class ProcessOutputDecoder
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <param name="fallback">UTF-8 이 아닐 때 쓸 인코딩(시험용). 기본은 이 PC 의 OEM 코드 페이지.</param>
    public static string Decode(ReadOnlySpan<byte> bytes, Encoding? fallback = null)
    {
        if (bytes.IsEmpty) return "";
        try
        {
            return Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // UTF-8 이 아니다 — 아래에서 코드 페이지로 읽는다.
        }

        try
        {
            return (fallback ?? OemEncoding()).GetString(bytes);
        }
        catch (Exception e) when (e is DecoderFallbackException or ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);   // 잘못된 바이트는 대체 문자로
        }
    }

    private static Encoding OemEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // .NET 에서 cp949 같은 코드 페이지를 쓰려면 필요하다(여러 번 불러도 된다)
        try
        {
            var codePage = OperatingSystem.IsWindows() ? (int)GetOEMCP() : CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();
}
