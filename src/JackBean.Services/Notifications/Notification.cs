namespace JackBean.Services.Notifications;

public enum NotificationKind { Info, Error }

/// <summary>사용자에게 잠깐 보여 주는 알림 하나. <paramref name="Detail"/>은 오류 원문 — "복사"가 이것을 복사한다(없으면 메시지).</summary>
public sealed record Notification(NotificationKind Kind, string Message, string? Detail = null)
{
    /// <summary>복사할 글: 원문이 있으면 원문, 없으면 메시지.</summary>
    public string CopyText => string.IsNullOrWhiteSpace(Detail) ? Message : $"{Message}\n{Detail}";
}

/// <summary>
/// 알림이 얼마나 머무를지(ui-ux.md §6: 완료 알림은 4초, 오류는 읽고 복사할 시간이 필요해 더 길게)와,
/// 같은 알림이 연달아 올 때 걸러 내는 규칙. UI 와 무관해서 시계를 바꿔 끼워 시험한다.
/// </summary>
public sealed class NotificationGate(TimeProvider? time = null)
{
    public static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(10);
    /// <summary>실행 취소 같은 액션 버튼이 있는 알림이 최소한 머무는 시간.</summary>
    public static readonly TimeSpan ActionDuration = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private Notification? _last;
    private DateTimeOffset _lastAt;

    public static TimeSpan DurationOf(NotificationKind kind) => kind == NotificationKind.Error ? ErrorDuration : InfoDuration;

    /// <summary>보여 줘야 하면 true. 같은 종류·같은 메시지가 <see cref="DuplicateWindow"/> 안에 또 오면 false(반복 오류가 창을 깜박이게 하지 않는다).</summary>
    public bool Accept(Notification n)
    {
        var now = _time.GetUtcNow();
        if (_last is { } l && l.Kind == n.Kind && l.Message == n.Message && now - _lastAt < DuplicateWindow) return false;
        _last = n;
        _lastAt = now;
        return true;
    }
}
