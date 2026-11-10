// 창의 위치와 크기를 한 번에 바꾼다(Windows). Avalonia 의 Width/Height 와 Position 은 따로 적용되어
// 그 사이 한 프레임이 어긋난 자리에 보인다 — 오른쪽 끝을 지키며 왼쪽으로 자라는 독에서 "튕기는" 모습의 원인.
// 다른 OS 에서는 false 를 돌려주고, 호출한 쪽이 기존 방식으로 바꾼다.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace JackBean.App.Windows;

internal static partial class NativeBounds
{
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public static bool TrySet(Window window, PixelPoint position, PixelSize size)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        return handle != 0 && SetWindowPos(handle, 0, position.X, position.Y, size.Width, size.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
