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

        Assert.True((await s.UpsertRuleAsync(new RuleEdit("mine", RuleCondition.Extension(["qqq"])!, ["a"]))).Ok);
        Assert.True((await s.UndoAsync()).Ok);
        Assert.DoesNotContain("mine", (await s.LoadAsync()).Rules.Select(r => r.Id));

        Assert.True((await s.RememberAsync([new(SuggestionKind.Folder)], "a")).Ok);
        Assert.Equal(["a"], (await s.LoadAsync()).Rules.First(r => r.Id == "folders").Choices);
        Assert.True((await s.UndoAsync()).Ok);
        Assert.Equal(["explorer", "terminal", "archive"], (await s.LoadAsync()).Rules.First(r => r.Id == "folders").Choices);   // 기본 정책으로 돌아온다

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

    [Fact]
    public async Task RestoreDefaultsReplacesRulesAndAppsKeepsOtherKeysAndIsUndoable()
    {
        using var t = new TempDir();
        var s = Store(t);
        await s.LoadAsync();
        await s.UpsertAppAsync(new AppUpsert("notepad", "notepad.exe", ["{path}"]));
        await s.UpsertRuleAsync(new RuleEdit("mine", RuleCondition.Extension(["qqq"])!, ["notepad"]));
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(s.FilePath))!.AsObject();
        json["note"] = "내 메모";
        await File.WriteAllTextAsync(s.FilePath, json.ToJsonString());
        var before = await File.ReadAllTextAsync(s.FilePath);

        var result = await s.RestoreDefaultsAsync(new DateTime(2026, 11, 10, 15, 0, 0));
        Assert.Null(result.Error);
        Assert.Equal(before, await File.ReadAllTextAsync(result.BackupPath!));          // 이전 내용이 사본으로 남는다
        Assert.EndsWith(".backup-20261110-150000", result.BackupPath);
        var doc = await s.LoadAsync();
        Assert.DoesNotContain("mine", doc.Rules.Select(r => r.Id));
        Assert.DoesNotContain("notepad", doc.Apps.Keys);
        Assert.Contains("images", doc.Rules.Select(r => r.Id));
        Assert.Equal("내 메모", (string?)System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(s.FilePath))!["note"]);   // 모르는 키는 보존

        Assert.True(s.CanUndo);
        Assert.True((await s.UndoAsync()).Ok);
        Assert.Contains("mine", (await s.LoadAsync()).Rules.Select(r => r.Id));       // 되돌릴 수 있다
    }
}
