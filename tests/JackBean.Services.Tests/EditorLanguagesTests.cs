using JackBean.Services.Text;

namespace JackBean.Services.Text.Tests;

public class EditorLanguagesTests
{
    [Theory]
    [InlineData("Program.cs", ".cs")]
    [InlineData("main.PY", ".py")]
    [InlineData("config.toml", ".ini")]
    [InlineData("app.env", ".properties")]
    [InlineData("run.bat", ".bat")]
    [InlineData("run.cmd", ".bat")]
    [InlineData("Main.axaml", ".xml")]
    [InlineData("build.gradle", ".groovy")]
    [InlineData("a.yml", ".yaml")]
    public void 확장자는_문법이_아는_확장자로_바뀐다(string name, string expected) =>
        Assert.Equal(expected, EditorLanguages.ExtensionFor(name));

    [Theory]
    [InlineData("data.csv")]
    [InlineData("a.tsv")]
    [InlineData("server.log")]
    [InlineData("notes.txt")]
    [InlineData("noext")]
    [InlineData("trailing.")]
    public void 강조하지_않는_파일은_null이다(string name) => Assert.Null(EditorLanguages.ExtensionFor(name));

    [Theory]
    [InlineData("Dockerfile", ".dockerfile")]
    [InlineData("makefile", ".mk")]
    [InlineData(".gitignore", ".ini")]
    public void 확장자_없는_이름은_이름_표로_정한다(string name, string expected) =>
        Assert.Equal(expected, EditorLanguages.ExtensionFor(name));
}
