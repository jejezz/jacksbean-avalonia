using JackBean.Domain.Settings;

namespace JackBean.Domain.Tips;

/// <summary>시작할 때 독 옆에 보여 주는 사용 팁 하나. 문구는 앱의 <c>IAppStrings</c> 가 정한다.</summary>
public enum TipId
{
    /// <summary>파일을 콩에 끌어다 놓으면 열린다 — 아직 한 번도 놓아 보지 않은 사용자에게만.</summary>
    DropOnBean,
    /// <summary>규칙에 앱이 여럿이면 독에서 고른다.</summary>
    ChooseApp,
    /// <summary>고를 때 "항상 이렇게 열기"로 규칙을 만든다.</summary>
    AlwaysOpen,
    /// <summary>독을 더블클릭하면 홈 창(규칙·앱)이 열린다.</summary>
    OpenHome,
    /// <summary>알림 영역 아이콘으로 독을 숨기고 다시 보인다.</summary>
    HideDock,
    /// <summary>규칙을 GitHub 로 다른 PC 와 맞춘다.</summary>
    Sync,
}

/// <summary>
/// 어느 팁을 보일지 정한다. 처음 놓아 보기 전에는 늘 <see cref="TipId.DropOnBean"/> 하나만(익히면 저절로 그친다),
/// 그 뒤에는 실행할 때마다 <see cref="Rotation"/> 을 하나씩 돌아가며 보인다. 로그인할 때 자동으로 시작한 경우와 끈 경우는 보이지 않는다.
/// </summary>
public static class StartupTips
{
    public static IReadOnlyList<TipId> Rotation { get; } = [TipId.ChooseApp, TipId.AlwaysOpen, TipId.OpenHome, TipId.HideDock, TipId.Sync];

    /// <summary>시작할 때 보일 팁과, 보인 뒤 저장할 설정(다음 차례). 보이지 않으면 null.</summary>
    /// <param name="autostart">로그인할 때 자동으로 시작했다(`--autostart`) — 부팅할 때마다 뜨면 귀찮다.</param>
    public static (TipId Tip, AppSettings Next)? OnStartup(AppSettings settings, bool autostart) =>
        autostart || !settings.StartupTips ? null : Next(settings);

    /// <summary>다음 팁(메뉴의 "사용 팁 보기" — 꺼 두었어도 보인다).</summary>
    public static (TipId Tip, AppSettings Next) Next(AppSettings settings)
    {
        if (!settings.DockDroppedOnce) return (TipId.DropOnBean, settings);
        var i = ((settings.StartupTipsNext % Rotation.Count) + Rotation.Count) % Rotation.Count;   // 저장 값이 틀려도 범위 안으로
        return (Rotation[i], settings with { StartupTipsNext = (i + 1) % Rotation.Count });
    }
}
