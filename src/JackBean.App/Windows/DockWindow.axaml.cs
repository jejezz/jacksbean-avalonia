// 독: 화면 가장자리에 항상 떠 있는 작은 창. 드래그가 들어오면 커지고, 떨어뜨리면 정책(규칙)에 따라 앱으로 보낸다.
// 규칙이 "물어보기"이거나 맞는 규칙이 없으면 독이 아래로 늘어나 앱 목록을 보여 준다.
//
// Avalonia 는 창 크기를 DIP, 위치를 "데스크톱 좌표"로 준다 — 그 배율은 DesktopScaling(= Screen.Scaling)이고 RenderScaling 과 다를 수 있다.
// Windows 는 데스크톱 좌표가 물리 픽셀이라 둘이 같지만, macOS 는 포인트라 DesktopScaling=1, RenderScaling=2(레티나)다.
// 위치 계산에 RenderScaling 을 쓰면 macOS 에서 펼칠 때 창이 두 배 멀리 옮겨가 포인터를 벗어나고 드롭이 오지 않는다.
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
using JackBean.App.Recent;
using JackBean.App.Views;
using JackBean.Domain;
using JackBean.Domain.Window;
using JackBean.Domain.Settings;
using JackBean.Domain.Theme;
using JackBean.Services;
using JackBean.Services.DragWatch;
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
    /// <summary>독과 목록을 둘 다 벗어나고 이만큼 지나면 닫는다(그 사이 돌아오면 그대로).</summary>
    private static readonly TimeSpan RecentLinger = TimeSpan.FromMilliseconds(300);

    private readonly MenuItem OpenWindowItem = new();
    private readonly MenuItem QuitItem = new();
    private readonly MenuItem HideItem = new();
    private readonly MenuItem SettingsItem = new();
    private readonly MenuItem AboutItem = new();
    private readonly MenuItem TipItem = new();
    private readonly DockExpansion _expansion;
    private readonly DispatcherTimer _clearTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _pickTimer = new() { Interval = ChooserTimeout };
    private readonly DispatcherTimer _movedTimer = new() { Interval = MovedSaveDelay };
    /// <summary>
    /// 콩을 한 번 누르고 이만큼 기다렸다가 최근 작업 목록을 연다 — 그 사이 한 번 더 누르면(두 번 누르기 = 홈 창) 열지 않는다.
    /// 바로 여는 것도 해 봤지만 두 번 누를 때 목록이 번쩍여서 사용자가 이쪽을 골랐다.
    /// </summary>
    private static readonly TimeSpan RecentClickDelay = TimeSpan.FromMilliseconds(250);
    private readonly DispatcherTimer _recentClickTimer = new() { Interval = RecentClickDelay };
    private Task<IReadOnlyList<RecentRow>>? _recentRows; // 누르는 순간 읽기 시작한 목록 — 떼었을 때 바로 띄운다
    private readonly DispatcherTimer _recentLingerTimer = new() { Interval = RecentLinger };
    private RecentWindow? _recent;
    private bool _pointerOverDock;
    private bool _recentOpening; // 목록을 읽는 중(비동기) — 그 사이 조건이 바뀌면 띄우지 않는다

    private static readonly Lazy<Avalonia.Media.Imaging.Bitmap> DockIcon =
        new(() => new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://JacksBean/Assets/dock_icon.png"))));

    private bool _hovering;
    private bool _outsideDrag; // 다른 앱에서 끌기가 시작됐다(아직 독 위는 아님) — 미리 펼쳐서 "여기에 놓으세요"를 보인다
    private (bool, bool, bool, int, string?, bool) _renderedKey;
    private bool _renderedHover; // 마지막으로 그린 모양이 "끌어다 놓는 중"이었는지
    private IReadOnlyList<DroppedItem> _last = [];
    private string? _status;
    private bool _statusIsError;

    private IReadOnlyList<NeedsChoice>? _pickAsks;
    private IReadOnlyList<AppEntry> _pickApps = [];
    private IReadOnlyList<RuleSuggestion> _suggestions = [];
    private bool _remember;

    /// <summary>평소(정원) 크기일 때 독의 왼쪽 끝(물리 픽셀). 콩은 늘 이 자리에 있다 — 펼치면 화면 가운데 쪽으로 자란다(<see cref="GrowsRight"/>).</summary>
    private double _idleLeft;
    private bool _userMoving;
    private bool _placing;
    private PixelPoint _lastSetPos; // 코드가 마지막으로 정한 창 위치 — 이와 다르면 사용자가 옮긴 것이다

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
                    if (_hovering || _outsideDrag || _last.Count > 0 || _pickAsks != null) return;
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
        // 창(TopLevel) 자체에는 Enter/Exit 가 오지 않아서 창을 꽉 채운 Root 에 건다.
        _recentClickTimer.Tick += (_, _) => { _recentClickTimer.Stop(); ShowRecent(); };
        Root.PointerEntered += (_, _) => { _pointerOverDock = true; _recentLingerTimer.Stop(); };
        Root.PointerExited += (_, _) => { _pointerOverDock = false; if (_recent != null) { _recentLingerTimer.Stop(); _recentLingerTimer.Start(); } };
        Root.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left && OperatingSystem.IsLinux()) FinishPress(Position == _pressedAt); };   // X11 은 창 옮기기가 바로 돌아온다 — 뗄 때 판단
        _releaseWatch.Tick += (_, _) => OnReleaseWatch();
        _recentLingerTimer.Tick += (_, _) => { _recentLingerTimer.Stop(); if (!_pointerOverDock && _recent?.IsPointerInside != true) CloseRecent(); };
        DoubleTapped += (_, _) => OpenHome();
        ContextMenu = new ContextMenu { ItemsSource = new[] { OpenWindowItem, SettingsItem, AboutItem, TipItem, HideItem, QuitItem } };
        ContextMenu.Opening += (_, _) => CloseRecent();
        OpenWindowItem.Click += (_, _) => OpenHome();
        QuitItem.Click += (_, _) => App.Quit();
        HideItem.Click += (_, _) => ToggleVisible();
        SettingsItem.Click += (_, _) => SettingsWindow.Open(this);
        AboutItem.Click += (_, _) => Dialogs.AboutDialog.Open(this);
        TipItem.Click += (_, _) => App.ShowTip(atStartup: false);
        PositionChanged += OnPositionChanged;
        Opened += (_, _) => Place(App.Settings.Value.Dock);
        ActualThemeVariantChanged += (_, _) => Render();
        App.Settings.Changed += st => Dispatcher.UIThread.Post(() => { UpdateMenuText(); Render(); if (!st.RecentHistory) CloseRecent(); });
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
        TipItem.Header = Strings.Current.DockMenuShowTip;
        QuitItem.Header = Strings.Current.DockMenuQuit;
        HideItem.Header = Strings.Current.TrayHideDock;
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
        Position = _lastSetPos = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        _idleLeft = Position.X;
        Dispatcher.UIThread.Post(() => _placing = false, DispatcherPriority.Background);
    }

    /// <summary>
    /// 펼칠 때 오른쪽으로 자라는가. 콩은 포인터가 있는 자리에 그대로 있어야 하므로(펼쳐도 콩이 움직이지 않는다) 창은 콩의 반대쪽으로만 자란다 —
    /// 독이 화면 왼쪽 절반에 있으면 오른쪽으로(콩 글자), 오른쪽 절반에 있으면 왼쪽으로(글자 콩) 자라서 화면 가장자리로 밀리지 않는다.
    /// </summary>
    private bool GrowsRight
    {
        get
        {
            var scale = DesktopScaling;
            var center = new PixelPoint((int)Math.Round(_idleLeft + IdleSize * scale / 2), Position.Y + (int)Math.Round(IdleSize * scale / 2));
            var screen = ScreenAt(center);
            if (screen == null) return false;
            var work = screen.WorkingArea;
            return center.X < work.X + work.Width / 2.0;
        }
    }

    /// <summary>크기를 바꾸되 콩(정원 크기일 때의 자리)을 지킨다 — 가장자리에 붙은 독이 화면 밖으로 밀리지 않고 콩의 반대쪽으로 자란다.</summary>
    private void ResizeTo(Size size)
    {
        var scale = DesktopScaling;
        SyncIdleLeft(scale);
        _placing = true;
        var left = GrowsRight ? _idleLeft : _idleLeft + IdleSize * scale - size.Width * scale;
        var target = new PixelPoint((int)Math.Round(left), Position.Y);
        _lastSetPos = target;
        // 위치와 크기를 한 번에 바꾼다(그 사이 어긋난 프레임이 안 보이게). 안 되는 OS 에서는 차례로 바꾼다.
        NativeBounds.TrySet(this, target, new PixelSize((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)));
        Width = size.Width;
        Height = size.Height;
        if (Position != target) Position = target;
        Dispatcher.UIThread.Post(() => _placing = false, DispatcherPriority.Background);
    }

    /// <summary>창이 코드가 정한 자리에 있지 않으면 사용자가 옮긴 것이다 — 그 자리를 콩의 자리로 다시 잡는다(이벤트를 놓쳐도 원래 자리로 되돌아가지 않게).</summary>
    private void SyncIdleLeft(double scale)
    {
        if (Position == _lastSetPos) return;
        _idleLeft = GrowsRight ? Position.X : Position.X + (Width - IdleSize) * scale;
        _lastSetPos = Position;
        RememberPosition();
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        // 코드가 정한 자리로의 변경은 무시한다. 플래그(_placing)는 끌기 중에 늦게 풀릴 수 있어 위치 비교로 가른다.
        if (!_userMoving || e.Point == _lastSetPos) return;
        _idleLeft = GrowsRight ? e.Point.X : e.Point.X + (Width - IdleSize) * DesktopScaling;
        _movedTimer.Stop();
        _movedTimer.Start(); // 끌기가 멈춘 뒤 한 번만 저장한다
    }

    private void RememberPosition() =>
        App.Settings.Update(x => x with { Dock = new DockPosition(_idleLeft + IdleSize * DesktopScaling, Position.Y, DesktopScaling) });

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 누르면 열려 있던 목록은 비킨다(옮기기·메뉴·두 번 누르기). 열려 있을 때 누른 것은 "닫기"다 — 다시 열지 않는다.
        _clickOpens = _recent == null && !_recentClickTimer.IsEnabled && !_releaseWatch.IsEnabled;   // 기다리는 중에 또 눌렀다 = 두 번 누르기
        _releaseWatch.Stop();
        CloseRecent();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount > 1) { _clickOpens = false; return; }
        if (_clickOpens && CanShowRecent) _recentRows = LoadRecentRows();   // 옮기기가 끝나기를 기다리는 동안 미리 읽는다
        _userMoving = true;
        _pressedAt = Position;
        _pressedTime = DateTime.UtcNow;
        BeginMoveDrag(e);
        // 클릭인지(옮기지 않고 뗐는지) 가린다. Windows 는 BeginMoveDrag 가 떼기 전에 돌아오고 떼는 이벤트도 오지 않아 버튼 상태를 지켜본다.
        // macOS 는 떼야 돌아온다. Linux(X11)는 바로 돌아오지만 떼는 이벤트가 온다(Root.PointerReleased).
        if (OperatingSystem.IsWindows()) { _releaseWatch.Stop(); _releaseWatch.Start(); }
        else if (OperatingSystem.IsMacOS()) FinishPress(Position == _pressedAt);
    }

    private PixelPoint _pressedAt;
    private DateTime _pressedTime;
    /// <summary>누른 뒤 버튼을 뗄 때까지 30ms 마다 본다(Windows). 그 사이 독이 움직였으면 옮기기, 그대로 뗐으면 클릭.</summary>
    private readonly DispatcherTimer _releaseWatch = new() { Interval = TimeSpan.FromMilliseconds(30) };
    /// <summary>이보다 오래 누르고 있다가 떼면 클릭으로 보지 않는다(누른 채 망설인 것).</summary>
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(800);

    private void OnReleaseWatch()
    {
        if (Position != _pressedAt) { _releaseWatch.Stop(); FinishPress(clicked: false); return; }
        if (NativeMouse.IsPrimaryDown() == true) return;
        _releaseWatch.Stop();
        FinishPress(clicked: DateTime.UtcNow - _pressedTime < LongPress);
    }

    /// <summary>누름이 끝났다: 클릭이면 목록을 열 준비를, 옮기기였으면 미리 읽은 목록을 버린다.</summary>
    private void FinishPress(bool clicked)
    {
        if (clicked) { _userMoving = false; OnClicked(); }
        else { _clickOpens = false; _recentRows = null; }
    }

    private bool _clickOpens;

    /// <summary>콩을 한 번 눌렀다(옮기지 않고) — 잠깐 기다렸다가 최근 작업 목록을 연다(두 번 누르기면 열지 않는다). 목록은 누를 때 이미 읽기 시작했다.</summary>
    private void OnClicked()
    {
        if (!_clickOpens) return;
        _clickOpens = false;
        if (!CanShowRecent) return;
        PlayRipple();   // 눌렀다는 것을 바로 보인다 — 목록은 0.25초 뒤
        _recentClickTimer.Stop();
        _recentClickTimer.Start();
    }

    private readonly DispatcherTimer _rippleTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _rippleStart;
    private const double RippleFrom = 0.2, RippleOpacity = 0.6;   // 사용자 확인: 처음 값(0.35·0.42초)은 잘 안 보였다
    private static readonly TimeSpan RippleDuration = TimeSpan.FromMilliseconds(650);

    /// <summary>
    /// 안드로이드의 물결(ripple)처럼 콩 가운데에서 강조색 원이 퍼지며 사라진다. 알약이 원(정원)일 때만 불린다 — 원이 알약 안에 들어맞아 잘라 낼 필요가 없다.
    /// 프레임마다 크기·투명도를 직접 바꾼다(애니메이션 스타일 없이, NativeAOT 에도 안전).
    /// </summary>
    private void PlayRipple()
    {
        Ripple.Fill = new SolidColorBrush(Color.FromUInt32(Colors_.Primary));
        Ripple.RenderTransform = new ScaleTransform(RippleFrom, RippleFrom);
        Ripple.Opacity = RippleOpacity;
        _rippleStart = DateTime.UtcNow;
        if (!_rippleTimer.IsEnabled)
        {
            _rippleTimer.Tick -= OnRippleFrame;
            _rippleTimer.Tick += OnRippleFrame;
            _rippleTimer.Start();
        }
    }

    private void OnRippleFrame(object? sender, EventArgs e)
    {
        var t = Math.Clamp((DateTime.UtcNow - _rippleStart) / RippleDuration, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);   // 처음엔 빠르게, 끝에서 느리게
        var scale = RippleFrom + (1 - RippleFrom) * eased;
        Ripple.RenderTransform = new ScaleTransform(scale, scale);
        Ripple.Opacity = RippleOpacity * (1 - t * t);   // 끝 무렵에 빠르게 사라진다
        if (t >= 1) { _rippleTimer.Stop(); Ripple.Opacity = 0; }
    }

    private static async Task<IReadOnlyList<RecentRow>> LoadRecentRows()
    {
        try { return await RecentActions.RowsAsync(); }
        catch (Exception) { return []; }   // 못 읽으면 빈 목록으로
    }

    private void OpenHome() => App.OpenHome();

    /// <summary>독이 숨어 있으면 보인다(앱을 다시 실행했을 때 — 이미 떠 있다는 것을 보여 준다).</summary>
    public void EnsureVisible()
    {
        if (!IsVisible) Show();
        Topmost = true;
    }

    /// <summary>독을 숨기거나 다시 보인다(트레이 아이콘·전역 단축키·독 메뉴). 위치는 그대로 기억한다.</summary>
    public void ToggleVisible()
    {
        CloseRecent();
        if (IsVisible) Hide();
        else Show();
    }

    // ── 드래그와 드롭 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 다른 앱에서 끌기가 시작되거나 끝났다(<see cref="IDragWatcher"/>, 지금은 Windows 만). 시작하면 독을 미리 펼쳐 놓을 곳을 알리고,
    /// 끝나면(어디에 놓았든) 평소처럼 줄인다. 추정이라 오탐이 있을 수 있으니 펼치기만 하고 드롭 판정은 <see cref="OnDrop"/> 이 한다.
    /// </summary>
    public void OnOutsideDrag(DragSignal signal)
    {
        if (signal == DragSignal.Started)
        {
            CloseRecent();
            if (!IsVisible || _pickAsks != null || _hovering || _outsideDrag) return;
            _outsideDrag = true;
            _clearTimer.Stop();
            _last = []; // 지난 결과 대신 안내를 보인다
            _expansion.Expand(); // 창을 먼저 키운 뒤 그린다
            Render();
        }
        else if (signal == DragSignal.Ended && _outsideDrag)
        {
            _outsideDrag = false;
            if (!_hovering && _last.Count == 0) _expansion.ShrinkSoon(); // 모양은 줄이기 지연 뒤 창과 함께 바뀐다
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        CloseRecent();
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
        if (_outsideDrag) { Render(); return; } // 아직 끄는 중 — 펼친 채로 "놓으면 열립니다" → "여기에 떨어뜨리세요"
        if (_last.Count == 0) _expansion.ShrinkSoon(); // 떨어뜨린 직후에도 불릴 수 있다
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_pickAsks != null) return;
        var paths = (e.DataTransfer.TryGetFiles() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        if (!App.Settings.Value.DockDroppedOnce) App.Settings.Update(x => x with { DockDroppedOnce = true }); // 익혔다 — "콩에 놓으세요" 팁은 그만
        var items = paths.Select(p => PathClassifier.Classify(p)).ToList();
        _clearTimer.Stop();
        _hovering = false;
        _outsideDrag = false; // 놓았다 — 뒤이어 오는 "끝" 신호는 할 일이 없다
        _last = items;
        _status = null;
        _statusIsError = false;
        _expansion.Expand(); // 창을 먼저 키운 뒤 그린다
        Render();

        CloseRecent();
        var plan = await App.Router.RouteAsync(items);
        RecentActions.Remember(plan.Outcomes);
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

    // ── 최근 작업 목록 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 콩을 한 번 누르면(옮기지 않고) 연다 — 마우스를 올리는 것만으로는 열지 않는다(사용자 결정: 지나가다 뜨면 어수선하다, 클릭은 원래 아무 일도 하지 않았다).
    /// 지금 목록을 띄워도 되는가: 드래그·앱 고르기·드롭 결과 표시·그 밖의 상태가 아니고, 같은 자리에 알림이 떠 있지 않을 때.
    /// 놓은 직후에는 결과(<see cref="_last"/>)를 몇 초 보이므로 그동안도 뜨지 않는다.
    /// </summary>
    private bool CanShowRecent =>
        IsVisible && App.Settings.Value.RecentHistory && !_hovering && !_outsideDrag && _pickAsks == null && _last.Count == 0
        && !Snackbar.IsShowing && ContextMenu?.IsOpen != true;

    private async void ShowRecent()
    {
        if (_recent != null || _recentOpening || !CanShowRecent) return;
        _recentOpening = true;
        var pending = _recentRows ?? LoadRecentRows();
        _recentRows = null;
        IReadOnlyList<RecentRow> rows;
        try { rows = await pending; }
        finally { _recentOpening = false; }
        // 읽는 사이 드래그가 시작됐으면 그만둔다. 기록이 없으면 "아직 없습니다" 한 줄을 보인다(눌렀는데 아무 반응이 없으면 고장 같다).
        if (_recent != null || !CanShowRecent) return;
        var window = new RecentWindow(rows);
        _recent = window;
        window.PointerInside += inside => { _recentLingerTimer.Stop(); if (!inside) _recentLingerTimer.Start(); };
        window.Done += CloseRecent;
        window.Closed += (_, _) => { if (ReferenceEquals(_recent, window)) _recent = null; };
        window.Show();
        window.PlaceNear(this, GrowsRight);
    }

    private void CloseRecent()
    {
        _recentClickTimer.Stop();
        _recentLingerTimer.Stop();
        var window = _recent;
        _recent = null;
        window?.Close();
    }

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
        RecentActions.Remember(result.Outcomes);
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

    private bool Expanded => _hovering || _outsideDrag || _last.Count > 0;
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

    private (bool, bool, bool, int, string?, bool) StateKey() => (_hovering, _outsideDrag, _pickAsks != null, _last.Count, _status, _statusIsError);

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
        Pill.BorderBrush = DockRing.Brush(palette, _hovering || _outsideDrag, IsDark); // 다른 앱에서 끄는 중이면 색만 활성, 두께는 평소대로
        var surface = Color.FromUInt32(Colors_.Surface);
        Pill.Background = new SolidColorBrush(surface, _hovering || _outsideDrag || chooser ? 0.98 : 0.90);
        Pill.Height = chooser ? double.NaN : PillHeight;
        Pill.Width = IsCircle ? PillHeight : double.NaN;
        Pill.VerticalAlignment = chooser ? VerticalAlignment.Top : VerticalAlignment.Center;
        Pill.HorizontalAlignment = GrowsRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;   // 콩 쪽 끝에 붙는다

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
        var right = GrowsRight;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right };
        // 콩은 정원일 때와 같은 자리에 둔다(알약 안쪽 여백 13 = (72 - 46) / 2, 테두리 두께만큼 뺀다). 글자는 콩의 반대쪽에 붙는다.
        var beanGap = (PillHeight - IconSize) / 2 - (_hovering ? DockRing.ActiveWidth : DockRing.IdleWidth);
        if (!IsCircle) row.Margin = right ? new Thickness(beanGap, 0, 12, 0) : new Thickness(12, 0, beanGap, 0);
        else row.HorizontalAlignment = HorizontalAlignment.Center;
        // 싹이 난 콩만, 정지된 그림 하나 — 상태는 테두리와 글자로만 알린다.
        var bean = new Image
        {
            Source = DockIcon.Value, // 한 번만 읽어 재사용 — 다시 그릴 때마다 새로 읽으면 한 프레임 비어 보일 수 있다
            Width = IconSize, Height = IconSize,
        };
        if (right) row.Children.Add(bean);
        if (Expanded)
        {
            var text = new StackPanel { Margin = right ? new Thickness(8, 0, 6, 0) : new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = ActiveWidth - IconSize - 12 * 2 - 8 - 6 - 12 };
            // 제목은 반드시 한 줄 — 모자라면 줄바꿈 대신 글자를 줄여 맞춘다.
            text.Children.Add(new Viewbox
            {
                StretchDirection = Avalonia.Media.StretchDirection.DownOnly,
                HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Child = new TextBlock { Text = title ?? Strings.Current.DockDropHint, FontWeight = FontWeight.SemiBold, Foreground = Fg, TextWrapping = TextWrapping.NoWrap },
            });
            if (subtitle != null)
                text.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, FontFamily = new FontFamily("$Default, Apple SD Gothic Neo, Malgun Gothic, Noto Sans CJK KR, Noto Sans KR"), Foreground = _statusIsError ? Danger : FgDim, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = right ? TextAlignment.Left : TextAlignment.Right });
            row.Children.Add(text);
        }
        if (!right) row.Children.Add(bean);
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
