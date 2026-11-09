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
        args.Select(a => a.Replace("{path}", path, StringComparison.Ordinal)).ToList();

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
