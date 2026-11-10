using System.Text.Json.Serialization;
using JackBean.Domain.Dock;
using JackBean.Domain.Theme;
using JackBean.Domain.Window;

namespace JackBean.Domain.Settings;

/// <summary>
/// UI 언어 (conventions localization.md): 한국어가 기준이고 영어를 더한다. 저장 값은 <c>ko</c> / <c>en</c>이고,
/// 설정하지 않으면(null) 시스템 언어를 따른다 — <see cref="Localization.Resolve"/>.
/// </summary>
public enum AppLocale { Korean, English }

public static class Localization
{
    /// <summary>
    /// 실제로 쓸 언어. 설정이 있으면 그대로, 없으면(시스템) <b>OS 언어가 한국어면 한국어, 그 밖의 모든 언어는 영어</b>다
    /// (localization.md §3). <paramref name="osLanguage"/>는 <c>CultureInfo.CurrentUICulture.TwoLetterISOLanguageName</c> 같은 언어 코드.
    /// </summary>
    public static AppLocale Resolve(AppLocale? setting, string osLanguage) =>
        setting ?? (osLanguage.Equals("ko", StringComparison.OrdinalIgnoreCase) ? AppLocale.Korean : AppLocale.English);
}

/// <summary>화면 글자 크기 배율의 범위·간격 (슬라이더 80%~160%, 5% 단위). 저장은 배율 숫자(<c>text_scale</c>)로 한다.</summary>
public static class TextScales
{
    public const double Min = 0.8, Max = 1.6, Step = 0.05, Default = 1.0;

    /// <summary>범위 안으로, 5% 단위로 맞춘다. 숫자가 아니면 기본(100%).</summary>
    public static double Snap(double value) =>
        !double.IsFinite(value) ? Default : Math.Round(Math.Clamp(Math.Round(value / Step) * Step, Min, Max), 2);
}

/// <summary>
/// 영속화되는 앱 설정. 저장 키는 모든 앱 공통 규약을 따른다 (conventions localization.md §5):
/// <c>theme_mode</c>, <c>accent_palette</c>, <c>text_scale</c>, <c>app_locale</c>, <c>window_placement</c>.
/// 앱 고유 설정은 <c>&lt;기능&gt;_&lt;이름&gt;</c> 키를 쓴다 (<c>dock_*</c>, <c>global_hotkey</c>). 같은 키 이름의 JSON 파일 하나에 저장한다
/// (정책 파일 <c>policy.&lt;os&gt;.json</c>과는 별개 — 정책만 동기화 대상이다).
/// <para>
/// 모든 필드에 기본값이 있어서 옛 파일에 없는 키가 있어도, 새 버전이 키를 더해도 읽힌다. 불변이라 <c>with</c>로 바꾼다.
/// </para>
/// </summary>
public sealed record AppSettings
{
    [JsonPropertyName("theme_mode")]
    [JsonConverter(typeof(ThemeModeConverter))]
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    [JsonPropertyName("accent_palette")]
    [JsonConverter(typeof(ColorPaletteConverter))]
    public ColorPalette Palette { get; init; } = ColorPalette.Saturn;

    [JsonPropertyName("text_scale")]
    public double TextScale { get; init; } = TextScales.Default;

    /// <summary>null이면 시스템 언어를 따른다.</summary>
    [JsonPropertyName("app_locale")]
    [JsonConverter(typeof(AppLocaleConverter))]
    public AppLocale? Locale { get; init; }

    /// <summary>UI 글꼴 이름. null이면 기본(SeoulNamsan). 시스템에 설치된 글꼴 이름을 고를 수 있다.</summary>
    [JsonPropertyName("ui_font_family")]
    public string? UiFontFamily { get; init; }

    /// <summary>홈 창의 마지막 위치와 크기 (window-placement.md).</summary>
    [JsonPropertyName("window_placement")]
    public WindowPlacement? Window { get; init; }

    /// <summary>사용자가 끌어다 놓은 독의 자리(오른쪽 끝·위쪽). null이면 오른쪽 가장자리 세로 가운데.</summary>
    [JsonPropertyName("dock_position")]
    public DockPosition? Dock { get; init; }

    /// <summary>전역 단축키(Ctrl+Alt+J)로 독을 숨기거나 보이게 한다. 기본은 꺼짐 — 다른 앱과 겹칠 수 있어서 사용자가 켠다. 이 OS 에서 쓸 수 없으면 의미 없다.</summary>
    [JsonPropertyName("global_hotkey")]
    public bool GlobalHotkey { get; init; }

    /// <summary>정책을 동기화할 GitHub 저장소 주소(https 또는 ssh). null 이면 동기화를 쓰지 않는다. 로그인은 이 PC 의 git 설정이 처리하므로 토큰은 저장하지 않는다.</summary>
    [JsonPropertyName("sync_repository")]
    public string? SyncRepository { get; init; }

    /// <summary>마지막으로 동기화에 성공한 시각(표시용).</summary>
    [JsonPropertyName("sync_last_at")]
    public DateTimeOffset? SyncLastAt { get; init; }

    public AppSettings WithTextScale(double scale) => this with { TextScale = TextScales.Snap(scale) };

    /// <summary>파일에서 읽은 값을 쓸 수 있는 값으로 고친다 (배율은 범위 안으로, 틀린 독 위치는 버린다).</summary>
    public AppSettings Normalized()
    {
        var result = this;
        var snapped = TextScales.Snap(TextScale);
        if (snapped != TextScale) result = result with { TextScale = snapped };
        if (Dock is { IsValid: false }) result = result with { Dock = null };
        return result;
    }
}

/// <summary>설정을 읽고 쓴다. 구현은 data 계층.</summary>
public interface ISettingsRepository
{
    /// <summary>파일이 없거나 읽을 수 없으면 기본값. 예외를 던지지 않는다.</summary>
    AppSettings Load();

    void Save(AppSettings settings);
}
