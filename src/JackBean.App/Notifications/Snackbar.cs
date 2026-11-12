using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using JackBean.Services.Notifications;

namespace JackBean.App.Notifications;

/// <summary>
/// 앱 어디서든 부르는 알림 진입점. 어느 스레드에서 불러도 된다. 한 번에 하나만 보이고, 새 알림은 이전 것을 대신한다.
/// 같은 알림이 연달아 오면 한 번만 보인다(<see cref="NotificationGate"/>).
/// </summary>
internal static class Snackbar
{
    private static readonly NotificationGate Gate = new();
    private static SnackbarWindow? _current;

    /// <summary>알림이 떠 있다(독 옆 같은 자리 — 최근 작업 목록은 그동안 띄우지 않는다).</summary>
    public static bool IsShowing => _current != null;

    /// <summary>오류를 알린다. <paramref name="detail"/>은 "복사"가 함께 복사할 원문(예외 메시지 등).</summary>
    /// <param name="actionLabel">있으면 "복사" 옆에 이 이름의 버튼을 둔다(눌러서 <paramref name="action"/> 을 하고 알림을 닫는다).</param>
    public static void Error(string message, string? detail = null, string? actionLabel = null, Action? action = null) =>
        Show(new Notification(NotificationKind.Error, message, detail), actionLabel, action);

    public static void Info(string message, string? actionLabel = null, Action? action = null) =>
        Show(new Notification(NotificationKind.Info, message), actionLabel, action);

    /// <summary>사용 팁(독 옆 말풍선, 12초). <paramref name="actionLabel"/> 은 보통 "다시 보지 않기".</summary>
    public static void Tip(string message, string? actionLabel = null, Action? action = null) =>
        Show(new Notification(NotificationKind.Tip, message), actionLabel, action);

    public static void Show(Notification notification, string? actionLabel = null, Action? action = null)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Show(notification, actionLabel, action)); return; }
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (!Gate.Accept(notification)) return;

        _current?.Close();
        var window = new SnackbarWindow(notification, actionLabel, action);
        _current = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_current, window)) _current = null; };
        window.Show();
        window.PlaceNear(desktop.MainWindow); // 독
    }
}
