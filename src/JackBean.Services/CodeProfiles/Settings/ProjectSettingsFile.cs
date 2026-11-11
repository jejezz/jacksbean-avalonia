using System.Text;
using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;

namespace JackBean.Services.CodeProfiles;

/// <summary>설정 파일 쓰기 결과. <see cref="Written"/> 은 바뀔 것이 없어 쓰지 않았으면 false, <see cref="BackupPath"/> 는 기존 파일을 덮어쓰기 전에 만든 백업(없으면 null).</summary>
public sealed record SettingsWriteResult(SettingsDiff Diff, bool Written, string? BackupPath);

/// <summary>
/// 프로젝트의 `.vscode/settings.json` 을 읽고 병합해 쓴다.
/// 쓸 때는 (1) 기존 파일을 `settings.json.bak.&lt;yyyyMMdd-HHmmss&gt;` 로 복사하고, (2) 같은 폴더의 임시 파일에 쓴 뒤 교체해서 도중에 꺼져도 원본이 깨지지 않게 하고,
/// (3) `.vscode/` 폴더가 없으면 만든다. 바뀔 것이 없으면 아무것도 쓰지 않는다. **호출하기 전에 <see cref="DiffAsync"/> 로 사용자에게 diff 를 보여주고 확인을 받아야 한다.**
/// </summary>
public sealed class ProjectSettingsFile(Func<DateTime>? clock = null)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);

    public static string SettingsPath(string projectFolder) => Path.Combine(projectFolder, ".vscode", "settings.json");

    /// <summary>패치를 적용했을 때의 diff 를 만든다(파일을 쓰지 않는다).</summary>
    public async Task<CodeResult<SettingsDiff>> DiffAsync(string projectFolder, JsonObject patch, IReadOnlySet<string>? replaceConflicts = null)
    {
        string? text;
        try
        {
            var path = SettingsPath(projectFolder);
            text = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CodeResult<SettingsDiff>.Fail($"settings.json 을 읽을 수 없습니다: {e.Message}");
        }
        return SettingsDiffer.Compute(text, patch, replaceConflicts);
    }

    public async Task<CodeResult<SettingsWriteResult>> ApplyAsync(string projectFolder, JsonObject patch, IReadOnlySet<string>? replaceConflicts = null)
    {
        var diffResult = await DiffAsync(projectFolder, patch, replaceConflicts);
        if (!diffResult.Ok) return CodeResult<SettingsWriteResult>.Fail(diffResult.Error!);
        var diff = diffResult.Value!;
        if (!diff.HasChanges) return CodeResult<SettingsWriteResult>.Success(new SettingsWriteResult(diff, false, null));

        var path = SettingsPath(projectFolder);
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string? backup = null;
            if (diff.ExistingFile)
            {
                backup = FreeBackupPath(path);
                File.Copy(path, backup);
            }
            await File.WriteAllTextAsync(temp, diff.MergedJson, Utf8NoBom);
            File.Move(temp, path, overwrite: true);
            return CodeResult<SettingsWriteResult>.Success(new SettingsWriteResult(diff, true, backup));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* 임시 파일 정리 실패는 원래 오류보다 덜 중요하다 */ }
            return CodeResult<SettingsWriteResult>.Fail($"settings.json 을 쓰지 못했습니다: {e.Message}");
        }
    }

    /// <summary>같은 초에 두 번 쓰는 경우를 위해 이미 있는 이름이면 `-1`, `-2` 를 붙인다.</summary>
    private string FreeBackupPath(string settingsPath)
    {
        var baseName = $"{settingsPath}.bak.{_clock():yyyyMMdd-HHmmss}";
        var candidate = baseName;
        for (var n = 1; File.Exists(candidate); n++) candidate = $"{baseName}-{n}";
        return candidate;
    }
}
