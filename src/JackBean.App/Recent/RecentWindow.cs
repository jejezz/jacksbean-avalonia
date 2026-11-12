// 최근 작업 목록: 독의 콩을 한 번 누르면(옮기지 않고) 독 옆에 뜬다. 언제 뜨고 닫힐지는 독이 정한다(DockWindow).
// 스낵바처럼 포커스를 가져가지 않는 별도 창이다(쓰던 앱의 입력이 끊기지 않는다). 줄을 누르면 같은 앱으로 다시 열고,
// 커서를 올린 줄에만 X(목록에서 지우기)가 보인다. 맨 아래 [모두 지우기].

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JackBean.App.Icons;
using JackBean.Domain.Drop;
using JackBean.Domain.Recent;

namespace JackBean.App.Recent;

internal sealed class RecentWindow : Window
{
    private const double ListWidth = 340;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private IReadOnlyList<RecentRow> _items;
    private Window? _dock;
    private bool _growsRight;

    /// <summary>목록이 비었다(모두 지웠거나 마지막 줄을 지웠다) 또는 줄을 눌러 다시 열었다 — 독이 닫는다.</summary>
    public event Action? Done;

    /// <summary>커서가 목록(투명 여백 포함)에 들어왔다(true)·나갔다(false). 창 자체에는 Enter/Exit 가 오지 않아 바깥 테두리에 건다.</summary>
    public event Action<bool>? PointerInside;

    public bool IsPointerInside => _root?.IsPointerOver == true;
    private readonly Border? _root;

    public RecentWindow(IReadOnlyList<RecentRow> items)
    {
        _items = items;
        Title = Domain.AppIdentity.DisplayName;
        SizeToContent = SizeToContent.Height;
        Width = ListWidth;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Opacity = 0; // 자리를 잡은 뒤에 보인다(PlaceNear)

        var title = new TextBlock { Text = Strings.Current.RecentTitle, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 2, 8, 6) };
        title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));

        var clear = new Button { Content = Strings.Current.RecentClearAll, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 2, 0) };
        clear.Bind(Button.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        clear.Click += async (_, _) =>
        {
            await App.Recent.ClearAsync();
            Done?.Invoke();
        };

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(_rows);
        if (items.Count > 0) panel.Children.Add(clear);
        else
        {
            var empty = new TextBlock { Text = Strings.Current.RecentEmpty, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 8, 6) };
            empty.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsBody"));
            empty.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            panel.Children.Add(empty);
        }

        var card = new Border { Padding = new Thickness(6, 8, 6, 6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("0 2 10 0 #40000000"), Child = panel };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        // 그림자가 잘리지 않을 여백. 투명 배경을 칠해 두어 여백 위에서도 "목록 위"로 친다(독과 목록 사이를 지날 때 닫히지 않게).
        Content = _root = new Border { Padding = new Thickness(6), Background = Brushes.Transparent, Child = card };
        _root.PointerEntered += (_, _) => PointerInside?.Invoke(true);
        _root.PointerExited += (_, _) => PointerInside?.Invoke(false);
        BuildRows();
    }

    private void BuildRows()
    {
        _rows.Children.Clear();
        foreach (var item in _items) _rows.Children.Add(Row(item));
    }

    private Control Row(RecentRow item)
    {
        var e = item.Entry;
        var icon = new DuoIcon { Icon = e.Kind switch { DropKind.Drive => "drive", DropKind.Folder => "folder", _ => "file" }, VerticalAlignment = VerticalAlignment.Center };

        var name = new TextBlock
        {
            Text = e.SingleName is { } single ? RecentHistory.Shorten(single) : Strings.Current.DropSummary(KindLabel(e.Kind), e.Paths.Count),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        if (e.SingleName != null) name.Classes.Add("user");   // 파일 이름은 시스템 글꼴(fonts.md)
        name.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsBody"));
        name.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextHi"));

        var arrow = new DuoIcon { Icon = "arrow-right", ColorKey = "AppTextMid", Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var app = new TextBlock { Text = e.AppId, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        app.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        app.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));

        // X 는 커서를 올린 줄에만 — 자리는 늘 차지해서 커서를 올려도 글자가 밀리지 않는다.
        var remove = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = "x" }, Opacity = 0, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(remove, Strings.Current.RecentRemove);
        remove.Click += async (_, args) =>
        {
            args.Handled = true;
            await App.Recent.RemoveAsync(e);
            _items = _items.Where(i => !ReferenceEquals(i, item)).ToList();
            if (_items.Count == 0) { Done?.Invoke(); return; }
            BuildRows();
            PlaceNear(_dock, _growsRight);   // 높이가 줄었다 — 위에 떠 있으면 독 쪽 끝을 맞춘다
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(icon);
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        Grid.SetColumn(arrow, 2); grid.Children.Add(arrow);
        Grid.SetColumn(app, 3); grid.Children.Add(app);
        Grid.SetColumn(remove, 4); grid.Children.Add(remove);

        var row = new Border { Padding = new Thickness(8, 3, 2, 3), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = grid, MinHeight = 34 };
        if (item.CanOpen)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(row, string.Join(Environment.NewLine, e.Paths.Take(5).Append(Strings.Current.RecentOpenAgain(e.AppId))));
        }
        else
        {
            grid.Children[0].Opacity = name.Opacity = arrow.Opacity = app.Opacity = 0.45;
            ToolTip.SetTip(row, item.Reason);
        }
        row.PointerEntered += (_, _) =>
        {
            row.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppHover"));
            remove.Opacity = 1; remove.IsHitTestVisible = true;
        };
        row.PointerExited += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            remove.Opacity = 0; remove.IsHitTestVisible = false;
        };
        row.PointerReleased += async (_, args) =>
        {
            // X 는 자기가 처리한다(Button 이 Handled 로 표시해 여기까지 오지 않는다).
            if (args.InitialPressMouseButton != MouseButton.Left || !item.CanOpen) return;
            if (!row.Bounds.Contains(args.GetPosition(row.Parent as Visual))) return;   // 누른 채 밖으로 나가서 뗐다
            Done?.Invoke();
            await RecentActions.ReopenAsync(item);
        };
        return row;
    }

    private static string KindLabel(DropKind? kind) => kind switch
    {
        DropKind.Drive => Strings.Current.DropKindDrive,
        DropKind.Folder => Strings.Current.DropKindFolder,
        DropKind.File => Strings.Current.DropKindFile,
        _ => Strings.Current.DropKindMixed,
    };

    /// <summary>
    /// 독 아래에(화면 아래로 넘치면 위에) 붙여 놓는다. 가로는 콩 쪽 끝을 맞춘다 — 독이 화면 왼쪽 절반이면 왼쪽 끝, 오른쪽 절반이면 오른쪽 끝.
    /// 독과 목록이 맞닿게 두어(사이 틈 없음) 커서를 옮기는 동안 둘 중 어느 하나 위에 있게 한다.
    /// </summary>
    public void PlaceNear(Window? dock, bool growsRight)
    {
        _dock = dock;
        _growsRight = growsRight;
        Dispatcher.UIThread.Post(() =>
        {
            var screen = (dock != null ? dock.Screens.ScreenFromWindow(dock) : null) ?? Screens.Primary;
            if (screen == null || dock == null) { Opacity = 1; return; }
            var scale = DesktopScaling;
            var work = screen.WorkingArea;
            var w = (int)Math.Ceiling(Bounds.Width * scale);
            var h = (int)Math.Ceiling(Bounds.Height * scale);
            var dockScale = dock.DesktopScaling;
            var left = dock.Position.X;
            var right = left + (int)Math.Round(dock.Bounds.Width * dockScale);
            var top = dock.Position.Y;
            var bottom = top + (int)Math.Round(dock.Bounds.Height * dockScale);
            var x = growsRight ? left : right - w;
            var y = bottom + h <= work.Bottom ? bottom : top - h;
            x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w));
            y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - h));
            Position = new PixelPoint(x, y);
            Opacity = 1;
        }, DispatcherPriority.Loaded);
    }
}
