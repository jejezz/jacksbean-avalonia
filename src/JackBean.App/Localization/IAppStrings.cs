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
    string TrayHideDock { get; }
    string TrayShowDock { get; }
    string SettingsSectionBehavior { get; }
    string SettingsHotkey { get; }
    string SettingsHotkeyToggle(string combo);
    string SettingsHotkeyNote { get; }
    string HotkeyFailed(string combo);
    string SettingsStartup { get; }
    string SettingsStartupToggle { get; }
    string SettingsStartupNote { get; }
    string StartupFailed(string error);
    string HomeSyncNow { get; }
    string SettingsSectionSync { get; }
    string SyncTitle { get; }
    string SyncDescription { get; }
    string SyncSignedInAs(string login);
    string SyncSignIn { get; }
    string SyncSignOut { get; }
    string SyncReauth { get; }
    string SyncRepoLabel { get; }
    string SyncRepoNone { get; }
    string SyncRepoCurrent(string repo);
    string SyncChooseRepo { get; }
    string SyncNoSecureStore { get; }
    string GitHubLoginTitle { get; }
    string GitHubLoginWait { get; }
    string GitHubLoginWaiting { get; }
    string GitHubOpenBrowser { get; }
    string GitHubCopyCode { get; }
    string GitHubTokenAdvanced { get; }
    string GitHubTokenHint { get; }
    string GitHubSignInButton { get; }
    string GitHubNoClientId { get; }
    string GitHubLoginFailed(string error);
    string RepoDialogTitle { get; }
    string RepoLoading { get; }
    string RepoNone { get; }
    string RepoAppRepo { get; }
    string RepoPublicWarning { get; }
    string RepoPublicConfirm(string repo);
    string RepoCreateTitle { get; }
    string RepoExistingTitle { get; }
    string RepoCreateButton { get; }
    string RepoConnect { get; }
    string RepoConnected(string repo);
    string RepoFailed(string error);
    string SyncNow { get; }
    string SyncRunning { get; }
    string SyncNever { get; }
    string SyncLast(string when);
    string SyncDone(bool received, bool pushed);
    string SyncFailed(string error);
    string SyncConflicts(int count);
    string SyncNote(JackBean.Domain.Policy.MergeNote note);
    string SyncOsNote { get; }
    string ExtensionGroupName(string id);
    string ViewerFailed(string error);
    string EditorSave { get; }
    string EditorSaveAs { get; }
    string EditorDontSave { get; }
    string EditorUnsavedTitle { get; }
    string EditorUnsavedBody(string name);
    string EditorExternalChangedTitle { get; }
    string EditorExternalChangedBody { get; }
    string EditorOverwrite { get; }
    string EditorSaveFailed(string error);
    string EditorLoadFailed(string error);
    string EditorFind { get; }
    string EditorFindHint { get; }
    string EditorReplaceHint { get; }
    string EditorReplace { get; }
    string EditorReplaceAll { get; }
    string EditorMatchCase { get; }
    string EditorRegex { get; }
    string EditorPrevMatch { get; }
    string EditorNextMatch { get; }
    string EditorNoMatches { get; }
    string EditorMatchCount(int current, int total);
    string EditorWordWrap { get; }
    string EditorGoToLine { get; }
    string EditorGoToLineHint(int max);
    string EditorStatusPosition(int line, int column);
    string EditorStatusSelected(int count);
    string EditorHighlightOff { get; }
    string EditorSaveAsHint { get; }
    string EditorOverwriteExistingBody(string name);
    string EditorReadOnlyBinary { get; }
    string EditorUndo { get; }
    string EditorRedo { get; }
    string EditorMenuView { get; }
    string EditorCut { get; }
    string EditorCopy { get; }
    string EditorPaste { get; }
    string EditorGroupFile { get; }
    string EditorGroupEdit { get; }
    string EditorGroupFind { get; }
    string EditorScClose { get; }
    string EditorScSelectAll { get; }
    string EditorScDeleteLine { get; }
    string EditorTooLarge(string name);
    string PdfViewerFailed(string error);
    string ViewerOpenWithDefaultApp { get; }
    string HomeRestoreDefaults { get; }
    string RestoreDefaultsTitle { get; }
    string RestoreDefaultsBody(int rules, int apps);
    string RestoreDefaultsDone { get; }
    string RestoreDefaultsFailed(string error);
    string ArchiveCompressTitle { get; }
    string ArchiveExtract { get; }
    string ArchiveCompress { get; }
    string ArchiveBrowse { get; }
    string ArchiveItemCount(int count);
    string ArchiveZipName { get; }
    string ArchiveExtractTo { get; }
    string ArchiveSaveIn { get; }
    string ArchiveOpenFolder { get; }
    string ArchiveListingInfo(int files, int folders, string size);
    string ArchiveListTruncated(int max);
    string ArchiveBroken(string error);
    string ArchiveErrAlreadyExists(string name);
    string ArchiveErrUnsafe(string name);
    string ArchiveErrBadName { get; }
    string ArchiveErrNothing { get; }
    string ArchiveErrNoFolder { get; }
    string ArchiveFailed(string error);
    string ArchiveExtracting { get; }
    string ArchiveCompressing { get; }
    string ArchiveExtracted(string folder);
    string ArchiveCompressed(string zip);
    string VideoPlayerFailed(string error);
    string VideoPlayerHint { get; }
    string AudioPlayerFailed(string error);
    string AudioPlayerHint { get; }
}
