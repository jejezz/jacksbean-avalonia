using System.Text.RegularExpressions;
using JackBean.Services.CodeProfiles;

namespace JackBean.Services.Tests.CodeProfiles;

/// <summary>
/// **실제 VS Code(`code`)** 로 하는 시험. VS Code 를 찾지 못하면(다른 PC·CI) 아무것도 하지 않고 통과한다.
/// 읽기 전용 호출만 한다 — 프로필을 만들거나 VS Code 창을 열지 않는다(사용자의 VS Code 에 흔적이 남지 않는다).
/// </summary>
public class VsCodeIntegrationTests
{
    private static VsCodeCli? RealCli()
    {
        var path = CodeLocator.Discover();
        return path == null ? null : new VsCodeCli(new SystemProcessRunner(), path);
    }

    [Fact]
    public async Task TheDiscoveredCodeReportsAVersion()
    {
        if (RealCli() is not { } cli) return;
        var v = await cli.VersionAsync();
        Assert.True(v.Ok, v.Error?.ToString());
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), v.Value!);
    }

    /// <summary>
    /// 설치 경로에 공백("Microsoft VS Code")이 있고 프로필 이름에도 공백과 `/` 가 있어도, 셸 없이 직접 실행한 `code.cmd` 에 인자가 그대로 닿는다:
    /// 존재하지 않는 프로필이라 실패하지만, VS Code 가 **그 이름을 그대로 돌려주며** 거절해야 한다(cmd 의 따옴표 오류가 아니라).
    /// </summary>
    [Fact]
    public async Task AProfileNameWithSpacesReachesVsCodeIntact()
    {
        if (RealCli() is not { } cli) return;
        const string name = "No Such Profile / Test";
        var r = await cli.ListExtensionsAsync(name);
        Assert.False(r.Ok);   // 없는 프로필
        var message = r.Error!.Message;
        Assert.Contains(name, message);
        Assert.DoesNotContain("is not recognized", message);
        Assert.DoesNotContain("인식", message);
        Assert.DoesNotContain("구문이 잘못", message);
        Assert.DoesNotContain('�', message);
    }

    [Fact]
    public async Task AMissingProfileIsReportedAsNotExisting()
    {
        if (RealCli() is not { } cli) return;
        Assert.False(await cli.ProfileExistsAsync("No Such Profile / Test"));
    }

    [Fact]
    public async Task ShellCharactersInAProfileNameNeverReachCmd()
    {
        if (RealCli() is not { } cli) return;
        var marker = Path.Combine(Path.GetTempPath(), $"jackbean_injection_{Guid.NewGuid():N}.txt");
        try
        {
            var r = await cli.ListExtensionsAsync($"x & echo hacked > \"{marker}\"");
            Assert.False(r.Ok);
            Assert.Contains("셸 특수문자", r.Error!.Message);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
    }
}
