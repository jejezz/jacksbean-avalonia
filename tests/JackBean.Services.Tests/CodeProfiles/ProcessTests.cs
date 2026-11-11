using System.Diagnostics;
using System.Text;
using JackBean.Services.CodeProfiles;

namespace JackBean.Services.Tests.CodeProfiles;

public class ProcessOutputDecoderTests
{
    [Fact]
    public void ValidUtf8IsDecodedAsUtf8() =>
        Assert.Equal("한글 ✓ ok", ProcessOutputDecoder.Decode(Encoding.UTF8.GetBytes("한글 ✓ ok")));

    [Fact]
    public void InvalidUtf8FallsBackToTheGivenEncoding() =>
        Assert.Equal("aé", ProcessOutputDecoder.Decode([0x61, 0xE9], Encoding.Latin1));   // 0xE9 는 Latin-1 의 é, UTF-8 로는 미완성 시퀀스

    [Fact]
    public void Cp949BytesAreReadableThroughTheCodePageFallback()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var bytes = cp949.GetBytes("명령 구문이 잘못되었습니다.");
        Assert.Equal("명령 구문이 잘못되었습니다.", ProcessOutputDecoder.Decode(bytes, cp949));
    }

    [Fact]
    public void WithoutAFallbackItUsesThisPcOemCodePageAndNeverThrows()
    {
        var text = ProcessOutputDecoder.Decode([0xFF, 0xFE, 0x80, 0x81]);
        Assert.NotNull(text);
    }

    [Fact]
    public void EmptyInputIsEmpty() => Assert.Equal("", ProcessOutputDecoder.Decode([]));
}

/// <summary>실제 프로세스로 하는 시험(Windows 의 cmd·ping·sort 를 쓴다 — 다른 OS 에서는 건너뛴다).</summary>
public class SystemProcessRunnerTests
{
    private static readonly SystemProcessRunner Runner = new();

    private static string[] Cmd(string script) => ["/c", script];

    [Fact]
    public async Task CapturesStdoutStderrAndTheExitCode()
    {
        if (!OperatingSystem.IsWindows()) return;
        var r = await Runner.RunAsync("cmd.exe", Cmd("echo out& echo err 1>&2& exit /b 3"));
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("out", r.StdOut.Trim());
        Assert.Equal("err", r.StdErr.Trim());
        Assert.False(r.Ok);
        Assert.Equal("err", r.FailureReason);
    }

    [Fact]
    public async Task DecodesUtf8Output()
    {
        if (!OperatingSystem.IsWindows()) return;
        var r = await Runner.RunAsync("cmd.exe", Cmd("chcp 65001>nul & echo 한글 문장"));
        Assert.True(r.Ok, r.FailureReason);
        Assert.Equal("한글 문장", r.StdOut.Trim());
    }

    [Fact]
    public async Task AMissingExecutableIsAStartErrorNotAnException()
    {
        var r = await Runner.RunAsync("definitely-not-a-real-program-xyz", []);
        Assert.Equal(-1, r.ExitCode);
        Assert.Contains("definitely-not-a-real-program-xyz", r.StartError);
        Assert.False(r.Ok);
        Assert.Contains("실행하지 못했습니다", r.FailureReason);
    }

    [Fact]
    public async Task ATimeoutStopsTheProcessAndSetsTimedOut()
    {
        if (!OperatingSystem.IsWindows()) return;
        var watch = Stopwatch.StartNew();
        var r = await Runner.RunAsync("ping.exe", ["-n", "30", "127.0.0.1"], TimeSpan.FromMilliseconds(800));
        Assert.True(r.TimedOut);
        Assert.False(r.Ok);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task StdinIsClosedSoProgramsThatReadItDoNotHang()
    {
        if (!OperatingSystem.IsWindows()) return;
        var r = await Runner.RunAsync("sort.exe", [], TimeSpan.FromSeconds(20));
        Assert.False(r.TimedOut);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task ArgumentsReachTheProcessAsSingleArgumentsEvenWithSpaces()
    {
        if (!OperatingSystem.IsWindows()) return;
        // 셸을 거치지 않으므로 공백이 든 인자도 하나로 간다(cmd 의 echo 가 그대로 찍는다).
        var r = await Runner.RunAsync("cmd.exe", ["/c", "echo", "Web / Python"]);
        Assert.Equal("Web / Python", r.StdOut.Trim().Trim('"'));
    }

    [Fact]
    public async Task StartStreamsLinesAndEndsWithProcessExited()
    {
        if (!OperatingSystem.IsWindows()) return;
        var events = new List<ProcessEvent>();
        await foreach (var e in Runner.StartAsync("cmd.exe", Cmd("echo one& echo two 1>&2& echo three& exit /b 2"))) events.Add(e);
        Assert.Equal(2, Assert.IsType<ProcessExited>(events[^1]).ExitCode);
        var lines = events.OfType<OutputLine>().ToList();
        Assert.Equal(["one", "three"], lines.Where(l => !l.IsError).Select(l => l.Line));
        Assert.Equal(["two"], lines.Where(l => l.IsError).Select(l => l.Line.Trim()));   // cmd 의 `echo two 1>&2&` 는 끝에 공백을 남긴다
    }

    [Fact]
    public async Task StartDecodesMultiByteLinesAndTrimsCarriageReturns()
    {
        if (!OperatingSystem.IsWindows()) return;
        var lines = new List<string>();
        await foreach (var e in Runner.StartAsync("cmd.exe", Cmd("chcp 65001>nul & echo 한글 하나& echo 둘째 줄")))
            if (e is OutputLine { IsError: false } l) lines.Add(l.Line);
        Assert.Equal(["한글 하나", "둘째 줄"], lines);
    }

    [Fact]
    public async Task StartWithAMissingExecutableYieldsAnErrorLineAndExitMinusOne()
    {
        var events = new List<ProcessEvent>();
        await foreach (var e in Runner.StartAsync("definitely-not-a-real-program-xyz", [])) events.Add(e);
        Assert.True(Assert.IsType<OutputLine>(events[0]).IsError);
        Assert.Equal(-1, Assert.IsType<ProcessExited>(events[^1]).ExitCode);
    }

    [Fact]
    public async Task StoppingTheEnumerationKillsTheProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var watch = Stopwatch.StartNew();
        await foreach (var e in Runner.StartAsync("ping.exe", ["-n", "60", "127.0.0.1"]))
        {
            if (e is OutputLine) break;
        }
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task CancellationBeforeCompletionThrows()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner.RunAsync("ping.exe", ["-n", "30", "127.0.0.1"], ct: cts.Token));
    }
}
