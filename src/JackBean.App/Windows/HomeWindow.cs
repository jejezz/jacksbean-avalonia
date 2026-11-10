// 홈 창: 지금 정책의 규칙과 앱을 보여 준다(룰 메이커가 생기기 전의 읽기 전용 화면). 독과 별개의 창이다.
// 앱 바 44(ui-ux.md §3·§4: 포커스를 받으면 강조색으로 물들고, 버튼 순서는 `… | 설정 | 정보`) + 카드.
// 단축키(ui-ux.md §7): 설정 Ctrl/⌘+, · 정보 F1 · 새로 고침 F5·Ctrl/⌘+R · 창 닫기 Ctrl/⌘+W.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JackBean.App.Icons;
using JackBean.App.Notifications;
using JackBean.App.Tools;
using JackBean.App.Views;
using JackBean.Domain;
using JackBean.Domain.Policy;
using JackBean.Domain.Window;
using JackBean.Services;

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
        App.PolicyChanged += OnPolicyChanged;
        App.SyncChanged += OnSyncChanged;
        Closed += (_, _) => { Strings.Changed -= OnLanguageChanged; App.PolicyChanged -= OnPolicyChanged; App.SyncChanged -= OnSyncChanged; };
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

    /// <summary>편집 창(또는 복구)이 정책을 바꿨다 — 홈 창은 보기 전용이라 다시 읽어 보여 주기만 한다.</summary>
    private void OnSyncChanged() => Dispatcher.UIThread.Post(Build);

    private void OnPolicyChanged() => Dispatcher.UIThread.Post(async () => await ReloadAsync());

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

        if (_policy != null) stack.Children.Add(SummaryStrip(_policy));

        if (_policy != null && _policy.Warnings.Count > 0)
        {
            var warn = UserText(string.Join(Environment.NewLine, _policy.Warnings), "failed");
            var warnBody = new StackPanel { Spacing = 8, Children = { Heading(s.HomeWarnings), warn } };
            if (_policy.IsFallback)
            {
                // 파일 전체를 못 읽었다 — 기본 정책으로 다시 만들 수 있게 한다(지금 파일은 .broken-날짜 로 옮겨 둔다).
                var note = new TextBlock { Text = s.HomeFallbackNote, TextWrapping = TextWrapping.Wrap };
                var reset = new Button { Content = s.HomeResetPolicy, Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Left };
                reset.Click += async (_, _) => await ResetPolicyAsync();
                warnBody.Children.Add(note);
                warnBody.Children.Add(reset);
            }
            stack.Children.Add(Card(warnBody));
        }

        if (_policy != null && _policy.Rules.Count == 0 && _policy.Apps.Count == 0)
            stack.Children.Add(Card(EmptyState()));   // 규칙도 앱도 없다 — 무엇을 하면 되는지 알려 준다
        else if (_policy != null)
        {
            stack.Children.Add(RulesCard(_policy));
            stack.Children.Add(AppsCard(_policy));
        }

        stack.Children.Add(PolicyFileCard());
        return stack;
    }

    // --- 요약 띠 ---

    private Border Stat(string label, string value, string? sub = null, string? valueColorKey = null)
    {
        var l = new TextBlock { Text = label };
        l.Classes.Add("small");
        l.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var v = new TextBlock { Text = value, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        v.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        if (valueColorKey != null) v.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(valueColorKey));
        var panel = new StackPanel { Spacing = 2, Children = { l, v } };
        if (sub != null)
        {
            var t = UserText(sub, "small");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.TextWrapping = TextWrapping.NoWrap;
            t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            panel.Children.Add(t);
        }
        var b = new Border { Child = panel, Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        b.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        b.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        return b;
    }

    private Control SummaryStrip(PolicyDocument policy)
    {
        var s = Strings.Current;
        var settings = App.Settings.Value;
        var connected = !string.IsNullOrEmpty(settings.SyncRepo);
        var syncSub = connected
            ? settings.SyncRepo + (settings.SyncLastAt is { } at ? "  ·  " + at.LocalDateTime.ToString("g") : "  ·  " + s.HomeSyncNotYet)
            : null;
        var os = policy.Os?.JsonName() ?? PolicyOsExtensions.Current?.JsonName() ?? "";
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 8 };
        var stats = new[]
        {
            Stat(s.HomeStatRules, policy.Rules.Count.ToString()),
            Stat(s.HomeStatApps, policy.Apps.Count.ToString(), s.HomeAppsSummary(policy.Apps.Count, policy.Apps.Keys.Count(id => ToolRegistry.IsBuiltIn(policy.Apps[id].Command)))),
            Stat(s.HomeStatSync, connected ? s.HomeSyncOn : s.HomeSyncOff, syncSub, connected ? "AppPrimary" : null),
            Stat(s.HomeStatPolicy, Path.GetFileName(App.PolicyStore.FilePath), os.Length > 0 ? os : null),
        };
        for (var i = 0; i < stats.Length; i++)
        {
            Grid.SetColumn(stats[i], i);
            grid.Children.Add(stats[i]);
        }
        return grid;
    }

    // --- 규칙 카드: 대상 → 확장자 칩 → 연결한 앱 칩 ---

    private static Border Chip(string text, bool mono, string? iconKey = null, bool accent = false, bool muted = false)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.Classes.Add("small");
        if (mono) label.Classes.Add("mono");
        label.Bind(TextBlock.ForegroundProperty, Application.Current!.GetResourceObservable(accent ? "AppPrimary" : muted ? "AppTextMid" : "AppTextHi"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (iconKey != null)
        {
            var icon = new DuoIcon { Icon = iconKey, Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center };
            icon.Bind(DuoIcon.ForegroundProperty, Application.Current!.GetResourceObservable(accent ? "AppPrimary" : "AppTextMid"));
            row.Children.Add(icon);
        }
        row.Children.Add(label);
        var chip = new Border { Child = row, Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Margin = new Thickness(0, 2, 4, 2) };
        chip.Bind(Border.BackgroundProperty, Application.Current!.GetResourceObservable("AppSurfaceHi"));
        chip.Bind(Border.BorderBrushProperty, Application.Current!.GetResourceObservable("AppStroke"));
        return chip;
    }

    /// <summary>제목에 쓰려고 첫 글자만 대문자로(영어 "drive" → "Drive").</summary>
    private static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string AppIconKey(AppEntry app) => app.Id switch
    {
        "image-viewer" => "image", "text-editor" => "file-text", "pdf-viewer" => "pdf", "video-player" => "video",
        "audio-player" => "audio", "archive" => "file-zip", "explorer" => "folder", "default" => "monitor", "terminal" => "terminal",
        _ => "app",
    };

    private Control RuleRow(Rule rule, int index)
    {
        var s = Strings.Current;
        var label = RulePresentation.Label(rule);
        var title = label.Kind switch
        {
            RuleKind.Drive => Cap(s.DropKindDrive),
            RuleKind.Folder => Cap(s.DropKindFolder),
            RuleKind.File => s.HomeOtherFiles,
            _ => label.HasGroupName ? s.ExtensionGroupName(label.GroupId!) : label.RuleId,
        };
        var sub = label.Kind switch
        {
            RuleKind.Drive => s.HomeTargetAnyDrive,
            RuleKind.Folder => s.HomeTargetAnyFolder,
            RuleKind.File => s.HomeTargetAnyFile,
            _ => s.HomeTargetFileType,
        };

        var icon = new DuoIcon { Icon = label.Icon, Width = 20, Height = 20, ColorKey = "AppPrimary", VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = title, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var subText = new TextBlock { Text = sub, TextTrimming = TextTrimming.CharacterEllipsis };
        subText.Classes.Add("small");
        subText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var nameBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, subText } };

        var matches = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (label.Kind == RuleKind.Extension)
        {
            var (shown, more) = RulePresentation.Chips(label.Extensions, 6);
            foreach (var e in shown) matches.Children.Add(Chip(e, mono: true));
            if (more > 0) matches.Children.Add(Chip("+" + more, mono: false, muted: true));
        }

        var arrow = new DuoIcon { Icon = "arrow-right", Width = 16, Height = 16, ColorKey = "AppTextMid", VerticalAlignment = VerticalAlignment.Center };
        var apps = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (rule.Choices.Count == 0) apps.Children.Add(Chip(s.HomeOsDefault, mono: false, "monitor", muted: true));
        foreach (var id in rule.Choices)
            apps.Children.Add(Chip(id, mono: false, _policy!.Apps.TryGetValue(id, out var a) ? AppIconKey(a) : "warning", accent: true));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,170,*,24,230"), ColumnSpacing = 10 };
        var cells = new Control[] { icon, nameBox, matches, arrow, apps };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }

        var row = new Border { Child = grid, Padding = new Thickness(8, 8), CornerRadius = new CornerRadius(8), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
        row.Classes.Add("row");
        var section = rule.When.Kind switch { RuleKind.Drive => RulesEditorSection.Drive, RuleKind.Folder => RulesEditorSection.Folder, RuleKind.File => RulesEditorSection.Default, _ => RulesEditorSection.Files };
        row.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) RulesEditorWindow.Open(this, section); };
        return row;
    }

    private Control RulesCard(PolicyDocument policy)
    {
        var s = Strings.Current;
        var rulesTitle = Heading(s.HomeRules);
        rulesTitle.VerticalAlignment = VerticalAlignment.Center;
        var rulesHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { rulesTitle } };
        var editRules = ToolButton("edit", s.HomeEditRules, () => RulesEditorWindow.Open(this));
        Grid.SetColumn(editRules, 1);
        rulesHeader.Children.Add(editRules);
        var rules = new StackPanel { Spacing = 2, Children = { rulesHeader } };
        var n = 1;
        foreach (var r in policy.Rules) rules.Children.Add(RuleRow(r, n++));
        if (policy.Rules.Count == 0) rules.Children.Add(Muted(s.HomeNoRules));
        return Card(rules);
    }

    // --- 앱 카드: 타일 + [열기] ---

    private Control AppTile(AppEntry app, PolicyDocument policy)
    {
        var s = Strings.Current;
        var builtIn = ToolRegistry.IsBuiltIn(app.Command);
        var icon = new DuoIcon { Icon = AppIconKey(app), Width = 22, Height = 22, ColorKey = builtIn ? "AppPrimary" : "AppTextMid", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var iconBox = new Border { Child = icon, Width = 36, Height = 36, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center };
        iconBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));

        var name = UserText(app.Id);
        name.FontWeight = FontWeight.Bold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        var cmd = UserText(string.Join(' ', new[] { app.Command }.Concat(app.Args)), "small", "mono");
        cmd.TextTrimming = TextTrimming.CharacterEllipsis;
        cmd.TextWrapping = TextWrapping.NoWrap;
        cmd.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        ToolTip.SetTip(cmd, string.Join(' ', new[] { app.Command }.Concat(app.Args)));
        var head = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, cmd } };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        Grid.SetColumn(head, 1);
        top.Children.Add(iconBox);
        top.Children.Add(head);

        var used = policy.RulesUsing(app.Id).Count;
        var usage = new TextBlock { Text = used > 0 ? s.HomeAppRuleCount(used) : s.HomeAppUnused, VerticalAlignment = VerticalAlignment.Center };
        usage.Classes.Add("small");
        usage.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (builtIn) left.Children.Add(Chip(s.HomeBuiltIn, mono: false, accent: true));
        left.Children.Add(usage);
        var open = new Button { Height = 30, Padding = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
        open.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { new DuoIcon { Icon = "play", Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = s.HomeOpenApp, VerticalAlignment = VerticalAlignment.Center } },
        };
        ToolTip.SetTip(open, s.HomeOpenAppTip);
        open.Click += async (_, _) => await OpenAppAsync(app);
        Grid.SetColumn(open, 1);
        foot.Children.Add(left);
        foot.Children.Add(open);

        var tile = new Border
        {
            Child = new StackPanel { Spacing = 10, Children = { top, foot } }, Padding = new Thickness(12), Width = 270,
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8),
        };
        tile.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        return tile;
    }

    private Control AppsCard(PolicyDocument policy)
    {
        var s = Strings.Current;
        var appsTitle = Heading(s.HomeApps);
        appsTitle.VerticalAlignment = VerticalAlignment.Center;   // 오른쪽 편집 버튼(30px)과 같은 줄 가운데에 맞춘다
        var summary = new TextBlock { Text = s.HomeAppsSummary(policy.Apps.Count, policy.Apps.Values.Count(a => ToolRegistry.IsBuiltIn(a.Command))), VerticalAlignment = VerticalAlignment.Center };
        summary.Classes.Add("small");
        summary.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { appsTitle, summary } };
        var appsHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { titleRow } };
        var editApps = ToolButton("edit", s.HomeEditApps, () => AppsEditorWindow.Open(this));
        Grid.SetColumn(editApps, 1);
        appsHeader.Children.Add(editApps);
        var tiles = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var a in policy.Apps.Values) tiles.Children.Add(AppTile(a, policy));
        var body = new StackPanel { Spacing = 2, Children = { appsHeader } };
        if (policy.Apps.Count == 0) body.Children.Add(Muted(s.HomeNoApps)); else body.Children.Add(tiles);
        return Card(body);
    }

    /// <summary>앱을 한 번 열어 본다. 외부 앱은 대상 없이 띄우고(<see cref="AppLauncher.ArgsForTryOut"/>), 내장 도구는 파일을 골라 연다.</summary>
    private async Task OpenAppAsync(AppEntry app)
    {
        var s = Strings.Current;
        try
        {
            if (ToolRegistry.IsBuiltIn(app.Command))
            {
                var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = s.HomeOpenAppPick(app.Id), AllowMultiple = AppLauncher.AcceptsMany(app),
                });
                var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
                if (paths.Count == 0) return;
                await App.Tools.OpenAsync(app.Command, paths);
            }
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var result = await App.Router.Launcher.TryOutAsync(app, home);
                if (!result.Ok) Snackbar.Error(s.DockOpenFailed(result.Error!));
            }
        }
        catch (Exception e)
        {
            Snackbar.Error(s.DockOpenFailed(e.Message), e.ToString());
        }
    }

    // --- 정책 파일 카드 ---

    private Control PolicyFileCard()
    {
        var s = Strings.Current;
        var path = UserText(App.PolicyStore.FilePath, "small");
        var reload = new Button { Content = s.HomeReload, HorizontalAlignment = HorizontalAlignment.Left };
        reload.Click += async (_, _) => await ReloadAsync();
        var openFile = new Button { Content = s.HomeOpenPolicyFile };
        openFile.Click += (_, _) => OpenPolicyFile();
        var undo = new Button { Content = s.HomeUndo, IsEnabled = App.PolicyStore.CanUndo };
        undo.Click += (_, _) => PolicyNotices.Undo();
        var restore = new Button { Content = s.HomeRestoreDefaults, IsVisible = _policy is { IsFallback: false } };
        restore.Click += async (_, _) => await RestoreDefaultsAsync();
        var fileButtons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { reload, openFile, undo, restore } };
        foreach (var b in fileButtons.Children) ((Control)b).Margin = new Thickness(0, 0, 8, 8);
        if (App.Github.CanSync)
        {
            var sync = new Button { Content = App.SyncRunning ? s.SyncRunning : s.HomeSyncNow, IsEnabled = !App.SyncRunning, Margin = new Thickness(0, 0, 8, 8) };
            sync.Click += async (_, _) => await App.SyncNowAsync();
            fileButtons.Children.Add(sync);
        }
        return Card(new StackPanel { Spacing = 8, Children = { Heading(s.HomePolicyFile), path, fileButtons } });
    }

    private TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        return t;
    }

    /// <summary>빈 상태(ui-ux.md §6): 48px 아이콘, 한 줄 안내, 주 행동 버튼 하나.</summary>
    private Control EmptyState()
    {
        var icon = new DuoIcon { Icon = "tray", ColorKey = "AppTextMid", Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        var line = Muted(Strings.Current.HomeEmpty);
        line.TextAlignment = TextAlignment.Center;
        line.HorizontalAlignment = HorizontalAlignment.Center;
        var add = new Button { Content = Strings.Current.AppsEditorAdd, Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) => AppsEditorWindow.Open(this);
        return new StackPanel { Spacing = 12, Margin = new Thickness(0, 24), Children = { icon, line, add } };
    }

    private void OpenPolicyFile()
    {
        try { new OpenWithDefaultApp().Open(App.PolicyStore.FilePath); }
        catch (FileOperationException e) { Snackbar.Error(Strings.Current.DockOpenFailed(e.Message)); }
    }

    /// <summary>규칙·앱을 기본 정책으로 바꾼다(확인한 뒤). 이전 내용은 `.backup-날짜` 로 남고 [실행 취소]로 되돌린다.</summary>
    private async Task RestoreDefaultsAsync()
    {
        var s = Strings.Current;
        var choice = await Dialogs.MessageDialog.ShowAsync(this, s.RestoreDefaultsTitle,
            s.RestoreDefaultsBody(_policy?.Rules.Count ?? 0, _policy?.Apps.Count(a => !_policy.IsImplicitApp(a.Key)) ?? 0),
            [s.HomeRestoreDefaults, s.CommonCancel], defaultIndex: 1, cancelIndex: 1);
        if (choice != 0) return;
        var result = await App.PolicyStore.RestoreDefaultsAsync();
        if (result.Error != null) { Snackbar.Error(s.RestoreDefaultsFailed(result.Error)); return; }
        App.NotifyPolicyChanged();
        PolicyNotices.Saved(s.RestoreDefaultsDone);
    }

    private async Task ResetPolicyAsync()
    {
        var result = await App.PolicyStore.ResetToDefaultsAsync();
        if (!result.Ok) { Snackbar.Error(Strings.Current.HomeResetFailed(result.Error!)); return; }
        await ReloadAsync();
        Snackbar.Info(Strings.Current.HomeResetDone(result.BackupPath));
    }

    private static string Describe(RuleCondition c) =>
        c.Kind == RuleKind.Extension ? "ext: " + string.Join(", ", c.Extensions) : c.Kind.ToString().ToLowerInvariant();
}
