using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace JackBean.App.Dialogs;

/// <summary>
/// 버튼 여러 개 중 하나를 고르는 짧은 질문 창 (저장 여부, 덮어쓰기 확인 등). 결과는 고른 버튼의 번호이고,
/// 창을 그냥 닫으면(X, Esc) <c>cancelIndex</c>번을 고른 것으로 본다.
/// </summary>
public sealed class MessageDialog : DialogBase
{
    readonly TaskCompletionSource<int> _result = new();

    /// <param name="side">단추 줄 왼쪽 끝의 글자 단추 (예: 릴리스 노트 보기). 눌러도 창은 닫히지 않는다.</param>
    /// <param name="buttons">왼쪽에서 오른쪽 순서. 기본 버튼은 주색이고 처음에 초점을 받는다.</param>
    public static Task<int> ShowAsync(Window owner, string title, string message, string[] buttons, int defaultIndex, int cancelIndex,
        (string Label, Action Run)? side = null)
    {
        var dialog = new MessageDialog(title, message, buttons, defaultIndex, cancelIndex, side);
        _ = dialog.ShowDialog(owner);
        return dialog._result.Task;
    }

    /// <summary>고른 버튼의 번호를 이벤트로 알리는 형태 (홈 상태가 대화상자를 관리할 때). 창을 그냥 닫으면 <see cref="DialogBase.Dismissed"/>.</summary>
    public static MessageDialog Create(string title, string message, string[] buttons, int defaultIndex, int cancelIndex) =>
        new(title, message, buttons, defaultIndex, cancelIndex);

    public event Action<int>? Chosen;

    MessageDialog(string title, string message, string[] buttons, int defaultIndex, int cancelIndex, (string Label, Action Run)? side = null)
    {
        Title = WindowTitle.Of(title);
        MinWidth = 440;
        MaxWidth = 440;   // WINDOW_CHROME.md §3

        var heading = new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) };
        heading.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        var body = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button? focus = null;
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i] };
            if (i == defaultIndex) { button.Classes.Add("accent"); focus = button; }
            button.Click += (_, _) =>
            {
                _result.TrySetResult(index);
                Chosen?.Invoke(index);
                CloseFromState();
            };
            row.Children.Add(button);
        }

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (side is { } extra)
        {
            var sideButton = new Button { Content = extra.Label, Margin = new Thickness(0, 0, 16, 0) };
            sideButton.Click += (_, _) => extra.Run();
            DockPanel.SetDock(sideButton, Dock.Left);
            footer.Children.Add(sideButton);
        }
        footer.Children.Add(row);

        Content = new StackPanel { Margin = new Thickness(24), Spacing = 8, Children = { heading, body, footer } };
        Dismissed += () => _result.TrySetResult(cancelIndex);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => focus?.Focus());
    }
}
