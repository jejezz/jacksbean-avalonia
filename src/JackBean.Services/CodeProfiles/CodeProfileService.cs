using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>분석 결과와 그에 따른 추천. 판정된 유형이 없으면 <see cref="Recommendation"/> 은 null. <see cref="Rules"/> 는 분석에 쓴 규칙(주 유형을 바꿔 다시 추천할 때 쓴다).</summary>
public sealed record CodeAnalysis(AnalysisResult Analysis, Recommendation? Recommendation, IReadOnlyList<DetectedType> Competitors, ProfileRules Rules);

/// <summary>"적용" 한 번에 필요한 모든 입력.</summary>
public sealed record ApplyPlan(
    string Folder,
    string ProfileName,
    IReadOnlyList<string> Extensions,
    JsonObject SettingsPatch,
    IReadOnlySet<string>? ReplaceConflicts = null);

/// <summary>적용 단계. 프로필이 있어야 확장을 설치할 수 있으므로 프로필 준비가 가장 앞이다.</summary>
public enum ApplyStep { Profile, Extensions, Settings }

/// <summary>적용 진행 이벤트. 화면이 현재 언어로 문구를 만들 수 있도록 문구 대신 데이터를 담는다.</summary>
public abstract record ApplyEvent;

public sealed record StepStarted(ApplyStep Step) : ApplyEvent;

/// <summary>프로필이 이미 있다.</summary>
public sealed record ProfileExists : ApplyEvent;

/// <summary>프로필이 없어 VS Code 를 열어 새로 만드는 중이다(수십 초 걸릴 수 있다).</summary>
public sealed record ProfileCreating : ApplyEvent;

public sealed record ProfileCreated : ApplyEvent;

public sealed record ExtensionSkipped(string Id) : ApplyEvent;

public sealed record ExtensionStarted(string Id) : ApplyEvent;

public sealed record ExtensionOutput(string Id, string Line, bool IsError = false) : ApplyEvent;

public sealed record ExtensionFinished(string Id, bool Ok, int ExitCode) : ApplyEvent;

/// <param name="Written">settings 단계에서 파일을 실제로 썼는지.</param>
public sealed record StepFinished(ApplyStep Step, bool? Written = null, string? BackupPath = null) : ApplyEvent;

/// <summary>단계가 실패해 적용을 멈췄다.</summary>
public sealed record StepFailed(ApplyStep Step, string Message) : ApplyEvent;

/// <summary>
/// 폴더를 분석해 추천을 만들고, 사용자가 확인하면 **프로필 준비 → 확장 설치 → 설정 쓰기**를 차례로 한 뒤 VS Code 로 연다.
/// 화면은 엔진·VS Code 를 이 클래스로만 쓴다.
/// </summary>
public sealed class CodeProfileService(VsCodeCli cli, ProjectAnalyzer analyzer, CodeProfileRulesStore rulesStore, ProjectSettingsFile settingsFile, CodeProjectMemory memory, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public VsCodeCli Cli => cli;

    public CodeProjectMemory Memory => memory;

    public async Task<CodeResult<CodeAnalysis>> AnalyzeAsync(string folder, CancellationToken ct = default)
    {
        var rules = await rulesStore.LoadAsync();
        if (!rules.Ok) return CodeResult<CodeAnalysis>.Fail(rules.Error!);
        var analysis = await analyzer.AnalyzeAsync(folder, rules.Value!, ct);
        if (!analysis.Ok) return CodeResult<CodeAnalysis>.Fail(analysis.Error!);

        var result = analysis.Value!;
        var recommendation = Recommender.Recommend(result, rules.Value!).Value;
        return CodeResult<CodeAnalysis>.Success(new CodeAnalysis(result, recommendation, ProjectClassifier.CloseCompetitors(result, rules.Value!), rules.Value!));
    }

    /// <summary>사용자가 주 유형을 바꿨을 때 그 유형을 주 유형으로 삼아 다시 추천한다.</summary>
    public static CodeResult<Recommendation> RecommendWithPrimary(CodeAnalysis analysis, string primaryTypeId) =>
        Recommender.RecommendWithPrimary(analysis.Analysis, analysis.Rules, primaryTypeId);

    /// <summary>설정 패치를 적용했을 때의 diff(쓰지 않는다). 사용자에게 보여 주고 확인받는 단계다.</summary>
    public Task<CodeResult<SettingsDiff>> DiffAsync(string folder, JsonObject patch, IReadOnlySet<string>? replaceConflicts = null) =>
        settingsFile.DiffAsync(folder, patch, replaceConflicts);

    /// <summary>
    /// 적용한다. 한 단계가 실패하면 <see cref="StepFailed"/> 를 내고 멈춘다(재시도하면 이미 끝난 부분은 건너뛴다: 있는 프로필·설치된 확장·바뀐 것 없는 설정).
    /// 끝까지 성공하면 이 폴더에 적용한 프로필을 기억한다.
    /// </summary>
    public async IAsyncEnumerable<ApplyEvent> ApplyAsync(ApplyPlan plan, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StepStarted(ApplyStep.Profile);
        if (await cli.ProfileExistsAsync(plan.ProfileName))
        {
            yield return new ProfileExists();
        }
        else
        {
            yield return new ProfileCreating();
            if (await cli.CreateProfileAsync(plan.ProfileName, plan.Folder, ct) is { } error)
            {
                yield return new StepFailed(ApplyStep.Profile, error.Message);
                yield break;
            }
            yield return new ProfileCreated();
        }
        yield return new StepFinished(ApplyStep.Profile);

        yield return new StepStarted(ApplyStep.Extensions);
        var failed = new List<string>();
        await foreach (var e in cli.InstallExtensionsAsync(plan.ProfileName, plan.Extensions, ct))
        {
            switch (e)
            {
                case InstallSkipped s:
                    yield return new ExtensionSkipped(s.ExtensionId);
                    break;
                case InstallStarted s:
                    yield return new ExtensionStarted(s.ExtensionId);
                    break;
                case InstallLog l:
                    yield return new ExtensionOutput(l.ExtensionId, l.Line, l.IsError);
                    break;
                case InstallFinished f:
                    if (!f.Success) failed.Add(f.ExtensionId);
                    yield return new ExtensionFinished(f.ExtensionId, f.Success, f.ExitCode);
                    break;
            }
        }
        if (failed.Count > 0)
        {
            yield return new StepFailed(ApplyStep.Extensions, string.Join(", ", failed));
            yield break;
        }
        yield return new StepFinished(ApplyStep.Extensions);

        yield return new StepStarted(ApplyStep.Settings);
        var written = await settingsFile.ApplyAsync(plan.Folder, plan.SettingsPatch, plan.ReplaceConflicts);
        if (!written.Ok)
        {
            yield return new StepFailed(ApplyStep.Settings, written.Error!.Message);
            yield break;
        }
        yield return new StepFinished(ApplyStep.Settings, written.Value!.Written, written.Value.BackupPath);

        try
        {
            await memory.RememberAsync(plan.Folder, plan.ProfileName, _clock());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 기억하지 못해도 적용은 끝났다. 다음 드롭 때 한 번 더 확인하게 될 뿐이다.
        }
    }

    /// <summary>폴더를 그 프로필로 연다. 성공하면 null.</summary>
    public Task<CodeError?> OpenAsync(string folder, string profile) => cli.OpenAsync(profile, folder);
}
