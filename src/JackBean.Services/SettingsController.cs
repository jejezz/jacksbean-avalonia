using JackBean.Domain.Settings;

namespace JackBean.Services;

/// <summary>
/// 앱 설정의 단일 출처. 값은 <see cref="Value"/>로 읽고 <see cref="Update"/>로 바꾸며 <see cref="Changed"/>가 알려 준다.
/// 바뀐 값은 잠깐(창을 끄는 동안의 연속 변경을 모으려고) 기다렸다가 저장하고, 종료할 때는 <see cref="Flush"/>로 바로 저장한다.
/// 시작할 때 파일에서 읽는다. <see cref="Changed"/>는 바꾼 스레드에서 불리고, 저장은 별도 스레드에서 한다.
/// </summary>
public sealed class SettingsController : IDisposable
{
    readonly ISettingsRepository _repository;
    readonly TimeSpan _saveDelay;
    readonly object _gate = new();
    readonly Timer _timer;
    AppSettings _value;

    /// <summary>마지막으로 저장소에 쓴(또는 읽어 온) 값. 같은 값을 다시 쓰지 않으려고 둔다.</summary>
    AppSettings _lastSaved;

    public SettingsController(ISettingsRepository repository, TimeSpan? saveDelay = null)
    {
        _repository = repository;
        _saveDelay = saveDelay ?? TimeSpan.FromMilliseconds(500);
        _value = _lastSaved = repository.Load();
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppSettings Value
    {
        get { lock (_gate) return _value; }
    }

    public event Action<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings next;
        lock (_gate)
        {
            next = change(_value);
            if (ReferenceEquals(next, _value)) return;
            _value = next;
            _timer.Change(_saveDelay, Timeout.InfiniteTimeSpan); // 마지막 변경 뒤 일정 시간이 지나면 저장 (디바운스)
        }
        Changed?.Invoke(next);
    }

    /// <summary>지금 값을 바로 저장한다 (종료 직전).</summary>
    public void Flush()
    {
        AppSettings value;
        lock (_gate)
        {
            value = _value;
            if (value == _lastSaved) return;
            _lastSaved = value;
        }
        _repository.Save(value);
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
