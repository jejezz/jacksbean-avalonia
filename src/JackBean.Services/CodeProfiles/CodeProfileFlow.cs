using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>마법사 화면의 단계. 분석 결과 → 추천 편집 → 설정 비교 → 적용 → 끝.</summary>
public enum FlowStep { Analysis, Recommendation, Diff, Apply, Done }

/// <summary>프로필 이름의 문제. 없으면(null) 쓸 수 있다.</summary>
public enum ProfileNameProblem { Empty, TooLong, UnsafeCharacter }

public static class ProfileNames
{
    public const int MaxLength = 60;

    /// <summary>`code.cmd`(배치 파일)가 해석하는 셸 특수문자와 줄바꿈 — VsCodeCli 가 거부하는 것과 같다. 프로필 이름에는 쓸 수 없다.</summary>
    private static readonly char[] Unsafe = ['&', '|', '<', '>', '^', '%', '"', '`', '\n', '\r'];

    public static ProfileNameProblem? Validate(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return ProfileNameProblem.Empty;
        if (n.Length > MaxLength) return ProfileNameProblem.TooLong;
        if (n.IndexOfAny(Unsafe) >= 0) return ProfileNameProblem.UnsafeCharacter;
        return null;
    }
}

/// <summary>확장 하나의 선택 상태. <see cref="Optional"/> 은 다른 프로필을 쓰는 보조 유형의 확장(기본은 선택 안 함).</summary>
public sealed record ExtensionChoice(string Id, bool Selected, bool Optional);

/// <summary>적용 단계 하나의 진행 상태.</summary>
public enum StepStatus { Pending, Running, Done, Failed }

/// <summary>적용 로그 한 줄의 종류. 화면이 현재 언어로 문구를 만든다(문구 대신 데이터를 담는다).</summary>
public enum LogEvent
{
    ProfileExists, ProfileCreating, ProfileCreated,
    ExtensionSkipped, ExtensionStarted, ExtensionOutput, ExtensionOk, ExtensionFailed,
    Failure, Cancelled,
}

/// <param name="Text">확장 id(Skipped/Started/Ok/Failed), VS Code 가 낸 줄(Output), 실패 사유(Failure). 없으면 빈 문자열.</param>
public sealed record LogLine(LogEvent Event, string Text = "", bool IsError = false);

/// <summary>
/// 마법사의 상태와 전이. 화면(Avalonia)은 이 객체를 읽고 그리기만 한다 — 그래서 창 없이 시험할 수 있다.
/// 비동기 작업은 한 번에 하나만 돈다(<see cref="IsBusy"/>). 상태가 바뀔 때마다 <see cref="Changed"/> 를 낸다(UI 스레드에서 구독해 다시 그린다).
/// </summary>
public sealed class CodeProfileFlow(CodeProfileService service, string folder)
{
    private readonly List<ExtensionChoice> _extensions = [];
    private readonly HashSet<string> _replace = new(StringComparer.Ordinal);
    private readonly List<LogLine> _log = [];
    private readonly Dictionary<ApplyStep, StepStatus> _status = new();
    private CancellationTokenSource? _cts;

    public string Folder => folder;

    public FlowStep Step { get; private set; } = FlowStep.Analysis;

    public bool IsBusy { get; private set; }

    /// <summary>분석 결과(실패하면 null 이고 <see cref="Error"/> 가 있다).</summary>
    public CodeAnalysis? Analysis { get; private set; }

    /// <summary>화면에 보일 실패 사유(분석·비교·적용·열기 중 마지막 것). 없으면 null.</summary>
    public CodeError? Error { get; private set; }

    public string? PrimaryTypeId { get; private set; }

    public Recommendation? Recommendation { get; private set; }

    public string ProfileName { get; private set; } = "";

    public IReadOnlyList<ExtensionChoice> Extensions => _extensions;

    public IReadOnlyList<string> SelectedExtensions => _extensions.Where(e => e.Selected).Select(e => e.Id).ToList();

    public SettingsDiff? Diff { get; private set; }

    /// <summary>덮어쓰기로 고른 충돌의 JSON 포인터.</summary>
    public IReadOnlySet<string> ReplaceConflicts => _replace;

    public IReadOnlyList<LogLine> Log => _log;

    public IReadOnlyDictionary<ApplyStep, StepStatus> Status => _status;

    /// <summary>끝까지 성공했을 때 settings 단계가 실제로 파일을 썼는지(바뀔 것이 없으면 false).</summary>
    public bool? SettingsWritten { get; private set; }

    public string? BackupPath { get; private set; }

    /// <summary>실패한 적용 단계(재시도 안내용). 실패하지 않았으면 null.</summary>
    public ApplyStep? FailedStep { get; private set; }

    /// <summary>적용하는 동안은 창을 닫지 못하게 한다(VS Code 프로세스가 도는 중이라서).</summary>
    public bool CanClose => !(IsBusy && Step == FlowStep.Apply);

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    // ───── 1. 분석 ─────

    public async Task AnalyzeAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        Step = FlowStep.Analysis;
        Raise();
        try
        {
            var r = await service.AnalyzeAsync(folder);
            if (!r.Ok)
            {
                Analysis = null;
                Error = r.Error;
                return;
            }
            Analysis = r.Value;
            PrimaryTypeId = r.Value!.Analysis.Types.FirstOrDefault()?.TypeId;
            LoadRecommendation(r.Value.Recommendation);
        }
        finally
        {
            IsBusy = false;
            Raise();
        }
    }

    private void LoadRecommendation(Recommendation? rec)
    {
        Recommendation = rec;
        _extensions.Clear();
        _replace.Clear();
        Diff = null;
        if (rec == null)
        {
            ProfileName = "";
            return;
        }
        ProfileName = rec.ProfileName;
        _extensions.AddRange(rec.Extensions.Select(e => new ExtensionChoice(e, true, false)));
        _extensions.AddRange(rec.OptionalExtensions.Select(e => new ExtensionChoice(e, false, true)));
    }

    /// <summary>분석 결과에서 추천 편집으로 간다. 추천이 없으면(판정된 유형이 없으면) 가지 않는다.</summary>
    public bool GoToRecommendation()
    {
        if (IsBusy || Recommendation == null) return false;
        Step = FlowStep.Recommendation;
        Raise();
        return true;
    }

    /// <summary>추천 편집에서 분석 결과 화면으로 돌아간다(다시 분석하지 않는다 — 고친 것도 그대로 둔다).</summary>
    public void BackToAnalysis()
    {
        if (IsBusy || Analysis == null) return;
        Step = FlowStep.Analysis;
        Error = null;
        Raise();
    }

    // ───── 2. 추천 편집 ─────

    /// <summary>주 유형을 바꾼다. 프로필 이름·확장·설정이 그 유형 기준으로 다시 추천된다(손으로 고친 것은 사라진다).</summary>
    public bool SelectPrimary(string typeId)
    {
        if (IsBusy || Analysis == null || typeId == PrimaryTypeId || Analysis.Analysis.Types.All(t => t.TypeId != typeId)) return false;
        var r = CodeProfileService.RecommendWithPrimary(Analysis, typeId);
        if (!r.Ok)
        {
            Error = r.Error;
            Raise();
            return false;
        }
        PrimaryTypeId = typeId;
        Error = null;
        LoadRecommendation(r.Value);
        Raise();
        return true;
    }

    public void SetProfileName(string name)
    {
        ProfileName = name;
        Raise();
    }

    public ProfileNameProblem? NameProblem => ProfileNames.Validate(ProfileName);

    public void SetExtension(string id, bool selected)
    {
        var i = _extensions.FindIndex(e => e.Id == id);
        if (i < 0 || _extensions[i].Selected == selected) return;
        _extensions[i] = _extensions[i] with { Selected = selected };
        Raise();
    }

    /// <summary>설정 비교로 갈 수 있다: 이름이 올바르고 추천이 있다.</summary>
    public bool CanContinueToDiff => !IsBusy && Recommendation != null && NameProblem == null;

    // ───── 3. 설정 비교 ─────

    /// <summary>설정 파일을 읽어 dry-run diff 를 만든다(파일은 건드리지 않는다). 실패하면(문법 오류 등) 비교 단계로 가지 않고 <see cref="Error"/> 를 둔다.</summary>
    public async Task<bool> GoToDiffAsync()
    {
        if (!CanContinueToDiff) return false;
        IsBusy = true;
        Error = null;
        Raise();
        try
        {
            if (!await RecomputeDiffAsync()) return false;
            Step = FlowStep.Diff;
            return true;
        }
        finally
        {
            IsBusy = false;
            Raise();
        }
    }

    private async Task<bool> RecomputeDiffAsync()
    {
        var r = await service.DiffAsync(folder, Recommendation!.SettingsPatch, _replace);
        if (!r.Ok)
        {
            Error = r.Error;
            return false;
        }
        Diff = r.Value;
        return true;
    }

    /// <summary>충돌 하나를 교체할지 유지할지 바꾸고 diff 를 다시 만든다.</summary>
    public async Task ToggleReplaceAsync(string pointer)
    {
        if (IsBusy || Diff == null || Diff.Conflicts.All(c => c.Pointer != pointer)) return;
        if (!_replace.Add(pointer)) _replace.Remove(pointer);
        await RecomputeBusyAsync();
    }

    /// <summary>충돌을 전부 교체하거나 전부 유지한다.</summary>
    public async Task SetAllReplaceAsync(bool replace)
    {
        if (IsBusy || Diff == null) return;
        _replace.Clear();
        if (replace) foreach (var c in Diff.Conflicts) _replace.Add(c.Pointer);
        await RecomputeBusyAsync();
    }

    private async Task RecomputeBusyAsync()
    {
        IsBusy = true;
        Raise();
        try { await RecomputeDiffAsync(); }
        finally { IsBusy = false; Raise(); }
    }

    public void BackToRecommendation()
    {
        if (IsBusy) return;
        Step = FlowStep.Recommendation;
        Error = null;
        Raise();
    }

    // ───── 4. 적용 ─────

    /// <summary>
    /// 적용을 시작한다(재시도도 같다 — 이미 끝난 부분은 서비스가 건너뛴다). 끝까지 성공하면 <see cref="Step"/> 이 Done 이고,
    /// 실패하면 Apply 에 머물며 <see cref="FailedStep"/> 이 있다.
    /// </summary>
    public async Task ApplyAsync()
    {
        if (IsBusy || Recommendation == null || NameProblem != null) return;
        IsBusy = true;
        Error = null;
        FailedStep = null;
        SettingsWritten = null;
        BackupPath = null;
        Step = FlowStep.Apply;
        _log.Clear();
        _status.Clear();
        foreach (var s in Enum.GetValues<ApplyStep>()) _status[s] = StepStatus.Pending;
        _cts = new CancellationTokenSource();
        Raise();

        var plan = new ApplyPlan(folder, ProfileName.Trim(), SelectedExtensions, Recommendation.SettingsPatch, new HashSet<string>(_replace));
        var succeeded = false;
        try
        {
            await foreach (var e in service.ApplyAsync(plan, _cts.Token))
            {
                Consume(e);
                Raise();
                _cts.Token.ThrowIfCancellationRequested();   // 서비스가 취소를 바로 알아차리지 못해도 다음 이벤트에서 멈춘다
            }
            succeeded = FailedStep == null;
        }
        catch (OperationCanceledException)
        {
            _log.Add(new LogLine(LogEvent.Cancelled));
            FailedStep ??= _status.Where(kv => kv.Value == StepStatus.Running).Select(kv => (ApplyStep?)kv.Key).FirstOrDefault() ?? ApplyStep.Profile;
            _status[FailedStep.Value] = StepStatus.Failed;
            Error = new CodeError("cancelled");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            if (succeeded) Step = FlowStep.Done;
            Raise();
        }
    }

    private void Consume(ApplyEvent e)
    {
        switch (e)
        {
            case StepStarted s: _status[s.Step] = StepStatus.Running; break;
            case StepFinished f:
                _status[f.Step] = StepStatus.Done;
                if (f.Step == ApplyStep.Settings)
                {
                    SettingsWritten = f.Written;
                    BackupPath = f.BackupPath;
                }
                break;
            case StepFailed f:
                _status[f.Step] = StepStatus.Failed;
                FailedStep = f.Step;
                Error = new CodeError(f.Message);
                _log.Add(new LogLine(LogEvent.Failure, f.Message, IsError: true));
                break;
            case ProfileExists: _log.Add(new LogLine(LogEvent.ProfileExists)); break;
            case ProfileCreating: _log.Add(new LogLine(LogEvent.ProfileCreating)); break;
            case ProfileCreated: _log.Add(new LogLine(LogEvent.ProfileCreated)); break;
            case ExtensionSkipped x: _log.Add(new LogLine(LogEvent.ExtensionSkipped, x.Id)); break;
            case ExtensionStarted x: _log.Add(new LogLine(LogEvent.ExtensionStarted, x.Id)); break;
            case ExtensionOutput x: _log.Add(new LogLine(LogEvent.ExtensionOutput, x.Line, x.IsError)); break;
            case ExtensionFinished x: _log.Add(new LogLine(x.Ok ? LogEvent.ExtensionOk : LogEvent.ExtensionFailed, x.Id, !x.Ok)); break;
        }
    }

    /// <summary>진행 중인 적용을 멈춘다(창을 닫을 때 확인을 받은 뒤 부른다).</summary>
    public void Cancel() => _cts?.Cancel();

    /// <summary>판정된 유형이 없는 폴더인가(분석은 끝났다). 이때는 프로필 대신 기본 프로필로 열지 고른다.</summary>
    public bool HasNoTypes => Analysis is { } a && a.Analysis.Types.Count == 0;

    /// <summary>프로필 없이 폴더만 연다. 실패하면 <see cref="Error"/> 에 사유를 둔다.</summary>
    public async Task<bool> OpenDefaultAsync()
    {
        if (IsBusy) return false;
        IsBusy = true;
        Error = null;
        Raise();
        try
        {
            var err = await service.OpenDefaultAsync(folder);
            Error = err;
            return err == null;
        }
        finally
        {
            IsBusy = false;
            Raise();
        }
    }

    // ───── 5. 열기 ─────

    /// <summary>폴더를 이 프로필로 연다. 실패하면 <see cref="Error"/> 에 사유를 둔다.</summary>
    public async Task<bool> OpenAsync()
    {
        if (IsBusy) return false;
        IsBusy = true;
        Error = null;
        Raise();
        try
        {
            var err = await service.OpenAsync(folder, ProfileName.Trim());
            Error = err;
            return err == null;
        }
        finally
        {
            IsBusy = false;
            Raise();
        }
    }
}
