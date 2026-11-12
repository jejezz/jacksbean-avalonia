using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using JackBean.App.Notifications;
using JackBean.App.Tools;
using JackBean.App.Tray;
using JackBean.App.Updates;
using JackBean.App.Views;
using JackBean.App.Windows;
using JackBean.Data;
using JackBean.Domain;
using JackBean.Domain.Policy;
using JackBean.Domain.Settings;
using JackBean.Domain.Tips;
using JackBean.Services;
using JackBean.Services.DragWatch;
using JackBean.Services.GitHub;
using JackBean.Services.Recent;
using JackBean.Services.Startup;
using JackBean.Services.Sync;
using JackBean.Services.Updates;

namespace JackBean.App;

public partial class App : Application
{
    /// <summary>앱 설정의 단일 출처. 모든 창이 이것을 읽고 바꾼다.</summary>
    public static SettingsController Settings { get; private set; } = null!;

    /// <summary>이 OS의 정책 파일과 드롭 처리. 창이 여러 개여도 정책·도구 레지스트리는 하나다.</summary>
    internal static DropRouter Router { get; private set; } = null!;

    internal static PolicyStore PolicyStore { get; private set; } = null!;

    /// <summary>정책 파일을 앱 안에서 바꿨다(편집 창의 저장·삭제, 복구). 홈 창처럼 정책을 보여 주는 창이 다시 읽는다.</summary>
    internal static event Action? PolicyChanged;

    internal static void NotifyPolicyChanged() => PolicyChanged?.Invoke();

    internal static ToolRegistry Tools { get; private set; } = null!;

    /// <summary>최근 작업(독 알약에 마우스를 올리면 보이는 목록). 이 PC 전용, 설정과 같은 폴더의 `recent.json`.</summary>
    internal static RecentStore Recent { get; private set; } = null!;

    /// <summary>설정에서 켜야 쓰는 선택 도구(VS Code 프로필)를 꺼 둔 상태라 목록에서 숨길 앱인가.</summary>
    internal static bool IsHiddenApp(string id) => id == VsCodeProfileTool.ToolId && !Settings.Value.VsCodeProfileEnabled;

    /// <summary>`jackbean:` 명령을 연다. 선택 도구는 꺼져 있거나 이 OS 에서 쓸 수 없으면 이유를 알리며 실패한다.</summary>
    static Task OpenBuiltIn(string command, IReadOnlyList<string> args)
    {
        if (string.Equals(command, ToolRegistry.Scheme + VsCodeProfileTool.ToolId, StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows()) return Task.FromException(new InvalidOperationException(Strings.Current.CodeToolWindowsOnly));
            if (!Settings.Value.VsCodeProfileEnabled) return Task.FromException(new InvalidOperationException(Strings.Current.CodeToolOff));
        }
        return Tools.OpenAsync(command, args);
    }

    /// <summary>전역 단축키(독 숨기기·보이기). 이 OS 에서 쓸 수 없으면 <see cref="HotkeyService.IsSupported"/> 가 false.</summary>
    internal static HotkeyService Hotkeys { get; private set; } = null!;

    /// <summary>로그인할 때 자동 시작(시작 프로그램). 등록 여부는 OS 에 있는 등록이 원본이다.</summary>
    internal static IStartupRegistration Startup { get; private set; } = null!;

    /// <summary>GitHub 로그인·저장소 연결·정책 동기화(REST, git 불필요). 토큰은 OS 보안 저장소에만 있다.</summary>
    internal static GitHubSyncService Github { get; private set; } = null!;

    /// <summary>동기화가 돌고 있다(버튼을 막는다).</summary>
    internal static bool SyncRunning { get; private set; }

    /// <summary>마지막 동기화 결과를 사람이 읽을 글로(설정 창에 보인다). 이번 실행에서 한 적이 없으면 null.</summary>
    internal static string? LastSyncSummary { get; private set; }

    /// <summary>동기화가 시작되거나 끝났다 — 버튼·안내를 다시 그린다.</summary>
    internal static event Action? SyncChanged;

    private TrayService? _tray;
    private IDragWatcher? _dragWatch;

    /// <summary>업데이트 확인·설치. 서버 주소가 없는 빌드(<c>-p:AppUpdateServer=</c>)나 로컬 빌드는 null.</summary>
    internal static UpdateService? Updates { get; private set; }

    /// <summary>화면에 이미 적용한 설정. 다른 설정(독 위치 등)이 바뀔 때마다 외형을 다시 적용하지 않으려고 둔다.</summary>
    AppSettings? _applied;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var os = PolicyOsExtensions.Current ?? throw new PlatformNotSupportedException("Windows, macOS, Linux 만 지원합니다.");
        // 확인용 --snapshot 은 실제 설정·정책을 건드리지 않는다 — 설정을 이리저리 바꾸고(테마·언어·sync_repo …) 그대로 저장하므로
        // 매번 새 임시 폴더에 쓴다. HOME 을 바꾸는 것으로는 Windows 에서 격리되지 않는다(설정 위치는 APPDATA).
        var snapshot = Environment.GetCommandLineArgs().Contains("--snapshot");
        var settingsFile = snapshot
            ? Path.Combine(Directory.CreateTempSubdirectory("JacksBean-snapshot-").FullName, "settings.json")
            : JsonSettingsRepository.DefaultFile(AppIdentity.FileName);
        Settings = new SettingsController(new JsonSettingsRepository(settingsFile));

        PolicyStore = new PolicyStore(os, Path.GetDirectoryName(settingsFile));
        // --snapshot(확인용)은 진짜 로그인 토큰을 읽거나 건드리지 않는다.
        ITokenStore tokens = snapshot ? new MemoryTokenStore() : TokenStores.Create();
        Github = new GitHubSyncService(Settings, tokens, PolicyStore, Path.Combine(PolicyStore.Directory, "sync"));
        Startup = StartupRegistrations.Create(AppIdentity.FileName, AppIdentity.Id, AppIdentity.DisplayName, LaunchCommand.Current().WithAutostart());
        Recent = new RecentStore(PolicyStore.Directory);
        Settings.Changed += st => { if (!st.RecentHistory) _ = Recent.ClearAsync(); };   // 끄면 기록을 지운다
        Tools = new ToolRegistry();
        Tools.Register(new ImageViewerTool());
        Tools.Register(new TextEditorTool());
        Tools.Register(new PdfViewerTool());
        Tools.Register(new ArchiveTool());
        Tools.Register(new VideoPlayerTool());
        Tools.Register(new AudioPlayerTool());
        if (OperatingSystem.IsWindows()) Tools.Register(new VsCodeProfileTool());   // 설정에서 켠 사람에게만 열린다(OpenBuiltIn)
        // `jackbean:` 로 시작하는 명령은 프로세스를 띄우지 않고 내장 도구가 연다. 나머지는 셸 없이 프로세스로.
        var external = new AppLauncher();
        Router = new DropRouter(PolicyStore, new AppLauncher((command, args) =>
            ToolRegistry.IsBuiltIn(command) ? OpenBuiltIn(command, args) : external.SpawnAsync(command, args)));

        // UI 스레드에서 처리하지 못한 예외로 독이 사라지면 곤란하다 — 알림으로 알리고(원문 복사 가능) 계속 떠 있는다.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Snackbar.Error(Strings.Current.ErrorUnexpected, e.Exception.ToString());
        };

        // 창을 만들기 전에 색·글꼴·언어를 정해 둔다 (첫 프레임부터 올바른 모습).
        ApplyAppearance(Settings.Value);
        Settings.Changed += s => Dispatcher.UIThread.Post(() => ApplyAppearance(s));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 창이 전부 닫혀도 독은 떠 있어야 하므로 종료는 명시적으로만 한다(Program.cs, 독의 "종료").
            var dock = new DockWindow();
            desktop.MainWindow = dock;
            dock.Show();
            // 두 번째 실행(보이기)과 설치 프로그램의 종료 요청(`--quit`)을 받는다.
            SingleInstance.Listen(
                () => Dispatcher.UIThread.Post(dock.EnsureVisible),
                () => Dispatcher.UIThread.Post(Quit));
            _tray = new TrayService(this, dock);
            Hotkeys = new HotkeyService();
            // 전역 단축키는 시작이 끝난 뒤(3초 뒤)에 등록한다. 시작하자마자 등록하면 UI 스레드가 멈춘 듯한 증상(알약 위의 대기 커서·유휴 CPU 상승)이
            // 간헐적으로 났다 — 같은 데이터로 6번씩 시험해 즉시 등록은 5번, 3초 뒤 등록은 0번이었다(원인 경합은 아직 모른다, CLAUDE.md "알려진 문제").
            if (Settings.Value.GlobalHotkey)
                _ = Task.Run(async () => { await Task.Delay(3000); Dispatcher.UIThread.Post(() => Hotkeys.Apply(Settings.Value.GlobalHotkey)); });
            Settings.Changed += st => Dispatcher.UIThread.Post(() => Hotkeys.Apply(st.GlobalHotkey));
            // 다른 앱에서 파일을 끌기 시작하면 독이 미리 펼쳐진다(Windows 만, 시험 중). 단축키와 같은 이유로 UI 스레드 밖에서, 시작이 끝난 뒤 건다.
            var dragWatch = _dragWatch = DragWatchers.Create();
            if (dragWatch.IsSupported)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    dragWatch.Start(signal => Dispatcher.UIThread.Post(() => dock.OnOutsideDrag(signal)), out _);
                });
            Updates = UpdateService.Create(
                FileUpdateStateStore.BesideSettings(settingsFile),
                owner: () => desktop.Windows.OfType<HomeWindow>().FirstOrDefault(w => w.IsActive) ?? desktop.Windows.OfType<HomeWindow>().FirstOrDefault(),
                isBusy: () => false);
            Updates?.StartAutomaticCheck();
            desktop.Exit += (_, _) => { _dragWatch?.Dispose(); Hotkeys.Dispose(); _tray?.Dispose(); Settings.Dispose(); }; // 종료할 때 단축키를 풀고 마지막 변경까지 저장

            // 확인용 인자: `--home` `--settings` `--about` 으로 그 창도 함께 띄운다.
            foreach (var arg in desktop.Args ?? [])
                switch (arg)
                {
                    case "--home": OpenHome(); break;
                    case "--settings": SettingsWindow.Open(dock); break;
                    case "--about": Dialogs.AboutDialog.Open(dock); break;
                }
            // 확인용: `--tool <도구 id> <파일...>` 로 내장 도구 창을 바로 연다(예: --tool pdf-viewer a.pdf).
            var tool = Array.IndexOf(desktop.Args ?? [], "--tool");
            if (tool >= 0 && desktop.Args!.Length > tool + 2)
                _ = Tools.OpenAsync(ToolRegistry.Scheme + desktop.Args[tool + 1], desktop.Args.Skip(tool + 2).TakeWhile(x => !x.StartsWith("--")).ToList());
            var snap = Array.IndexOf(desktop.Args ?? [], "--snapshot");
            if (snap >= 0 && desktop.Args!.Length > snap + 1) _ = Snapshot.RunAsync(desktop.Args[snap + 1]);
            if (OperatingSystem.IsMacOS()) BuildMacMenu(dock);

            // 사용 팁: 독이 자리를 잡은 뒤 옆에 말풍선으로. 확인용 실행(--snapshot·--tool)과 로그인 때 자동 시작에는 보이지 않는다.
            var args = desktop.Args ?? [];
            if (!args.Contains("--snapshot") && !args.Contains("--tool"))
            {
                var autostart = args.Contains(LaunchCommand.AutostartArg);
                DispatcherTimer.RunOnce(() => ShowTip(atStartup: true, autostart), TimeSpan.FromMilliseconds(1500));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    void ApplyAppearance(AppSettings s)
    {
        var previous = _applied;
        _applied = s;
        if (previous is null || previous.Theme != s.Theme || previous.Palette != s.Palette
            || previous.TextScale != s.TextScale || previous.UiFontFamily != s.UiFontFamily)
            ThemeService.Apply(s);
        if (previous is null || previous.Locale != s.Locale) Strings.Apply(s.Locale);
    }

    /// <summary>
    /// 지금 정책을 동기화한다(설정 창·홈 창의 버튼). 이미 돌고 있거나 저장소 주소가 없으면 아무것도 하지 않는다.
    /// 끝나면 결과를 알림으로 알리고, 받은 변경이 있으면 열린 창들이 정책을 다시 읽게 한다.
    /// </summary>
    internal static async Task SyncNowAsync()
    {
        if (SyncRunning || !Github.CanSync) return;
        SyncRunning = true;
        SyncChanged?.Invoke();
        var s = Strings.Current;
        try
        {
            var result = await Task.Run(() => Github.SyncAsync());
            if (!result.Ok)
            {
                LastSyncSummary = result.AuthFailed ? s.SyncReauth : s.SyncFailed(result.Error ?? "");
                Notifications.Snackbar.Error(LastSyncSummary);
                return;
            }
            var conflicts = result.MergeNotes.Count(n => n.Kind == MergeNoteKind.ConflictResolved);
            var headline = s.SyncDone(result.ChangedLocal, result.ChangedRemote);
            LastSyncSummary = string.Join('\n', new[] { headline }.Concat(result.MergeNotes.Select(s.SyncNote)));
            Settings.Update(x => x with { SyncLastAt = DateTimeOffset.Now });
            if (result.ChangedLocal) NotifyPolicyChanged();
            Notifications.Snackbar.Info(conflicts > 0 ? $"{headline} {s.SyncConflicts(conflicts)}" : headline);
        }
        finally
        {
            SyncRunning = false;
            SyncChanged?.Invoke();
        }
    }

    /// <summary>
    /// 사용 팁 하나를 독 옆에 보인다. 시작할 때(<paramref name="atStartup"/>)는 설정이 꺼져 있거나 자동 시작이면 보이지 않고,
    /// 메뉴의 "사용 팁 보기"는 늘 보인다. 차례는 보일 때마다 넘어간다(<see cref="StartupTips"/>). 팁이 켜져 있으면 [다시 보지 않기]가 붙는다.
    /// </summary>
    internal static void ShowTip(bool atStartup, bool autostart = false)
    {
        var current = Settings.Value;
        TipId tip;
        AppSettings next;
        if (atStartup)
        {
            if (StartupTips.OnStartup(current, autostart) is not { } picked) return;
            (tip, next) = picked;
        }
        else (tip, next) = StartupTips.Next(current);
        if (next.StartupTipsNext != current.StartupTipsNext) Settings.Update(x => x with { StartupTipsNext = next.StartupTipsNext });
        var s = Strings.Current;
        if (current.StartupTips) Snackbar.Tip(s.Tip(tip), s.TipDontShowAgain, () => Settings.Update(x => x with { StartupTips = false }));
        else Snackbar.Tip(s.Tip(tip));
    }

    /// <summary>독을 숨기거나 다시 보인다.</summary>
    internal static void ToggleDock()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: DockWindow dock }) dock.ToggleVisible();
    }

    /// <summary>홈 창을 연다(이미 있으면 앞으로).</summary>
    internal static void OpenHome()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (desktop.Windows.OfType<HomeWindow>().FirstOrDefault() is { } open) { open.Activate(); return; }
        new HomeWindow().Show();
    }

    /// <summary>macOS 메뉴 막대(ui-ux.md §7): 앱 메뉴에 정보, 설정(⌘,), 종료.</summary>
    void BuildMacMenu(Window dock)
    {
        var about = new NativeMenuItem($"{Strings.Current.AboutTooltip} {AppIdentity.DisplayName}");
        about.Click += (_, _) => Dialogs.AboutDialog.Open(dock);
        var settings = new NativeMenuItem(Strings.Current.SettingsTooltip + "…") { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.OemComma, Avalonia.Input.KeyModifiers.Meta) };
        settings.Click += (_, _) => SettingsWindow.Open(dock);
        var quit = new NativeMenuItem(Strings.Current.DockMenuQuit) { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Q, Avalonia.Input.KeyModifiers.Meta) };
        quit.Click += (_, _) => Quit();
        NativeMenu.SetMenu(this, new NativeMenu { about, settings, new NativeMenuItemSeparator(), quit });
    }

    /// <summary>앱을 끝낸다: 열려 있는 모든 창(독, 홈, 도구)이 닫힌다.</summary>
    public static void Quit() => (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
