using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>
/// 내장 기본 규칙에 사용자 규칙 파일(`code-profile-rules.user.json`, 사용자 데이터 폴더)을 얹어 돌려준다.
/// 사용자 파일이 없으면 기본 규칙만. 사용자 파일이 있는데 잘못됐으면 파일 경로와 위치가 담긴 오류를 돌려주고 **기본 규칙으로 대신하지 않는다**
/// (잘못된 규칙이 조용히 무시되면 사용자가 이유를 모른다). 매번 파일을 다시 읽는다 — 고치면 바로 반영된다.
/// </summary>
public sealed class CodeProfileRulesStore(string directory)
{
    public const string UserFileName = "code-profile-rules.user.json";

    public string UserRulesPath => Path.Combine(directory, UserFileName);

    public async Task<CodeResult<ProfileRules>> LoadAsync()
    {
        var baseResult = ProfileRulesParser.Parse(DefaultProfileRules.Json);
        if (!baseResult.Ok) return CodeResult<ProfileRules>.Fail($"내장 기본 규칙이 잘못됐습니다: {baseResult.Error}");
        var baseRules = baseResult.Value!;

        string text;
        try
        {
            if (!File.Exists(UserRulesPath)) return Checked(baseRules);
            text = await File.ReadAllTextAsync(UserRulesPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CodeResult<ProfileRules>.Fail($"{UserRulesPath}: 사용자 규칙 파일을 읽을 수 없습니다: {e.Message}");
        }

        var user = ProfileRulesParser.Parse(text, baseRules.Scan);
        if (!user.Ok) return CodeResult<ProfileRules>.Fail(new CodeError($"{UserRulesPath}: {user.Error!.Message}", user.Error.Line));
        return Checked(baseRules.OverriddenBy(user.Value!));
    }

    private static CodeResult<ProfileRules> Checked(ProfileRules rules) =>
        rules.Validate() is { } problem ? CodeResult<ProfileRules>.Fail(problem) : CodeResult<ProfileRules>.Success(rules);
}
