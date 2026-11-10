# Jack’s Bean (Avalonia)

Flutter 판 `jacksbean-flutter`를 C#/Avalonia(.NET 10)로 옮기는 프로젝트. 표시 이름 `Jack’s Bean`(U+2019), 식별자 `art.zoomon.jacksbean.avalonia`,
사용자 데이터 폴더는 Flutter 판과 같은 `JacksBean`(정책 파일 `policy.<os>.json`을 이어 쓴다. 설정은 같은 폴더의 `settings.json`).

**구조·스타일은 형제 프로젝트 `../daylight-commander-avalonia`를 따른다.** 새 화면·설정·업데이트를 만들 때 먼저 거기서 같은 것을 찾아 가져온다
(`ThemeService`, `UiScale`, `Strings`/`IAppStrings`, `DialogBase`, `SettingsWindow`, `AboutDialog`, `LicensesWindow`, `WindowPlacementHelper`, `Updates/`).
공통 규약은 `application-release-templates/conventions`(ui-ux, theming, settings, settings-screen, about-dialog, fonts, icons, licensing).

## 구조 (Presentation(App) → Services → Domain ← Data)
- `src/JackBean.Domain` — UI·IO 없는 모델과 규칙: 분류 결과(`Drop/`), 정책 문서·규칙 판정·포팅·"항상 열기" 제안(`Policy/`), 독 위치(`Dock/`),
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

## 정책 편집(룰 메이커)
홈 창은 **보기 전용**이고 편집은 별도 창에서 한다(JSON 을 직접 고치지 않아도 되게 폼으로). 지금은 **앱 편집 창**(`Views/AppsEditorWindow`: 왼쪽 앱 목록 + 오른쪽 폼 — 이름·실행 파일·인자)만 있고,
홈 창 "앱" 카드의 [앱 편집]과 빈 상태의 [앱 추가]로 연다. 저장·삭제는 `PolicyStore.UpsertAppAsync/RemoveAppAsync`(임시 파일에 쓰고 교체, 사용자가 적은 모르는 키는 보존, 깨진 파일은 쓰지 않음).
앱 ID 는 규칙이 가리키는 이름이라 만들 때만 정하고 바꾸지 않으며(`AppRules`), 지울 때 그 앱을 쓰는 규칙은 지우지 않고 "물어보기"로 바꾼다. 정책을 바꾸면 `App.NotifyPolicyChanged()` 로 홈 창 등이 다시 읽는다.
규칙 편집 창은 아직 없다(ROADMAP 3-2).

## 멀티 윈도우와 내장 도구
- 독은 항상 떠 있고, 홈·설정·도구는 **별도 창**이다. 종료는 독 메뉴의 "종료"(`ShutdownMode.OnExplicitShutdown`).
- 정책의 앱 `command`가 `jackbean:<id>`면 프로세스를 띄우지 않고 `ToolRegistry`가 `IBuiltInTool.CreateWindow(path)`로 창을 연다(파일 하나당 창 하나).
  텍스트 에디터·PDF 뷰어·압축 도구는 `IBuiltInTool`을 구현해 `App.OnFrameworkInitializationCompleted`에서 `Tools.Register`한다. 첫 도구: `image-viewer`.
  형제 앱에 에디터(`EditorWindow`)·뷰어(`ViewerWindow`)·압축(`ArchiveService`)이 이미 있으니 새로 만들지 말고 가져온다.

## 규약 적용
- **색·글꼴·크기는 리소스로만 읽는다**: `AppBg AppSurface AppSurfaceHi AppStroke AppStrokeStrong AppTextHi AppTextMid AppPrimary AppOnPrimary AppAccent AppDanger AppCursor AppHover`,
  `AppFont`, `FsBody FsMid FsSmall FsTitle`(글자 크기 배율 적용). `{DynamicResource}`/`GetResourceObservable`로 읽고 색 값을 직접 쓰지 않는다. 적용은 `ThemeService.Apply(settings)`.
- **문구**: `IAppStrings`(인터페이스 → `KoreanStrings` → `EnglishStrings` 순으로 모두 추가, 빠뜨리면 컴파일 오류). 코드는 `Strings.Current`, XAML은 `{DynamicResource Str.*}`(새 문구를 XAML에서 쓰면 `Strings.PublishResources`에 더한다).
- **글꼴(fonts.md)**: SeoulNamsan 400/700/800 번들 + 설정에서 시스템 글꼴 선택. 파일 이름·경로는 `user` 클래스(시스템 글꼴), 고정폭은 `mono`.
- **아이콘(icons.md)**: Phosphor Duotone 하나(`Icons/DuoIcon`). 쓸 아이콘은 `scripts/gen-phosphor-icons.py`의 `ICONS`에 더하고 다시 돌린다(`PhosphorIcons.g.cs`는 직접 고치지 않는다). 단, `+` `x` `✓` 같은 단순 기호는 18px 에서 채움 사각형이 윤곽을 흐리므로 `OUTLINE_ONLY` 에 넣어 **윤곽 층만** 쓴다(규약 "Duotone 하나로 통일"의 이 앱 한정 예외 — 같은 duotone 파일의 윤곽 층이라 선 모양은 같다).
- **설정**: 저장 키 `theme_mode accent_palette text_scale app_locale ui_font_family window_placement dock_position`. `SettingsController.Update(...)`로 바꾸면 바로 적용·(모아서)저장되고 열린 모든 창이 따라온다.
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
테스트 167개(Domain 67 · Services 92 · Data 8). 앱이 실행 중이면 실행 파일이 잠기므로 `dotnet test tests/<프로젝트>`로 개별 실행.

## 확인 상태
- 설정·정보·홈 창은 `--snapshot` PNG 로 라이트/다크/고대비/색 구성/영어/글자 크기를 확인했고(열린 창에 즉시 반영), NativeAOT 게시본이 뜨고 정책 파일을 만드는 것까지 확인했다.
- **못 본 것**: 실제 파일 드롭·물어보기 목록, 독 끌기·위치 기억, 여러 모니터, Windows/Linux, 업데이트 흐름(서버에 이 앱이 아직 없다), 설정 창 좁은 배치(<700).

## 아직 없는 것
룰 메이커 중 규칙 편집(앱 편집은 있다), 정책 GitHub 동기화, 트레이·전역 단축키, 독 시작 시 다른 앱의 드래그 감지,
설치 파일·릴리스 워크플로·README, 앱 아이콘의 소품 글리프 교체.
