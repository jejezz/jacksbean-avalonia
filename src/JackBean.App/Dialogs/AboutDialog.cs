using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JackBean.App.Views;
using JackBean.Domain;
using JackBean.Services;

namespace JackBean.App.Dialogs;

/// <summary>
/// 정보 창 (conventions about-dialog.md): 앱 아이콘·표시 이름·버전(빌드)·소개·저작권·라이선스 이름과
/// [오픈소스 라이선스] [GitHub] [닫기] 버튼. 너비는 최대 440. 짧은 모달 정보라 대화상자로 둔다.
/// 이름·저작권·버전은 <see cref="AppIdentity"/>/<see cref="AppVersion"/>에서만 읽는다.
/// </summary>
public sealed class AboutDialog : DialogBase
{
    public static void Open(Window owner) => _ = new AboutDialog().ShowDialog(owner);

    AboutDialog()
    {
        var s = Strings.Current;
        Title = AppIdentity.DisplayName;
        MinWidth = 440;
        MaxWidth = 440;

        var icon = new Image
        {
            Width = 48,
            Height = 48,
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://JacksBean/Assets/app_icon.png"))),
        };
        var name = new TextBlock { Text = AppIdentity.DisplayName, FontWeight = FontWeight.ExtraBold };
        name.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        // 버전은 복사할 수 있게 선택 가능한 글로 둔다.
        var version = new SelectableTextBlock { Text = s.AboutVersion(AppVersion.Version, AppVersion.Build) };
        version.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        version.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children =
        {
            icon,
            new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { name, version } },
        } };

        var tagline = new TextBlock { Text = s.AboutTagline, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold };
        var description = new TextBlock { Text = s.AboutDescription, TextWrapping = TextWrapping.Wrap };
        var divider = new Border { Height = 1, Margin = new Thickness(0, 4) };
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppStroke"));
        var legal = new TextBlock { Text = $"{AppIdentity.Copyright}\n{AppIdentity.LicenseName}" };
        legal.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        legal.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));

        var licenses = new Button { Content = s.AboutOpenSourceLicenses };
        licenses.Click += (_, _) => LicensesWindow.Open(this);
        var repo = new Button { Content = s.AboutRepository };
        repo.Click += (_, _) =>
        {
            try { new OpenWithDefaultApp().Open(AppIdentity.RepositoryUrl); }
            catch (FileOperationException) { /* 브라우저를 못 열어도 정보 창은 그대로 둔다 */ }
        };
        var close = new Button { Content = s.AboutClose, Classes = { "accent" } };
        close.Click += (_, _) => Close();
        // 업데이트 확인은 서버 주소가 없는 빌드(로컬 빌드 포함)에서는 보이지 않는다.
        Button? update = null;
        if (App.Updates is { } updates)
        {
            update = new Button { Content = s.UpdateCheckButton };
            update.Click += async (_, _) =>
            {
                update.IsEnabled = false;
                try { await updates.CheckManuallyAsync(this); }
                finally { update.IsEnabled = true; }
            };
        }
        // 버튼이 많아 좁은 창(최대 440)에서 넘치지 않게, 왼쪽 버튼들은 줄을 바꿀 수 있게 두고 닫기는 오른쪽에 붙인다.
        var left = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { licenses, repo, update }.OfType<Button>())
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            left.Children.Add(b);
        }
        close.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(close, Dock.Right);
        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { close, left } };

        Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = { head, tagline, description, divider, legal, buttons } };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }
}
