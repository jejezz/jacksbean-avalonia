using JackBean.Domain.Policy;
using JackBean.Domain.Drop;
using System.ComponentModel;
using System.Diagnostics;

namespace JackBean.Services;

public sealed record LaunchResult(string? Error)
{
    public bool Ok => Error == null;
    public static LaunchResult Success { get; } = new((string?)null);
}

/// <summary>
/// 앱을 띄운다. **셸을 거치지 않는다** — 경로에 공백·`&amp;`·따옴표가 있어도 인자 하나로 그대로 넘어가므로
/// 파일 이름이 명령으로 해석될 일이 없다.
/// </summary>
public sealed class AppLauncher
{
    private readonly Func<string, IReadOnlyList<string>, Task> _spawn;

    /// <param name="spawn">프로세스를 띄우는 함수. 테스트에서 바꿔 끼운다.</param>
    public AppLauncher(Func<string, IReadOnlyList<string>, Task>? spawn = null) => _spawn = spawn ?? SpawnDetached;

    /// <summary>`args`의 `{path}`를 path 로 바꾼다(그 밖의 글자는 건드리지 않는다).</summary>
    public static IReadOnlyList<string> ExpandArgs(IEnumerable<string> args, string path) =>
        args.Select(a => a == PathsToken ? path : a.Replace("{path}", path, StringComparison.Ordinal)).ToList();   // `{paths}` 도 항목이 하나일 땐 그 경로 하나

    /// <summary>
    /// 홈 창 [열기](앱이 잘 뜨는지 시험)에서 쓸 인자: 대상(`{path}`·`{paths}`)을 뺀다 — 바로 앞의 `-d` 같은 옵션도 함께 뺀다(`wt.exe -d {path}` → `wt.exe`).
    /// 대상이 없으면 실패하는 열기 도구(`explorer.exe`·`open`·`xdg-open`)에는 <paramref name="fallbackFolder"/> 를 준다.
    /// </summary>
    public static IReadOnlyList<string> ArgsForTryOut(AppEntry app, string fallbackFolder)
    {
        var list = new List<string>();
        foreach (var a in app.Args)
        {
            if (a is "{path}" or PathsToken)
            {
                if (list.Count > 0 && list[^1].StartsWith('-')) list.RemoveAt(list.Count - 1);
                continue;
            }
            list.Add(a.Replace("{path}", fallbackFolder, StringComparison.Ordinal));
        }
        if (list.Count == 0 && Path.GetFileNameWithoutExtension(app.Command).ToLowerInvariant() is "explorer" or "open" or "xdg-open") list.Add(fallbackFolder);
        return list;
    }

    /// <summary>대상 없이 앱만 띄운다(<see cref="ArgsForTryOut"/>).</summary>
    public async Task<LaunchResult> TryOutAsync(AppEntry app, string fallbackFolder)
    {
        try
        {
            await _spawn(app.Command, ArgsForTryOut(app, fallbackFolder));
            return LaunchResult.Success;
        }
        catch (Win32Exception e)
        {
            return new LaunchResult(string.IsNullOrEmpty(e.Message) ? $"{app.Command} 을(를) 실행하지 못했습니다" : e.Message);
        }
        catch (Exception e)
        {
            return new LaunchResult(e.Message);
        }
    }

    /// <summary>앱이 여러 항목을 한 번에 받는가: 인자에 `{paths}`(떨어뜨린 경로 전부로 펼쳐진다)가 있으면 그렇다 — 한 번만 띄운다.</summary>
    public static bool AcceptsMany(AppEntry app) => app.Args.Contains(PathsToken);

    public const string PathsToken = "{paths}";

    /// <summary>`args`의 `{paths}` 를 경로 전부로 펼치고(인자 하나씩), `{path}` 는 첫 경로로 바꾼다.</summary>
    public static IReadOnlyList<string> ExpandArgsMany(IEnumerable<string> args, IReadOnlyList<string> paths)
    {
        var list = new List<string>();
        foreach (var a in args)
        {
            if (a == PathsToken) list.AddRange(paths);
            else list.Add(a.Replace("{path}", paths.Count > 0 ? paths[0] : "", StringComparison.Ordinal));
        }
        return list;
    }

    /// <summary>여러 항목을 앱 하나로 한 번에 연다(<see cref="AcceptsMany"/> 인 앱).</summary>
    public async Task<LaunchResult> LaunchManyAsync(AppEntry app, IReadOnlyList<string> paths)
    {
        try
        {
            await _spawn(app.Command, ExpandArgsMany(app.Args, paths));
            return LaunchResult.Success;
        }
        catch (Win32Exception e)
        {
            return new LaunchResult(string.IsNullOrEmpty(e.Message) ? $"{app.Command} 을(를) 실행하지 못했습니다" : e.Message);
        }
        catch (Exception e)
        {
            return new LaunchResult(e.Message);
        }
    }

    public async Task<LaunchResult> LaunchAsync(AppEntry app, string path)
    {
        try
        {
            await _spawn(app.Command, ExpandArgs(app.Args, path));
            return LaunchResult.Success;
        }
        catch (Win32Exception e)
        {
            return new LaunchResult(string.IsNullOrEmpty(e.Message) ? $"{app.Command} 을(를) 실행하지 못했습니다" : e.Message);
        }
        catch (Exception e)
        {
            return new LaunchResult(e.Message);
        }
    }

    // 독이 닫혀도 연 앱은 계속 떠 있어야 하므로 기다리지 않고 놓는다. UseShellExecute=false 라 셸을 거치지 않는다.
    /// <summary>launch 의 실행 부분만: 프로세스를 띄운다(결과 판정 없이 예외를 그대로 던진다).</summary>
    public Task SpawnAsync(string command, IReadOnlyList<string> args) => SpawnDetached(command, args);

    private static Task SpawnDetached(string command, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi);
        return Task.CompletedTask;
    }
}
