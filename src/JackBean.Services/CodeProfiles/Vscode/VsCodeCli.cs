using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>확장 설치 진행 상황.</summary>
public abstract record InstallEvent(string ExtensionId);

/// <summary>이미 설치되어 있어 건너뜀.</summary>
public sealed record InstallSkipped(string ExtensionId) : InstallEvent(ExtensionId);

public sealed record InstallStarted(string ExtensionId) : InstallEvent(ExtensionId);

public sealed record InstallLog(string ExtensionId, string Line, bool IsError = false) : InstallEvent(ExtensionId);

public sealed record InstallFinished(string ExtensionId, bool Success, int ExitCode) : InstallEvent(ExtensionId);

/// <summary>
/// VS Code CLI(`code`) 래퍼. 프로필 생성·확장 설치·열기는 이 CLI 로만 한다(VS Code 사용자 데이터 폴더에는 쓰지 않는다).
/// <list type="bullet">
/// <item>Windows 의 `code` 는 `code.cmd`(배치 파일)라서 셸 없이 직접 실행해도 공백이 든 경로·프로필 이름이 안전하다. 다만 배치 파일은 `cmd` 가 인자를 해석하므로
/// 인자 속 셸 특수문자(`&amp; | &lt; &gt; ^ % "`)와 줄바꿈은 거부한다.</item>
/// <item>**프로필 생성 명령은 없다.** 없는 이름으로 `code --profile &lt;이름&gt; &lt;폴더&gt;` 를 실행하면 빈 프로필이 만들어지고 그 폴더가 그 프로필로 열린다.
/// 그리고 `--install-extension` 은 **이미 있는 프로필**에만 된다 → <see cref="CreateProfileAsync"/>.</item>
/// <item>stderr 의 Node 경고(`DEP0169` 등)는 오류가 아니다 — 실패 여부는 종료 코드로만 본다.</item>
/// </list>
/// </summary>
public sealed partial class VsCodeCli
{
    private static readonly char[] ShellMeta = ['&', '|', '<', '>', '^', '%', '"', '`', '\n', '\r'];

    private readonly IProcessRunner _runner;
    private readonly string _codePath;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _profileTimeout;
    private readonly Func<TimeSpan, Task> _wait;

    /// <param name="codePath">`code` 실행 파일 경로(<see cref="CodeLocator"/>).</param>
    /// <param name="profilePollInterval"><see cref="CreateProfileAsync"/> 가 프로필이 생겼는지 확인하는 간격(기본 1초).</param>
    /// <param name="profileTimeout">그 최대 대기 시간(기본 60초).</param>
    /// <param name="wait">대기 함수(시험에서 실제로 기다리지 않게 바꾼다).</param>
    public VsCodeCli(IProcessRunner runner, string codePath = CodeLocator.FallbackCommand, TimeSpan? profilePollInterval = null, TimeSpan? profileTimeout = null, Func<TimeSpan, Task>? wait = null)
    {
        _runner = runner;
        _codePath = codePath;
        _pollInterval = profilePollInterval ?? TimeSpan.FromSeconds(1);
        _profileTimeout = profileTimeout ?? TimeSpan.FromSeconds(60);
        _wait = wait ?? (d => Task.Delay(d));
    }

    public string CodePath => _codePath;

    [GeneratedRegex(@"\.(cmd|bat)$", RegexOptions.IgnoreCase)]
    private static partial Regex BatchFile();

    private bool IsBatchFile => BatchFile().IsMatch(_codePath);

    /// <summary>`code --version` 의 첫 줄(버전 번호).</summary>
    public async Task<CodeResult<string>> VersionAsync()
    {
        var output = await _runner.RunAsync(_codePath, ["--version"], TimeSpan.FromSeconds(15));
        if (!output.Ok) return CodeResult<string>.Fail($"VS Code 버전을 확인하지 못했습니다: {output.FailureReason}");
        var first = output.StdOut.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return first == null ? CodeResult<string>.Fail("VS Code 가 버전을 출력하지 않았습니다") : CodeResult<string>.Success(first);
    }

    /// <summary>프로필에 설치된 확장 ID 목록(소문자).</summary>
    public async Task<CodeResult<IReadOnlyList<string>>> ListExtensionsAsync(string profile)
    {
        if (CheckArgs(profile) is { } bad) return CodeResult<IReadOnlyList<string>>.Fail(bad);
        var output = await _runner.RunAsync(_codePath, ["--profile", profile, "--list-extensions"], TimeSpan.FromSeconds(60));
        if (!output.Ok) return CodeResult<IReadOnlyList<string>>.Fail($"확장 목록을 읽지 못했습니다: {output.FailureReason}");
        IReadOnlyList<string> list = output.StdOut.Split('\n').Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0).ToList();
        return CodeResult<IReadOnlyList<string>>.Success(list);
    }

    /// <summary>
    /// 프로필이 VS Code 에 이미 있는지. `--list-extensions` 는 없는 프로필에 `Profile '&lt;이름&gt;' not found.` 로 실패하므로, 성공하면 있는 것으로 본다.
    /// 다른 이유로 실패해도 false 이고, 그 경우 <see cref="CreateProfileAsync"/> 가 같은 원인을 오류로 돌려준다.
    /// </summary>
    public async Task<bool> ProfileExistsAsync(string profile) => (await ListExtensionsAsync(profile)).Ok;

    /// <summary>
    /// 없는 프로필을 만든다: <paramref name="folder"/> 를 그 프로필로 연 뒤, 제한 시간 안에 `--list-extensions` 가 성공할 때까지 일정 간격으로 확인한다.
    /// 성공하면 null, 실패하면 오류.
    /// </summary>
    public async Task<CodeError?> CreateProfileAsync(string profile, string folder, CancellationToken ct = default)
    {
        if (await OpenAsync(profile, folder) is { } openError) return openError;

        var waited = TimeSpan.Zero;
        while (waited < _profileTimeout)
        {
            ct.ThrowIfCancellationRequested();
            await _wait(_pollInterval);
            waited += _pollInterval;
            if (await ProfileExistsAsync(profile)) return null;
        }
        return new CodeError($"프로필 '{profile}'이(가) 만들어졌는지 {(int)_profileTimeout.TotalSeconds}초 안에 확인하지 못했습니다. VS Code 가 시작 중이면 잠시 뒤 다시 시도하세요");
    }

    /// <summary>`code --profile &lt;프로필&gt; &lt;폴더&gt;`. 성공하면 null.</summary>
    public async Task<CodeError?> OpenAsync(string profile, string folder)
    {
        if (CheckArgs(profile, folder) is { } bad) return bad;
        var output = await _runner.RunAsync(_codePath, ["--profile", profile, folder], TimeSpan.FromSeconds(30));
        return output.Ok ? null : new CodeError($"VS Code 를 열지 못했습니다: {output.FailureReason}");
    }

    /// <summary>`code &lt;폴더&gt;` — 프로필을 지정하지 않고 연다(VS Code 가 그 폴더에 쓰던 프로필, 없으면 기본 프로필). 성공하면 null.</summary>
    public async Task<CodeError?> OpenWithoutProfileAsync(string folder)
    {
        if (CheckArgs(folder) is { } bad) return bad;
        var output = await _runner.RunAsync(_codePath, [folder], TimeSpan.FromSeconds(30));
        return output.Ok ? null : new CodeError($"VS Code 를 열지 못했습니다: {output.FailureReason}");
    }

    /// <summary>확장 하나를 설치한다(프로필이 이미 있어야 한다). 출력을 줄 단위로 흘리고 마지막에 <see cref="ProcessExited"/> 가 온다.</summary>
    public async IAsyncEnumerable<ProcessEvent> InstallExtensionAsync(string profile, string extensionId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (CheckArgs(profile, extensionId) is { } bad)
        {
            yield return new OutputLine(bad.Message, IsError: true);
            yield return new ProcessExited(-1);
            yield break;
        }
        await foreach (var e in _runner.StartAsync(_codePath, ["--profile", profile, "--install-extension", extensionId], ct)) yield return e;
    }

    /// <summary>
    /// 확장들을 차례로 설치한다. 이미 설치된 확장은 건너뛴다. 확장 목록을 읽지 못하면 모두 설치를 시도한다(건너뛰기는 최적화일 뿐이다).
    /// 한 확장이 실패해도 나머지는 계속한다(결과는 <see cref="InstallFinished"/> 로 알린다).
    /// </summary>
    public async IAsyncEnumerable<InstallEvent> InstallExtensionsAsync(string profile, IReadOnlyList<string> extensionIds, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var listed = await ListExtensionsAsync(profile);
        var installed = listed.Ok ? listed.Value!.ToHashSet() : [];
        foreach (var id in extensionIds)
        {
            if (installed.Contains(id.ToLowerInvariant()))
            {
                yield return new InstallSkipped(id);
                continue;
            }
            yield return new InstallStarted(id);
            var exitCode = -1;
            await foreach (var e in InstallExtensionAsync(profile, id, ct))
            {
                switch (e)
                {
                    case OutputLine line:
                        yield return new InstallLog(id, line.Line, line.IsError);
                        break;
                    case ProcessExited exited:
                        exitCode = exited.ExitCode;
                        break;
                }
            }
            yield return new InstallFinished(id, exitCode == 0, exitCode);
        }
    }

    /// <summary>배치 파일(`code.cmd`)은 `cmd` 가 인자를 해석하므로 셸 특수문자가 든 인자를 거부한다.</summary>
    private CodeError? CheckArgs(params string[] args)
    {
        if (!IsBatchFile) return null;
        foreach (var a in args)
            if (a.IndexOfAny(ShellMeta) >= 0) return new CodeError($"셸 특수문자가 들어 있어 VS Code 에 넘길 수 없습니다: '{a}'");
        return null;
    }
}
