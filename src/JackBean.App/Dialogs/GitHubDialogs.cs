using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JackBean.App.Localization;
using JackBean.Domain;
using JackBean.Services;
using JackBean.Services.GitHub;

namespace JackBean.App.Dialogs;

/// <summary>
/// GitHub 로그인 창. 브라우저 로그인(Device Flow)이 기본이다 — 코드를 보여 주고 브라우저를 열면 사용자가 승인하고, 그동안 앱이 기다린다.
/// 이 빌드에 Client ID 가 없으면(<see cref="AppIdentity.GitHubClientId"/>) 토큰 입력만 보인다. 토큰은 OS 보안 저장소에만 보관한다.
/// </summary>
public sealed class GitHubLoginDialog : DialogBase
{
    private readonly TaskCompletionSource<bool> _result = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private bool _done;

    /// <summary>로그인에 성공하면 true, 취소하거나 창을 닫으면 false.</summary>
    public static Task<bool> ShowAsync(Window owner)
    {
        var dialog = new GitHubLoginDialog();
        _ = dialog.ShowDialog(owner);
        return dialog._result.Task;
    }

    private GitHubLoginDialog()
    {
        var s = Strings.Current;
        Title = WindowTitle.Of(s.GitHubLoginTitle);
        MinWidth = 480;
        MaxWidth = 560;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        var heading = new TextBlock { Text = s.GitHubLoginTitle, FontWeight = FontWeight.Bold };
        heading.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        panel.Children.Add(heading);

        if (AppIdentity.GitHubClientId.Length > 0) panel.Children.Add(DeviceFlowPart(s));
        else
        {
            var note = new TextBlock { Text = s.GitHubNoClientId, TextWrapping = TextWrapping.Wrap };
            note.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            panel.Children.Add(note);
        }

        panel.Children.Add(TokenPart(s, expanded: AppIdentity.GitHubClientId.Length == 0));
        _status.Classes.Add("failed");
        _status.IsVisible = false;
        panel.Children.Add(_status);

        var close = new Button { Content = s.CommonCancel, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Finish(false);
        panel.Children.Add(close);
        Content = panel;

        Dismissed += () => { _cts.Cancel(); _result.TrySetResult(false); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
    }

    private void Finish(bool ok)
    {
        if (_done) return;
        _done = true;
        _cts.Cancel();
        _result.TrySetResult(ok);
        CloseFromState();
    }

    private void ShowError(string message)
    {
        _status.Text = message;
        _status.IsVisible = true;
    }

    // --- 브라우저 로그인 ---

    private Control DeviceFlowPart(IAppStrings s)
    {
        var info = new TextBlock { Text = s.GitHubLoginWait, TextWrapping = TextWrapping.Wrap };
        var code = new TextBlock { FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8) };
        code.Classes.Add("mono");
        code.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        var waiting = new TextBlock { Text = s.GitHubLoginWaiting };
        waiting.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var open = new Button { Content = s.GitHubOpenBrowser, IsEnabled = false };
        var copy = new Button { Content = s.GitHubCopyCode, IsEnabled = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { open, copy } };
        var part = new StackPanel { Spacing = 6, Children = { info, code, buttons, waiting } };

        _ = RunDeviceFlowAsync(code, open, copy, waiting);
        return part;
    }

    private async Task RunDeviceFlowAsync(TextBlock codeText, Button open, Button copy, TextBlock waiting)
    {
        try
        {
            using var flow = new DeviceFlow(AppIdentity.GitHubClientId);
            var code = await flow.StartAsync(ct: _cts.Token);
            codeText.Text = code.UserCode;
            open.IsEnabled = copy.IsEnabled = true;
            open.Click += (_, _) => OpenUrl(code.VerificationUri);
            copy.Click += async (_, _) => { if (GetTopLevel(this)?.Clipboard is { } c) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(c, code.UserCode); };
            OpenUrl(code.VerificationUri);   // 바로 열어 준다 — 사용자는 코드를 입력하고 승인만 한다

            var token = await flow.AwaitTokenAsync(code, _cts.Token);
            waiting.Text = "…";
            await App.Github.SignInAsync(token, _cts.Token);
            Dispatcher.UIThread.Post(() => Finish(true));
        }
        catch (OperationCanceledException) { /* 취소했다 */ }
        catch (Exception e) when (e is DeviceFlowException or GitHubException)
        {
            Dispatcher.UIThread.Post(() => { ShowError(Strings.Current.GitHubLoginFailed(e.Message)); waiting.IsVisible = false; });
        }
    }

    private void OpenUrl(string url)
    {
        try { new OpenWithDefaultApp().Open(url); }
        catch (FileOperationException e) { ShowError(Strings.Current.GitHubLoginFailed(e.Message)); }
    }

    // --- 토큰으로 로그인(고급) ---

    private Control TokenPart(IAppStrings s, bool expanded)
    {
        var box = new TextBox { PasswordChar = '•', Watermark = "ghp_…" };
        var hint = new TextBlock { Text = s.GitHubTokenHint, TextWrapping = TextWrapping.Wrap };
        hint.Classes.Add("small");
        hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var tokenPage = new Button { Content = "github.com/settings/tokens", HorizontalAlignment = HorizontalAlignment.Left };
        tokenPage.Click += (_, _) => OpenUrl("https://github.com/settings/tokens/new?scopes=repo&description=Jack%E2%80%99s%20Bean");
        var signIn = new Button { Content = s.GitHubSignInButton, HorizontalAlignment = HorizontalAlignment.Left };
        signIn.Classes.Add("accent");
        async Task SignInAsync()
        {
            signIn.IsEnabled = false;
            try
            {
                await App.Github.SignInAsync(box.Text ?? "", _cts.Token);
                Finish(true);
            }
            catch (GitHubException e) { ShowError(Strings.Current.GitHubLoginFailed(e.Message)); }
            catch (OperationCanceledException) { }
            finally { signIn.IsEnabled = true; }
        }
        signIn.Click += async (_, _) => await SignInAsync();
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SignInAsync(); } };

        var body = new StackPanel { Spacing = 6, Children = { hint, tokenPage, box, signIn } };
        return new Expander { Header = s.GitHubTokenAdvanced, IsExpanded = expanded, Content = body, HorizontalAlignment = HorizontalAlignment.Stretch };
    }
}

/// <summary>동기화에 쓸 저장소를 고른다(새로 만들기 / 계정의 저장소 중 선택). 이 앱이 쓰던 저장소(토픽 표식)가 맨 위에 온다.</summary>
public sealed class GitHubRepoDialog : DialogBase
{
    private readonly TaskCompletionSource<bool> _result = new();
    private readonly StackPanel _list = new() { Spacing = 4 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private bool _done;

    /// <summary>저장소를 연결했으면 true.</summary>
    public static Task<bool> ShowAsync(Window owner)
    {
        var dialog = new GitHubRepoDialog();
        _ = dialog.ShowDialog(owner);
        return dialog._result.Task;
    }

    private GitHubRepoDialog()
    {
        var s = Strings.Current;
        Title = WindowTitle.Of(s.RepoDialogTitle);
        MinWidth = 520;
        MaxWidth = 600;
        var heading = new TextBlock { Text = s.RepoDialogTitle, FontWeight = FontWeight.Bold };
        heading.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));

        // 새 저장소
        var name = new TextBox { Text = "jacksbean-policy" };
        var create = new Button { Content = s.RepoCreateButton };
        create.Classes.Add("accent");
        create.Click += async (_, _) => await CreateAsync(name.Text ?? "", create);
        var createRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(create, 1);
        createRow.Children.Add(name);
        createRow.Children.Add(create);

        _status.Text = s.RepoLoading;
        _status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var close = new Button { Content = s.CommonCancel, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Finish(false);

        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 8,
            Children =
            {
                heading,
                new TextBlock { Text = s.RepoCreateTitle, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }, createRow,
                new TextBlock { Text = s.RepoExistingTitle, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) },
                _status, new ScrollViewer { Content = _list, MaxHeight = 260 }, close,
            },
        };
        Dismissed += () => _result.TrySetResult(false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
        Opened += async (_, _) => await LoadAsync();
    }

    private void Finish(bool ok)
    {
        if (_done) return;
        _done = true;
        _result.TrySetResult(ok);
        CloseFromState();
    }

    private void Fail(string message)
    {
        _status.Text = message;
        _status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppDanger"));   // 오류는 위험색으로
        _status.IsVisible = true;
    }

    private async Task LoadAsync()
    {
        var s = Strings.Current;
        try
        {
            var repos = await App.Github.ReposAsync();
            _list.Children.Clear();
            _status.IsVisible = false;
            foreach (var repo in repos) _list.Children.Add(RepoRow(repo));
            if (repos.Count == 0) { _status.Text = s.RepoNone; _status.IsVisible = true; }
        }
        catch (GitHubException e) { Fail(s.RepoFailed(e.Message)); }
    }

    private Control RepoRow(GitHubRepo repo)
    {
        var s = Strings.Current;
        var name = new TextBlock { Text = repo.FullName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Classes.Add("user");   // 저장소 이름은 사용자 글
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (repo.IsAppRepo) tags.Children.Add(Tag(s.RepoAppRepo, "AppPrimary"));
        if (!repo.IsPrivate) tags.Children.Add(Tag(s.RepoPublicWarning, "AppDanger"));
        var connect = new Button { Content = s.RepoConnect };
        connect.Click += async (_, _) => await ConnectAsync(repo);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name, tags } };
        Grid.SetColumn(connect, 1);
        row.Children.Add(left);
        row.Children.Add(connect);
        var border = new Border { Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(8), Child = row };
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        return border;
    }

    private TextBlock Tag(string text, string colorKey)
    {
        var t = new TextBlock { Text = text };
        t.Classes.Add("small");
        t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(colorKey));
        return t;
    }

    private async Task ConnectAsync(GitHubRepo repo)
    {
        var s = Strings.Current;
        if (!repo.IsPrivate)
        {
            // 정책에는 앱 경로 같은 것이 들어 있다 — 공개 저장소면 한 번 더 묻는다.
            var choice = await MessageDialog.ShowAsync(this, s.RepoDialogTitle, s.RepoPublicConfirm(repo.FullName), [s.RepoConnect, s.CommonCancel], defaultIndex: 1, cancelIndex: 1);
            if (choice != 0) return;
        }
        await App.Github.ConnectAsync(repo);
        Notifications.Snackbar.Info(s.RepoConnected(repo.FullName));
        Finish(true);
    }

    private async Task CreateAsync(string name, Button button)
    {
        var s = Strings.Current;
        name = name.Trim();
        if (name.Length == 0) return;
        button.IsEnabled = false;
        try
        {
            var repo = await App.Github.CreateRepoAsync(name);
            await App.Github.ConnectAsync(repo);
            Notifications.Snackbar.Info(s.RepoConnected(repo.FullName));
            Finish(true);
        }
        catch (GitHubException e) { Fail(s.RepoFailed(e.Message)); }
        finally { button.IsEnabled = true; }
    }
}
