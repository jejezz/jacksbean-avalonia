using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class RulePresentationTests
{
    private static Rule Ext(string id, params string[] exts) => new(id, RuleCondition.Extension(exts)!, ["a"]);

    [Fact]
    public void KindRulesGetTheirOwnIcon()
    {
        Assert.Equal("drive", RulePresentation.Label(new Rule("drives", RuleCondition.Drive, [])).Icon);
        Assert.Equal("folder", RulePresentation.Label(new Rule("folders", RuleCondition.Folder, [])).Icon);
        var file = RulePresentation.Label(new Rule("files", RuleCondition.File, []));
        Assert.Equal("file", file.Icon);
        Assert.False(file.HasGroupName);
        Assert.Empty(file.Extensions);
    }

    [Fact]
    public void ARuleNamedLikeAGroupUsesTheGroupNameAndIcon()
    {
        var label = RulePresentation.Label(Ext("image", "png", "jpg"));
        Assert.True(label.HasGroupName);
        Assert.Equal("image", label.GroupId);
        Assert.Equal("image", label.Icon);
        Assert.Equal(["png", "jpg"], label.Extensions);
    }

    [Fact]
    public void ThePluralDefaultIdsAlsoUseTheGroupName()
    {
        var label = RulePresentation.Label(Ext("images", "png"));    // 기본 정책의 규칙 id
        Assert.True(label.HasGroupName);
        Assert.Equal("image", label.GroupId);
        Assert.False(RulePresentation.Label(Ext("pdfs-mine", "pdf")).HasGroupName);
    }

    [Fact]
    public void AnyOtherNameKeepsTheRuleIdButCanStillBorrowTheGroupIcon()
    {
        var mine = RulePresentation.Label(Ext("my-photos", "png", "gif"));      // 이름은 내 것, 확장자가 모두 이미지 묶음
        Assert.False(mine.HasGroupName);
        Assert.Equal("my-photos", mine.RuleId);
        Assert.Equal("image", mine.Icon);

        var mixed = RulePresentation.Label(Ext("auto-ext-xyz", "png", "pdf"));  // 여러 묶음이 섞이면 일반 파일 아이콘
        Assert.Equal("file", mixed.Icon);

        Assert.Equal("file", RulePresentation.Label(Ext("odd", "qqq")).Icon);   // 어느 묶음에도 없다
    }

    [Fact]
    public void ChipsShowTheFirstFewAndCountTheRest()
    {
        var (shown, more) = RulePresentation.Chips(["a", "b", "c", "d"], 3);
        Assert.Equal(["a", "b", "c"], shown);
        Assert.Equal(1, more);
        Assert.Equal(0, RulePresentation.Chips(["a"], 3).More);
    }
}
