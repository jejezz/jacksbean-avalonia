using Avalonia.Controls;
using JackBean.Services.CodeProfiles;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 VS Code 프로필 도구. 프로젝트 폴더를 놓으면 폴더 구성을 분석해 프로젝트 유형을 판정하고, 알맞은 VS Code 프로필·확장·`.vscode/settings.json` 을
/// 추천한다. 사용자가 확인하면 적용하고 `code --profile` 로 연다. 폴더 하나당 창 하나다(레지스트리가 같은 폴더를 다시 놓으면 그 창을 앞으로 가져온다).
/// 지금은 Windows 에서만 쓴다 — 설정에서 켠 사람에게만 열린다(<see cref="App"/> 의 실행기가 막는다).
/// </summary>
public sealed class VsCodeProfileTool : IBuiltInTool
{
    public const string ToolId = "vscode-profile";

    public string Id => ToolId;

    public Window CreateWindow(string path)
    {
        var folder = path.TrimEnd('/', '\\');
        if (folder.Length == 0) folder = path;
        if (!Directory.Exists(folder))
            throw new InvalidOperationException(Strings.Current.CodeNotAFolder(Path.GetFileName(folder) is { Length: > 0 } n ? n : folder));
        return new CodeProfileWindow(new CodeProfileFlow(CreateService(), folder));
    }

    /// <summary>지금 설정(`vscode_path`)으로 서비스를 만든다. 경로가 비어 있으면 자동 탐지, 못 찾으면 PATH 의 `code.cmd`.</summary>
    internal static CodeProfileService CreateService()
    {
        var dir = App.PolicyStore.Directory;
        return new CodeProfileService(
            new VsCodeCli(new SystemProcessRunner(), ResolveCodePath(App.Settings.Value.VsCodePath)),
            new ProjectAnalyzer(),
            new CodeProfileRulesStore(dir),
            new ProjectSettingsFile(),
            new CodeProjectMemory(dir));
    }

    internal static string ResolveCodePath(string? configured) =>
        !string.IsNullOrWhiteSpace(configured) ? configured.Trim() : CodeLocator.Discover() ?? CodeLocator.FallbackCommand;
}
