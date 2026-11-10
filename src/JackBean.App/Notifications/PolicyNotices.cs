namespace JackBean.App.Notifications;

/// <summary>정책을 바꾼 뒤의 알림과 실행 취소. 저장·삭제 알림에는 [실행 취소]가 붙고, 홈 창에도 같은 버튼이 있다.</summary>
internal static class PolicyNotices
{
    /// <summary>"저장했습니다"류 알림. 되돌릴 수 있으면 [실행 취소] 를 붙인다.</summary>
    public static void Saved(string message)
    {
        var s = Strings.Current;
        if (App.PolicyStore.CanUndo) Snackbar.Info(message, s.SnackbarUndo, Undo);
        else Snackbar.Info(message);
    }

    /// <summary>마지막 변경을 되돌리고 열린 창들이 다시 읽게 한다.</summary>
    public static async void Undo()
    {
        var s = Strings.Current;
        var result = await App.PolicyStore.UndoAsync();
        if (!result.Ok) { Snackbar.Error(s.PolicyUndoFailed(result.Error!)); return; }
        App.NotifyPolicyChanged();
        Snackbar.Info(s.PolicyUndone);
    }
}
