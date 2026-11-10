using JackBean.Domain;

namespace JackBean.App;

/// <summary>
/// 창 제목 규칙: 홈 창(앱 창)은 앱 이름만, 그 밖의 모든 보조 창은 <c>창 이름 — Jack’s Bean</c> 이다.
/// 여러 창이 따로 떠 있는 구조라 작업 표시줄·Alt+Tab 에서 어느 앱에서 나온 창인지 알아볼 수 있어야 한다.
/// </summary>
static class WindowTitle
{
    /// <param name="name">창 이름(설정, 앱 편집 …). 없으면 앱 이름만.</param>
    public static string Of(string? name) =>
        string.IsNullOrWhiteSpace(name) ? AppIdentity.DisplayName : $"{name} — {AppIdentity.DisplayName}";
}
