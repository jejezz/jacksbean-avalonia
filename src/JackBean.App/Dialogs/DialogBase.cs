using Avalonia.Controls;

namespace JackBean.App.Dialogs;

/// <summary>
/// 짧은 모달 질문 창의 공통 바탕 (복사 대상·삭제 확인·이름 입력·이름 충돌·정보·업데이트 안내).
/// WINDOW_CHROME.md 의 분류 A(인-윈도 모달)이다: OS 타이틀바 없음(<c>WindowDecorations=None</c>), 작업 표시줄에 안 보임,
/// 소유 창 가운데에 열리고, 앱이 직접 그린 둥근 테두리(<c>Border.dialogframe</c>)를 가진다. 제목은 본문 첫 줄이 맡는다
/// (<see cref="Window.Title"/> 은 접근성·Alt+Tab 용으로만 둔다). 내용 크기에 맞춰 열리고 크기를 바꿀 수 없다.
/// 사용자가 창을 닫으면(Esc) <see cref="Dismissed"/>가 불리고, 상태가 바뀌어서 프로그램이 닫으면 불리지 않는다.
/// 서브클래스는 <c>Content</c> 에 본문만 넣는다 — 테두리는 여기서 감싼다.
/// </summary>
public abstract class DialogBase : Window
{
    readonly Border _frame = new() { Classes = { "dialogframe" }, ClipToBounds = true };
    bool _closedByState;

    protected DialogBase()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("dialog");
        base.Content = _frame;
    }

    /// <summary>대화상자 본문 (테두리 안).</summary>
    public new object? Content
    {
        get => _frame.Child;
        set => _frame.Child = value as Control;
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
