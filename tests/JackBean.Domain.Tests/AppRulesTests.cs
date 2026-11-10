using JackBean.Domain.Policy;

namespace JackBean.Domain.Tests;

public class AppRulesTests
{
    [Theory]
    [InlineData("notepad")] [InlineData("a")] [InlineData("img-viewer_2")] [InlineData("7zip")]
    public void ValidIds(string id) => Assert.True(AppRules.IsValidId(id));

    [Theory]
    [InlineData("")] [InlineData("Notepad")] [InlineData("-x")] [InlineData("_x")] [InlineData("a b")] [InlineData("한글")] [InlineData("a.b")]
    public void InvalidIds(string id) => Assert.False(AppRules.IsValidId(id));

    [Fact]
    public void IdLengthLimit()
    {
        Assert.True(AppRules.IsValidId(new string('a', AppRules.MaxIdLength)));
        Assert.False(AppRules.IsValidId(new string('a', AppRules.MaxIdLength + 1)));
    }

    [Fact]
    public void ValidateNewApp()
    {
        string[] taken = ["explorer", "default"];
        Assert.Equal(AppFieldError.IdEmpty, AppRules.Validate("  ", "x.exe", taken, isNew: true));
        Assert.Equal(AppFieldError.IdInvalid, AppRules.Validate("Bad Id", "x.exe", taken, isNew: true));
        Assert.Equal(AppFieldError.IdTaken, AppRules.Validate("explorer", "x.exe", taken, isNew: true));
        Assert.Equal(AppFieldError.CommandEmpty, AppRules.Validate("notepad", " ", taken, isNew: true));
        Assert.Equal(AppFieldError.None, AppRules.Validate("notepad", "notepad.exe", taken, isNew: true));
        Assert.Equal(AppFieldError.None, AppRules.Validate(" notepad ", "jackbean:image-viewer", taken, isNew: true));
    }

    [Fact]
    public void ExistingAppIsNotRevalidatedForId()
    {
        // 손으로 쓴 정책의 ID(대문자 등)도 명령만 고치는 편집은 막지 않는다.
        Assert.Equal(AppFieldError.None, AppRules.Validate("Old App", "x.exe", ["Old App"], isNew: false));
        Assert.Equal(AppFieldError.CommandEmpty, AppRules.Validate("Old App", "", ["Old App"], isNew: false));
    }

    [Fact]
    public void ArgsParsing()
    {
        Assert.Equal(["--open", "{path}"], AppRules.ParseArgs("  --open \r\n\n {path}  \n"));
        Assert.Empty(AppRules.ParseArgs(null));
        Assert.Equal("--a\n{path}", AppRules.FormatArgs(["--a", "{path}"]));
    }

    [Fact]
    public void DetectsMissingPathPlaceholder()
    {
        Assert.False(AppRules.LacksPathPlaceholder(["--file={path}"]));
        Assert.True(AppRules.LacksPathPlaceholder(["--new-window"]));
        Assert.True(AppRules.LacksPathPlaceholder([]));
    }
}
