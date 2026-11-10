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

    /// <summary>오류를 알린다. <paramref name="detail"/>은 "복사"가 함께 복사할 원문(예외 메시지 등).</summary>
    public static void Error(string message, string? detail = null) => Show(new Notification(NotificationKind.Error, message, detail));

    public static void Info(string message) => Show(new Notification(NotificationKind.Info, message));

    public static void Show(Notification notification)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Show(notification)); return; }
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (!Gate.Accept(notification)) return;

        _current?.Close();
        var window = new SnackbarWindow(notification);
        _current = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_current, window)) _current = null; };
        window.Show();
        window.PlaceNear(desktop.MainWindow); // 독
    }
}
