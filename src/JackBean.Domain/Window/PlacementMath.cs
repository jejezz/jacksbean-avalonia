namespace JackBean.Domain.Window;

/// <summary>창 배치 계산 (conventions window-placement.md). 화면에 아무것도 하지 않는 순수 함수라서 테스트할 수 있다.</summary>
public static class PlacementMath
{
    /// <summary>제목 표시줄을 잡고 끌 수 있으려면 이만큼(논리 단위)은 어느 모니터 위에 걸쳐 있어야 한다.</summary>
    public const double MinVisibleWidth = 100;
    public const double MinVisibleHeight = 50;

    /// <summary><paramref name="d"/>의 가운데에 <paramref name="width"/>×<paramref name="height"/>(논리 단위)인 창을 놓는다. 작업 영역보다 크면 줄인다.</summary>
    public static PlacementTarget CenterOn(DisplayArea d, double width, double height)
    {
        var w = Math.Min(width, d.WorkArea.Width / d.Scale);
        var h = Math.Min(height, d.WorkArea.Height / d.Scale);
        return new PlacementTarget(
            d.WorkArea.Left + (d.WorkArea.Width - w * d.Scale) / 2,
            d.WorkArea.Top + (d.WorkArea.Height - h * d.Scale) / 2,
            w, h);
    }

    public static DisplayArea? PrimaryOf(IReadOnlyList<DisplayArea> displays) =>
        displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();

    /// <summary>
    /// 저장한 창(<paramref name="saved"/>)이 가장 많이 걸친 모니터. 제목 표시줄을 잡을 만큼 걸친 모니터가 없으면 null.
    /// 이 계산은 저장할 때의 배율로 만든 픽셀 사각형으로 한다.
    /// </summary>
    public static DisplayArea? BestDisplayFor(RectD pixelBounds, double scale, IReadOnlyList<DisplayArea> displays)
    {
        DisplayArea? best = null;
        var bestArea = 0.0;
        foreach (var d in displays)
        {
            var i = pixelBounds.Intersect(d.WorkArea);
            if (i.Width < MinVisibleWidth * scale || i.Height < MinVisibleHeight * scale) continue;
            if (i.Width * i.Height > bestArea)
            {
                best = d;
                bestArea = i.Width * i.Height;
            }
        }
        return best;
    }

    /// <summary>
    /// 저장한 위치를 지금 연결된 모니터에 맞춰 복원한다. 가장 많이 걸친 모니터를 고르고, 걸친 모니터가 없으면
    /// (떼어낸 모니터 등) null. 돌려주는 창은 항상 한 모니터의 작업 영역 안에 완전히 들어온다.
    /// 크기는 논리 단위 그대로 두되 그 모니터의 작업 영역보다 크면 줄인다.
    /// </summary>
    public static PlacementTarget? Restore(WindowPlacement saved, IReadOnlyList<DisplayArea> displays)
    {
        if (!saved.IsValid) return null;
        var display = BestDisplayFor(saved.PixelRect, saved.Scale!.Value, displays);
        if (display is null) return null;

        var work = display.WorkArea;
        var w = Math.Min(saved.Width!.Value, work.Width / display.Scale);
        var h = Math.Min(saved.Height!.Value, work.Height / display.Scale);
        var wPx = w * display.Scale;
        var hPx = h * display.Scale;
        var left = Math.Clamp(saved.X!.Value, work.Left, Math.Max(work.Left, work.Right - wPx));
        var top = Math.Clamp(saved.Y!.Value, work.Top, Math.Max(work.Top, work.Bottom - hPx));
        return new PlacementTarget(left, top, w, h);
    }

    /// <summary>
    /// 창이 움직이거나 크기가 바뀔 때 저장할 값. 최대화 중이면 크기·위치는 최대화 직전의 값을 그대로 두고
    /// "최대화했다"는 사실만 새로 적는다. 위치를 아직 모르면(null) 이전 값을 유지하고, 이전도 없으면 저장하지 않는다.
    /// </summary>
    public static WindowPlacement? Updated(
        WindowPlacement? previous, double? x, double? y, double width, double height, double scale, bool maximized)
    {
        if (maximized) return previous is { IsValid: true } p ? p with { Maximized = true } : null;
        var px = x ?? previous?.X;
        var py = y ?? previous?.Y;
        if (px is null || py is null) return null;
        return new WindowPlacement { X = px, Y = py, Width = width, Height = height, Scale = scale, Maximized = false };
    }
}
