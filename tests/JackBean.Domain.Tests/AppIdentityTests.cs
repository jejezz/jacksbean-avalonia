using JackBean.Domain;

namespace JackBean.Domain.Tests;

public class AppIdentityTests
{
    [Fact]
    public void 표시_이름과_파일_이름은_규약대로다()
    {
        Assert.Equal("Jack’s Bean", AppIdentity.DisplayName);
        Assert.Equal("JacksBean", AppIdentity.FileName);   // 사용자 데이터 폴더 이름
        Assert.DoesNotContain(' ', AppIdentity.FileName);
    }

    [Fact]
    public void 식별자는_고정이다() => Assert.Equal("art.zoomon.jacksbean.avalonia", AppIdentity.Id);

    [Fact]
    public void 저작권_문구는_첫_릴리스_연도를_쓴다()
    {
        Assert.Equal(2026, AppIdentity.FirstReleaseYear);
        Assert.Equal("Copyright © 2026 Jongyun Ahn", AppIdentity.Copyright);
    }
}
