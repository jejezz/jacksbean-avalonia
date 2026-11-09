using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;

namespace JackBean.App;

/// <summary>
/// 모드·색 구성을 앱 전체에 적용한다. 색은 <c>AppBg</c>, <c>AppSurface</c> 같은 이름의 리소스로 내려가고,
/// 화면은 <c>{DynamicResource ...}</c>로만 읽는다 (색을 직접 적지 않는다 — theming.md). 바뀌면 열려 있는 모든 창이 같이 바뀐다.
/// 5단계에서 설정 창과 영속화가 이 서비스를 부른다.
/// </summary>
static class ThemeService
{
    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static ColorPalette Palette { get; private set; } = ColorPalette.Saturn;

    public static bool IsDark(Application app) => Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark,
    };

    /// <summary>번들한 기본 글꼴 (SeoulNamsan 400/700/800, fonts.md).</summary>
    const string DefaultFontUri = "avares://JacksBean/Assets/Fonts#SeoulNamsan";

    /// <summary>
    /// 설정 전체(모드·색 구성·글꼴·글자 크기)를 앱에 적용한다. 열려 있는 모든 창이 같이 바뀐다.
    /// 글꼴은 이름이 비었거나 기본 이름이면 번들 글꼴, 그 밖에는 시스템에 설치된 그 이름의 글꼴이다
    /// (시스템에서 못 찾으면 번들 글꼴로 돌아간다). 글자 크기는 13/12/11/16을 배율만큼 키우거나 줄인다 (ui-ux.md §4).
    /// </summary>
    public static void Apply(AppSettings settings)
    {
        Apply(settings.Theme, settings.Palette);
        var app = Application.Current!;
        app.Resources["AppFont"] = FontFamilyFor(settings.UiFontFamily);
        // fonts.md §3·§4: 파일 이름·경로는 시스템 글꼴, 로그·체크섬은 플랫폼별 고정폭 목록.
        app.Resources["AppUserFont"] = new FontFamily("$Default, Apple SD Gothic Neo, Malgun Gothic, Noto Sans CJK KR, Noto Sans KR");
        app.Resources["AppMonoFont"] = new FontFamily("Menlo, SF Mono, Consolas, Cascadia Mono, DejaVu Sans Mono, Noto Sans Mono, Courier New");
        var scale = settings.TextScale;
        app.Resources["FsBody"] = 13 * scale;
        app.Resources["FsMid"] = 12 * scale;
        app.Resources["FsSmall"] = 11 * scale;
        app.Resources["FsTitle"] = 16 * scale;
        UiScale.Set(scale); // 아이콘 크기·버튼/행 높이도 같은 배율
    }

    public const string DefaultFontName = "SeoulNamsan";

    static FontFamily FontFamilyFor(string? name)
    {
        var bundled = new FontFamily(DefaultFontUri);
        if (string.IsNullOrWhiteSpace(name) || name == DefaultFontName) return bundled;
        return FontManager.Current.SystemFonts.Any(f => f.Name == name) ? new FontFamily(name) : bundled;
    }

    public static void Apply(ThemeMode mode, ColorPalette palette)
    {
        var app = Application.Current ?? throw new InvalidOperationException("앱이 아직 없다");
        Mode = mode;
        Palette = palette;
        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        var dark = IsDark(app);
        var c = AppColors.For(palette, dark);
        Brush(app, "AppBg", c.Bg);
        Brush(app, "AppSurface", c.Surface);
        Brush(app, "AppSurfaceHi", c.SurfaceHi);
        Brush(app, "AppStroke", c.Stroke);
        Brush(app, "AppStrokeStrong", c.StrokeStrong);
        Brush(app, "AppTextHi", c.TextHi);
        Brush(app, "AppTextMid", c.TextMid);
        Brush(app, "AppPrimary", c.Primary);
        Brush(app, "AppOnPrimary", c.OnPrimary);
        Brush(app, "AppAccent", c.Accent);
        Brush(app, "AppDanger", c.Danger);
        Brush(app, "AppCursor", (c.Primary & 0x00FFFFFFu) | 0x4D000000u); // 선택·커서 배경: 주색 30%
        Brush(app, "AppHover", (c.TextHi & 0x00FFFFFFu) | 0x14000000u);   // 마우스 올림: 글자색 8%
        // 팝업 메뉴(독 우클릭 메뉴): Fluent 기본 자원을 앱 색으로 덮는다.
        Brush(app, "MenuFlyoutPresenterBackground", c.SurfaceHi);
        Brush(app, "MenuFlyoutPresenterBorderBrush", c.StrokeStrong);
        Brush(app, "MenuFlyoutItemBackgroundPointerOver", (c.TextHi & 0x00FFFFFFu) | 0x14000000u);
        Brush(app, "MenuFlyoutItemBackgroundPressed", (c.TextHi & 0x00FFFFFFu) | 0x1Fu << 24);
        Brush(app, "MenuFlyoutItemForeground", c.TextHi);
        Brush(app, "MenuFlyoutItemForegroundPointerOver", c.TextHi);
        Brush(app, "MenuFlyoutItemKeyboardAcceleratorTextForeground", c.TextMid);

        // Fluent 컨트롤(버튼·선택 색 등)도 같은 주색을 쓰게 한다.
        foreach (var fluent in app.Styles.OfType<FluentTheme>())
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                fluent.Palettes[variant] = new ColorPaletteResources
                {
                    Accent = Color.FromUInt32(c.Primary),
                    RegionColor = Color.FromUInt32(c.Bg),
                };
            }
        }
    }

    static void Brush(Application app, string key, uint argb) =>
        app.Resources[key] = new SolidColorBrush(Color.FromUInt32(argb));
}
