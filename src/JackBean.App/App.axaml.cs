using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using JackBean.App.Notifications;
using JackBean.App.Tools;
using JackBean.App.Updates;
using JackBean.App.Views;
using JackBean.App.Windows;
using JackBean.Data;
using JackBean.Domain;
using JackBean.Domain.Policy;
using JackBean.Domain.Settings;
using JackBean.Services;
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

    /// <summary>업데이트 확인·설치. 서버 주소가 없는 빌드(<c>-p:AppUpdateServer=</c>)나 로컬 빌드는 null.</summary>
    internal static UpdateService? Updates { get; private set; }

    /// <summary>화면에 이미 적용한 설정. 다른 설정(독 위치 등)이 바뀔 때마다 외형을 다시 적용하지 않으려고 둔다.</summary>
    AppSettings? _applied;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var os = PolicyOsExtensions.Current ?? throw new PlatformNotSupportedException("Windows, macOS, Linux 만 지원합니다.");
        var settingsFile = JsonSettingsRepository.DefaultFile(AppIdentity.FileName);
        Settings = new SettingsController(new JsonSettingsRepository(settingsFile));

        PolicyStore = new PolicyStore(os, Path.GetDirectoryName(settingsFile));
        Tools = new ToolRegistry();
        Tools.Register(new ImageViewerTool());
        // `jackbean:` 로 시작하는 명령은 프로세스를 띄우지 않고 내장 도구가 연다. 나머지는 셸 없이 프로세스로.
        var external = new AppLauncher();
        Router = new DropRouter(PolicyStore, new AppLauncher((command, args) =>
            ToolRegistry.IsBuiltIn(command) ? Tools.OpenAsync(command, args.Count > 0 ? args[0] : "") : external.SpawnAsync(command, args)));

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
            Updates = UpdateService.Create(
                FileUpdateStateStore.BesideSettings(settingsFile),
                owner: () => desktop.Windows.OfType<HomeWindow>().FirstOrDefault(w => w.IsActive) ?? desktop.Windows.OfType<HomeWindow>().FirstOrDefault(),
                isBusy: () => false);
            Updates?.StartAutomaticCheck();
            desktop.Exit += (_, _) => Settings.Dispose(); // 종료할 때 마지막 변경까지 저장

            // 확인용 인자: `--home` `--settings` `--about` 으로 그 창도 함께 띄운다.
            foreach (var arg in desktop.Args ?? [])
                switch (arg)
                {
                    case "--home": OpenHome(); break;
                    case "--settings": SettingsWindow.Open(dock); break;
                    case "--about": Dialogs.AboutDialog.Open(dock); break;
                }
            var snap = Array.IndexOf(desktop.Args ?? [], "--snapshot");
            if (snap >= 0 && desktop.Args!.Length > snap + 1) _ = Snapshot.RunAsync(desktop.Args[snap + 1]);
            if (OperatingSystem.IsMacOS()) BuildMacMenu(dock);
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
