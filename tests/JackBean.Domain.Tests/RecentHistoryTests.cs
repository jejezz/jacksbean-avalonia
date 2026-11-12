using JackBean.Domain.Drop;
using JackBean.Domain.Policy;
using JackBean.Domain.Recent;

namespace JackBean.Domain.Tests;

public class RecentHistoryTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static RecentEntry E(string app, params string[] paths) => new(paths, DropKind.File, app, T);

    [Fact]
    public void AddPutsNewestFirst()
    {
        var list = RecentHistory.Add(RecentHistory.Add([], E("a", "1"), true), E("b", "2"), true);
        Assert.Equal(["b", "a"], list.Select(e => e.AppId));
    }

    [Fact]
    public void SameWorkMovesToTopInsteadOfDuplicating()
    {
        IReadOnlyList<RecentEntry> list = [E("a", "1"), E("b", "2")];
        list = RecentHistory.Add(list, E("b", "2"), true);
        Assert.Equal(["b", "a"], list.Select(e => e.AppId));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void SameItemWithAnotherAppIsAnotherLine()
    {
        var list = RecentHistory.Add([E("a", "1")], E("b", "1"), true);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void PathOrderAndCaseFollowTheOs()
    {
        Assert.True(E("a", @"C:\x", @"C:\y").SameWork(E("a", @"c:\Y", @"C:\X"), ignoreCase: true));
        Assert.False(E("a", "/x").SameWork(E("a", "/X"), ignoreCase: false));
    }

    [Fact]
    public void KeepsOnlyTheNewestTen()
    {
        IReadOnlyList<RecentEntry> list = [];
        for (var i = 0; i < 15; i++) list = RecentHistory.Add(list, E("a", i.ToString()), true);
        Assert.Equal(RecentHistory.MaxEntries, list.Count);
        Assert.Equal("14", list[0].Paths[0]);
        Assert.Equal("5", list[^1].Paths[0]);
    }

    [Fact]
    public void OutcomesBecomeOneLinePerApp()
    {
        var viewer = new AppEntry("viewer", "v", ["{path}"]);
        var editor = new AppEntry("editor", "e", ["{path}"]);
        RouteOutcome[] outcomes =
        [
            new Opened(new DroppedItem(@"C:\a.png", DropKind.File, "png"), viewer),
            new Opened(new DroppedItem(@"C:\b.png", DropKind.File, "png"), viewer),
            new Opened(new DroppedItem(@"C:\docs", DropKind.Folder), editor),
            new Skipped(new DroppedItem(@"C:\gone", DropKind.Missing)),
        ];
        var entries = RecentHistory.FromOutcomes(outcomes, T);
        Assert.Equal(2, entries.Count);
        Assert.Equal([@"C:\a.png", @"C:\b.png"], entries[0].Paths);
        Assert.Equal(DropKind.File, entries[0].Kind);
        Assert.Null(entries[0].SingleName);
        Assert.Equal("docs", entries[1].SingleName);
    }

    [Fact]
    public void MixedKindsHaveNoKind()
    {
        var app = new AppEntry("x", "x", ["{paths}"]);
        var entries = RecentHistory.FromOutcomes([new Opened(new DroppedItem(@"C:\a", DropKind.Folder), app), new Opened(new DroppedItem(@"C:\b.txt", DropKind.File, "txt"), app)], T);
        Assert.Null(Assert.Single(entries).Kind);
    }

    [Fact]
    public void DriveNameIsThePath() => Assert.Equal(@"D:\", new RecentEntry([@"D:\"], DropKind.Drive, "default", T).SingleName);

    [Fact]
    public void ShortNamesStayAsTheyAre() => Assert.Equal("short.pdf", RecentHistory.Shorten("short.pdf", 32));

    [Fact]
    public void ShortenKeepsTheStartAndTheExtension()
    {
        var shortened = RecentHistory.Shorten("quarterly-report-final-version-2026.pdf", 32);
        Assert.Equal(32, shortened.Length);
        Assert.Contains("…", shortened);
        Assert.StartsWith("quarterly-report", shortened);
        Assert.EndsWith("-2026.pdf", shortened);
    }

    [Fact]
    public void ShortenWithoutExtensionStillFits()
    {
        var s = RecentHistory.Shorten(new string('a', 50) + new string('b', 50), 20);
        Assert.Equal(20, s.Length);
        Assert.StartsWith("a", s);
        Assert.EndsWith("b", s);
    }
}
