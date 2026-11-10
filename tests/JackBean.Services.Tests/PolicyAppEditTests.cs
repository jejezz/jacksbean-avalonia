using System.Text.Json.Nodes;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.Services.Tests;

public class PolicyAppEditTests
{
    static PolicyStore Store(TempDir t) => new(PolicyOs.Windows, t.Path);

    static async Task<JsonObject> ReadJson(PolicyStore s) => JsonNode.Parse(await File.ReadAllTextAsync(s.FilePath))!.AsObject();

    [Fact]
    public async Task AddsNewAppAndItLoads()
    {
        using var t = new TempDir();
        var store = Store(t);
        var result = await store.UpsertAppAsync(new AppUpsert("notepad", "notepad.exe", ["{path}"]));
        Assert.True(result.Ok, result.Error);

        var doc = await store.LoadAsync();
        Assert.Equal("notepad.exe", doc.Apps["notepad"].Command);
        Assert.Equal(["{path}"], doc.Apps["notepad"].Args);
        Assert.Contains("explorer", doc.Apps.Keys);   // 기본 앱은 그대로
    }

    [Fact]
    public async Task EditingKeepsUnknownKeysOfThatApp()
    {
        using var t = new TempDir();
        var store = Store(t);
        await store.LoadAsync();
        var json = await ReadJson(store);
        json["apps"]!["explorer"]!["note"] = "내가 적은 메모";
        json["extra"] = "모르는 최상위 키";
        await File.WriteAllTextAsync(store.FilePath, json.ToJsonString());

        var result = await store.UpsertAppAsync(new AppUpsert("explorer", "explorer.exe", ["/select,{path}"]));
        Assert.True(result.Ok, result.Error);

        var after = await ReadJson(store);
        Assert.Equal("내가 적은 메모", (string?)after["apps"]!["explorer"]!["note"]);
        Assert.Equal("모르는 최상위 키", (string?)after["extra"]);
        Assert.Equal("/select,{path}", (string?)after["apps"]!["explorer"]!["args"]![0]);
    }

    [Fact]
    public async Task CanSaveAppWithNoArgs()
    {
        using var t = new TempDir();
        var store = Store(t);
        Assert.True((await store.UpsertAppAsync(new AppUpsert("launcher", "launcher.exe", []))).Ok);
        Assert.Empty((await store.LoadAsync()).Apps["launcher"].Args);
    }

    [Fact]
    public async Task RemoveRefusedWhileRulesUseIt()
    {
        using var t = new TempDir();
        var store = Store(t);
        var doc = await store.LoadAsync();
        var ruleId = doc.Rules.First(r => r.Choices.Contains("explorer")).Id;

        var result = await store.RemoveAppAsync("explorer");
        Assert.False(result.Ok);
        Assert.Contains(ruleId, result.Error);
        Assert.Contains("explorer", (await store.LoadAsync()).Apps.Keys);   // 그대로
    }

    [Fact]
    public async Task RemoveWithMakeAskTurnsRulesIntoAsk()
    {
        using var t = new TempDir();
        var store = Store(t);
        var doc = await store.LoadAsync();
        var users = doc.RulesUsing("explorer");
        Assert.NotEmpty(users);

        var result = await store.RemoveAppAsync("explorer", ReferencedRules.MakeAsk, new DateTime(2026, 11, 10, 1, 2, 3, DateTimeKind.Utc));
        Assert.True(result.Ok, result.Error);

        var after = await store.LoadAsync();
        Assert.DoesNotContain("explorer", after.Apps.Keys);
        Assert.Equal(doc.Rules.Count, after.Rules.Count);                     // 규칙은 지우지 않는다
        Assert.Empty(after.Rules.First(r => r.Id == "drives").Choices);                    // 마지막 앱이었다 — 앱이 없는 규칙이 된다
        Assert.Equal(["terminal"], after.Rules.First(r => r.Id == "folders").Choices);   // 다른 앱이 남으면 그 앱만
        Assert.Empty(after.Warnings);
    }

    [Fact]
    public async Task RemoveUnusedAppJustRemovesIt()
    {
        using var t = new TempDir();
        var store = Store(t);
        await store.UpsertAppAsync(new AppUpsert("scratch", "x.exe", ["{path}"]));
        Assert.True((await store.RemoveAppAsync("scratch")).Ok);
        Assert.DoesNotContain("scratch", (await store.LoadAsync()).Apps.Keys);
    }

    [Fact]
    public async Task RemoveMissingAppFails()
    {
        using var t = new TempDir();
        var store = Store(t);
        Assert.False((await store.RemoveAppAsync("nope")).Ok);
    }

    [Fact]
    public async Task BrokenFileIsNeverOverwrittenByEdits()
    {
        using var t = new TempDir();
        var store = Store(t);
        await File.WriteAllTextAsync(store.FilePath, "{ 깨진");
        Assert.False((await store.UpsertAppAsync(new AppUpsert("a", "a.exe", []))).Ok);
        Assert.False((await store.RemoveAppAsync("a")).Ok);
        Assert.Equal("{ 깨진", await File.ReadAllTextAsync(store.FilePath));
    }
}
