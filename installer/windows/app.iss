; Inno Setup script for the Avalonia (NativeAOT) Windows build
; (adapted from jejezz/application-release-templates desktop/ @ conventions-v1).
;
; Run by .github/workflows/release.yml's `build-windows` job on every `vX.Y.Z` tag push, which passes MyAppName /
; MyFileName / MyAppVersion / MyAppNumericVersion / MyAppExeName via `ISCC /D...` (read from Directory.Build.props).
; The fallbacks below are only for a local, manual compile — MyAppVersion 0.0.0 marks such a build.
; Build the app first: powershell -File scripts\publish-aot.ps1 (output: publish\aot).
;
; AppId is this app's own GUID. Never reuse another app's GUID, and never change it after the first release, or
; Windows treats the next version as a different application (breaking upgrade/uninstall). conventions/identity.md §5.

#ifndef MyAppName
  #define MyAppName "Jack’s Bean"
#endif
#ifndef MyFileName
  #define MyFileName "JacksBean"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "0.0.0"
#endif
#ifndef MyAppExeName
  #define MyAppExeName "JacksBean.exe"
#endif
#define MyAppPublisher "Jongyun Ahn"
#define MyAppURL "https://github.com/jejezz/jackbean-avalonia"
; The app's first release year.
#define MyFirstReleaseYear "2026"
#define SourceDir "..\..\publish\aot"

[Setup]
AppId={{A49A6FAE-6D40-4C60-98E0-BFD9900B44DB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) {#MyFirstReleaseYear} {#MyAppPublisher}
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename={#MyFileName}-{#MyAppVersion}-windows-x64-setup
SetupIconFile=..\..\src\JackBean.App\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The app is a tray app that may be running with no visible window, which Setup's own "close applications" detection
; (Restart Manager) misses — so Setup stops it itself (see [Code]) and Setup's own auto-close is turned off.
CloseApplications=no
RestartApplications=no
; No license page: MIT apps have nothing to agree to (conventions/licensing.md §4).

[Languages]
; Installer text comes from Inno Setup's own translations — don't hardcode
; Korean (or English) strings below (conventions/packaging.md §3).
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; Debug symbols (.pdb) are not shipped.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// The app holds a named mutex while it runs (src/JackBean.App/Services/SingleInstance.cs — AppIdentity.Id, keep the two in sync).
const
  AppMutexName = 'art.zoomon.jacksbean.avalonia';

// Stops a running copy before files are replaced or removed: ask it to quit (`--quit`, understood by versions that
// hold the mutex), wait a few seconds, then end whatever is left (older versions had no mutex and no --quit).
procedure StopRunningApp(const ExePath: String);
var
  ResultCode, I: Integer;
begin
  if CheckForMutexes(AppMutexName) and FileExists(ExePath) then
  begin
    Exec(ExePath, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    for I := 1 to 20 do
    begin
      if not CheckForMutexes(AppMutexName) then Break;
      Sleep(250);
    end;
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopRunningApp(ExpandConstant('{app}\{#MyAppExeName}'));
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  StopRunningApp(ExpandConstant('{app}\{#MyAppExeName}'));
end;
