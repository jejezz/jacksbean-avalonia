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
/// 모든 필드에 기본값이 있어서 옛 파일에 없는 키가 있어도, 새 버전이 키를 더해도 읽힌다. <c>with</c>로 바꾼다.
/// <b>기본값이 0·false·null 이 아닌 속성은 <c>init</c> 대신 <c>set</c></b>이다: 소스 생성 JSON 은 <c>init</c> 속성을 객체 초기화식으로 채우면서
/// 파일에 없는 키에 기본값(0·false)을 넣어 버려, 키가 없는 옛 파일에서 <c>recent_history</c>·<c>startup_tips</c> 가 꺼지고 글자 크기가 80% 가 됐다.
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
    public double TextScale { get; set; } = TextScales.Default;   // init 이 아니다 — 아래 설명

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

    /// <summary>VS Code 프로필 도구(프로젝트 폴더를 분석해 프로필을 추천·생성). 기본은 꺼짐 — 필요 없는 사람도 있어서 사용자가 켠다. Windows 에서만 쓴다.</summary>
    [JsonPropertyName("vscode_profile_enabled")]
    public bool VsCodeProfileEnabled { get; init; }

    /// <summary>VS Code 실행 파일(`code.cmd`) 경로. null 이면 자동으로 찾고, 못 찾으면 `code` 를 PATH 에서 찾는다.</summary>
    [JsonPropertyName("vscode_path")]
    public string? VsCodePath { get; init; }

    /// <summary>정책을 동기화할 GitHub 저장소(`소유자/이름`). null 이면 동기화를 쓰지 않는다. 토큰은 설정 파일이 아니라 OS 보안 저장소에만 둔다.</summary>
    [JsonPropertyName("sync_repo")]
    public string? SyncRepo { get; init; }

    /// <summary>GitHub 에 로그인한 사용자 이름(표시용). 토큰이 아니다.</summary>
    [JsonPropertyName("sync_login")]
    public string? SyncLogin { get; init; }

    /// <summary>마지막으로 동기화에 성공한 시각(표시용).</summary>
    [JsonPropertyName("sync_last_at")]
    public DateTimeOffset? SyncLastAt { get; init; }

    /// <summary>시작할 때 독 옆에 사용 팁을 보인다(기본 켜짐). 로그인할 때 자동으로 시작한 경우에는 켜져 있어도 보이지 않는다.</summary>
    [JsonPropertyName("startup_tips")]
    public bool StartupTips { get; set; } = true;   // init 이 아니다 — 아래 설명

    /// <summary>다음에 보일 팁의 차례(<c>Tips.StartupTips.Rotation</c> 의 위치).</summary>
    [JsonPropertyName("startup_tips_next")]
    public int StartupTipsNext { get; init; }

    /// <summary>독에 한 번이라도 놓아 보았다 — 그 전에는 "콩에 끌어다 놓으세요" 팁만 보인다.</summary>
    [JsonPropertyName("dock_dropped_once")]
    public bool DockDroppedOnce { get; init; }

    /// <summary>독에 놓아 연 작업을 최근 10개까지 이 PC 에 기록하고, 알약에 마우스를 올리면 보인다(기본 켜짐). 끄면 기록을 지운다.</summary>
    [JsonPropertyName("recent_history")]
    public bool RecentHistory { get; set; } = true;   // init 이 아니다 — 아래 설명

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
