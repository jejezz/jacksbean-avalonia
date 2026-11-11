using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace JackBean.Services.CodeProfiles;

/// <summary>
/// <see cref="Process"/> 로 실제 프로세스를 실행한다. 셸을 거치지 않고(`UseShellExecute=false`, 인자는 `ArgumentList`), 표준 입력은 바로 닫아
/// 입력을 기다리며 멈추는 프로그램이 없게 한다. 출력은 바이트로 읽어 <see cref="ProcessOutputDecoder"/> 로 디코딩한다.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <summary>
    /// 프로세스가 끝난 뒤 출력 읽기를 기다리는 최대 시간. `code` 가 띄운 VS Code 창이 우리 파이프를 물고 있으면 파이프가 끝나지 않으므로,
    /// 그때는 받은 만큼만 쓰고 넘어간다.
    /// </summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);

    public async Task<ProcessOutput> RunAsync(string executable, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executable, args) };
        if (!TryStart(process, executable, out var startError)) return new ProcessOutput(-1, StartError: startError);

        var stdout = ReadAllAsync(process.StandardOutput.BaseStream);
        var stderr = ReadAllAsync(process.StandardError.BaseStream);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) cts.CancelAfter(t);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            if (ct.IsCancellationRequested) throw;
            return new ProcessOutput(-1, TimedOut: true);
        }

        await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(DrainGrace, CancellationToken.None));
        return new ProcessOutput(
            process.ExitCode,
            stdout.IsCompletedSuccessfully ? ProcessOutputDecoder.Decode(stdout.Result) : "",
            stderr.IsCompletedSuccessfully ? ProcessOutputDecoder.Decode(stderr.Result) : "");
    }

    public async IAsyncEnumerable<ProcessEvent> StartAsync(string executable, IReadOnlyList<string> args, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executable, args) };
        if (!TryStart(process, executable, out var startError))
        {
            yield return new OutputLine(startError!, IsError: true);
            yield return new ProcessExited(-1);
            yield break;
        }

        var channel = Channel.CreateUnbounded<ProcessEvent>();
        var readers = Task.WhenAll(
            PumpLinesAsync(process.StandardOutput.BaseStream, isError: false, channel.Writer),
            PumpLinesAsync(process.StandardError.BaseStream, isError: true, channel.Writer));
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAny(readers, Task.Delay(DrainGrace, CancellationToken.None));
            channel.Writer.TryWrite(new ProcessExited(process.ExitCode));
            channel.Writer.TryComplete();
        }, CancellationToken.None);

        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(ct)) yield return e;
        }
        finally
        {
            KillTree(process);   // 열거를 멈췄거나 취소됐으면 끝낸다(이미 끝났으면 아무 일도 없다)
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static bool TryStart(Process process, string executable, out string? error)
    {
        try
        {
            process.Start();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            error = $"{executable} 을(를) 실행하지 못했습니다: {e.Message}";
            return false;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // 이미 끝난 프로세스의 입력 닫기 실패는 무시한다.
        }
        error = null;
        return true;
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 이미 끝났거나 끝낼 수 없다.
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        try
        {
            await stream.CopyToAsync(buffer);
        }
        catch (IOException)
        {
            // 프로세스가 끝나며 파이프가 닫혔다 — 받은 만큼만 쓴다.
        }
        return buffer.ToArray();
    }

    /// <summary>바이트에서 `\n` 으로 줄을 먼저 나눈 뒤 줄마다 디코딩한다(0x0A 는 UTF-8 과 cp949 모두에서 다른 글자의 일부가 될 수 없다).</summary>
    private static async Task PumpLinesAsync(Stream stream, bool isError, ChannelWriter<ProcessEvent> writer)
    {
        var pending = new List<byte>();
        var chunk = new byte[4096];
        try
        {
            int n;
            while ((n = await stream.ReadAsync(chunk)) > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    if (chunk[i] == (byte)'\n')
                    {
                        writer.TryWrite(new OutputLine(DecodeLine(pending), isError));
                        pending.Clear();
                    }
                    else
                    {
                        pending.Add(chunk[i]);
                    }
                }
            }
        }
        catch (IOException)
        {
            // 파이프가 닫혔다.
        }
        if (pending.Count > 0) writer.TryWrite(new OutputLine(DecodeLine(pending), isError));
    }

    private static string DecodeLine(List<byte> bytes)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes);
        if (span.Length > 0 && span[^1] == (byte)'\r') span = span[..^1];
        return ProcessOutputDecoder.Decode(span);
    }
}
