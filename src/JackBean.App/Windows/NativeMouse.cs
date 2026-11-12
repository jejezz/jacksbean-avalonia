// 지금 마우스 주 버튼이 눌려 있는가(Windows). 독을 누르면 창 옮기기(BeginMoveDrag)를 시작하는데, Windows 에서는 그 호출이
// 버튼을 떼기 전에 돌아와서 "눌렀다 뗐다(클릭)"를 알 수 없다 — 옮기기 중에는 떼는 이벤트도 오지 않는다. 그래서 버튼 상태를 직접 본다.

using System.Runtime.InteropServices;

namespace JackBean.App.Windows;

internal static partial class NativeMouse
{
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int SM_SWAPBUTTON = 23;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    /// <summary>주 버튼(보통 왼쪽 — 왼손잡이 설정이면 오른쪽 물리 버튼)이 눌려 있다. Windows 가 아니면 null(알 수 없다).</summary>
    public static bool? IsPrimaryDown()
    {
        if (!OperatingSystem.IsWindows()) return null;
        // GetAsyncKeyState 는 물리 버튼을 본다 — 버튼을 바꿔 두었으면 오른쪽 물리 버튼이 주 버튼이다.
        var vk = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
