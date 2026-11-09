using JackBean.Domain;

namespace JackBean.Domain.Tests;

public class AppIdentityTests
{
    [Fact]
    public void 표시_이름과_파일_이름은_규약대로다()
    {
        Assert.Equal("Jack’s Bean", AppIdentity.DisplayName);
        Assert.Equal("JacksBean", AppIdentity.FileName);   // Flutter 판과 같은 사용자 데이터 폴더(정책 파일을 이어 쓴다)
        Assert.DoesNotContain(' ', AppIdentity.FileName);
    }

    [Fact]
    public void 식별자는_Flutter_판과_다르다()
    {
        // 같은 PC에 나란히 설치해 비교할 수 있게 한다.
        Assert.Equal("art.zoomon.jacksbean.avalonia", AppIdentity.Id);
        Assert.NotEqual("art.zoomon.jacksbean", AppIdentity.Id);
    }

    [Fact]
    public void 저작권_문구는_첫_릴리스_연도를_쓴다()
    {
        Assert.Equal(2026, AppIdentity.FirstReleaseYear);
        Assert.Equal("Copyright © 2026 Jongyun Ahn", AppIdentity.Copyright);
    }
}
