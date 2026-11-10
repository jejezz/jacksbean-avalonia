// 독: 화면 가장자리에 항상 떠 있는 작은 창. 드래그가 들어오면 커지고, 떨어뜨리면 정책(규칙)에 따라 앱으로 보낸다.
// 규칙이 "물어보기"이거나 맞는 규칙이 없으면 독이 아래로 늘어나 앱 목록을 보여 준다.
//
// Avalonia 는 창 크기를 DIP, 위치를 물리 픽셀로 주고 모니터별 배율(Screen.Scaling)을 직접 알려 준다.
// 독 ↔ 창 전환은 없다 — 창(홈, 도구)은 **별도 창**으로 독과 함께 뜬다.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JackBean.App.Notifications;
using JackBean.App.Views;
using JackBean.Domain;
using JackBean.Domain.Window;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;
using JackBean.Services;
using JackBean.Services.Updates;
using JackBean.Domain.Dock;
using JackBean.Domain.Drop;
using JackBean.Domain.Policy;

namespace JackBean.App.Windows;

public partial class DockWindow : Window
{
    private const double IdleSize = 84;
    private const double ActiveWidth = 250;
    private const double RowHeight = 40;
    private const double PillHeight = 72;
    private const double IconSize = 46;
    private static readonly TimeSpan ChooserTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MovedSaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly MenuItem OpenWindowItem = new();
    private readonly MenuItem QuitItem = new();
    private readonly MenuItem SettingsItem = new();
    private readonly MenuItem AboutItem = new();
    private readonly DockExpansion _expansion;
    private readonly DispatcherTimer _clearTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _pickTimer = new() { Interval = ChooserTimeout };
    private readonly DispatcherTimer _movedTimer = new() { Interval = MovedSaveDelay };

    private static readonly Lazy<Avalonia.Media.Imaging.Bitmap> DockIcon =
        new(() => new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://JacksBean/Assets/dock_icon.png"))));

    private bool _hovering;
    private (bool, bool, int, string?, bool) _renderedKey;
    private bool _renderedHover; // 마지막으로 그린 모양이 "끌어다 놓는 중"이었는지
    private IReadOnlyList<DroppedItem> _last = [];
    private string? _status;
    private bool _statusIsError;

    private IReadOnlyList<NeedsChoice>? _pickAsks;
    private IReadOnlyList<AppEntry> _pickApps = [];
    private IReadOnlyList<RuleSuggestion> _suggestions = [];
    private bool _remember;

    /// <summary>독의 오른쪽 끝(물리 픽셀). 크기가 바뀔 때 이 끝을 지키며 왼쪽으로 자란다.</summary>
    private double _right;
    private bool _userMoving;
    private bool _placing;

    public DockWindow()
    {
        InitializeComponent();
        // 줄이기는 스레드 풀 타이머에서 불리므로 UI 스레드로 넘겨서 창 속성을 만진다.
        _expansion = new DockExpansion(expanded =>
        {
            void Apply()
            {
                if (!expanded)
                {
                    // 기다리는 사이 다시 들어왔거나 결과를 보여 주는 중이면 줄이지 않는다.
                    if (_hovering || _last.Count > 0 || _pickAsks != null) return;
                    RenderIfChanged(); // 모양(알약 → 정원)도 창과 함께, 지연 뒤에 한 번만 바뀐다
                }
                ResizeTo(expanded ? new Size(ActiveWidth, IdleSize) : new Size(IdleSize, IdleSize));
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
        });

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        PointerPressed += OnPointerPressed;
        DoubleTapped += (_, _) => OpenHome();
        ContextMenu = new ContextMenu { ItemsSource = new[] { OpenWindowItem, SettingsItem, AboutItem, QuitItem } };
        OpenWindowItem.Click += (_, _) => OpenHome();
        QuitItem.Click += (_, _) => App.Quit();
        SettingsItem.Click += (_, _) => SettingsWindow.Open(this);
        AboutItem.Click += (_, _) => Dialogs.AboutDialog.Open(this);
        PositionChanged += OnPositionChanged;
        Opened += (_, _) => Place(App.Settings.Value.Dock);
        ActualThemeVariantChanged += (_, _) => Render();
        App.Settings.Changed += _ => Dispatcher.UIThread.Post(() => { UpdateMenuText(); Render(); });
        Strings.Changed += () => Dispatcher.UIThread.Post(() => { UpdateMenuText(); Render(); });
        _clearTimer.Tick += (_, _) => { _clearTimer.Stop(); _last = []; _expansion.ShrinkSoon(); }; // 모양은 줄이기 지연 뒤 창과 함께 바뀐다
        _pickTimer.Tick += (_, _) => CloseChooser();
        _movedTimer.Tick += (_, _) => { _movedTimer.Stop(); _userMoving = false; RememberPosition(); };
        SettingsItem.InputGesture = new KeyGesture(Key.OemComma, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        AboutItem.InputGesture = new KeyGesture(Key.F1);
        UpdateMenuText();
        Render();
    }

    private bool IsDark => ThemeService.IsDark(Application.Current!);
    private AppColorSet Colors_ => AppColors.For(App.Settings.Value.Palette, IsDark);
    private IBrush Fg => new SolidColorBrush(Color.FromUInt32(Colors_.TextHi));
    private IBrush FgDim => new SolidColorBrush(Color.FromUInt32(Colors_.TextMid));
    private IBrush Danger => new SolidColorBrush(Color.FromUInt32(Colors_.Danger));

    private void UpdateMenuText()
    {
        OpenWindowItem.Header = Strings.Current.DockMenuOpenWindow;
        SettingsItem.Header = Strings.Current.SettingsTooltip;
        AboutItem.Header = Strings.Current.AboutTooltip;
        QuitItem.Header = Strings.Current.DockMenuQuit;
    }

    // ── 창 위치와 크기 ──────────────────────────────────────────────────────────────

    private Screen? ScreenAt(PixelPoint p) => Screens.ScreenFromPoint(p) ?? Screens.Primary ?? Screens.All.FirstOrDefault();

    /// <summary>저장한 자리가 있으면 그 자리(작업 영역 안으로 끌어들여서), 없으면 주 모니터 오른쪽 가장자리 세로 가운데.</summary>
    private void Place(DockPosition? at)
    {
        var screen = at == null ? Screens.Primary : ScreenAt(new PixelPoint((int)at.Right - 1, (int)at.Top + 1));
        if (screen == null) return;
        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        var size = new Size(IdleSize, IdleSize);
        double left, top;
        if (at != null)
        {
            var r = at.Fit(new SizeD(size.Width, size.Height), new RectD(work.X, work.Y, work.Width, work.Height), scale);
            left = r.Left; top = r.Top;
        }
        else
        {
            left = work.Right - size.Width * scale - 8 * scale;
            top = work.Y + (work.Height - size.Height * scale) / 2;
        }
        _placing = true;
        Width = size.Width; Height = size.Height;
        Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        _right = left + size.Width * scale;
        Dispatcher.UIThread.Post(() => _placing = false, DispatcherPriority.Background);
    }

    /// <summary>크기를 바꾸되 오른쪽 끝을 지킨다 — 가장자리에 붙은 독이 화면 밖으로 밀리지 않고 왼쪽으로 자란다.</summary>
    private void ResizeTo(Size size)
    {
        var scale = RenderScaling;
        _placing = true;
        var target = new PixelPoint((int)Math.Round(_right - size.Width * scale), Position.Y);
        // 위치와 크기를 한 번에 바꾼다(그 사이 어긋난 프레임이 안 보이게). 안 되는 OS 에서는 차례로 바꾼다.
        NativeBounds.TrySet(this, target, new PixelSize((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)));
        Width = size.Width;
        Height = size.Height;
        if (Position != target) Position = target;
        Dispatcher.UIThread.Post(() => _placing = false, DispatcherPriority.Background);
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_placing || !_userMoving) return;
        _right = e.Point.X + Width * RenderScaling;
        _movedTimer.Stop();
        _movedTimer.Start(); // 끌기가 멈춘 뒤 한 번만 저장한다
    }

    private void RememberPosition() =>
        App.Settings.Update(x => x with { Dock = new DockPosition(Position.X + Width * RenderScaling, Position.Y, RenderScaling) });

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
        _userMoving = true;
        BeginMoveDrag(e);
    }

    private void OpenHome() => App.OpenHome();

    // ── 드래그와 드롭 ───────────────────────────────────────────────────────────────

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _clearTimer.Stop();
        if (_pickAsks != null) return; // 고르는 중에는 새 드래그를 받지 않는다
        _hovering = true;
        _last = [];
        // 이미 알약으로 그려져 있으면 다시 그리지 않는다 — 자식 컨트롤을 갈아 끼우면 포인터 아래 대상이 바뀌어
        // Leave/Enter 가 다시 불리고, 그것이 깜박임으로 이어진다.
        // 창을 먼저 키우고 그 다음에 그린다 — 반대로 하면 넓어진 알약이 아직 작은 창에 갇혀 잘려 보인다.
        _expansion.Expand(); // 키우기는 바로
        if (!_renderedHover) Render();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _pickAsks == null ? DragDropEffects.Copy : DragDropEffects.None;
        if (_pickAsks != null) return;
        // 위에 머무는 동안은 줄이기 예약을 계속 취소한다(Leave 가 자식 경계에서 잘못 불려도 안전).
        _hovering = true;
        _expansion.Expand();
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_pickAsks != null) return;
        _hovering = false; // 상태만 — 모양과 창 크기는 지연(ShrinkDelay) 뒤 함께 줄인다
        if (_last.Count == 0) _expansion.ShrinkSoon(); // 떨어뜨린 직후에도 불릴 수 있다
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_pickAsks != null) return;
        var paths = (e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        var items = paths.Select(p => PathClassifier.Classify(p)).ToList();
        _clearTimer.Stop();
        _hovering = false;
        _last = items;
        _status = null;
        _statusIsError = false;
        _expansion.Expand(); // 창을 먼저 키운 뒤 그린다
        Render();

        var plan = await App.Router.RouteAsync(items);
        if (plan.Policy.Warnings.Count > 0)
            Snackbar.Error(Strings.Current.PolicyWarningsFound, string.Join(Environment.NewLine, plan.Policy.Warnings),
                plan.Policy.IsFallback ? Strings.Current.SnackbarOpenHome : null, plan.Policy.IsFallback ? App.OpenHome : null); // 파일을 못 읽었으면 복구 버튼이 있는 홈 창으로
        var failure = plan.Failures.FirstOrDefault();
        var missing = plan.Outcomes.OfType<AppNotInPolicy>().FirstOrDefault();
        var opened = plan.Outcomes.OfType<Opened>().FirstOrDefault();
        if (failure != null) { SetError(Strings.Current.DockOpenFailed(failure.Error)); }
        else if (missing != null) { SetError(Strings.Current.DockAppMissing(missing.AppId)); }
        else if (opened != null) _status = Strings.Current.DockOpenedWith(opened.App.Id);
        else if (plan.AskOutcomes.Count > 0 && plan.ChoiceApps().Count == 0) SetError(Strings.Current.DockNoCommonApp); // 종류가 다른 항목을 함께 놓아 겹치는 앱이 없다 — 조용히 사라지지 않게
        Render();

        var asks = plan.AskOutcomes;
        if (asks.Count > 0 && plan.ChoiceApps() is { Count: > 0 } choices) OpenChooser(asks, choices);
        else ScheduleClear();
    }

    /// <summary>알약 부제목에 오류를 올리고, 원문을 복사할 수 있는 알림도 띄운다(알약은 한 줄이라 길면 잘린다).</summary>
    private void SetError(string message, string? actionLabel = null, Action? action = null)
    {
        _status = message;
        _statusIsError = true;
        Snackbar.Error(message, null, actionLabel, action);
    }


    private void ScheduleClear() { _clearTimer.Stop(); _clearTimer.Start(); }

    // ── 앱 고르기 ───────────────────────────────────────────────────────────────────

    private void OpenChooser(IReadOnlyList<NeedsChoice> asks, IReadOnlyList<AppEntry> apps)
    {
        _clearTimer.Stop();
        _pickAsks = asks;
        _pickApps = apps;
        _suggestions = RuleSuggester.Suggest(asks);
        _remember = false;
        Render();
        // 제목 28 + 줄마다 40(앱들 + 취소, "항상 이렇게 열기"가 있으면 한 줄 더) + 바깥 여백 12 + 안쪽 여백 16 + 여유 4
        var rows = apps.Count + 1 + (_suggestions.Count == 0 ? 0 : 1);
        ResizeTo(new Size(ActiveWidth, 28 + rows * RowHeight + 12 + 16 + 4));
        _pickTimer.Stop();
        _pickTimer.Start();
    }

    private void CloseChooser()
    {
        _pickTimer.Stop();
        if (_pickAsks == null) return;
        _pickAsks = null; _pickApps = []; _suggestions = []; _last = [];
        Render();
        ResizeTo(new Size(ActiveWidth, IdleSize));
        _expansion.ShrinkSoon();
    }

    private async void Choose(AppEntry app)
    {
        var asks = _pickAsks;
        if (asks == null) return;
        _pickTimer.Stop();
        var result = await App.Router.ChooseAsync(app, asks, _remember && _suggestions.Count > 0);
        var failure = result.Failures.FirstOrDefault();
        if (failure != null) { SetError(Strings.Current.DockOpenFailed(failure.Error)); }
        else if (result.SaveError != null) { SetError(Strings.Current.DockRuleSaveFailed(result.SaveError)); }
        else if (result.SavedRules.Count > 0) { App.NotifyPolicyChanged(); _status = $"{Strings.Current.DockOpenedWith(app.Id)} · {Strings.Current.DockRuleSaved}"; _statusIsError = false; }
        else { _status = Strings.Current.DockOpenedWith(app.Id); _statusIsError = false; }
        _last = asks.Select(a => a.Item).ToList();
        _pickAsks = null; _pickApps = []; _suggestions = [];
        Render();
        ResizeTo(new Size(ActiveWidth, IdleSize));
        ScheduleClear();
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────────────

    private bool Expanded => _hovering || _last.Count > 0;
    private bool IsCircle => _pickAsks == null && !Expanded;

    private static string KindLabel(DropKind? kind) => kind switch
    {
        DropKind.Drive => Strings.Current.DropKindDrive,
        DropKind.Folder => Strings.Current.DropKindFolder,
        DropKind.File => Strings.Current.DropKindFile,
        DropKind.Missing => Strings.Current.DropKindMissing,
        _ => Strings.Current.DropKindMixed,
    };

    private string RememberLabel() => Strings.Current.DockAlwaysOpen(string.Join(", ", _suggestions.Select(s => s.Kind switch
    {
        SuggestionKind.Extension => Strings.Current.DockSuggestExtension(s.Arg),
        SuggestionKind.Folder => Strings.Current.DockSuggestFolder,
        _ => Strings.Current.DockSuggestDrive,
    })));

    /// <summary>화면 상태가 마지막으로 그린 것과 같으면 다시 그리지 않는다 — 같은 모양을 또 그리면 알약 안의 컨트롤을
    /// 갈아 끼우느라 한 프레임 깜박인다. 테마·언어·설정이 바뀐 경우는 <see cref="Render"/> 를 직접 부른다.</summary>
    private void RenderIfChanged()
    {
        if (_renderedKey == StateKey()) return;
        Render();
    }

    private (bool, bool, int, string?, bool) StateKey() => (_hovering, _pickAsks != null, _last.Count, _status, _statusIsError);

    private void Render()
    {
        var palette = App.Settings.Value.Palette;
        var chooser = _pickAsks != null;
        _renderedHover = _hovering;
        _renderedKey = StateKey();
        PillContent.IsHitTestVisible = chooser; // 글자·그림이 드롭 대상을 가로채 Leave/Enter 를 만들지 않게
        var radius = chooser ? 20 : 36;
        Pill.CornerRadius = new CornerRadius(radius);
        // 평소엔 테마 강조색 2px, 끌어다 놓으려고 들어왔을 때는 4px 그라데이션.
        Pill.BorderThickness = new Thickness(_hovering ? DockRing.ActiveWidth : DockRing.IdleWidth);
        Pill.BorderBrush = DockRing.Brush(palette, _hovering, IsDark);
        var surface = Color.FromUInt32(Colors_.Surface);
        Pill.Background = new SolidColorBrush(surface, _hovering || chooser ? 0.98 : 0.90);
        Pill.Height = chooser ? double.NaN : PillHeight;
        Pill.Width = IsCircle ? PillHeight : double.NaN;
        Pill.VerticalAlignment = chooser ? VerticalAlignment.Top : VerticalAlignment.Center;

        PillContent.Children.Clear();
        PillContent.Children.Add(chooser ? BuildChooser() : BuildPill());
    }

    private Control BuildPill()
    {
        string? title = null, subtitle = null;
        if (_hovering) title = Strings.Current.DockDropRelease;
        else if (_last.Count > 0)
        {
            title = Strings.Current.DropSummary(KindLabel(PathClassifier.CommonKind(_last)), _last.Count);
            subtitle = _status ?? _last[0].Name;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (!IsCircle) row.Margin = new Thickness(12, 0);
        // 싹이 난 콩만, 정지된 그림 하나 — 상태는 테두리와 글자로만 알린다.
        row.Children.Add(new Image
        {
            Source = DockIcon.Value, // 한 번만 읽어 재사용 — 다시 그릴 때마다 새로 읽으면 한 프레임 비어 보일 수 있다
            Width = IconSize, Height = IconSize,
        });
        if (Expanded)
        {
            var text = new StackPanel { Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = ActiveWidth - IconSize - 12 * 2 - 8 - 6 - 12 };
            // 제목은 반드시 한 줄 — 모자라면 줄바꿈 대신 글자를 줄여 맞춘다.
            text.Children.Add(new Viewbox
            {
                StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = title ?? Strings.Current.DockDropHint, FontWeight = FontWeight.SemiBold, Foreground = Fg, TextWrapping = TextWrapping.NoWrap },
            });
            if (subtitle != null)
                text.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, FontFamily = new FontFamily("$Default, Apple SD Gothic Neo, Malgun Gothic, Noto Sans CJK KR, Noto Sans KR"), Foreground = _statusIsError ? Danger : FgDim, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(text);
        }
        return row;
    }

    /// <summary>어떤 앱으로 열지 묻는 목록. 앱마다 한 줄, 마지막이 취소.</summary>
    private Control BuildChooser()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Current.DockChooseTitle, Height = 28, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (_suggestions.Count > 0)
        {
            var box = new CheckBox { IsChecked = _remember, Content = new TextBlock { Text = RememberLabel(), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Fg }, Height = RowHeight };
            box.IsCheckedChanged += (_, _) => _remember = box.IsChecked == true;
            panel.Children.Add(box);
        }
        foreach (var app in _pickApps)
        {
            var a = app;
            var b = new Button { Content = a.Id, Height = RowHeight, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Foreground = Fg };
            b.Click += (_, _) => Choose(a);
            panel.Children.Add(b);
        }
        var cancel = new Button { Content = Strings.Current.DockChooseCancel, Height = RowHeight, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Foreground = FgDim };
        cancel.Click += (_, _) => CloseChooser();
        panel.Children.Add(cancel);
        return panel;
    }
}
