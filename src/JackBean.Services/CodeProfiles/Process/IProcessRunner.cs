namespace JackBean.Services.CodeProfiles;

/// <summary>외부 프로세스 실행 결과. 시작 실패와 시간 초과는 예외가 아니라 이 값으로 알린다.</summary>
/// <param name="TimedOut">시간 제한에 걸려 프로세스를 중단했는지.</param>
/// <param name="StartError">실행 파일을 시작하지 못한 이유(없으면 null). 이때 <see cref="ExitCode"/> 는 -1.</param>
public sealed record ProcessOutput(int ExitCode, string StdOut = "", string StdErr = "", bool TimedOut = false, string? StartError = null)
{
    public bool Ok => ExitCode == 0 && !TimedOut && StartError == null;

    /// <summary>사용자에게 보여줄 실패 사유 한 줄.</summary>
    public string FailureReason
    {
        get
        {
            if (StartError != null) return StartError;
            if (TimedOut) return "시간 제한을 넘겨 중단했습니다";
            var text = (StdErr.Trim().Length > 0 ? StdErr : StdOut).Trim();
            return text.Length == 0 ? $"종료 코드 {ExitCode}" : text.Split('\n')[0].Trim();
        }
    }
}

/// <summary><see cref="IProcessRunner.StartAsync"/> 가 흘려보내는 이벤트.</summary>
public abstract record ProcessEvent;

/// <summary>한 줄의 출력.</summary>
public sealed record OutputLine(string Line, bool IsError = false) : ProcessEvent;

/// <summary>프로세스 종료. 스트림의 마지막 이벤트다.</summary>
public sealed record ProcessExited(int ExitCode) : ProcessEvent;

/// <summary>
/// 외부 프로세스 실행 추상화. VS Code CLI(`code`) 호출은 모두 이 인터페이스를 거친다 — 테스트는 가짜로 바꿔 끼운다.
/// **셸을 거치지 않는다**(인자는 인자 하나씩 그대로 넘어간다).
/// </summary>
public interface IProcessRunner
{
    /// <summary>끝날 때까지 기다렸다가 결과를 돌려준다. 시간 제한을 넘기면 프로세스 트리를 끝내고 <c>TimedOut</c> 로 알린다.</summary>
    Task<ProcessOutput> RunAsync(string executable, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>출력을 줄 단위로 흘려보낸다(확장 설치용). 마지막 이벤트는 <see cref="ProcessExited"/>. 열거를 멈추면 프로세스를 끝낸다.</summary>
    IAsyncEnumerable<ProcessEvent> StartAsync(string executable, IReadOnlyList<string> args, CancellationToken ct = default);
}
