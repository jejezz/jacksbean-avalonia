namespace JackBean.Domain.Theme;

/// <summary>색 구성 6종 (conventions theming.md). 기본은 <see cref="Saturn"/>.</summary>
public enum ColorPalette { Saturn, Teal, Amber, Rose, Slate, HighContrast }

public static class ColorPaletteExtensions
{
    /// <summary>다크 모드의 주색 (0xAARRGGBB).</summary>
    public static uint DarkPrimary(this ColorPalette p) => p switch
    {
        ColorPalette.Saturn => 0xFF4C9DFF,
        ColorPalette.Teal => 0xFF2DD4BF,
        ColorPalette.Amber => 0xFFFBBF24,
        ColorPalette.Rose => 0xFFFB7185,
        ColorPalette.Slate => 0xFF94A3B8,
        ColorPalette.HighContrast => 0xFFFFD60A,
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    /// <summary>라이트 모드의 주색 (0xAARRGGBB).</summary>
    public static uint LightPrimary(this ColorPalette p) => p switch
    {
        ColorPalette.Saturn => 0xFF2C6BE0,
        ColorPalette.Teal => 0xFF0F766E,
        ColorPalette.Amber => 0xFFB45309,
        ColorPalette.Rose => 0xFFBE123C,
        ColorPalette.Slate => 0xFF475569,
        ColorPalette.HighContrast => 0xFF0000CC,
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };
}
