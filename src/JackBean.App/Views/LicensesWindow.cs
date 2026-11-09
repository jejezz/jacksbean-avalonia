using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using JackBean.Domain;

namespace JackBean.App.Views;

/// <summary>
/// 오픈소스 라이선스 창 (conventions licensing.md): 이 프로젝트의 라이선스 원문, 번들 글꼴의 사용 조건, 그리고 쓰는 라이브러리의
/// 이름·라이선스 종류·저장소. 독립 창이라 정보 창을 닫아도 열어 둘 수 있다. 하나만 열린다.
/// </summary>
public sealed class LicensesWindow : Window
{
    /// <summary>라이브러리 목록. 앞 줄이 직접 쓰는 것, 뒤는 함께 배포되는 네이티브 구성 요소다.</summary>
    static readonly (string Name, string License, string Url)[] Libraries =
    [
        ("Avalonia", "MIT", "https://github.com/AvaloniaUI/Avalonia"),
        (".NET runtime", "MIT", "https://github.com/dotnet/runtime"),
        ("SkiaSharp / Skia", "MIT / BSD-3-Clause", "https://github.com/mono/SkiaSharp"),
        ("HarfBuzz (HarfBuzzSharp)", "MIT", "https://github.com/harfbuzz/harfbuzz"),
        ("ANGLE", "BSD-3-Clause", "https://github.com/google/angle"),
        ("Phosphor Icons (아이콘 / icons)", "MIT", "https://github.com/phosphor-icons/core"),
    ];

    static LicensesWindow? _instance;

    public static void Open(Window origin)
    {
        if (_instance is { } open) { open.Activate(); return; }
        var window = new LicensesWindow();
        _instance = window;
        window.Closed += (_, _) => _instance = null;
        WindowPlacementHelper.OpenNear(origin, window);
    }

    static string ReadAsset(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://JacksBean/Assets/{name}"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    LicensesWindow()
    {
        var s = Strings.Current;
        Title = s.LicensesTitle;
        Width = 720;
        Height = 620;
        MinWidth = 420;
        MinHeight = 300;

        var page = new StackPanel { Spacing = 8, Margin = new Thickness(32, 24), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };

        TextBlock Heading(string text)
        {
            var h = new TextBlock { Text = text, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 16, 0, 4) };
            h.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsMid"));
            return h;
        }
        SelectableTextBlock Body(string text)
        {
            var b = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            b.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
            return b;
        }

        var title = new TextBlock { Text = s.LicensesTitle, FontWeight = FontWeight.ExtraBold };
        title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        var note = new TextBlock { Text = s.LicensesNote, TextWrapping = TextWrapping.Wrap };
        note.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        note.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        page.Children.Add(title);
        page.Children.Add(note);

        page.Children.Add(Heading($"{s.LicensesProject} — {AppIdentity.DisplayName}"));
        page.Children.Add(Body(ReadAsset("LICENSE.txt")));

        page.Children.Add(Heading("SeoulNamsan"));
        page.Children.Add(Body(ReadAsset("seoul-namsan.txt")));

        page.Children.Add(Heading("Libraries"));
        foreach (var (name, license, url) in Libraries) page.Children.Add(Body($"{name} — {license}\n{url}"));

        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }
}
