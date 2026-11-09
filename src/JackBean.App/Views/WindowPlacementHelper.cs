using Avalonia;
using Avalonia.Controls;

namespace JackBean.App.Views;

/// <summary>독립 창을 어디에 열지 정하는 도우미.</summary>
static class WindowPlacementHelper
{
    /// <summary>
    /// <paramref name="window"/>를 <paramref name="origin"/>이 있는 모니터에, origin 위치에서 조금 비켜서 연다. 위치를 지정하지 않으면
    /// 새 창이 항상 주 모니터에 뜬다. 소유자(owner)로 묶지 않는다: 소유된 창은 소유자 위에 붙어 다니고 같이 최소화되어 독립 창이 아니다.
    /// </summary>
    public static void OpenNear(Window origin, Window window)
    {
        // 최대화 상태의 Position은 화면 가장자리라, 보통 크기로 열 새 창은 그 모니터의 작업 영역 안쪽에 둔다.
        var screen = origin.Screens.ScreenFromWindow(origin) ?? origin.Screens.Primary;
        var area = screen?.WorkingArea.Position ?? origin.Position;
        var offset = (int)(40 * (screen?.Scaling ?? 1.0));
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = origin.WindowState == WindowState.Normal
            ? new PixelPoint(origin.Position.X + offset, origin.Position.Y + offset)
            : new PixelPoint(area.X + 2 * offset, area.Y + 2 * offset);
        window.Show();
    }
}
