namespace JackBean.Services;

/// <summary>
/// 독 **창**을 언제 키우고 줄일지 정한다(화면 속 알약의 모양은 따로 바로 바뀐다).
/// 키우기는 바로, 줄이기는 ShrinkDelay 만큼 기다렸다가 한다. 그 사이에 다시 Expand 가 오면 취소한다 —
/// 창 크기를 바꾸는 순간 새 크기로 그리기 전의 프레임이 보여 알약이 반만 그려지는 것을 막는다.
/// </summary>
public sealed class DockExpansion : IDisposable
{
    private readonly Action<bool> _onChanged;
    private readonly TimeProvider _time;
    private ITimer? _timer;

    /// <param name="onChanged">창을 키워야 하면 true, 줄여야 하면 false. 상태가 실제로 바뀔 때만 부른다.</param>
    public DockExpansion(Action<bool> onChanged, TimeSpan? shrinkDelay = null, TimeProvider? time = null)
    {
        _onChanged = onChanged;
        ShrinkDelay = shrinkDelay ?? TimeSpan.FromMilliseconds(500);
        _time = time ?? TimeProvider.System;
    }

    public TimeSpan ShrinkDelay { get; }
    public bool IsExpanded { get; private set; }

    public void Expand()
    {
        CancelTimer();
        if (IsExpanded) return;
        IsExpanded = true;
        _onChanged(true);
    }

    public void ShrinkSoon()
    {
        CancelTimer();
        _timer = _time.CreateTimer(_ =>
        {
            CancelTimer();
            if (!IsExpanded) return;
            IsExpanded = false;
            _onChanged(false);
        }, null, ShrinkDelay, Timeout.InfiniteTimeSpan);
    }

    private void CancelTimer() { _timer?.Dispose(); _timer = null; }

    public void Dispose() => CancelTimer();
}
