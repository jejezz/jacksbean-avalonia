using Avalonia.Media;
using JackBean.Domain.Theme;

namespace JackBean.App.Windows;

/// <summary>독 알약 테두리 색. 평소엔 테마 강조색 2px 단색, 끌어다 놓으려고 들어왔을 때는 4px 그라데이션(색 구성을 따른다).</summary>
public static class DockRing
{
    public const double IdleWidth = 2;
    public const double ActiveWidth = 4;

    /// <summary>색 구성별 주색: 어두운 테마엔 밝은 주색, 밝은 테마엔 진한 주색(앱 전체 강조색과 같은 값).</summary>
    public static Color Primary(ColorPalette p, bool dark) => Color.FromUInt32(dark ? p.DarkPrimary() : p.LightPrimary());

    /// <summary>그라데이션 끝 색. 새턴 = 보조색(보라), 고대비 = 주색 그대로(단색), 그 밖 = 색상 40° 회전.</summary>
    public static Color GradientEnd(Color primary, ColorPalette p)
    {
        if (p == ColorPalette.HighContrast) return primary;
        if (p == ColorPalette.Saturn) return Color.Parse("#7C5CFF");
        var hsv = new HsvColor(1, 0, 0, 0);
        hsv = primary.ToHsv();
        return new HsvColor(hsv.A, (hsv.H + 40) % 360, hsv.S, hsv.V).ToRgb();
    }

    public static IBrush Brush(ColorPalette p, bool highlighted, bool dark)
    {
        var start = Primary(p, dark);
        if (!highlighted) return new SolidColorBrush(start);
        return new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = { new GradientStop(start, 0), new GradientStop(GradientEnd(start, p), 1) },
        };
    }
}
