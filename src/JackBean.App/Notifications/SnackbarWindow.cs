// 알림(스낵바) 창: 독 옆에 잠깐 떠서 오류를 알리고, 원문을 복사할 수 있게 한다 (ui-ux.md §6).
// 멀티 윈도우 구조라 홈 창이 닫혀 있어도 뜰 수 있도록 별도 창이다. 포커스를 가져가지 않고(ShowActivated=false),
// 마우스를 올려 두는 동안은 닫히지 않는다.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JackBean.App.Icons;
using JackBean.Services.Notifications;

namespace JackBean.App.Notifications;

internal sealed class SnackbarWindow : Window
{
    private readonly Notification _notification;
    private readonly DispatcherTimer _closeTimer = new();
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly Button _copyButton;
    private readonly DuoIcon _copyIcon = new() { Icon = "copy" };

    public SnackbarWindow(Notification notification, string? actionLabel = null, Action? action = null)
    {
        _notification = notification;
        var isError = notification.Kind == NotificationKind.Error;

        Title = Domain.AppIdentity.DisplayName;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Opacity = 0; // 자리를 잡은 뒤에 보인다(PlaceNear)

        var icon = new DuoIcon { Icon = isError ? "warning" : "check", ColorKey = isError ? "AppDanger" : "AppPrimary", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var text = new TextBlock { Text = notification.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsBody"));
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextHi"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        if (actionLabel != null && action != null)
        {
            var act = new Button { Content = actionLabel, VerticalAlignment = VerticalAlignment.Center };
            act.Click += (_, _) => { action(); Close(); };
            buttons.Children.Add(act);
        }
        _copyButton = ToolButton(_copyIcon, Strings.Current.SnackbarCopy, CopyAsync);
        if (isError) buttons.Children.Add(_copyButton);
        buttons.Children.Add(ToolButton(new DuoIcon { Icon = "x" }, Strings.Current.SnackbarClose, Close));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var card = new Border { Padding = new Thickness(14, 10, 8, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("0 2 10 0 #40000000"), Child = grid };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable(isError ? "AppDanger" : "AppStroke"));
        Content = new Border { Padding = new Thickness(12), Child = card }; // 그림자가 잘리지 않을 여백

        var duration = NotificationGate.DurationOf(notification.Kind);
        if (actionLabel != null && action != null && duration < NotificationGate.ActionDuration) duration = NotificationGate.ActionDuration;   // 누를 시간을 준다
        _closeTimer.Interval = duration;
        _closeTimer.Tick += (_, _) => Close();
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); ShowCopied(false); };
        PointerEntered += (_, _) => _closeTimer.Stop();   // 읽고 복사하는 동안은 닫지 않는다
        PointerExited += (_, _) => { _closeTimer.Stop(); _closeTimer.Start(); };
        Opened += (_, _) => _closeTimer.Start();
        Closed += (_, _) => { _closeTimer.Stop(); _copiedTimer.Stop(); };
    }

    private static Button ToolButton(DuoIcon icon, string tooltip, Action click)
    {
        var b = new Button { Classes = { "tool" }, Content = icon };
        ToolTip.SetTip(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    private async void CopyAsync()
    {
        try
        {
            if (GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(_notification.CopyText);
            ShowCopied(true);
        }
        catch (Exception)
        {
            // 클립보드를 못 잡아도 알림은 그대로 둔다(다시 누를 수 있다).
        }
    }

    private void ShowCopied(bool copied)
    {
        _copyIcon.Icon = copied ? "check" : "copy";
        ToolTip.SetTip(_copyButton, copied ? Strings.Current.SnackbarCopied : Strings.Current.SnackbarCopy);
        _closeTimer.Stop();
        _closeTimer.Start(); // 복사한 뒤에도 읽을 시간을 더 준다
        if (copied) { _copiedTimer.Stop(); _copiedTimer.Start(); }
    }

    /// <summary>독 아래(화면 아래로 넘치면 위)에, 독의 오른쪽 끝에 맞춰 놓는다. 독이 없으면 주 모니터 오른쪽 아래.</summary>
    public void PlaceNear(Window? dock)
    {
        // 크기는 첫 배치가 끝나야 알 수 있다.
        Dispatcher.UIThread.Post(() =>
        {
            var screen = (dock != null ? dock.Screens.ScreenFromWindow(dock) : null) ?? Screens.Primary;
            if (screen != null)
            {
                var scale = DesktopScaling;
                var work = screen.WorkingArea;
                var w = (int)Math.Ceiling(Bounds.Width * scale);
                var h = (int)Math.Ceiling(Bounds.Height * scale);
                int x, y;
                if (dock != null)
                {
                    var dockScale = dock.DesktopScaling;
                    var right = dock.Position.X + (int)Math.Round(dock.Bounds.Width * dockScale);
                    var top = dock.Position.Y;
                    var bottom = top + (int)Math.Round(dock.Bounds.Height * dockScale);
                    x = right - w;
                    y = bottom;
                    if (y + h > work.Bottom) y = top - h;
                }
                else { x = work.Right - w; y = work.Bottom - h; }
                x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w));
                y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - h));
                Position = new PixelPoint(x, y);
            }
            Opacity = 1;
        }, DispatcherPriority.Loaded);
    }
}
