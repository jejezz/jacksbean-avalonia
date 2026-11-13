// From daylight-commander-avalonia @ 783c8db
using JackBean.Services.Updates;

namespace JackBean.Services.Tests;

public sealed class ReleaseNotesSummaryTests
{
    // v1.4.3 릴리스 노트 그대로 (generate-notes + 설치 안내 헤더).
    const string GitHubNotes = """
        ## What's Changed
        * feat: 대화상자를 타이틀바 없는 모달로, 보조 단추를 글자 단추로 (창 크롬 정책) by @jejezz in https://github.com/jejezz/daylight-commander-avalonia/pull/28
        * chore(release): v1.4.3 by @jejezz in https://github.com/jejezz/daylight-commander-avalonia/pull/29


        **Full Changelog**: https://github.com/jejezz/daylight-commander-avalonia/compare/v1.4.2...v1.4.3


        ---

        ## 설치 / Install

        | OS | 받을 파일 / File |
        |---|---|
        | Windows 10/11 (x64) | `*-windows-x64-setup.exe` (설치 프로그램) |

        **Windows**: 설치 프로그램에 아직 코드 서명이 없어서 SmartScreen이 창을 띄웁니다.
        - 목록처럼 보이는 설치 안내
        """;

    [Fact]
    public void GitHubNotesKeepOnlyUserFacingChanges()
    {
        var summary = ReleaseNotesSummary.From(GitHubNotes);
        Assert.Equal(["대화상자를 타이틀바 없는 모달로, 보조 단추를 글자 단추로 (창 크롬 정책)"], summary.Items);
        Assert.Equal(0, summary.More);
    }

    [Fact]
    public void ConventionalPrefixesAreStrippedAndInternalKindsHidden()
    {
        var summary = ReleaseNotesSummary.From("""
            * fix(windows): 경로 오류 고침 by @a in https://x/pull/1
            - feat!: 큰 변경 by @dependabot[bot] in https://x/pull/2
            * ci: 워크플로 by @a in https://x/pull/3
            * docs: README by @a in https://x/pull/4
            * refactor(core): 정리
            * perf: 목록이 빨라짐
            * 접두어 없는 항목
            """);
        Assert.Equal(["경로 오류 고침", "큰 변경", "목록이 빨라짐", "접두어 없는 항목"], summary.Items);
    }

    [Fact]
    public void NewContributorsSectionIsDropped()
    {
        var summary = ReleaseNotesSummary.From("""
            ## What's Changed
            * feat: 새 기능 by @b in https://x/pull/5

            ## New Contributors
            * @b made their first contribution in https://x/pull/5
            """);
        Assert.Equal(["새 기능"], summary.Items);
    }

    [Fact]
    public void InlineMarkdownIsRemoved()
    {
        var summary = ReleaseNotesSummary.From("* feat: **굵게**, `코드`, [링크](https://x) 지원");
        Assert.Equal(["굵게, 코드, 링크 지원"], summary.Items);
    }

    [Fact]
    public void LongListsAreCappedWithCount()
    {
        var notes = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"* feat: 항목 {i}"));
        var summary = ReleaseNotesSummary.From(notes, max: 6);
        Assert.Equal(6, summary.Items.Count);
        Assert.Equal("항목 6", summary.Items[^1]);
        Assert.Equal(3, summary.More);
    }

    [Fact]
    public void HandWrittenParagraphsAreUsedWhenThereIsNoList()
    {
        var summary = ReleaseNotesSummary.From("""
            # 1.5.0
            탭을 **끌어서** 옮길 수 있습니다.

            | 표 | 는 |
            ---
            설치 안내
            """);
        Assert.Equal(["탭을 끌어서 옮길 수 있습니다."], summary.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    [InlineData("* chore(release): v1.4.3 by @jejezz in https://x/pull/29")]
    public void NothingToShowGivesEmpty(string? notes)
    {
        var summary = ReleaseNotesSummary.From(notes);
        Assert.Empty(summary.Items);
        Assert.Equal(0, summary.More);
    }
}
