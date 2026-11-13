using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using JackBean.App.Dialogs;
using JackBean.Domain;
using JackBean.Services;
using JackBean.Services.Updates;

namespace JackBean.App.Updates;

/// <summary>
/// 업데이트 흐름을 앱에 연결한다 (Flutter 앱의 UpdateService와 같은 규약):
/// 시작 후 자동 확인(하루 1회) → 새 버전 알림 → 동의 → 내려받기·검증 → 설치.
/// 확인 때문에 앱이 방해받으면 안 되므로 <see cref="Create"/>와 자동 확인은 <b>절대 던지지 않는다</b>.
/// </summary>
sealed class UpdateService
{
    readonly UpdateClient _client;
    readonly UpdatePolicy _policy;
    readonly Func<bool>? _isBusy;
    readonly Func<Window?> _owner;
    readonly string _os;

    public UpdateService(UpdateClient client, UpdatePolicy policy, string os, Func<Window?> owner, Func<bool>? isBusy = null)
    {
        _client = client;
        _policy = policy;
        _os = os;
        _owner = owner;
        _isBusy = isBusy;
    }

    /// <summary>
    /// 서버 주소가 비어 있거나(<c>-p:AppUpdateServer=</c>) 현재 버전·플랫폼을 알 수 없으면 null — 그 빌드는 업데이트 확인을 하지 않는다.
    /// </summary>
    public static UpdateService? Create(string stateFile, Func<Window?> owner, Func<bool>? isBusy = null)
    {
        if (AppIdentity.UpdateServerUrl.Length == 0) return null;
        try
        {
            if (SemanticVersion.TryParse(AppVersion.Version) is null || AppVersion.Version == "0.0.0") return null; // 로컬 빌드
            var platform = UpdatePlatform.Current();
            var client = new UpdateClient(
                new Uri(AppIdentity.UpdateServerUrl), AppIdentity.UpdateAppId, AppVersion.Version,
                platform, installer: UpdateInstallers.ForPlatform(platform));
            return new UpdateService(client, new UpdatePolicy(new FileUpdateStateStore(stateFile)), platform.Os, owner, isBusy);
        }
        catch (Exception e) when (e is UpdateException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>새 버전이 있다고 확인된 뒤 true. 사용자가 "나중에"로 닫아도 켜져 있다.</summary>
    public bool Available { get; private set; }

    bool Busy => _isBusy?.Invoke() ?? false;

    async Task WaitUntilIdleAsync()
    {
        while (Busy) await Task.Delay(TimeSpan.FromSeconds(5));
    }

    /// <summary>앱 시작용. 5초 기다린 뒤 하루 1회만 확인하고, 알릴 것이 없거나 실패하면 아무것도 띄우지 않는다.</summary>
    public void StartAutomaticCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await WaitUntilIdleAsync(); // 일하는 중이면 끝날 때까지 미룬다 ('오늘은 확인함'으로 기록되지 않게)
                _client.CleanUpOldDownloads();
                var result = await _client.CheckAutomaticallyAsync(_policy);
                await WaitUntilIdleAsync();
                if (result is null) return;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (_owner() is { } owner) await PresentAsync(owner, result, manual: false);
                });
            }
            catch (Exception)
            {
                // 업데이트 확인이 앱을 방해하면 안 된다.
            }
        });
    }

    /// <summary>"업데이트 확인" 단추. 하루 1회 제한과 건너뛴 버전을 무시하고 결과를 항상 알려 준다.</summary>
    public async Task CheckManuallyAsync(Window owner)
    {
        var checking = new UpdateCheckingDialog();
        _ = checking.ShowDialog(owner);
        UpdateCheckResult result;
        try { result = await _client.CheckAsync(); }
        finally { checking.CloseFromState(); }
        if (result is not UpdateCheckResult.Failed) _policy.MarkChecked();
        await PresentAsync(owner, result, manual: true);
    }

    Task Message(Window owner, string title, string text) =>
        MessageDialog.ShowAsync(owner, title, text, [Strings.Current.AboutClose], 0, 0);

    async Task PresentAsync(Window owner, UpdateCheckResult result, bool manual)
    {
        var s = Strings.Current;
        switch (result)
        {
            case UpdateCheckResult.UpToDate:
                if (manual) await Message(owner, s.UpdateUpToDateTitle, s.UpdateUpToDateBody(AppVersion.Version));
                break;
            case UpdateCheckResult.Failed failed:
                if (manual) await Message(owner, s.UpdateCheckFailedTitle, UpdateText.ErrorMessage(failed.Error.Kind));
                break;
            case UpdateCheckResult.Unavailable unavailable:
                await ShowUnavailableAsync(owner, unavailable);
                break;
            case UpdateCheckResult.Available available:
                Available = true;
                var info = available.Info;
                var body = s.UpdateAvailableBody(AppIdentity.DisplayName, info.CurrentVersion, info.LatestVersion) + ChangesText(info.Notes);
                var notesUrl = ReleaseNotesUrl(info);
                // 나중에 = 1, 건너뛰기 = 0, 지금 = 2. 창을 그냥 닫으면 "나중에".
                var choice = await MessageDialog.ShowAsync(owner, s.UpdateAvailableTitle, body,
                    [s.UpdateSkipVersion, s.UpdateLater, s.UpdateNow], 2, 1,
                    notesUrl is null ? null : (s.UpdateViewReleaseNotes, () => OpenUrl(notesUrl)));
                if (choice == 0) _policy.Skip(info.LatestVersion);
                else if (choice == 2)
                {
                    if (Busy) await Message(owner, s.UpdateBusyTitle, s.UpdateBusyBody);
                    else await DownloadAsync(owner, info);
                }
                break;
        }
    }

    /// <summary>릴리스 노트 전문 대신 사용자에게 보이는 변경 항목만 짧게 보인다. 전문은 [릴리스 노트 보기].</summary>
    static string ChangesText(string notes)
    {
        var summary = ReleaseNotesSummary.From(notes);
        if (summary.Items.Count == 0) return "";
        var s = Strings.Current;
        var text = $"\n\n{s.UpdateReleaseNotes}\n" + string.Join("\n", summary.Items.Select(item => $" • {item}"));
        if (summary.More > 0) text += $"\n   {s.UpdateMoreChanges(summary.More)}";
        return text;
    }

    /// <summary>서버가 준 릴리스 주소, 없으면 저장소의 그 태그 페이지.</summary>
    static Uri? ReleaseNotesUrl(UpdateInfo info) =>
        info.ReleaseUrl ?? (info.Tag.Length > 0 ? new Uri($"{AppIdentity.RepositoryUrl}/releases/tag/{Uri.EscapeDataString(info.Tag)}") : null);

    /// <summary>새 버전은 있지만 앱이 자동 설치할 수 없는 경우(체크섬 없음, 이 OS용 파일 없음).</summary>
    async Task ShowUnavailableAsync(Window owner, UpdateCheckResult.Unavailable unavailable)
    {
        var s = Strings.Current;
        var buttons = unavailable.ReleaseUrl is null
            ? new[] { s.UpdateSkipVersion, s.AboutClose }
            : [s.UpdateSkipVersion, s.AboutClose, s.UpdateOpenReleasePage];
        var choice = await MessageDialog.ShowAsync(owner, s.UpdateUnavailableTitle, s.UpdateUnavailableBody(unavailable.LatestVersion),
            buttons, buttons.Length - 1, 1);
        if (choice == 0) _policy.Skip(unavailable.LatestVersion);
        else if (choice == 2 && unavailable.ReleaseUrl is { } url) OpenUrl(url);
    }

    static void OpenUrl(Uri url)
    {
        try { new OpenWithDefaultApp().Open(url.ToString()); }
        catch (FileOperationException) { /* 브라우저를 못 열어도 앱은 그대로 둔다 */ }
    }

    async Task DownloadAsync(Window owner, UpdateInfo info)
    {
        var s = Strings.Current;
        DownloadedUpdate? downloaded;
        try { downloaded = await UpdateDownloadDialog.RunAsync(owner, _client, info); }
        catch (UpdateException e)
        {
            await Message(owner, s.UpdateFailedTitle, UpdateText.ErrorMessage(e.Kind));
            return;
        }
        if (downloaded is null) return;

        // 내려받는 사이에 일이 시작됐으면 설치하지 않는다 (설치는 앱을 종료시킨다).
        if (Busy)
        {
            _client.Discard(downloaded);
            await Message(owner, s.UpdateBusyTitle, s.UpdateBusyBody);
            return;
        }

        InstallOutcome outcome;
        try { outcome = _client.Install(downloaded); }
        catch (UpdateException e)
        {
            _client.Discard(downloaded);
            await Message(owner, s.UpdateFailedTitle, UpdateText.ErrorMessage(e.Kind));
            return;
        }

        var name = AppIdentity.DisplayName;
        if (outcome == InstallOutcome.OpenedForUser)
        {
            // macOS: DMG 창이 열렸다. 앱은 계속 실행된다.
            await Message(owner, s.UpdateMacosOpenedTitle, s.UpdateMacosOpenedBody(name));
            return;
        }

        // Windows·Linux: 설치하려면 이 앱이 종료돼야 한다. 사용자가 누를 때 종료한다.
        var linux = _os == "linux";
        string[] buttons = linux ? [s.UpdateQuitAndInstall] : [s.AboutClose, s.UpdateQuitApp(name)];
        var quit = await MessageDialog.ShowAsync(owner,
            linux ? s.UpdateLinuxInstallTitle : s.UpdateWindowsInstallTitle,
            linux ? s.UpdateLinuxInstallBody(name) : s.UpdateWindowsInstallBody(name),
            buttons, buttons.Length - 1, 0); // Linux는 버튼이 하나뿐이라 창을 닫아도 종료·설치한다 (도우미가 이미 종료를 기다리는 중)
        if ((linux || quit == buttons.Length - 1) && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
