using System.Text.Json.Serialization;
using JackBean.Domain.Window;

namespace JackBean.Domain.Dock;

public readonly record struct SizeD(double Width, double Height);

/// <summary>
/// 사용자가 끌어다 놓은 독의 자리. 독은 **오른쪽 끝과 위쪽**을 기준으로 놓는다 — 크기가 바뀔 때 오른쪽 끝을 지키고
/// 위쪽은 그대로이기 때문이다. 값은 가상 화면의 **물리 픽셀**이다(모니터마다 배율이 달라도 비교할 수 있다).
/// Scale 은 저장할 때 독이 있던 모니터의 배율.
/// </summary>
public sealed record DockPosition
{
    public DockPosition() { }

    public DockPosition(double right, double top, double scale)
    {
        Right = right;
        Top = top;
        Scale = scale;
    }

    [JsonPropertyName("right")] public double Right { get; init; }
    [JsonPropertyName("top")] public double Top { get; init; }
    [JsonPropertyName("scale")] public double Scale { get; init; }

    /// <summary>쓸 수 있는 값인가: 모두 유한하고 배율이 양수. 파일을 손으로 고쳐 틀린 값이 들어와도 독이 화면 밖에 남지 않게 한다.</summary>
    [JsonIgnore]
    public bool IsValid => double.IsFinite(Right) && double.IsFinite(Top) && double.IsFinite(Scale) && Scale > 0;

    /// <summary>
    /// 저장한 자리에 논리 크기 logicalSize 인 독을 놓을 사각형(물리 픽셀). 모니터 작업 영역 work 를 벗어나지 않게 끌어들인다.
    /// </summary>
    public RectD Fit(SizeD logicalSize, RectD work, double scale)
    {
        var w = logicalSize.Width * scale;
        var h = logicalSize.Height * scale;
        var fittedRight = Math.Clamp(Right, work.Left + w, Math.Max(work.Left + w, work.Right));
        var fittedTop = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - h));
        return new RectD(fittedRight - w, fittedTop, w, h);
    }
}
