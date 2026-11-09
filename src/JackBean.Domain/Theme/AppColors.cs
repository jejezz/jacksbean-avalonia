namespace JackBean.Domain.Theme;

/// <summary>해석된 색 한 벌 (모두 0xAARRGGBB). UI 프레임워크에 의존하지 않아 테스트할 수 있다.</summary>
public sealed record AppColorSet(
    uint Bg, uint Surface, uint SurfaceHi, uint Stroke, uint StrokeStrong,
    uint TextHi, uint TextMid, uint Primary, uint OnPrimary, uint Accent, uint Danger);

/// <summary>원본 Flutter <c>AppColors</c> / <c>AppTheme</c>의 색 (UI_UX.md 2장). 다크·라이트 동등 지원.</summary>
public static class AppColors
{
    const uint Accent = 0xFF7C5CFF;
    const uint Danger = 0xFFFF5A5F;

    public static AppColorSet For(ColorPalette palette, bool dark)
    {
        if (palette == ColorPalette.HighContrast) return HighContrast(dark);
        var primary = dark ? palette.DarkPrimary() : palette.LightPrimary();
        // 다크에서 새턴은 흰 글자, 다른 색 구성은 밝은 주색이라 어두운 글자 (원본 그대로).
        var onPrimary = dark && palette != ColorPalette.Saturn ? 0xFF0A0E14u : 0xFFFFFFFFu;
        return dark
            ? new AppColorSet(0xFF0A0E14, 0xFF151D27, 0xFF1D2733, 0x1AFFFFFF, 0x33FFFFFF,
                0xFFF1F5F9, 0xFFA9B4C4, primary, onPrimary, Accent, Danger)
            : new AppColorSet(0xFFF4F6FA, 0xFFFFFFFF, 0xFFE3E8EF, 0x14000000, 0x24000000,
                0xFF101828, 0xFF5B6676, primary, onPrimary, Accent, Danger);
    }

    /// <summary>고대비: 배경·글자·테두리를 순흑/순백으로.</summary>
    static AppColorSet HighContrast(bool dark)
    {
        uint fg = dark ? 0xFFFFFFFF : 0xFF000000;
        uint bg = dark ? 0xFF000000 : 0xFFFFFFFF;
        uint dim = dark ? 0xFFBFBFBF : 0xFF404040;
        uint hi = dark ? 0xFF262626 : 0xFFE0E0E0;
        uint primary = dark ? ColorPalette.HighContrast.DarkPrimary() : ColorPalette.HighContrast.LightPrimary();
        return new AppColorSet(bg, bg, hi, dim, fg, fg, dim, primary, bg, primary,
            dark ? 0xFFFF6B6B : 0xFFCC0000);
    }
}
