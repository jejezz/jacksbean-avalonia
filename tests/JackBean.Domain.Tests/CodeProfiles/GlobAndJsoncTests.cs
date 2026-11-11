using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;

namespace JackBean.Domain.Tests.CodeProfiles;

public class GlobTests
{
    private static bool M(string glob, string path) => new Glob(glob).Matches(path);

    [Fact]
    public void PlainNameMatchesOnlyAtTheRoot()
    {
        Assert.True(M("sdkconfig", "sdkconfig"));
        Assert.False(M("sdkconfig", "main/sdkconfig"));
        Assert.False(M("sdkconfig", "sdkconfig.old"));
    }

    [Fact]
    public void DoubleStarSlashMatchesZeroOrMoreDirectories()
    {
        Assert.True(M("**/idf_component.yml", "idf_component.yml"));
        Assert.True(M("**/idf_component.yml", "main/idf_component.yml"));
        Assert.True(M("**/idf_component.yml", "a/b/idf_component.yml"));
        Assert.False(M("**/idf_component.yml", "xidf_component.yml"));
    }

    [Fact]
    public void SingleStarStaysInsideADirectoryAndDoubleStarDoesNot()
    {
        Assert.True(M("*.c", "main.c"));
        Assert.False(M("*.c", "src/main.c"));
        Assert.True(M("src/**", "src/a/b.c"));
    }

    [Fact]
    public void QuestionMarkIsExactlyOneNonSeparatorCharacter()
    {
        Assert.True(M("a?c", "abc"));
        Assert.False(M("a?c", "a/c"));
        Assert.False(M("a?c", "ac"));
    }

    [Fact]
    public void RegexMetacharactersAreLiteral()
    {
        Assert.True(M("a.b", "a.b"));
        Assert.False(M("a.b", "axb"));
        Assert.True(M("[x]+", "[x]+"));
    }

    [Fact]
    public void MatchingIsCaseSensitive() => Assert.False(M("Makefile", "makefile"));
}

public class JsoncTests
{
    private static JsoncDocument Ok(string text)
    {
        var r = Jsonc.Parse(text);
        Assert.True(r.Ok, r.Error?.ToString());
        return r.Value!;
    }

    [Fact]
    public void PlainJsonHasNoComments()
    {
        var d = Ok("""{"a": 1, "b": [true, null]}""");
        Assert.False(d.HadComments);
        Assert.Equal(1, d.Value!["a"]!.GetValue<int>());
        Assert.Equal(2, d.Value["b"]!.AsArray().Count);
    }

    [Fact]
    public void LineComments()
    {
        var d = Ok("{\n  // editor font\n  \"a\": 1 // trailing\n}\n");
        Assert.True(d.HadComments);
        Assert.Equal(1, d.Value!["a"]!.GetValue<int>());
    }

    [Fact]
    public void BlockCommentsIncludingAcrossLines()
    {
        var d = Ok("{ /* one */ \"a\": /* two\nlines */ 2 }");
        Assert.True(d.HadComments);
        Assert.Equal(2, d.Value!["a"]!.GetValue<int>());
    }

    [Fact]
    public void SlashesInsideStringsAreNotComments()
    {
        var d = Ok("""{"url": "http://x.y/*z*/", "p": "a//b"}""");
        Assert.False(d.HadComments);
        Assert.Equal("http://x.y/*z*/", d.Value!["url"]!.GetValue<string>());
        Assert.Equal("a//b", d.Value["p"]!.GetValue<string>());
    }

    [Fact]
    public void TrailingCommasInObjectsAndArrays()
    {
        var d = Ok("""{"a": [1, 2,], "b": {"c": 1,},}""");
        Assert.Equal(2, d.Value!["a"]!.AsArray().Count);
        Assert.Equal(1, d.Value["b"]!["c"]!.GetValue<int>());
    }

    [Fact]
    public void TrailingCommaFollowedByACommentBeforeTheBracket()
    {
        var d = Ok("{\"a\": [1, // one\n 2, /* two */ ], }");
        Assert.Equal(2, d.Value!["a"]!.AsArray().Count);
    }

    [Fact]
    public void ACommaInsideAStringBeforeABracketIsKept() =>
        Assert.Equal("x,}", Ok("""{"a": "x,}"}""").Value!["a"]!.GetValue<string>());

    [Fact]
    public void EscapedQuoteAndTrailingBackslashBeforeAComment()
    {
        var d = Ok("{\"a\": \"say \\\"//hi\\\"\", \"b\": \"c:\\\\\" // comment\n}");
        Assert.True(d.HadComments);
        Assert.Equal("say \"//hi\"", d.Value!["a"]!.GetValue<string>());
        Assert.Equal("c:\\", d.Value["b"]!.GetValue<string>());
    }

    [Fact]
    public void ByteOrderMarkIsIgnored() =>
        Assert.Equal(1, Ok("\uFEFF{\"a\": 1}").Value!["a"]!.GetValue<int>());

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    [InlineData("// nothing here\n")]
    public void EmptyAndCommentOnlyFilesHaveNoValue(string text)
    {
        var d = Ok(text);
        Assert.Null(d.Value);
        Assert.Equal(text.Contains("//"), d.HadComments);
    }

    [Fact]
    public void UnterminatedBlockCommentIsAnError()
    {
        var r = Jsonc.Parse("{\n  \"a\": 1 /* never closed\n}");
        Assert.False(r.Ok);
    }

    [Fact]
    public void SyntaxErrorKeepsOriginalLineNumbersDespiteComments()
    {
        var r = Jsonc.Parse("{\n  // c\n  /* x\n y */\n  \"a\": oops\n}");
        Assert.False(r.Ok);
        Assert.Equal(5, r.Error!.Line);
    }

    [Fact]
    public void KeyOrderFollowsTheFile()
    {
        var d = Ok("""{"z": 1, "a": 2, "m": 3}""");
        Assert.Equal(["z", "a", "m"], d.Value!.AsObject().Select(kv => kv.Key));
    }

    [Fact]
    public void PositionsMapPathsToLines()
    {
        var text = "{\n  \"types\": [\n    {\"id\": 1},\n    {\n      \"id\": 2\n    }\n  ]\n}";
        var p = JsonPositions.Build(text);
        Assert.Equal(1, p.LineOf(""));
        Assert.Equal(2, p.LineOf("types"));
        Assert.Equal(3, p.LineOf("types[0]"));
        Assert.Equal(4, p.LineOf("types[1]"));
        Assert.Equal(5, p.LineOf("types[1].id"));
        Assert.Null(p.LineOf("nope"));
    }
}
