using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JackBean.Services.DragWatch;

public enum DragSignal { None, Started, Ended }

/// <summary>
/// 다른 앱에서 파일 드래그가 시작됐는지 마우스와 커서만으로 추정한다(Windows 의 OLE 드래그는 시스템에 알림을 남기지 않는다).
/// 왼쪽 버튼을 누른 채 <see cref="Threshold"/> 보다 많이 움직였고 드래그의 흔적(셸 드래그 이미지 창이 보이거나, 커서가 드래그 모양)이 보이면 시작,
/// 버튼을 떼면 끝. 창 끌기·글자 선택·크기 조절은 커서가 화살표·I빔·크기 조절 커서로 남으므로 걸러진다.
/// 오탐(커서를 직접 그리는 앱)이 날 수 있으니 받는 쪽은 "살짝 알리기"만 하고 실제 판정은 드롭 대상이 한다.
/// </summary>
public sealed class DragDetector
{
    public const int Threshold = 6;

    private bool _down, _moved, _started;
    private int _x, _y;

    public bool IsStarted => _started;

    /// <summary>지금 끄는 중이라 커서를 볼 만한가(폴링을 아끼려고).</summary>
    public bool Watching => _down && _moved && !_started;

    public void ButtonDown(int x, int y) { _down = true; _moved = false; _started = false; _x = x; _y = y; }

    public void Move(int x, int y)
    {
        if (_down && !_moved && (Math.Abs(x - _x) > Threshold || Math.Abs(y - _y) > Threshold)) _moved = true;
    }

    /// <param name="dragCursor">드래그의 흔적이 보인다 — 셸 드래그 이미지 창이 보이거나 커서가 드래그 모양(보통 커서가 아니거나 "놓을 수 없음")이다.</param>
    public DragSignal Cursor(bool dragCursor)
    {
        if (!Watching || !dragCursor) return DragSignal.None;
        _started = true;
        return DragSignal.Started;
    }

    public DragSignal ButtonUp()
    {
        var was = _started;
        _down = _moved = _started = false;
        return was ? DragSignal.Ended : DragSignal.None;
    }
}

/// <summary>
/// 다른 앱에서 시작한 드래그를 알려 준다. 콜백은 **다른 스레드**에서 불린다 — UI 를 만지려면 받는 쪽이 UI 스레드로 넘긴다.
/// </summary>
public interface IDragWatcher : IDisposable
{
    /// <summary>이 OS 에서 쓸 수 있다(지금은 Windows 만).</summary>
    bool IsSupported { get; }

    bool Start(Action<DragSignal> onSignal, out string? error);

    void Stop();
}

public static class DragWatchers
{
    public static IDragWatcher Create() => OperatingSystem.IsWindows() ? new WindowsDragWatcher() : new UnsupportedDragWatcher();
}

internal sealed class UnsupportedDragWatcher : IDragWatcher
{
    public bool IsSupported => false;
    public bool Start(Action<DragSignal> onSignal, out string? error) { error = "unsupported"; return false; }
    public void Stop() { }
    public void Dispose() { }
}

/// <summary>
/// Windows: 전용 스레드에 저수준 마우스 후크(WH_MOUSE_LL)를 걸고, 같은 스레드의 50ms 타이머로 끄는 동안에만 드래그의 흔적을 본다.
/// 흔적은 둘이다. ① 셸 드래그 이미지 창(`SysDragImage`, 탐색기·바탕화면처럼 드래그 이미지를 쓰는 앱 — 이 앱들은 드롭 대상 위에서 커서를 화살표로
/// 두고 효과를 이미지에 그리므로 커서만으로는 놓친다) ② 커서 모양(드래그 이미지를 안 쓰는 앱, 드롭 대상이 아닌 곳의 "놓을 수 없음").
/// 후크 콜백은 그 스레드의 메시지 루프에서 불리므로 <see cref="DragDetector"/> 를 잠금 없이 쓴다. 콜백은 가볍게 — 늦으면 시스템이 후크를 건너뛴다.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsDragWatcher : IDragWatcher
{
    private const int WH_MOUSE_LL = 14;
    private const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, PM_NOREMOVE = 0;
    private const int IDC_NO = 32648;
    private const uint PollMs = 50;

    /// <summary>드래그가 아닌 보통 커서(화살표·I빔·대기·십자·크기 조절·손·도움말·펜 …). "놓을 수 없음"은 OLE 드래그가 쓰므로 뺀다.</summary>
    private static readonly int[] PlainCursorIds = [32512, 32513, 32514, 32515, 32516, 32631, 32642, 32643, 32644, 32645, 32646, 32649, 32650, 32651, 32671, 32672];

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int ptX, ptY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public nuint extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize, flags; public nint hCursor; public int x, y; }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(int idHook, delegate* unmanaged<int, nuint, nint, nint> fn, nint hMod, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int code, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out MSG msg, nint hWnd, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out MSG msg, nint hWnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hWnd, nuint id, uint ms, nint fn);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CURSORINFO ci);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint hInstance, nint id);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string? className, string? title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    // 후크 콜백은 정적이어야 한다(UnmanagedCallersOnly). 한 번에 하나만 돈다.
    private static WindowsDragWatcher? _active;

    private readonly object _gate = new();
    private readonly DragDetector _detector = new();
    private HashSet<nint> _plain = [];
    private Action<DragSignal> _onSignal = _ => { };
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;

    public bool IsSupported => true;

    public bool Start(Action<DragSignal> onSignal, out string? error)
    {
        lock (_gate)
        {
            StopLocked();
            _onSignal = onSignal;
            _plain = PlainCursorIds.Select(id => LoadCursor(0, id)).Where(h => h != 0).ToHashSet();
            var ready = new ManualResetEventSlim();
            string? failure = null;
            var thread = new Thread(() =>
            {
                _ = PeekMessage(out _, 0, 0, 0, PM_NOREMOVE);   // 메시지 큐를 먼저 만든다(WM_QUIT 를 받을 수 있게)
                _threadId = GetCurrentThreadId();
                _active = this;
                _hook = SetWindowsHookEx(WH_MOUSE_LL, &HookProc, GetModuleHandle(null), 0);
                if (_hook == 0) failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                ready.Set();
                if (_hook == 0) { _active = null; return; }
                SetTimer(0, 0, PollMs, 0);
                while (GetMessage(out var msg, 0, 0, 0) > 0)
                    if (msg.message == WM_TIMER && _detector.Watching) Emit(_detector.Cursor(DragImageVisible() || IsDragCursor()));
                UnhookWindowsHookEx(_hook);
                _hook = 0;
                _active = null;
            }) { IsBackground = true, Name = "JackBean drag watch" };
            thread.Start();
            ready.Wait();
            error = failure;
            if (failure == null) _thread = thread; else thread.Join(1000);
            return failure == null;
        }
    }

    /// <summary>셸 드래그 이미지 창이 보이는가. 창은 드래그가 끝나도 숨은 채 남을 수 있어 보이는 것만 센다.</summary>
    private static bool DragImageVisible()
    {
        for (nint h = FindWindowEx(0, 0, "SysDragImage", null); h != 0; h = FindWindowEx(0, h, "SysDragImage", null))
            if (IsWindowVisible(h)) return true;
        return false;
    }

    private bool IsDragCursor()
    {
        var ci = new CURSORINFO { cbSize = sizeof(CURSORINFO) };
        if (!GetCursorInfo(ref ci) || ci.hCursor == 0) return false;   // 커서가 숨어 있으면(게임·영상) 드래그로 보지 않는다
        return !_plain.Contains(ci.hCursor);
    }

    private void Emit(DragSignal signal)
    {
        if (signal == DragSignal.None) return;
        try { _onSignal(signal); } catch (Exception) { /* 받는 쪽이 실패해도 후크는 계속 돈다 */ }
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int code, nuint wParam, nint lParam)
    {
        var self = _active;
        if (code >= 0 && self != null)
        {
            var m = (MSLLHOOKSTRUCT*)lParam;
            switch ((uint)wParam)
            {
                case WM_LBUTTONDOWN: self._detector.ButtonDown(m->x, m->y); break;
                case WM_MOUSEMOVE: self._detector.Move(m->x, m->y); break;
                case WM_LBUTTONUP: self.Emit(self._detector.ButtonUp()); break;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    private void StopLocked()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(2000);
        _thread = null;
    }

    public void Dispose() => Stop();
}
