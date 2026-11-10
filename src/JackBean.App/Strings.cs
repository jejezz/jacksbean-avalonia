using System.Globalization;
using Avalonia;
using JackBean.App.Localization;
using JackBean.Domain.Settings;

namespace JackBean.App;

/// <summary>
/// 지금 쓰는 언어의 문구. 코드는 <see cref="Current"/>로 읽고, XAML은 <c>{DynamicResource Str.SettingsTooltip}</c>처럼 읽는다.
/// <see cref="Apply"/>가 언어를 바꾸면 리소스를 새로 채우므로 열려 있는 모든 창의 XAML 문구가 바로 바뀌고,
/// 코드가 만든 문구는 <see cref="Changed"/>를 받아 다시 만든다.
/// </summary>
static class Strings
{
    public static IAppStrings Current { get; private set; } = KoreanStrings.Instance;

    /// <summary>언어가 바뀌었다. 코드가 만들어 둔 문구를 다시 만들어야 한다.</summary>
    public static event Action? Changed;

    /// <summary>설정의 언어(없으면 시스템)를 풀어 적용한다. 시스템은 OS가 한국어일 때만 한국어, 그 밖에는 영어.</summary>
    public static void Apply(AppLocale? setting)
    {
        var locale = JackBean.Domain.Settings.Localization.Resolve(setting, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        Current = locale == AppLocale.Korean ? KoreanStrings.Instance : EnglishStrings.Instance;
        PublishResources();
        Changed?.Invoke();
    }

    /// <summary>XAML이 읽는 문구를 앱 리소스에 올린다 (<c>Str.&lt;이름&gt;</c>).</summary>
    static void PublishResources()
    {
        if (Application.Current is not { } app) return;
        var s = Current;
        void Set(string key, string value) => app.Resources["Str." + key] = value;

        Set("AboutClose", s.AboutClose);
        Set("AboutDescription", s.AboutDescription);
        Set("AboutOpenSourceLicenses", s.AboutOpenSourceLicenses);
        Set("AboutRepository", s.AboutRepository);
        Set("AboutTagline", s.AboutTagline);
        Set("AboutTooltip", s.AboutTooltip);
        Set("CommonCancel", s.CommonCancel);
        Set("CommonConfirm", s.CommonConfirm);
        Set("DockChooseCancel", s.DockChooseCancel);
        Set("DockChooseTitle", s.DockChooseTitle);
        Set("DockDropHint", s.DockDropHint);
        Set("DockDropRelease", s.DockDropRelease);
        Set("DockMenuOpenWindow", s.DockMenuOpenWindow);
        Set("DockMenuQuit", s.DockMenuQuit);
        Set("DockRuleSaved", s.DockRuleSaved);
        Set("DockSuggestDrive", s.DockSuggestDrive);
        Set("DockSuggestFolder", s.DockSuggestFolder);
        Set("DropKindDrive", s.DropKindDrive);
        Set("DropKindFile", s.DropKindFile);
        Set("DropKindFolder", s.DropKindFolder);
        Set("DropKindMissing", s.DropKindMissing);
        Set("DropKindMixed", s.DropKindMixed);
        Set("FontSearchHint", s.FontSearchHint);
        Set("HomeApps", s.HomeApps);
        Set("HomeAsk", s.HomeAsk);
        Set("HomePolicyFile", s.HomePolicyFile);
        Set("HomeReload", s.HomeReload);
        Set("HomeRules", s.HomeRules);
        Set("HomeWarnings", s.HomeWarnings);
        Set("LanguageSystemShort", s.LanguageSystemShort);
        Set("LicensesNote", s.LicensesNote);
        Set("LicensesProject", s.LicensesProject);
        Set("LicensesTitle", s.LicensesTitle);
        Set("PaletteAmber", s.PaletteAmber);
        Set("PaletteHighContrast", s.PaletteHighContrast);
        Set("PaletteRose", s.PaletteRose);
        Set("PaletteSaturn", s.PaletteSaturn);
        Set("PaletteSlate", s.PaletteSlate);
        Set("PaletteTeal", s.PaletteTeal);
        Set("SettingsFont", s.SettingsFont);
        Set("SettingsFontSize", s.SettingsFontSize);
        Set("SettingsFontSizeHint", s.SettingsFontSizeHint);
        Set("SettingsLanguage", s.SettingsLanguage);
        Set("SettingsMode", s.SettingsMode);
        Set("SettingsModeSystem", s.SettingsModeSystem);
        Set("SettingsPalette", s.SettingsPalette);
        Set("SettingsSectionAppearance", s.SettingsSectionAppearance);
        Set("SettingsTooltip", s.SettingsTooltip);
        Set("ThemeDark", s.ThemeDark);
        Set("ThemeLight", s.ThemeLight);
        Set("UpdateAvailableTitle", s.UpdateAvailableTitle);
        Set("UpdateBusyBody", s.UpdateBusyBody);
        Set("UpdateBusyTitle", s.UpdateBusyTitle);
        Set("UpdateCancel", s.UpdateCancel);
        Set("UpdateCheckButton", s.UpdateCheckButton);
        Set("UpdateCheckFailedTitle", s.UpdateCheckFailedTitle);
        Set("UpdateChecking", s.UpdateChecking);
        Set("UpdateDownloading", s.UpdateDownloading);
        Set("UpdateErrorChecksum", s.UpdateErrorChecksum);
        Set("UpdateErrorGeneric", s.UpdateErrorGeneric);
        Set("UpdateErrorInstall", s.UpdateErrorInstall);
        Set("UpdateErrorNetwork", s.UpdateErrorNetwork);
        Set("UpdateFailedTitle", s.UpdateFailedTitle);
        Set("UpdateLater", s.UpdateLater);
        Set("UpdateLinuxInstallTitle", s.UpdateLinuxInstallTitle);
        Set("UpdateMacosOpenedTitle", s.UpdateMacosOpenedTitle);
        Set("UpdateNow", s.UpdateNow);
        Set("UpdateOpenReleasePage", s.UpdateOpenReleasePage);
        Set("UpdateQuitAndInstall", s.UpdateQuitAndInstall);
        Set("UpdateReleaseNotes", s.UpdateReleaseNotes);
        Set("UpdateSkipVersion", s.UpdateSkipVersion);
        Set("UpdateUnavailableTitle", s.UpdateUnavailableTitle);
        Set("UpdateUpToDateTitle", s.UpdateUpToDateTitle);
        Set("UpdateVerifying", s.UpdateVerifying);
        Set("UpdateWindowsInstallTitle", s.UpdateWindowsInstallTitle);
    }
}
