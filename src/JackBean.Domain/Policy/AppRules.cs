using System.Text.RegularExpressions;

namespace JackBean.Domain.Policy;

/// <summary>앱 편집 폼의 검증 결과. 문구는 화면이 정한다.</summary>
public enum AppFieldError { None, IdEmpty, IdInvalid, IdTaken, CommandEmpty }

/// <summary>
/// 정책의 앱(<c>apps</c>)을 만들거나 고칠 때의 규칙: 이름(ID)의 모양, 실행 파일 필수, 인자 입력(한 줄에 하나).
/// 읽을 때는 관대하다(<see cref="PolicyDocument"/>) — 손으로 쓴 정책의 ID 는 그대로 받아들이고, 여기 규칙은 **새로 만들 때**만 적용한다.
/// </summary>
public static partial class AppRules
{
    public const string PathPlaceholder = "{path}";
    public const int MaxIdLength = 32;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex IdPattern();

    /// <summary>새 앱의 ID: 영문 소문자·숫자로 시작하고 소문자·숫자·`-`·`_` 만, 32자 이하(규칙의 `open` 이 이 이름으로 가리킨다).</summary>
    public static bool IsValidId(string? id) => id is { Length: > 0 and <= MaxIdLength } && IdPattern().IsMatch(id);

    /// <param name="takenIds">이미 있는 앱 ID 들.</param>
    /// <param name="isNew">새 앱이면 ID 모양과 중복을 본다. 있는 앱을 고칠 때는 ID 를 바꾸지 않으므로 보지 않는다.</param>
    public static AppFieldError Validate(string? id, string? command, IEnumerable<string> takenIds, bool isNew)
    {
        if (isNew)
        {
            var trimmed = id?.Trim() ?? "";
            if (trimmed.Length == 0) return AppFieldError.IdEmpty;
            if (!IsValidId(trimmed)) return AppFieldError.IdInvalid;
            if (takenIds.Contains(trimmed, StringComparer.Ordinal)) return AppFieldError.IdTaken;
        }
        return string.IsNullOrWhiteSpace(command) ? AppFieldError.CommandEmpty : AppFieldError.None;
    }

    /// <summary>입력칸의 글 → 인자 목록. 한 줄에 하나, 앞뒤 공백을 버리고 빈 줄은 건너뛴다.</summary>
    public static IReadOnlyList<string> ParseArgs(string? text) =>
        (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>인자 목록 → 입력칸의 글.</summary>
    public static string FormatArgs(IEnumerable<string> args) => string.Join('\n', args);

    /// <summary>인자 어디에도 {path} 가 없으면 끌어다 놓은 대상이 앱에 전달되지 않는다.</summary>
    public static bool LacksPathPlaceholder(IEnumerable<string> args) =>
        !args.Any(a => a.Contains(PathPlaceholder, StringComparison.Ordinal));
}
