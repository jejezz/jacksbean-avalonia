// 홈 창: 지금 정책의 규칙과 앱을 보여 준다(룰 메이커가 생기기 전의 읽기 전용 화면). 독과 별개의 창이다.
// 앱 바 44(ui-ux.md §3·§4: 포커스를 받으면 강조색으로 물들고, 버튼 순서는 `… | 설정 | 정보`) + 카드.
// 단축키(ui-ux.md §7): 설정 Ctrl/⌘+, · 정보 F1 · 새로 고침 F5·Ctrl/⌘+R · 창 닫기 Ctrl/⌘+W.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JackBean.App.Icons;
using JackBean.App.Views;
using JackBean.Domain;
using JackBean.Domain.Policy;
using JackBean.Domain.Window;

namespace JackBean.App.Windows;

public sealed class HomeWindow : Window
{
    private const double DefaultWidth = 960, DefaultHeight = 640;   // 작은 유틸리티 기본 크기, 최소는 960×600 이하 (ui-ux.md §5)
    private PolicyDocument? _policy;
    private bool _trackPlacement;
    private Border _tint = new() { Classes = { "tint" } };

    public HomeWindow()
    {
        Width = DefaultWidth; Height = DefaultHeight; MinWidth = 960; MinHeight = 600;
        RestorePlacement();
        Build();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Strings.Changed += OnLanguageChanged;
        Closed += (_, _) => Strings.Changed -= OnLanguageChanged;
        Activated += (_, _) => _tint.Classes.Set("active", true);
        Deactivated += (_, _) => _tint.Classes.Set("active", false);
        PositionChanged += (_, _) => SavePlacement();
        PropertyChanged += (_, e) => { if (e.Property == ClientSizeProperty || e.Property == WindowStateProperty) SavePlacement(); };
        Closing += (_, _) => App.Settings.Flush();
        Opened += async (_, _) =>
        {
            await ReloadAsync();
            await Task.Delay(300);   // 복원하는 동안의 크기 변화가 저장되지 않게, 열린 뒤에 추적을 시작한다
            _trackPlacement = true;
        };
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(Build);

    private async Task ReloadAsync()
    {
        _policy = await App.PolicyStore.LoadAsync();
        Build();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mod = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.F1) { Dialogs.AboutDialog.Open(this); e.Handled = true; }
        else if (e.Key == Key.F5) { _ = ReloadAsync(); e.Handled = true; }
        else if (e.KeyModifiers == mod)
        {
            switch (e.Key)
            {
                case Key.OemComma: SettingsWindow.Open(this); e.Handled = true; break;   // 설정 (ui-ux.md §7)
                case Key.R: _ = ReloadAsync(); e.Handled = true; break;
                case Key.W: Close(); e.Handled = true; break;
            }
        }
    }

    // --- 창 위치·크기 (window-placement.md) ---

    private IReadOnlyList<DisplayArea> DisplayAreas() =>
        Screens.All.Select(sc => new DisplayArea(
            new RectD(sc.WorkingArea.X, sc.WorkingArea.Y, sc.WorkingArea.Width, sc.WorkingArea.Height), sc.Scaling, sc.IsPrimary)).ToList();

    /// <summary>저장한 위치로 연다. 기억한 곳이 없거나 그 모니터가 없어졌으면 주 모니터 가운데에 연다. 보이기 전에 정한다.</summary>
    private void RestorePlacement()
    {
        var displays = DisplayAreas();
        var saved = App.Settings.Value.Window;
        var restored = saved is null ? null : PlacementMath.Restore(saved, displays);
        var target = restored ?? (PlacementMath.PrimaryOf(displays) is { } d ? PlacementMath.CenterOn(d, DefaultWidth, DefaultHeight) : (PlacementTarget?)null);
        if (target is not { } t) { WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)Math.Round(t.X), (int)Math.Round(t.Y));
        Width = t.Width;
        Height = t.Height;
        if (restored is not null && saved!.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        if (!_trackPlacement || WindowState is WindowState.Minimized or WindowState.FullScreen) return;
        var next = PlacementMath.Updated(App.Settings.Value.Window, Position.X, Position.Y, ClientSize.Width, ClientSize.Height, RenderScaling,
            maximized: WindowState == WindowState.Maximized);
        if (next is not null && next != App.Settings.Value.Window) App.Settings.Update(s => s with { Window = next });
    }

    // --- 화면 ---

    private static Button ToolButton(string icon, string tooltip, Action click)
    {
        var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
        ToolTip.SetTip(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    private void Build()
    {
        var s = Strings.Current;
        Title = AppIdentity.DisplayName;
        _tint = new Border { Classes = { "tint" } };
        _tint.Classes.Set("active", IsActive);

        var title = new TextBlock { Text = AppIdentity.DisplayName, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.ExtraBold };
        title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(ToolButton("settings", s.SettingsTooltip, () => SettingsWindow.Open(this)));
        buttons.Children.Add(ToolButton("info", s.AboutTooltip, () => Dialogs.AboutDialog.Open(this)));
        Grid.SetColumn(buttons, 1);
        var barGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 0) };
        barGrid.Children.Add(title);
        barGrid.Children.Add(buttons);
        var bar = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Height = 44 };
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        bar.Child = new Grid { Children = { _tint, barGrid } };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new ScrollViewer { Content = Body() });
        Content = root;
    }

    private static Border Card(Control child)
    {
        var card = new Border { Child = child, Padding = new Thickness(16) };
        card.Classes.Add("card");
        return card;
    }

    private static TextBlock Heading(string text)
    {
        var h = new TextBlock { Text = text, FontWeight = FontWeight.Bold };
        h.Bind(TextBlock.FontSizeProperty, Application.Current!.GetResourceObservable("FsMid"));
        return h;
    }

    private static TextBlock UserText(string text, params string[] classes)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.Classes.Add("user");   // 파일 이름·경로는 시스템 글꼴 (fonts.md §3)
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    private Control Body()
    {
        var s = Strings.Current;
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 16, MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };

        var tagline = new TextBlock { Text = s.AboutTagline };
        tagline.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        stack.Children.Add(tagline);

        var path = UserText(App.PolicyStore.FilePath, "small");
        var reload = new Button { Content = s.HomeReload, HorizontalAlignment = HorizontalAlignment.Left };
        reload.Click += async (_, _) => await ReloadAsync();
        stack.Children.Add(Card(new StackPanel { Spacing = 8, Children = { Heading(s.HomePolicyFile), path, reload } }));

        if (_policy == null) return stack;

        if (_policy.Warnings.Count > 0)
        {
            var warn = UserText(string.Join('\n', _policy.Warnings), "failed");
            stack.Children.Add(Card(new StackPanel { Spacing = 4, Children = { Heading(s.HomeWarnings), warn } }));
        }

        var rules = new StackPanel { Spacing = 4, Children = { Heading(s.HomeRules) } };
        var n = 1;
        foreach (var r in _policy.Rules)
            rules.Children.Add(UserText($"{n++}. {r.Id}  ·  {Describe(r.When)}  →  {r.Open ?? s.HomeAsk}"));
        stack.Children.Add(Card(rules));

        var apps = new StackPanel { Spacing = 4, Children = { Heading(s.HomeApps) } };
        foreach (var a in _policy.Apps.Values) apps.Children.Add(UserText($"{a.Id}  ·  {a.Command} {string.Join(' ', a.Args)}"));
        stack.Children.Add(Card(apps));
        return stack;
    }

    private static string Describe(RuleCondition c) => c.Kind switch
    {
        "extension" => "ext: " + string.Join(", ", c.Values),
        "nameGlob" => "name: " + c.Pattern,
        "hasGit" => c.Value == "true" ? "git folder" : "folder without git",
        _ => $"{c.Kind}: {c.Value}",
    };
}
