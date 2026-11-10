using JackBean.Services.Text;

namespace JackBean.Services.Text.Tests;

public class TextSearchTests
{
    [Fact]
    public void 일반_검색은_대소문자를_가린다_가리지_않는다()
    {
        const string text = "Apple apple APPLE";
        Assert.Equal(3, TextSearch.FindAll(text, "apple", matchCase: false, regex: false).Count);
        var exact = TextSearch.FindAll(text, "apple", matchCase: true, regex: false);
        Assert.Equal([new TextMatch(6, 5)], exact);
    }

    [Fact]
    public void 겹치지_않게_찾는다()
    {
        Assert.Equal([new TextMatch(0, 2), new TextMatch(2, 2)], TextSearch.FindAll("aaaa", "aa", true, false));
    }

    [Fact]
    public void 빈_검색어는_결과가_없다()
    {
        Assert.Empty(TextSearch.FindAll("abc", "", false, false));
        Assert.Empty(TextSearch.FindAll("abc", "", false, true));
    }

    [Fact]
    public void 정규식_검색()
    {
        var m = TextSearch.FindAll("a1 b22 c333", @"\d+", matchCase: false, regex: true);
        Assert.Equal([new TextMatch(1, 1), new TextMatch(4, 2), new TextMatch(8, 3)], m);
    }

    [Fact]
    public void 잘못된_정규식과_길이_0_결과는_결과_없음이다()
    {
        Assert.Empty(TextSearch.FindAll("abc", "(", false, true));
        Assert.Empty(TextSearch.FindAll("abc", "x*", false, true));
        Assert.False(TextSearch.IsValidRegex("("));
        Assert.True(TextSearch.IsValidRegex(@"\d+"));
    }

    [Fact]
    public void 다음_이전_결과는_끝에서_돌아간다()
    {
        var m = TextSearch.FindAll("ab ab ab", "ab", true, false); // 0, 3, 6
        Assert.Equal(1, TextSearch.IndexOfNext(m, from: 1, forward: true));
        Assert.Equal(0, TextSearch.IndexOfNext(m, from: 7, forward: true)); // 끝 → 처음
        Assert.Equal(0, TextSearch.IndexOfNext(m, from: 4, forward: false));
        Assert.Equal(2, TextSearch.IndexOfNext(m, from: 0, forward: false)); // 처음 → 끝
        Assert.Equal(-1, TextSearch.IndexOfNext([], 0, true));
    }

    [Fact]
    public void 현재_위치_자체는_다음으로_친다()
    {
        var m = TextSearch.FindAll("ab ab", "ab", true, false);
        Assert.Equal(1, TextSearch.IndexOfNext(m, from: 3, forward: true));
    }

    [Fact]
    public void 모두_바꾸기()
    {
        Assert.Equal("x b x", TextSearch.ReplaceAll("a b A", "a", "x", matchCase: false, regex: false));
        Assert.Equal("x b A", TextSearch.ReplaceAll("a b A", "a", "x", matchCase: true, regex: false));
        Assert.Equal("[1] [22]", TextSearch.ReplaceAll("1 22", @"(\d+)", "[$1]", matchCase: false, regex: true));
        Assert.Equal("abc", TextSearch.ReplaceAll("abc", "(", "x", false, true)); // 잘못된 정규식은 그대로
        Assert.Equal("abc", TextSearch.ReplaceAll("abc", "", "x", false, false));
    }

    [Fact]
    public void 결과_하나의_바꿀_글()
    {
        var m = TextSearch.FindAll("id=42", @"id=(\d+)", false, true)[0];
        Assert.Equal("num:42", TextSearch.ReplacementFor("id=42", m, @"id=(\d+)", "num:$1", false, true));
        Assert.Equal("$1", TextSearch.ReplacementFor("id=42", m, "id=42", "$1", false, regex: false)); // 일반 검색은 그대로
    }
}
