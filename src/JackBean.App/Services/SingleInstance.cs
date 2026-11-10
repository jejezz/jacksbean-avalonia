using JackBean.Domain;

namespace JackBean.App;

/// <summary>
/// 앱은 한 번에 하나만 뜬다. 이름 있는 뮤텍스(<see cref="AppIdentity.Id"/> — 설치 프로그램도 같은 이름으로 실행 중인지 확인한다)를 먼저 잡은 쪽이 본체이고,
/// 나중에 실행한 쪽은 본체에 신호를 보내고 바로 끝난다. 신호는 Windows 에서만(이름 있는 이벤트가 다른 OS 에는 없다) — 다른 OS 에서는 두 번째 실행이 조용히 끝난다.
/// 신호 둘: <b>보이기</b>(독이 숨어 있으면 보인다 — 다시 실행했는데 아무것도 안 보이면 이상하니까), <b>종료</b>(`--quit` — 설치 프로그램이 업그레이드 전에 앱을 닫을 때).
/// 두 번째 실행이 본체를 못 만들면(둘 다 뜬 것처럼 보이는 일이 없도록) 항상 여기서 끝낸다.
/// </summary>
internal static class SingleInstance
{
    static string Name => AppIdentity.Id;
    static string ShowName => Name + ".show";
    static string QuitName => Name + ".quit";

    static Mutex? _mutex;
    static EventWaitHandle? _show, _quit;

    /// <summary>본체가 되는가. 이미 본체가 있으면 false(이때는 <see cref="SignalPrimary"/> 로 알리고 끝낸다).</summary>
    public static bool TryBecomePrimary()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, Name, out var created);
            if (!created) { _mutex.Dispose(); _mutex = null; return false; }
            if (OperatingSystem.IsWindows())
            {
                _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
                _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
            }
            return true;
        }
        catch (Exception)
        {
            return true;   // 뮤텍스를 못 만드는 환경이면 막지 않고 그냥 뜬다
        }
    }

    /// <summary>본체에 신호를 보낸다(<paramref name="quit"/> 이면 종료, 아니면 보이기). 못 보내도 괜찮다.</summary>
    public static void SignalPrimary(bool quit)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var handle = EventWaitHandle.OpenExisting(quit ? QuitName : ShowName);
            handle.Set();
        }
        catch (Exception) { /* 본체가 막 끝났거나 이벤트가 아직 없다 */ }
    }

    /// <summary>본체가 신호를 기다린다(백그라운드 스레드). 콜백은 호출한 쪽이 UI 스레드로 넘긴다.</summary>
    public static void Listen(Action onShow, Action onQuit)
    {
        if (_show is null || _quit is null) return;
        WaitHandle[] handles = [_show, _quit];
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    var index = WaitHandle.WaitAny(handles);
                    if (index == 0) onShow(); else onQuit();
                }
                catch (Exception) { return; }
            }
        }) { IsBackground = true, Name = "single-instance" }.Start();
    }
}
