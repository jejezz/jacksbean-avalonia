using System.Text.Json.Serialization;

namespace JackBean.Domain.Window;

/// <summary>
/// 마지막으로 기억한 창 위치와 크기 (conventions window-placement.md §4). 저장 키 <c>window_placement</c>,
/// JSON 필드는 <c>x</c>, <c>y</c>, <c>w</c>, <c>h</c>, <c>scale</c>, <c>max</c>.
/// <para>
/// 단위: <b>위치(x, y)는 가상 화면의 물리 픽셀</b>(Avalonia의 <c>Window.Position</c>), <b>크기(w, h)는 논리 단위(DIP)</b>다.
/// 모니터마다 배율이 달라 위치는 픽셀이어야 모니터를 정확히 고를 수 있고, 크기는 논리 단위로 두어야
/// 배율이 다른 모니터에서도 창이 같은 논리 크기로 열린다 (규약의 "모니터 배율이 달라졌어도 논리 크기는 같게").
/// <see cref="Scale"/>은 저장할 때 창이 있던 모니터의 배율이다 (크기를 픽셀로 바꿔 어느 모니터에 걸쳤는지 보는 데 쓴다).
/// </para>
/// <see cref="Maximized"/>이면 x/y/w/h는 최대화하기 <b>직전</b>의 일반 크기다 (최대화를 풀면 원래 크기로 돌아가도록).
/// 필드를 모두 nullable로 둔 것은 손상된 값 하나가 설정 파일 전체를 못 읽게 만들지 않게 하려는 것이다.
/// </summary>
public sealed record WindowPlacement
{
    [JsonPropertyName("x")] public double? X { get; init; }
    [JsonPropertyName("y")] public double? Y { get; init; }
    [JsonPropertyName("w")] public double? Width { get; init; }
    [JsonPropertyName("h")] public double? Height { get; init; }
    [JsonPropertyName("scale")] public double? Scale { get; init; }
    [JsonPropertyName("max")] public bool Maximized { get; init; }

    /// <summary>쓸 수 있는 값인가: 모든 숫자가 있고 유한하며 크기·배율이 양수.</summary>
    [JsonIgnore]
    public bool IsValid =>
        new[] { X, Y, Width, Height, Scale }.All(v => v is { } d && double.IsFinite(d))
        && Width > 0 && Height > 0 && Scale > 0;

    /// <summary>화면(픽셀 좌표계)에서 이 창이 차지하는 사각형. 유효할 때만 부른다.</summary>
    internal RectD PixelRect => new(X!.Value, Y!.Value, Width!.Value * Scale!.Value, Height!.Value * Scale.Value);
}

/// <summary>사각형 (왼쪽 위 기준).</summary>
public readonly record struct RectD(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    /// <summary>겹치는 부분. 겹치지 않으면 너비·높이가 0인 사각형.</summary>
    public RectD Intersect(RectD other)
    {
        var l = Math.Max(Left, other.Left);
        var t = Math.Max(Top, other.Top);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return new RectD(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }
}

/// <summary>모니터 하나의 작업 영역(작업 표시줄 제외, 물리 픽셀). 모든 모니터가 같은 가상 화면 좌표계를 쓴다.</summary>
public sealed record DisplayArea(RectD WorkArea, double Scale, bool Primary = false);

/// <summary>복원할 창: 위치는 가상 화면의 물리 픽셀, 크기는 논리 단위(DIP).</summary>
public readonly record struct PlacementTarget(double X, double Y, double Width, double Height);
