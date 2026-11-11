using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JackBean.App.Tools;
using JackBean.App.Views;
using JackBean.App.Windows;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;
using JackBean.Services.CodeProfiles;

namespace JackBean.App;

/// <summary>
/// 확인용: `--snapshot &lt;폴더&gt;` 로 실행하면 모드·색 구성·글자 크기·언어를 바꿔 가며 홈·설정·정보 창을 PNG 로 저장하고 끝난다.
/// 화면 캡처 없이(가려지거나 다른 모니터에 걸리는 일 없이) 실행 중 설정 변경이 열린 창에 반영되는 것까지 눈으로 볼 수 있다.
/// 설정 파일을 바꾸므로 HOME 을 임시 폴더로 돌려서 실행한다.
/// </summary>
static class Snapshot
{
    public static async Task RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var configs = new (string Name, ThemeMode Mode, ColorPalette Palette, double Scale, AppLocale? Lang)[]
        {
            ("dark-saturn-en", ThemeMode.Dark, ColorPalette.Saturn, 1.0, AppLocale.English),   // README 스크린샷(영어)
            ("light-saturn-en", ThemeMode.Light, ColorPalette.Saturn, 1.0, AppLocale.English),
            ("dark-saturn", ThemeMode.Dark, ColorPalette.Saturn, 1.0, AppLocale.Korean),
            ("light-saturn", ThemeMode.Light, ColorPalette.Saturn, 1.0, AppLocale.Korean),
            ("dark-rose-large-en", ThemeMode.Dark, ColorPalette.Rose, 1.3, AppLocale.English),
            ("light-teal-small", ThemeMode.Light, ColorPalette.Teal, 0.85, AppLocale.Korean),
            ("dark-contrast", ThemeMode.Dark, ColorPalette.HighContrast, 1.0, AppLocale.Korean),
            ("light-contrast", ThemeMode.Light, ColorPalette.HighContrast, 1.0, AppLocale.Korean),
        };
        // 창을 먼저 연 채로 설정을 바꿔서 "열린 창에 즉시 반영"도 함께 확인한다.
        App.OpenHome();
        var dock = ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow!;
        SettingsWindow.Open(dock);
        Dialogs.AboutDialog.Open(dock);
        // 규칙 편집 창은 파일 형식 구역으로 열고, 예시 규칙 몇 개를 미리 넣는다(임시 HOME 이라 실제 정책은 건드리지 않는다).
        await App.PolicyStore.LoadAsync();
        await App.PolicyStore.UpsertAppAsync(new Services.AppUpsert("viewer", "viewer.exe", ["{path}"]));
        await App.PolicyStore.UpsertRuleAsync(new Services.RuleEdit("image", Domain.Policy.RuleCondition.Extension(Domain.Policy.ExtensionGroups.Find("image")!.Extensions)!, ["viewer", "default"]));
        await App.PolicyStore.UpsertRuleAsync(new Services.RuleEdit("pdf", Domain.Policy.RuleCondition.Extension(["pdf"])!, ["default"]));
        App.NotifyPolicyChanged();
        AppsEditorWindow.Open(dock);
        RulesEditorWindow.Open(dock, RulesEditorSection.Files, "video");   // 홈 창에서 규칙 줄을 눌렀을 때처럼 그 규칙을 고른다
        Notifications.Snackbar.Error(Strings.Current.DockOpenFailed("D:/작업/보고서 최종.pdf 을(를) 실행하지 못했습니다 — 파일 형식과 연결된 앱이 없습니다"), "System.ComponentModel.Win32Exception: 지정된 파일을 찾을 수 없습니다");
        // 텍스트 에디터(구문 강조 포함): 예시 C# 파일을 연다.
        var sample = Path.Combine(dir, "Sample.cs");
        File.WriteAllText(sample, """
        using System;

        namespace Demo;

        /// <summary>Greets someone.</summary>
        public static class Greeter
        {
            public static string Hello(string name) => $"Hello, {name}!"; // 42
        }
        """);
        await App.Tools.OpenAsync("jackbean:text-editor", sample);
        foreach (var w in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.OfType<HomeWindow>()) w.Height = 1100;   // 카드가 다 보이게
        await Task.Delay(800);
        foreach (var c in configs)
        {
            App.Settings.Update(s => (s with { Theme = c.Mode, Palette = c.Palette, Locale = c.Lang }).WithTextScale(c.Scale));
            await Task.Delay(900);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var w in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows)
                    if (w is not DockWindow) Save(w, Path.Combine(dir, $"{c.Name}-{w.GetType().Name}.png"));
            });
        }
        // 설정 창의 "동작" 구역(전역 단축키 스위치)도 한 장 남긴다.
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var settingsWindow = ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.OfType<SettingsWindow>().FirstOrDefault();
            if (settingsWindow == null) return;
            settingsWindow.ShowBehavior();
            await Task.Delay(500);
            Save(settingsWindow, Path.Combine(dir, "behavior-SettingsWindow.png"));
            App.Settings.Update(x => x with { SyncRepo = "octo/jacksbean-policy", SyncLastAt = DateTimeOffset.Now.AddMinutes(-3) });
            settingsWindow.ShowSync();
            await Task.Delay(500);
            Save(settingsWindow, Path.Combine(dir, "sync-SettingsWindow.png"));
            // 저장소 창: 만들 때 예외가 나지 않는지(같은 컨트롤을 두 번 담는 실수 등)도 이것으로 걸러진다. 로그인하지 않았으니 목록 대신 오류 안내가 보인다.
            _ = Dialogs.GitHubRepoDialog.ShowAsync(settingsWindow);
            await Task.Delay(1800);
            var repoDialog = ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.OfType<Dialogs.GitHubRepoDialog>().FirstOrDefault();
            if (repoDialog != null) { Save(repoDialog, Path.Combine(dir, "repo-GitHubRepoDialog.png")); repoDialog.CloseFromState(); }
            // 로그인 창(이 빌드에 Client ID 가 없으면 토큰 입력만 보인다).
            _ = Dialogs.GitHubLoginDialog.ShowAsync(settingsWindow);
            await Task.Delay(600);
            var login = ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows.OfType<Dialogs.GitHubLoginDialog>().FirstOrDefault();
            if (login != null) { Save(login, Path.Combine(dir, "login-GitHubLoginDialog.png")); login.CloseFromState(); }
        });
        await CodeProfileShotsAsync(dir);
        App.Quit();
    }

    /// <summary>VS Code 프로필 도구 창의 단계별 화면(성공·실패). 진짜 VS Code 를 건드리지 않도록 가짜 실행기를 쓴다.</summary>
    static async Task CodeProfileShotsAsync(string dir)
    {
        var project = Path.Combine(dir, "code-sample");
        Directory.CreateDirectory(Path.Combine(project, ".vscode"));
        File.WriteAllText(Path.Combine(project, "pom.xml"), "<project/>");
        File.WriteAllText(Path.Combine(project, "package.json"), "{}");
        File.WriteAllText(Path.Combine(project, ".vscode", "settings.json"), "// 내 설정" + Environment.NewLine + "{ \"java.configuration.updateBuildConfiguration\": \"disabled\" }");
        var data = Path.Combine(dir, "code-data");
        Directory.CreateDirectory(data);

        foreach (var (name, failInstall) in new[] { ("ok", false), ("fail", true) })
        {
            var service = new CodeProfileService(new VsCodeCli(new ScriptedRunner(failInstall), "code.cmd"), new ProjectAnalyzer(), new CodeProfileRulesStore(data), new ProjectSettingsFile(), new CodeProjectMemory(data));
            var flow = new CodeProfileFlow(service, project);
            var window = new CodeProfileWindow(flow);
            window.Show();
            await Task.Delay(1500);
            await Dispatcher.UIThread.InvokeAsync(() => Save(window, Path.Combine(dir, $"code-{name}-1-analysis.png")));
            flow.GoToRecommendation();
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => Save(window, Path.Combine(dir, $"code-{name}-2-recommend.png")));
            await flow.GoToDiffAsync();
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => Save(window, Path.Combine(dir, $"code-{name}-3-diff.png")));
            await flow.ApplyAsync();
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => Save(window, Path.Combine(dir, $"code-{name}-4-apply.png")));
            window.Close();
        }
        await CodeNoTypesShotAsync(dir, data);
    }

    static async Task CodeNoTypesShotAsync(string dir, string data)
    {
        var folder = Path.Combine(dir, "code-notes");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
        var service = new CodeProfileService(new VsCodeCli(new ScriptedRunner(false), "code.cmd"), new ProjectAnalyzer(), new CodeProfileRulesStore(data), new ProjectSettingsFile(), new CodeProjectMemory(data));
        var window = new CodeProfileWindow(new CodeProfileFlow(service, folder));
        window.Show();
        await Task.Delay(1500);
        await Dispatcher.UIThread.InvokeAsync(() => Save(window, Path.Combine(dir, "code-notypes-1-analysis.png")));
        window.Close();
    }

    /// <summary>`--snapshot` 전용: 무엇을 불러도 성공(또는 확장 설치만 실패)하는 가짜 실행기.</summary>
    sealed class ScriptedRunner(bool failInstall) : IProcessRunner
    {
        public Task<ProcessOutput> RunAsync(string executable, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult(new ProcessOutput(0, StdOut: "1.95.0"));

        public async IAsyncEnumerable<ProcessEvent> StartAsync(string executable, IReadOnlyList<string> args, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new OutputLine("Installing extensions...");
            if (failInstall)
            {
                yield return new OutputLine("Error: extension not found in marketplace", IsError: true);
                yield return new ProcessExited(1);
            }
            else
            {
                yield return new OutputLine("Extension was successfully installed.");
                yield return new ProcessExited(0);
            }
        }
    }

    static void Save(Window w, string path)
    {
        var scale = w.RenderScaling;
        var size = new PixelSize(Math.Max(1, (int)(w.Bounds.Width * scale)), Math.Max(1, (int)(w.Bounds.Height * scale)));
        using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bmp.Render(w);
        bmp.Save(path);
    }
}
