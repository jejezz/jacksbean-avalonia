using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace JackBean.App.Dialogs;

/// <summary>
/// 한 줄 입력 대화상자(다른 이름으로 저장, 줄 이동). Enter 로 확정하고 Esc 로 닫는다.
/// 확정이 실패하면 열어 둔 채 <see cref="SetError"/> 로 오류를 보여 준다(입력한 글은 그대로).
/// </summary>
public sealed class TextPromptDialog : DialogBase
{
    readonly TextBox _input = new();
    readonly TextBlock _error = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap };

    public TextPromptDialog(string title, string? prompt, string text, int selectStart, int selectEnd, string confirmLabel)
    {
        Title = WindowTitle.Of(title);
        MinWidth = 440;

        var heading = new TextBlock { Text = title, FontWeight = FontWeight.Bold };
        heading.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        _error.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        _error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppDanger"));

        var cancel = new Button { Content = Strings.Current.CommonCancel };
        var ok = new Button { Content = confirmLabel, Classes = { "accent" } };
        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) => Submit();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0), Children = { cancel, ok },
        };

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(heading);
        if (prompt is not null) panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_input);
        panel.Children.Add(_error);
        panel.Children.Add(buttons);
        Content = panel;

        _input.Text = text;
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Submit();
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _input.Focus();
            _input.SelectionStart = selectStart;
            _input.SelectionEnd = selectEnd;
        });
    }

    /// <summary>확정(Enter 또는 확인 버튼). 값은 입력한 글.</summary>
    public event Action<string>? Confirmed;

    void Submit() => Confirmed?.Invoke(_input.Text ?? "");

    public void SetError(string? message)
    {
        _error.Text = message;
        _error.IsVisible = !string.IsNullOrEmpty(message);
        _input.Focus();
    }
}
