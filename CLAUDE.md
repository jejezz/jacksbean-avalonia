# Jack’s Bean (Avalonia)

Flutter 판(`jacksbean-flutter`, 개발 종료)에서 C#/Avalonia(.NET 10)로 옮겨 온 프로젝트 — 이제 이쪽이 제품이고 Flutter 판에 맞출 필요가 없다(호환 코드는 두지 않는다).
표시 이름 `Jack’s Bean`(U+2019), 식별자 `art.zoomon.jacksbean.avalonia`, 사용자 데이터 폴더 `JacksBean`(정책 파일 `policy.<os>.json`, 설정은 같은 폴더의 `settings.json`).

**구조·스타일은 형제 프로젝트 `../daylight-commander-avalonia`를 따른다.** 새 화면·설정·업데이트를 만들 때 먼저 거기서 같은 것을 찾아 가져온다
(`ThemeService`, `UiScale`, `Strings`/`IAppStrings`, `DialogBase`, `SettingsWindow`, `AboutDialog`, `LicensesWindow`, `WindowPlacementHelper`, `Updates/`).
공통 규약은 `application-release-templates/conventions`(ui-ux, theming, settings, settings-screen, about-dialog, fonts, icons, licensing).

## 구조 (Presentation(App) → Services → Domain ← Data)
- `src/JackBean.Domain` — UI·IO 없는 모델과 규칙: 분류 결과(`Drop/`), 정책 문서·규칙 판정·검사·"항상 열기" 제안(`Policy/`), 독 위치(`Dock/`),
  설정 모델 `AppSettings`(`Settings/`), 색(`Theme/`), 창 위치 계산(`Window/`), `AppIdentity`/`AppVersion`.
- `src/JackBean.Services` — `PathClassifier`, `PolicyStore`(정책 파일), `AppLauncher`(셸 없이 실행), `DropRouter`, `DockExpansion`, `SettingsController`, `Updates/`.
- `src/JackBean.Data` — `JsonSettingsRepository`(소스 생성 JSON, NativeAOT 안전).
- `src/JackBean.App` — Avalonia UI. 창: `DockWindow`(항상 위 독), `HomeWindow`(규칙·앱 보기, 앱 바 44), `Views/SettingsWindow`, `Dialogs/AboutDialog`, `Views/LicensesWindow`.
- 정체성·버전·업데이트 서버는 `Directory.Build.props` 하나(`AppIdentity`/`AppVersion`이 어셈블리 메타데이터로 읽는다). **저장소 URL(`AppIdentity.RepositoryUrl`)은 업데이트 서버가 앱을 아는 이름이다 — 다른 앱 것을 복사해 두면 그 앱의 릴리스를 업데이트로 내놓는다.**

## 알림(스낵바)
오류는 `Notifications/Snackbar.Error(메시지, 원문?)` 로 알린다(어느 스레드에서든). 독 옆에 뜨는 별도 창(`SnackbarWindow`)이 포커스를 가져가지 않고, 오류 10초·안내 4초 뒤 닫히며 마우스를 올려 두면 멈춘다. "복사"는 메시지+원문을 클립보드로 보낸다. 같은 알림이 3초 안에 또 오면 거른다(`Services/Notifications/NotificationGate`). UI 스레드의 처리되지 않은 예외도 여기로 온다(`App`). 독의 실행 실패·정책 경고·규칙 저장 실패가 연결되어 있다.

## 빈 상태와 정책 복구
홈 창은 규칙·앱이 모두 없으면 빈 상태 카드(규약 ui-ux §6)를, 한쪽만 없으면 한 줄 안내를 보인다. 정책 파일 **전체**를 못 읽으면(`PolicyDocument.IsFallback`) 기본 정책을 대신 쓰되 파일은 그대로 두고,
홈 창의 [기본 정책 다시 만들기]가 `PolicyStore.ResetToDefaultsAsync` 로 새로 만든다(이전 파일은 `.broken-날짜시각`으로 옮겨 둔다). 규칙 하나만 건너뛴 경우는 경고만 보이고 복구 버튼은 없다.

## 정책 동기화 (GitHub)
설정 창 "동기화" 구역: **GitHub 로그인 → 저장소 고르기(새로 만들기/기존 선택) → [지금 동기화]**(홈 창 정책 카드에도 같은 버튼). **버튼으로 직접**만 한다(자동 동기화 없음). 같은 방식을 Notes2Hub(`../../FLUTTER/notes2hub-flutter`)가 먼저 쓴다 — 참고: `lib/github/device_flow.dart`, `github_api.dart`, `sync/sync_service.dart`.
- **로그인**(`Services/GitHub`): 브라우저 로그인(Device Flow, `DeviceFlow`) — 코드를 보여 주고 브라우저를 열면 사용자가 승인하고 앱이 토큰을 기다린다. **Client ID 가 있어야 한다**(`AppIdentity.GitHubClientId` ← `Directory.Build.props` 의 `AppGitHubClientId`; 로컬은 git 제외 파일 `Directory.Build.local.props`(견본 `.example`), 배포는 `-p:AppGitHubClientId=`). Client ID 가 없는 빌드는 **토큰(PAT, repo 권한) 입력 로그인만** 보인다. 토큰은 **OS 보안 저장소에만**(`ITokenStore`: Windows 자격 증명 관리자 · macOS 키체인(`security`) · Linux `secret-tool`, 없으면 이번 실행 동안만 메모리) — 설정 파일(`sync_login` = 이름만, `sync_repo` = `소유자/이름`)·로그·정책 파일에는 남기지 않는다.
- **저장소**: 새로 만들기(비공개, `auto_init` — 빈 저장소는 Contents API 로 올리기 어렵다) 또는 계정의 저장소 중 선택. 연결하면 GitHub 토픽 `jacksbean` 을 붙여서 다른 PC 에서 로그인하면 그 저장소가 맨 위에 제안된다. 공개 저장소는 경고 후 확인.
- **동기화**(`GitHubSyncService` → `PolicySync` → `IPolicyRemote`): **git 이 필요 없다** — GitHub REST Contents API 로 저장소 루트의 `policy.<os>.json` 하나를 읽고 쓴다. 읽을 때 받은 sha 를 올릴 때 함께 보내서 그 사이 다른 PC 가 올렸으면 409/422(`GitHubConflictException`) → 다시 읽어 합친다(최대 3번). 흐름: 원격 파일 읽기 → 원격·이 PC·base(`sync/base.<os>.json`, 마지막 동기화 때의 모습)를 `PolicyMerger` 로 **앱·규칙 단위 3-way 합치기** → 이 PC 파일 고치기(`PolicyStore.ReplaceAsync`, 실행 취소 기록을 비운다) → 달라졌으면 올리기 → base 갱신. 어느 쪽 파일이 깨져 있으면 아무것도 쓰지 않는다. 401 은 "다시 로그인"(`NeedsReauth`), 네트워크 실패는 `Offline`.
- OS 별 파일(`policy.<os>.json`)을 각자 동기화한다(같은 OS 끼리 공유). 합치기 규칙: 한쪽만 바뀌면 그쪽, 양쪽이 다르게 바뀌면 `updated` 가 더 최근인 쪽(같으면 이 PC), 한쪽에서 지운 것은 이쪽이 그 뒤로 안 고쳤으면 따라 지우고 고쳤으면 남긴다, base 가 없으면(처음) 합집합. 합친 뒤 어긋난 것은 편집 때와 같은 규칙으로 고친다(확장자는 한 규칙에만, 드라이브·폴더·파일 규칙은 하나씩, 없는 앱은 규칙에서 뺌). 앱도 `updated` 를 가진다.
- 시험은 가짜 GitHub 서버(`FakeGitHub`, HttpMessageHandler)로 한다 — 사용자·저장소·토픽·Contents API(sha 잠금)·Device Flow.

## 트레이·전역 단축키·시작 프로그램
**시작 프로그램**(로그인할 때 자동 시작)은 설정 창 "동작" 구역의 스위치다(`Services/Startup`). 등록 여부는 설정 파일이 아니라 OS 의 등록이 원본이다(스위치가 늘 실제 상태를 보인다): Windows `HKCU\…\Run` 값, macOS `~/Library/LaunchAgents/<앱 ID>.plist`, Linux `~/.config/autostart/jacksbean.desktop`.
실행 명령은 지금 실행 중인 앱의 경로(`LaunchCommand.Current` — 개발 중 `dotnet run` 이면 `dotnet "…dll"`). 등록하지 못하면 알림을 띄우고 스위치를 되돌린다.
알림 영역 아이콘(`Tray/TrayService`, Avalonia `TrayIcon` — macOS 는 메뉴 막대; 그림은 독과 같은 "새싹이 난 콩" `dock_icon.png`)이 항상 있다: 누르면 독 숨기기·보이기, 메뉴는 독 숨기기·보이기 / 홈 / 설정 / 정보 / 종료. 독 메뉴에도 [독 숨기기]가 있다(`DockWindow.ToggleVisible`, 숨겨도 위치는 기억하고 다음 실행에는 다시 보인다).
전역 단축키 **Ctrl+Alt+J**(독 숨기기·보이기)는 설정 창 "동작" 구역의 스위치로 켠다(`global_hotkey`, 기본 꺼짐 — 다른 앱과 겹칠 수 있어서). `Services/Hotkeys`: Windows 만(`WindowsGlobalHotkey` — 전용 스레드에서 `RegisterHotKey` + 메시지 루프), 그 밖의 OS 는 `IsSupported=false` 라 설정 항목이 숨는다(macOS 는 접근성 권한, Linux Wayland 는 막혀 있다). 등록에 실패하면(다른 앱이 쓰는 중) 알림을 띄우고 설정을 다시 끈다(`HotkeyService`). 조합을 바꾸는 화면은 아직 없다.

## 정책 편집(룰 메이커)
홈 창은 **보기 전용**이고 편집은 별도 창에서 한다(JSON 을 직접 고치지 않아도 되게 폼으로). 지금은 **앱 편집 창**(`Views/AppsEditorWindow`: 왼쪽 앱 목록 + 오른쪽 폼 — 이름·실행 파일·인자)만 있고,
홈 창 "앱" 카드의 [앱 편집]과 빈 상태의 [앱 추가]로 연다. 저장·삭제는 `PolicyStore.UpsertAppAsync/RemoveAppAsync`(임시 파일에 쓰고 교체, 사용자가 적은 모르는 키는 보존, 깨진 파일은 쓰지 않음).
앱 ID 는 규칙이 가리키는 이름이라 만들 때만 정하고 바꾸지 않으며(`AppRules`), 지울 때 그 앱을 쓰는 규칙은 지우지 않고 그 규칙의 앱 목록에서만 뺀다(앱이 하나도 안 남으면 그 규칙은 운영체제 기본 방식으로 열린다). 정책을 바꾸면 `App.NotifyPolicyChanged()` 로 홈 창 등이 다시 읽는다.
**규칙 편집 창**(`Views/RulesEditorWindow`, 홈 창 "규칙" 카드의 [규칙 편집]으로 연다)은 네 가지 대상 — 드라이브 · 폴더 · 파일 형식 · 그 밖의 파일 — 을 왼쪽 목록으로 고르고, 대상마다 연결한 앱(0~10개, 순서 바꾸기·빼기·등록된 앱에서 추가)을 고친다. 등록되지 않은 앱은 [다른 앱 찾아보기…]로 파일을 골라 바로 연결할 수 있다 — 이름은 파일 이름에서 정하고(`AppRules.SuggestId`), 같은 실행 파일이 이미 있으면 그 앱을 쓰며, 새 앱은 규칙을 **저장할 때** 정책의 `apps` 에 등록된다(저장하지 않으면 남지 않는다).
파일 형식 구역은 확장자 규칙 목록 + 묶음 칩(누르면 그 묶음의 확장자가 들어가고 빠진다) + 확장자 입력칸이며, 저장 전에 `RuleCheck`로 겹침·한도·없는 앱을 보여 준다(저장할 때 서비스가 한 번 더 막는다). 규칙 ID 는 사용자가 정하지 않는다(묶음과 똑같으면 묶음 이름, 아니면 `ext-첫확장자`). 규칙 모델은 아래.

**실행 취소(한 단계)**: 편집 창의 저장·삭제와 "항상 이렇게 열기"는 `PolicyStore.UndoAsync` 로 되돌릴 수 있다(앱을 끌 때까지, 마지막 변경 하나). 알림에 [실행 취소]가 붙고(`PolicyNotices.Saved`), 홈 창 정책 카드에 [마지막 변경 되돌리기]가 있다. 한 번 더 누르면 다시 실행. 그 뒤 파일을 손으로 고쳤으면 그 수정을 덮어쓰지 않도록 거절하고, [기본 정책 다시 만들기]는 기록을 비운다.

## 규칙 모델 (정책 파일 schema 2)
```
{ "schema": 2, "os": "windows",
  "apps":  { "<앱 ID>": { "command": "...", "args": ["{path}"] } },
  "rules": [ { "id": "...", "when": { "kind": "drive" | "folder" | "file" | "extension", "extensions": ["png"] }, "open": ["<앱 ID>", ...] } ] }
```
대상은 **네 가지**(`RuleKind`): 드라이브 · 폴더 · 그 밖의 파일(`file` = 기본 앱) · 확장자 묶음(`extension`). 드라이브·폴더·파일 규칙은 하나씩만, 확장자는 어느 규칙에서든 한 번만 나올 수 있다(`RuleCheck`가 저장을 막는다).
**Git 저장소 규칙은 없다**: 폴더는 폴더 하나, 저장소를 어떻게 다룰지는 지정한 앱이 정한다. 이름 패턴 규칙도 없다.
규칙 하나는 앱을 0~10개 가진다(`Rule.Choices` ← JSON `open` 배열, `PolicyLimits.MaxAppsPerRule`): 1개면 바로 열고, 2개 이상이면 **그 앱들만** 독에서 고르게 하고(`Ask.Candidates` → `NeedsChoice.Candidates` → `RoutePlan.ChoiceApps()`, 여러 항목이면 교집합), 0개면 **운영체제 기본 방식**으로 연다 — 카탈로그의 `default`(Windows 탐색기 · macOS Finder(`open`) · Linux `xdg-open`, `PolicyDocument.DefaultApp`)이고 정책의 `apps` 에 등록하지 않아도 늘 쓸 수 있다. 어느 규칙에도 안 맞는 항목도 마찬가지라 아무 설정이 없어도 OS 가 하는 대로 동작한다. 등록된 앱 전부를 보여 주며 묻지는 않는다(폴더에 pdf 앱이 뜨면 안 된다). 항목마다 겹치는 앱이 없으면 독이 "종류별로 따로 놓아 달라"고 알린다. 기본 정책은 드라이브 → 파일 관리자, 폴더 → 파일 관리자·터미널 중에서 고르기(있는 OS 만), 파일 → 기본 앱이다.
정책의 규칙은 255개까지(`PolicyLimits.MaxRules`) — 읽을 때는 넘어도 지우지 않고 경고만, 편집할 때만 막는다. 모양이 틀린 규칙·앱은 건너뛰고 경고(`PolicyDocument.Warnings`), schema 가 다르면 파일 전체를 못 읽은 것으로 보고 기본 정책을 쓴다(`IsFallback`, 홈 창의 [기본 정책 다시 만들기]).
확장자 묶음은 `ExtensionGroups`(이미지·음악·영상·PDF·문서·스프레드시트·프레젠테이션·압축·텍스트·코드, 한 확장자는 한 묶음에만) — 저장은 확장자 목록이라 묶음 정의가 바뀌어도 저장한 규칙은 그대로다.
저장할 때 규칙은 구체적인 것이 위로 정렬된다(`RuleOrder`: 확장자 → 드라이브·폴더 → 그 밖의 파일). `PolicyStore` 의 `UpsertRuleAsync`/`RemoveRuleAsync`(편집), `RememberAsync`("항상 이렇게 열기": 드라이브·폴더는 있는 규칙의 앱을 바꾸고, 확장자는 다른 규칙에서 빼서 `auto-ext-확장자` 로 옮긴다)가 이것을 한다. 앱을 지우면 규칙은 지우지 않고 그 규칙의 앱 목록에서만 빠진다.

## 멀티 윈도우와 내장 도구
- 독은 항상 떠 있고, 홈·설정·도구는 **별도 창**이다. 종료는 독 메뉴의 "종료"(`ShutdownMode.OnExplicitShutdown`).
  **알약을 펼쳐도 콩은 제자리**: 콩은 정원일 때의 자리(`_idleLeft`)에 그대로 있고 창은 콩의 반대쪽으로 자란다(`GrowsRight`: 화면 왼쪽 절반이면 오른쪽으로 `콩 글자`, 오른쪽 절반이면 왼쪽으로 `글자 콩`). 저장하는 독 위치(`dock_position.right`)는 정원 크기일 때의 오른쪽 끝이다.
- 정책의 앱 `command`가 `jackbean:<id>`면 프로세스를 띄우지 않고 `ToolRegistry`가 `IBuiltInTool.CreateWindow(path)`로 창을 연다(파일 하나당 창 하나).
  텍스트 에디터·PDF 뷰어·압축 도구는 `IBuiltInTool`을 구현해 `App.OnFrameworkInitializationCompleted`에서 `Tools.Register`한다. 도구: `image-viewer`, `text-editor`(AvaloniaEdit + TextMate 구문 강조, 5MB 한도, UTF-8·BOM·줄바꿈 보존, 찾기·바꾸기·줄 이동; 파일 로직은 `Services/Text`). 같은 도구로 같은 파일을 다시 열면 열려 있는 창을 앞으로 가져온다(`ToolRegistry`).
  **도구 공통 조건**: 등록은 가벼운 팩토리만, 무거운 초기화·스타일 로드(`EditorStyles`)는 `CreateWindow` 안에서 — 안 쓸 때 메모리·시작 시간이 늘지 않아야 한다. 정책에 연결하려면 앱의 `command` 를 `jackbean:text-editor` 로 적는다— **내장 도구는 정책 파일에 적지 않아도 앱 목록에 늘 있다**(`KnownApps.BuiltInTools` → `PolicyDocument.Apps` 가 더함, `IsImplicitApp`; 합치기 복구도 이 앱을 있는 것으로 본다). 새 기본 정책은 이미지(`png jpg jpeg gif webp bmp ico`) → 이미지 뷰어, 텍스트·코드 묶음 → 에디터 규칙(`images` `text` `code`)을 포함한다(이미 있는 정책 파일은 그대로 — 새로 만들거나 [기본 정책 다시 만들기]를 할 때만).
  형제 앱에 에디터(`EditorWindow`)·뷰어(`ViewerWindow`)·압축(`ArchiveService`)이 이미 있으니 새로 만들지 말고 가져온다.

## 규약 적용
- **색·글꼴·크기는 리소스로만 읽는다**: `AppBg AppSurface AppSurfaceHi AppStroke AppStrokeStrong AppTextHi AppTextMid AppPrimary AppOnPrimary AppAccent AppDanger AppCursor AppHover`,
  `AppFont`, `FsBody FsMid FsSmall FsTitle`(글자 크기 배율 적용). `{DynamicResource}`/`GetResourceObservable`로 읽고 색 값을 직접 쓰지 않는다. 적용은 `ThemeService.Apply(settings)`.
- **문구**: `IAppStrings`(인터페이스 → `KoreanStrings` → `EnglishStrings` 순으로 모두 추가, 빠뜨리면 컴파일 오류). 코드는 `Strings.Current`, XAML은 `{DynamicResource Str.*}`(새 문구를 XAML에서 쓰면 `Strings.PublishResources`에 더한다).
- **글꼴(fonts.md)**: SeoulNamsan 400/700/800 번들 + 설정에서 시스템 글꼴 선택. 파일 이름·경로는 `user` 클래스(시스템 글꼴), 고정폭은 `mono`.
- **아이콘(icons.md)**: Phosphor Duotone 하나(`Icons/DuoIcon`). 쓸 아이콘은 `scripts/gen-phosphor-icons.py`의 `ICONS`에 더하고 다시 돌린다(`PhosphorIcons.g.cs`는 직접 고치지 않는다). 단, `+` `x` `✓` 같은 단순 기호는 18px 에서 채움 사각형이 윤곽을 흐리므로 `OUTLINE_ONLY` 에 넣어 **윤곽 층만** 쓴다(규약 "Duotone 하나로 통일"의 이 앱 한정 예외 — 같은 duotone 파일의 윤곽 층이라 선 모양은 같다).
- **설정**: 저장 키 `theme_mode accent_palette text_scale app_locale ui_font_family window_placement dock_position global_hotkey sync_repo sync_login sync_last_at`. `SettingsController.Update(...)`로 바꾸면 바로 적용·(모아서)저장되고 열린 모든 창이 따라온다.
- **창 위치**: 홈 창은 `PlacementMath`로 기억·복원(window-placement.md), 새 독립 창은 `WindowPlacementHelper.OpenNear`.
- **창 제목**: 홈 창(앱 창)은 `Jack’s Bean`, 그 밖의 보조 창·대화상자는 항상 `창 이름 — Jack’s Bean`(`WindowTitle.Of`). 새 창을 만들면 이것으로 제목을 정한다.
- **단축키**: 설정 ⌘,/Ctrl+, · 정보 F1 · 새로 고침 F5/⌘R · 창 닫기 ⌘W · 대화상자 Esc. macOS 메뉴 막대에 정보·설정·종료.
- **업데이트**: 형제 앱과 같은 흐름(`Services/Updates` + `App/Updates`). 시작 5초 뒤 하루 1회 자동 확인, 정보 창의 [업데이트 확인]. 서버 주소는 `AppUpdateServer`(`-p:AppUpdateServer=`로 바꿈, 빈 값이면 끔).

## 빌드·테스트·실행
```
dotnet build JackBean.slnx
dotnet test JackBean.slnx
dotnet run --project src/JackBean.App            # --home --settings --about 로 그 창도 함께 띄움
./scripts/publish-aot.sh                          # NativeAOT 게시 → publish/aot/JacksBean (의존성을 더할 때마다 확인)
HOME=$(mktemp -d) dotnet src/JackBean.App/bin/Debug/net10.0/JacksBean.dll --snapshot <폴더>   # 모드·색·크기·언어를 바꿔 가며 창을 PNG 로 저장
```
테스트 299개(Domain 106 · Services 183 · Data 10). 앱이 실행 중이면 실행 파일이 잠기므로 `dotnet test tests/<프로젝트>`로 개별 실행.

## 확인 상태
- 설정·정보·홈 창은 `--snapshot` PNG 로 라이트/다크/고대비/색 구성/영어/글자 크기를 확인했고(열린 창에 즉시 반영), NativeAOT 게시본이 뜨고 정책 파일을 만드는 것까지 확인했다.
- **못 본 것**: 실제 파일 드롭·물어보기 목록, 독 끌기·위치 기억, 여러 모니터, Windows/Linux, 업데이트 흐름(서버에 이 앱이 아직 없다), 설정 창 좁은 배치(<700).

## 아직 없는 것
정책 GitHub 동기화, 트레이·전역 단축키, 독 시작 시 다른 앱의 드래그 감지,
앱 아이콘의 소품 글리프 교체, 데모 GIF. 설치 파일(`installer/`)·릴리스 워크플로(`.github/workflows/release.yml`)·README 는 작성했지만 Windows AOT 게시만 확인했고 Inno/Linux/macOS/워크플로는 아직 돌려 보지 않았다.
