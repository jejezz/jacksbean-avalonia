using JackBean.Domain.CodeProfiles;
using JackBean.Services.CodeProfiles;

namespace JackBean.Services.Tests.CodeProfiles;

internal sealed record ProcessCall(string Executable, IReadOnlyList<string> Args, TimeSpan? Timeout, bool Streamed);

/// <summary>테스트용 <see cref="IProcessRunner"/>. 호출을 기록하고 미리 정해 둔 응답을 돌려준다. 맞는 응답이 없으면 종료 코드 127.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private sealed class Stub(Func<string, IReadOnlyList<string>, bool> matches, ProcessOutput output, IReadOnlyList<ProcessEvent>? events, IReadOnlyList<ProcessOutput>? outputs)
    {
        private int _calls;

        public bool Matches(string exe, IReadOnlyList<string> args) => matches(exe, args);

        public IReadOnlyList<ProcessEvent>? Events { get; } = events;

        public ProcessOutput Next()
        {
            if (outputs is not { Count: > 0 }) return output;
            var i = _calls++;
            return outputs[Math.Min(i, outputs.Count - 1)];
        }
    }

    private readonly List<Stub> _stubs = [];

    public List<ProcessCall> Calls { get; } = [];

    /// <summary>실행 파일·인자가 조건에 맞으면 응답한다. <paramref name="outputs"/> 를 주면 호출마다 차례로 하나씩(마지막 것 반복).</summary>
    public void On(Func<string, IReadOnlyList<string>, bool> matches, ProcessOutput? output = null, IReadOnlyList<ProcessEvent>? events = null, IReadOnlyList<ProcessOutput>? outputs = null) =>
        _stubs.Add(new Stub(matches, output ?? new ProcessOutput(0), events, outputs));

    /// <summary>인자가 <paramref name="prefix"/> 로 시작하는 호출에 응답한다(실행 파일은 따지지 않는다).</summary>
    public void OnArgs(string[] prefix, ProcessOutput? output = null, IReadOnlyList<ProcessEvent>? events = null, IReadOnlyList<ProcessOutput>? outputs = null) =>
        On((_, args) => args.Count >= prefix.Length && prefix.Select((p, i) => args[i] == p).All(b => b), output, events, outputs);

    private Stub Find(string exe, IReadOnlyList<string> args) =>
        _stubs.FirstOrDefault(s => s.Matches(exe, args))
        ?? new Stub((_, _) => true, new ProcessOutput(127, StdErr: $"FakeProcessRunner: no response for \"{exe} {string.Join(' ', args)}\""), null, null);

    public Task<ProcessOutput> RunAsync(string executable, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Calls.Add(new ProcessCall(executable, args.ToList(), timeout, false));
        return Task.FromResult(Find(executable, args).Next());
    }

    public async IAsyncEnumerable<ProcessEvent> StartAsync(string executable, IReadOnlyList<string> args, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(new ProcessCall(executable, args.ToList(), null, true));
        var stub = Find(executable, args);
        var output = stub.Next();
        IEnumerable<ProcessEvent> events = stub.Events ??
        [
            .. output.StdOut.Split('\n').Where(l => l.Length > 0).Select(l => (ProcessEvent)new OutputLine(l)),
            .. output.StdErr.Split('\n').Where(l => l.Length > 0).Select(l => (ProcessEvent)new OutputLine(l, true)),
            new ProcessExited(output.ExitCode),
        ];
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }
}

internal static class CodeProfileFixtures
{
    /// <summary>임시 폴더에 가짜 프로젝트 트리를 만든다. 키가 `/` 로 끝나면 빈 디렉터리, 아니면 그 내용을 가진 파일이다.</summary>
    public static TempDir Create(Dictionary<string, string>? files = null)
    {
        var dir = new TempDir();
        foreach (var (rel, content) in files ?? [])
        {
            var path = Path.Combine(dir.Path, rel.Replace('/', Path.DirectorySeparatorChar));
            if (rel.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
        }
        return dir;
    }

    public static ProfileRules DefaultRules() => ProfileRulesParser.Parse(DefaultProfileRules.Json).Value!;

    public static IReadOnlyList<string> Ids(AnalysisResult a) => a.Types.Select(t => t.TypeId).ToList();

    public static AnalysisResult Analyze(string path, ProfileRules? rules = null)
    {
        var r = new ProjectAnalyzer().Analyze(path, rules ?? DefaultRules());
        Assert.True(r.Ok, r.Error?.ToString());
        return r.Value!;
    }
}
