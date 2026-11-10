using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using JackBean.Domain;
using JackBean.Services;

namespace JackBean.App.Tools;

/// <summary>
/// 운영체제의 웹뷰(Windows WebView2 · macOS WKWebView · <see cref="NativeWebView"/>)에 파일을 그대로 열어 보여 주는 창 — PDF 뷰어·비디오 플레이어가 함께 쓴다.
/// 별도 렌더러·재생 라이브러리를 싣지 않으므로 앱이 거의 커지지 않고, 웹뷰가 아는 형식만 열린다. 웹뷰가 없거나 열지 못하면 안내와
/// [기본 앱으로 열기]를 보인다. 창을 닫으면 웹뷰도 치워진다(소리도 멈춘다).
/// </summary>
internal static class WebFileWindow
{
    /// <param name="failureText">열지 못했을 때의 안내 문구(오류 설명을 받는다).</param>
    /// <param name="detectFailure">탐색이 실패로 끝나면 안내 화면으로 바꾼다. 미디어 파일은 WebView2 가 성공한 재생도 "실패"로 알려서 끈다 — 재생이 안 되는 형식은 위쪽 띠의 [기본 앱으로 열기]가 맡는다.</param>
    /// <param name="hint">위쪽 띠에 늘 보이는 안내(웹뷰가 못 여는 형식일 때 기본 앱으로 넘기라는 말). null 이면 띠가 없다.</param>
    public static Window Create(string path, Func<string, string> failureText, string? hint = null, double width = 1000, double height = 800, bool detectFailure = true)
    {
        var window = new Window
        {
            Title = WindowTitle.Of(Path.GetFileName(path)),
            Width = width,
            Height = height,
            MinWidth = 320,
            MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        try
        {
            var web = new NativeWebView();
            // WebView2 는 사용자 데이터 폴더를 기본으로 실행 파일 옆에 만든다 — 설치본(C:\Program Files)은 쓸 수 없으니 사용자 폴더로 옮긴다.
            // 초기화 전에(Source 설정 전에) 지정해야 한다.
            web.EnvironmentRequested += (_, e) =>
            {
                if (e is Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs w2)
                    w2.UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacksBean", "WebView2");
            };
            if (detectFailure)
                web.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess) window.Content = Failure(path, failureText(Path.GetFileName(path)));
                };
            web.Source = new Uri(path);
            window.Content = hint is null ? web : WithBar(path, web, hint);
            window.Closed += (_, _) => window.Content = null;
        }
        catch (Exception e)
        {
            window.Content = Failure(path, failureText(e.Message));
        }
        return window;
    }

    static Button OpenButton(string path)
    {
        var open = new Button { Content = Strings.Current.ViewerOpenWithDefaultApp, HorizontalAlignment = HorizontalAlignment.Right };
        open.Click += (_, _) =>
        {
            try { new OpenWithDefaultApp().Open(path); }
            catch (Exception) { /* 기본 앱도 못 열면 더 할 수 있는 것이 없다 */ }
        };
        return open;
    }

    /// <summary>위쪽에 안내와 [기본 앱으로 열기]를 둔 띠를 얹는다.</summary>
    static Control WithBar(string path, Control body, string hint)
    {
        var text = new TextBlock { Text = hint, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        text.Bind(TextBlock.ForegroundProperty, Application.Current!.GetResourceObservable("AppTextMid"));
        text.Bind(TextBlock.FontSizeProperty, Application.Current!.GetResourceObservable("FsSmall"));
        var open = OpenButton(path);
        Grid.SetColumn(open, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(text);
        grid.Children.Add(open);
        var bar = new Border { Padding = new Thickness(16, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        bar.Bind(Border.BackgroundProperty, Application.Current!.GetResourceObservable("AppSurface"));
        bar.Bind(Border.BorderBrushProperty, Application.Current!.GetResourceObservable("AppStroke"));
        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(body);
        return panel;
    }

    static Control Failure(string path, string message)
    {
        var open = OpenButton(path);
        open.Classes.Add("accent");
        open.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, TextAlignment = Avalonia.Media.TextAlignment.Center }, open },
        };
    }
}
