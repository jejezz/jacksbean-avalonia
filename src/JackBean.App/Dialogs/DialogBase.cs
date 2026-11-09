using Avalonia.Controls;

namespace JackBean.App.Dialogs;

/// <summary>
/// 짧은 모달 질문 창의 공통 바탕 (복사 대상·삭제 확인·이름 입력·이름 충돌). 내용 크기에 맞춰 열리고 크기를 바꿀 수 없다.
/// 사용자가 창을 닫으면(X, Esc) <see cref="Dismissed"/>가 불리고, 상태가 바뀌어서 프로그램이 닫으면 불리지 않는다.
/// </summary>
public abstract class DialogBase : Window
{
    bool _closedByState;

    protected DialogBase()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("dialog");
    }

    /// <summary>사용자가 확정하지 않고 창을 닫았다.</summary>
    public event Action? Dismissed;

    /// <summary>상태가 바뀌어서 닫는다 (<see cref="Dismissed"/>를 부르지 않는다).</summary>
    public void CloseFromState()
    {
        _closedByState = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_closedByState) Dismissed?.Invoke();
    }
}
