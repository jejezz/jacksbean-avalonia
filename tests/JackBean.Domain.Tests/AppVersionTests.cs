using System.Text.RegularExpressions;
using JackBean.Domain;

namespace JackBean.Domain.Tests;

public class AppVersionTests
{
    [Fact]
    public void 버전은_MAJOR_MINOR_PATCH_형식이다()
    {
        // 첫 정식 릴리스 전에는 0.y.z-rc.n 이다 (macOS 패키징이 0.y.z 를 거부하므로 1.0.0 부터 macOS 배포).
        var m = Regex.Match(AppVersion.Version, @"^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.]+)?$");
        Assert.True(m.Success, AppVersion.Version);
    }

    [Fact]
    public void 빌드_번호는_양수이고_표시는_버전과_빌드를_함께_보인다()
    {
        Assert.True(AppVersion.Build >= 1);
        Assert.Equal($"{AppVersion.Version} ({AppVersion.Build})", AppVersion.Display);
    }
}
