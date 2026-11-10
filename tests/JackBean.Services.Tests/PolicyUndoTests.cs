using JackBean.Domain.Drop;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.Services.Tests;

public class PolicyUndoTests
{
    private static PolicyStore Store(TempDir t) => new(PolicyOs.Windows, t.Path);

    [Fact]
    public async Task NothingToUndoAtFirst()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        Assert.False(s.CanUndo);
        Assert.False((await s.UndoAsync()).Ok);
    }

    [Fact]
    public async Task UndoRestoresThePreviousFileAndRedoBringsItBack()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        var original = await File.ReadAllTextAsync(s.FilePath);

        Assert.True((await s.UpsertAppAsync(new AppUpsert("notepad", "notepad.exe", ["{path}"]))).Ok);
        Assert.True(s.CanUndo);
        var changed = await File.ReadAllTextAsync(s.FilePath);
        Assert.NotEqual(original, changed);

        Assert.True((await s.UndoAsync()).Ok);
        Assert.Equal(original, await File.ReadAllTextAsync(s.FilePath));        // 글자 하나까지 그대로
        Assert.DoesNotContain("notepad", (await s.LoadAsync()).Apps.Keys);

        Assert.True((await s.UndoAsync()).Ok);                                  // 한 번 더 = 다시 실행
        Assert.Equal(changed, await File.ReadAllTextAsync(s.FilePath));
        Assert.Contains("notepad", (await s.LoadAsync()).Apps.Keys);
    }

    [Fact]
    public async Task OnlyTheLastChangeIsUndone()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        await s.UpsertAppAsync(new AppUpsert("a", "a.exe", ["{path}"]));
        await s.UpsertAppAsync(new AppUpsert("b", "b.exe", ["{path}"]));

        Assert.True((await s.UndoAsync()).Ok);
        var apps = (await s.LoadAsync()).Apps.Keys;
        Assert.Contains("a", apps);          // 한 단계만 되돌린다
        Assert.DoesNotContain("b", apps);
    }

    [Fact]
    public async Task RuleEditsAndRememberAreUndoable()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        await s.UpsertAppAsync(new AppUpsert("a", "a.exe", ["{path}"]));

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("pdf", RuleCondition.Extension(["pdf"])!, ["a"]))).Ok);
        Assert.True((await s.UndoAsync()).Ok);
        Assert.DoesNotContain("pdf", (await s.LoadAsync()).Rules.Select(r => r.Id));

        Assert.True((await s.RememberAsync([new(SuggestionKind.Folder)], "a")).Ok);
        Assert.Equal(["a"], (await s.LoadAsync()).Rules.First(r => r.Id == "folders").Choices);
        Assert.True((await s.UndoAsync()).Ok);
        Assert.Empty((await s.LoadAsync()).Rules.First(r => r.Id == "folders").Choices);

        Assert.True((await s.RemoveRuleAsync("files")).Ok);
        Assert.True((await s.UndoAsync()).Ok);
        Assert.Contains("files", (await s.LoadAsync()).Rules.Select(r => r.Id));
    }

    [Fact]
    public async Task RefusesWhenTheFileWasEditedByHandSince()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        await s.UpsertAppAsync(new AppUpsert("a", "a.exe", ["{path}"]));
        var edited = (await File.ReadAllTextAsync(s.FilePath)).Replace("a.exe", "손으로-고침.exe");
        await File.WriteAllTextAsync(s.FilePath, edited);

        var result = await s.UndoAsync();
        Assert.False(result.Ok);
        Assert.Equal(edited, await File.ReadAllTextAsync(s.FilePath));          // 손으로 고친 것을 덮어쓰지 않는다
    }

    [Fact]
    public async Task FailedEditsDoNotChangeTheUndoState()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        Assert.False((await s.RemoveAppAsync("ghost")).Ok);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public async Task ResetToDefaultsClearsTheUndoHistory()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        await s.UpsertAppAsync(new AppUpsert("a", "a.exe", ["{path}"]));
        Assert.True(s.CanUndo);
        Assert.True((await s.ResetToDefaultsAsync()).Ok);
        Assert.False(s.CanUndo);
    }
}
