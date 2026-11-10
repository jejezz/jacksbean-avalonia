using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JackBean.Domain;
using JackBean.Services;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 PDF 뷰어: 운영체제의 웹뷰(Windows WebView2 · macOS WKWebView)에 파일을 그대로 열어 보여 준다. 별도 PDF 렌더러를 싣지 않으므로
/// 앱이 거의 커지지 않는다. 웹뷰가 없거나 열지 못하면 안내와 [기본 앱으로 열기]를 보인다. 창을 닫으면 웹뷰도 치워진다.
/// </summary>
public sealed class PdfViewerTool : IBuiltInTool
{
    public string Id => "pdf-viewer";

    public Window CreateWindow(string path)
    {
        var window = new Window
        {
            Title = WindowTitle.Of(Path.GetFileName(path)),
            Width = 1000,
            Height = 800,
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
            web.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) window.Content = Failure(path, Strings.Current.PdfViewerFailed(Path.GetFileName(path)));
            };
            web.Source = new Uri(path);
            window.Content = web;
            window.Closed += (_, _) => window.Content = null;
        }
        catch (Exception e)
        {
            window.Content = Failure(path, Strings.Current.PdfViewerFailed(e.Message));
        }
        return window;
    }

    static Control Failure(string path, string message)
    {
        var open = new Button { Classes = { "accent" }, Content = Strings.Current.ViewerOpenWithDefaultApp, HorizontalAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) =>
        {
            try { new OpenWithDefaultApp().Open(path); }
            catch (Exception) { /* 기본 앱도 못 열면 더 할 수 있는 것이 없다 */ }
        };
        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }, open },
        };
    }
}
