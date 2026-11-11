using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>폴더를 훑어(<see cref="TreeScanner"/>) 변수를 해석하고(<see cref="VariableResolver"/>) 프로젝트 유형을 판정한다(<see cref="ProjectClassifier"/>).</summary>
public sealed class ProjectAnalyzer(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>스캔은 UI 스레드를 막지 않도록 스레드 풀에서 돌린다.</summary>
    public Task<CodeResult<AnalysisResult>> AnalyzeAsync(string path, ProfileRules rules, CancellationToken ct = default) =>
        Task.Run(() => Analyze(path, rules), ct);

    /// <summary>같은 일을 현재 스레드에서 한다(시험용).</summary>
    public CodeResult<AnalysisResult> Analyze(string path, ProfileRules rules)
    {
        if (!Directory.Exists(path)) return CodeResult<AnalysisResult>.Fail($"경로를 찾을 수 없거나 폴더가 아닙니다: {path}");
        var root = TrimSeparator(Path.GetFullPath(path));
        try
        {
            var snapshot = TreeScanner.Scan(root, rules.Scan);
            var variables = VariableResolver.Resolve(root, rules.Variables);
            return CodeResult<AnalysisResult>.Success(ProjectClassifier.Classify(snapshot, variables, rules, new FileTextReader(root), _clock()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CodeResult<AnalysisResult>.Fail($"폴더를 읽을 수 없습니다: {e.Message}");
        }
    }

    private static string TrimSeparator(string path)
    {
        var root = Path.GetPathRoot(path);
        return path.Length > (root?.Length ?? 0) && (path.EndsWith('\\') || path.EndsWith('/')) ? path[..^1] : path;
    }
}
