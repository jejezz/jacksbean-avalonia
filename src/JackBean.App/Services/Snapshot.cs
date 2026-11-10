using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JackBean.App.Views;
using JackBean.App.Windows;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;

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
        RulesEditorWindow.Open(dock, RulesEditorSection.Files);
        Notifications.Snackbar.Error(Strings.Current.DockOpenFailed("D:/작업/보고서 최종.pdf 을(를) 실행하지 못했습니다 — 파일 형식과 연결된 앱이 없습니다"), "System.ComponentModel.Win32Exception: 지정된 파일을 찾을 수 없습니다");
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
        App.Quit();
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
