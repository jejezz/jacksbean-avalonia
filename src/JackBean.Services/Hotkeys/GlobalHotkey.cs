using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JackBean.Services.Hotkeys;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>전역 단축키 하나: 수정키 + 가상 키 코드(Windows VK: `J` = 0x4A).</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>독을 숨기거나 다시 보이게 하는 단축키.</summary>
    public static HotkeyGesture ToggleDock { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4A);

    /// <summary>화면에 보여 줄 이름. 예: `Ctrl+Alt+J`.</summary>
    public string Display(bool mac = false)
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add(mac ? "⌃" : "Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add(mac ? "⌥" : "Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add(mac ? "⇧" : "Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add(mac ? "⌘" : "Win");
        parts.Add(VirtualKey is >= 0x30 and <= 0x5A ? ((char)VirtualKey).ToString() : $"0x{VirtualKey:X}");
        return string.Join(mac ? "" : "+", parts);
    }
}

/// <summary>
/// 어느 앱에 있어도 눌리는 단축키 하나. 실패 이유(다른 앱이 이미 쓰고 있음 등)는 <see cref="Register"/> 의 error 로 돌려준다.
/// <paramref name="onPressed"/> 는 **다른 스레드**에서 불린다 — UI 를 만지려면 호출한 쪽이 UI 스레드로 넘긴다.
/// </summary>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>이 OS 에서 전역 단축키를 쓸 수 있다(지금은 Windows 만).</summary>
    bool IsSupported { get; }

    bool Register(HotkeyGesture gesture, Action onPressed, out string? error);

    void Unregister();
}

public static class GlobalHotkeys
{
    public static IGlobalHotkey Create() => OperatingSystem.IsWindows() ? new WindowsGlobalHotkey() : new UnsupportedGlobalHotkey();
}

/// <summary>전역 단축키를 지원하지 않는 OS(macOS 는 접근성 권한이 필요하고, Linux Wayland 는 아예 막혀 있다). 기능을 숨긴다.</summary>
internal sealed class UnsupportedGlobalHotkey : IGlobalHotkey
{
    public bool IsSupported => false;
    public bool Register(HotkeyGesture gesture, Action onPressed, out string? error) { error = "unsupported"; return false; }
    public void Unregister() { }
    public void Dispose() { }
}

/// <summary>
/// Windows: 전용 스레드에서 `RegisterHotKey`(창 없이 스레드에 등록)를 하고 그 스레드의 메시지 루프가 WM_HOTKEY 를 받는다.
/// 해제는 스레드에 WM_QUIT 를 보내 루프를 끝낸다. 꾹 눌러도 한 번만 불리게 MOD_NOREPEAT.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsGlobalHotkey : IGlobalHotkey
{
    private const uint WM_HOTKEY = 0x0312, WM_QUIT = 0x0012, PM_NOREMOVE = 0, MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0x4A42;   // "JB"

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int ptX, ptY; }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessage(out MSG msg, nint hWnd, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out MSG msg, nint hWnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint msg, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;

    public bool IsSupported => true;

    public bool Register(HotkeyGesture gesture, Action onPressed, out string? error)
    {
        lock (_gate)
        {
            StopLocked();
            var ready = new ManualResetEventSlim();
            string? failure = null;
            var ok = false;
            var thread = new Thread(() =>
            {
                _ = PeekMessage(out _, 0, 0, 0, PM_NOREMOVE);   // 이 스레드의 메시지 큐를 먼저 만든다(WM_QUIT 를 받을 수 있게)
                _threadId = GetCurrentThreadId();
                ok = RegisterHotKey(0, HotkeyId, (uint)gesture.Modifiers | MOD_NOREPEAT, gesture.VirtualKey);
                if (!ok) failure = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                ready.Set();
                if (!ok) return;
                while (GetMessage(out var msg, 0, 0, 0) > 0)
                    if (msg.message == WM_HOTKEY && msg.wParam == (nuint)HotkeyId)
                    {
                        try { onPressed(); } catch (Exception) { /* 단축키 처리가 실패해도 루프는 계속 돈다 */ }
                    }
                UnregisterHotKey(0, HotkeyId);
            }) { IsBackground = true, Name = "JackBean global hotkey" };
            thread.Start();
            ready.Wait();
            error = failure;
            if (ok) _thread = thread; else thread.Join(1000);
            return ok;
        }
    }

    public void Unregister()
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

    public void Dispose() => Unregister();
}
