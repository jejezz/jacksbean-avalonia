namespace JackBean.App.Localization;

/// <summary>
/// 화면 문구 (한국어가 기준, 영어 필수 — localization.md). 새 문구는 이 인터페이스 → 한국어 → 영어 순으로 모두 더한다.
/// 빠뜨리면 컴파일 오류라서 번역 누락이 CI 전에 막힌다. 이름은 PascalCase에 화면 이름을 접두어로 붙인다.
/// </summary>
public interface IAppStrings
{
    string AboutClose { get; }
    string SettingsFontSizeHint { get; }
    string SettingsWindowTitle(string appName);
    string UpdateChecking { get; }
    string AboutOpenSourceLicenses { get; }
    string AboutRepository { get; }
    string AboutVersion(string version, int build);
    string FontDefaultOption(string name);
    string FontSearchHint { get; }
    string LanguageSystemShort { get; }
    string LicensesNote { get; }
    string LicensesProject { get; }
    string LicensesTitle { get; }
    string PaletteAmber { get; }
    string PaletteHighContrast { get; }
    string PaletteRose { get; }
    string PaletteSaturn { get; }
    string PaletteSlate { get; }
    string PaletteTeal { get; }
    string SettingsFont { get; }
    string SettingsFontSize { get; }
    string SettingsLanguage { get; }
    string SettingsMode { get; }
    string SettingsModeSystem { get; }
    string SettingsPalette { get; }
    string SettingsSectionAppearance { get; }
    string ThemeDark { get; }
    string ThemeLight { get; }
    string UpdateAvailableBody(string appName, string current, string latest);
    string UpdateAvailableTitle { get; }
    string UpdateBusyBody { get; }
    string UpdateBusyTitle { get; }
    string UpdateCancel { get; }
    string UpdateCheckButton { get; }
    string UpdateCheckFailedTitle { get; }
    string UpdateDownloadProgress(string received, string total);
    string UpdateDownloading { get; }
    string UpdateErrorChecksum { get; }
    string UpdateErrorGeneric { get; }
    string UpdateErrorInstall { get; }
    string UpdateErrorNetwork { get; }
    string UpdateFailedTitle { get; }
    string UpdateLater { get; }
    string UpdateLinuxInstallBody(string appName);
    string UpdateLinuxInstallTitle { get; }
    string UpdateMacosOpenedBody(string appName);
    string UpdateMacosOpenedTitle { get; }
    string UpdateNow { get; }
    string UpdateOpenReleasePage { get; }
    string UpdateQuitAndInstall { get; }
    string UpdateQuitApp(string appName);
    string UpdateReleaseNotes { get; }
    string UpdateSkipVersion { get; }
    string UpdateUnavailableBody(string latest);
    string UpdateUnavailableTitle { get; }
    string UpdateUpToDateBody(string version);
    string UpdateUpToDateTitle { get; }
    string UpdateVerifying { get; }
    string UpdateWindowsInstallBody(string appName);
    string UpdateWindowsInstallTitle { get; }
    string CommonCancel { get; }
    string CommonConfirm { get; }
    string SettingsTooltip { get; }
    string AboutTooltip { get; }
    string AboutTagline { get; }
    string AboutDescription { get; }
    string DockDropHint { get; }
    string DockDropRelease { get; }
    string DropKindDrive { get; }
    string DropKindFolder { get; }
    string DropKindFile { get; }
    string DropKindMixed { get; }
    string DropKindMissing { get; }
    string DropSummary(string kind, int count);
    string DockMenuOpenWindow { get; }
    string DockMenuQuit { get; }
    string DockOpenedWith(string app);
    string DockOpenFailed(string error);
    string DockAppMissing(string app);
    string DockChooseTitle { get; }
    string DockChooseCancel { get; }
    string DockAlwaysOpen(string what);
    string DockSuggestExtension(string ext);
    string DockSuggestFolder { get; }
    string DockSuggestDrive { get; }
    string DockRuleSaved { get; }
    string DockRuleSaveFailed(string error);
    string SnackbarCopy { get; }
    string SnackbarCopied { get; }
    string SnackbarClose { get; }
    string ErrorUnexpected { get; }
    string PolicyWarningsFound { get; }
    string HomeRules { get; }
    string HomeApps { get; }
    string HomeWarnings { get; }
    string HomePolicyFile { get; }
    string HomeReload { get; }
    string HomeAsk { get; }
    string HomeEmpty { get; }
    string HomeOpenPolicyFile { get; }
    string HomeNoRules { get; }
    string HomeNoApps { get; }
    string HomeFallbackNote { get; }
    string HomeResetPolicy { get; }
    string HomeResetDone(string? backup);
    string HomeResetFailed(string error);
    string DockNoCommonApp { get; }
    string SnackbarOpenHome { get; }
    string HomeEditApps { get; }
    string AppsEditorTitle { get; }
    string AppsEditorList { get; }
    string AppsEditorAdd { get; }
    string AppsEditorNewApp { get; }
    string FieldAppId { get; }
    string FieldAppIdHint { get; }
    string FieldAppCommand { get; }
    string FieldAppCommandHint { get; }
    string FieldAppBrowse { get; }
    string FieldAppArgs { get; }
    string FieldAppArgsHint { get; }
    string AppArgsNoPath { get; }
    string AppSave { get; }
    string AppDelete { get; }
    string AppRevert { get; }
    string AppErrIdEmpty { get; }
    string AppErrIdInvalid { get; }
    string AppErrIdTaken { get; }
    string AppErrCommandEmpty { get; }
    string AppSaved(string id);
    string AppSaveFailed(string error);
    string AppDeleteTitle { get; }
    string AppDeleteConfirm(string id, int rules);
    string AppDeleted(string id);
    string AppDeleteFailed(string error);
    string AppDiscardConfirm { get; }
    string AppDiscard { get; }
    string HomeEditRules { get; }
    string RulesEditorTitle { get; }
    string RuleSectionDrive { get; }
    string RuleSectionFolder { get; }
    string RuleSectionFiles { get; }
    string RuleSectionDefault { get; }
    string RuleDescDrive { get; }
    string RuleDescFolder { get; }
    string RuleDescFiles { get; }
    string RuleDescDefault { get; }
    string RuleCount(int count, int max);
    string RuleAppsTitle(int count, int max);
    string RuleAppsAdd { get; }
    string RuleAppsNoneNote { get; }
    string RuleAppsOneNote { get; }
    string RuleAppsManyNote { get; }
    string RuleAppMoveUp { get; }
    string RuleAppMoveDown { get; }
    string RuleAppRemove { get; }
    string RuleAppMissing(string id);
    string RuleExtGroups { get; }
    string RuleExtList { get; }
    string RuleExtHint { get; }
    string RuleExtAdd { get; }
    string RuleExtNew { get; }
    string RuleExtEmpty { get; }
    string RuleDelete { get; }
    string RuleDeleteTitle { get; }
    string RuleDeleteConfirm(string name);
    string RuleErrNoExt { get; }
    string RuleErrExtTaken(string ext, string other);
    string RuleErrLimit { get; }
    string RuleErrTooMany { get; }
    string RuleErrUnknownApp(string id);
    string RuleSaved(string name);
    string RuleSaveFailed(string error);
    string RuleDeleted(string name);
    string RuleDeleteFailed(string error);
    string RuleAppsBrowse { get; }
    string RuleAppsBrowseTitle { get; }
    string RuleAppPending(string id);
    string SnackbarUndo { get; }
    string HomeUndo { get; }
    string PolicyUndone { get; }
    string PolicyUndoFailed(string error);
    string ExtensionGroupName(string id);
    string ViewerFailed(string error);
}
