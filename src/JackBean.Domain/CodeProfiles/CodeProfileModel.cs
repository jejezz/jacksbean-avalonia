using System.Text.Json.Nodes;

namespace JackBean.Domain.CodeProfiles;

/// <summary>판정 근거 하나. <see cref="RuleId"/> 는 근거를 낸 매처 종류(`file` `dir` `fileContains` `variable`).</summary>
public sealed record Evidence(string RuleId, string Description, string? RelativePath = null);

/// <summary>판정된 프로젝트 유형 하나(규칙의 `types[].id`).</summary>
public sealed record DetectedType(string TypeId, string Label, int Priority, IReadOnlyList<Evidence> Evidence);

public sealed record TreeStats(int FileCount, bool Truncated, IReadOnlyList<string> LargeDirsPresent);

/// <summary>
/// 폴더를 훑은 결과. 경로는 모두 루트 기준 상대 경로이고 구분자는 `/` 다.
/// <see cref="Dirs"/> 는 들어가지 않은 skipDirs 도 포함한다(존재 여부 자체가 판정 근거가 될 수 있다).
/// </summary>
public sealed record ScanSnapshot(
    string Root,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Dirs,
    IReadOnlyList<string> SkippedDirNames,
    bool Truncated);

/// <summary>priority 내림차순, suppresses 적용 뒤의 판정 결과. <see cref="Variables"/> 는 해석된 변수(예: aospRoot).</summary>
public sealed record AnalysisResult(
    IReadOnlyList<DetectedType> Types,
    TreeStats Stats,
    IReadOnlyDictionary<string, string> Variables,
    DateTimeOffset AnalyzedAt);

/// <summary>
/// 추천: 프로필 이름, 그 프로필에 설치할 확장, 다른 프로필을 쓰는 보조 유형의 선택 확장,
/// `.vscode/settings.json` 에 병합할 내용, 사용자 안내 문구.
/// </summary>
public sealed record Recommendation(
    string ProfileName,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> OptionalExtensions,
    JsonObject SettingsPatch,
    IReadOnlyList<string> Notes);
