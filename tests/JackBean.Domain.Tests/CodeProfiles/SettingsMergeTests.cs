using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;
using static JackBean.Domain.Tests.CodeProfiles.CodeProfileTestSupport;

namespace JackBean.Domain.Tests.CodeProfiles;

public class SettingsMergeTests
{
    private static SettingChange Change(MergeResult r, string pointer) => r.Changes.Single(c => c.Pointer == pointer);

    private static IReadOnlySet<string> Set(params string[] pointers) => pointers.ToHashSet();

    // --- 표 4.2 ---

    [Fact]
    public void MapPlusMapMergesKeyByKeyRecursively()
    {
        var r = SettingsMerge.Merge(
            Obj("""{"files.watcherExclude": {"**/a": true}}"""),
            Obj("""{"files.watcherExclude": {"**/b": true, "**/a": true}}"""));
        Assert.Equal(["**/a", "**/b"], r.Merged["files.watcherExclude"]!.AsObject().Select(kv => kv.Key));
        Assert.Equal(ChangeKind.Unchanged, Change(r, "/files.watcherExclude/**~1a").Kind);
        Assert.Equal(ChangeKind.Added, Change(r, "/files.watcherExclude/**~1b").Kind);
    }

    [Fact]
    public void ListPlusListKeepsOrderAndAppendsOnlyMissingItems()
    {
        var r = SettingsMerge.Merge(Obj("""{"xs": ["a", "b"]}"""), Obj("""{"xs": ["b", "c", "c", "a", "d"]}"""));
        Assert.Equal(["a", "b", "c", "d"], r.Merged["xs"]!.AsArray().Select(x => x!.GetValue<string>()));
        var c = Change(r, "/xs");
        Assert.Equal(ChangeKind.Added, c.Kind);
        Assert.Equal(["c", "d"], c.Patch!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void ListWithNothingNewIsUnchanged()
    {
        var r = SettingsMerge.Merge(Obj("""{"xs": ["a"]}"""), Obj("""{"xs": ["a"]}"""));
        Assert.Equal(ChangeKind.Unchanged, Change(r, "/xs").Kind);
    }

    [Fact]
    public void ListItemsAreComparedDeeply()
    {
        var r = SettingsMerge.Merge(Obj("""{"xs": [{"k": 1}]}"""), Obj("""{"xs": [{"k": 1}, {"k": 2}]}"""));
        Assert.Equal(2, r.Merged["xs"]!.AsArray().Count);
    }

    [Fact]
    public void ScalarConflictKeepsTheExistingValueAndIsReported()
    {
        var r = SettingsMerge.Merge(Obj("""{"editor.tabSize": 4}"""), Obj("""{"editor.tabSize": 2}"""));
        Assert.Equal(4, r.Merged["editor.tabSize"]!.GetValue<int>());
        var c = Change(r, "/editor.tabSize");
        Assert.Equal(ChangeKind.Conflict, c.Kind);
        Assert.Equal(4, c.Existing!.GetValue<int>());
        Assert.Equal(2, c.Patch!.GetValue<int>());
        Assert.False(c.Replaced);
    }

    [Fact]
    public void EqualScalarsAreUnchangedNotConflicts()
    {
        var r = SettingsMerge.Merge(Obj("""{"a": "x"}"""), Obj("""{"a": "x"}"""));
        Assert.Equal(ChangeKind.Unchanged, Change(r, "/a").Kind);
    }

    [Fact]
    public void TypeMismatchIsAConflict()
    {
        Assert.Equal(ChangeKind.Conflict, Change(SettingsMerge.Merge(Obj("""{"a": 1}"""), Obj("""{"a": {"b": 2}}""")), "/a").Kind);
        Assert.Equal(ChangeKind.Conflict, Change(SettingsMerge.Merge(Obj("""{"a": ["x"]}"""), Obj("""{"a": {"b": 2}}""")), "/a").Kind);
        Assert.Equal(1, SettingsMerge.Merge(Obj("""{"a": 1}"""), Obj("""{"a": {"b": 2}}""")).Merged["a"]!.GetValue<int>());
    }

    [Fact]
    public void MissingKeyIsAddedWithThePatchValue()
    {
        var r = SettingsMerge.Merge(Obj("""{"a": 1}"""), Obj("""{"b": {"c": true}}"""));
        Assert.Equal(["a", "b"], r.Merged.Select(kv => kv.Key));
        Assert.Equal(ChangeKind.Added, Change(r, "/b").Kind);
    }

    [Fact]
    public void JsonNullIsAnExistingValue()
    {
        var r = SettingsMerge.Merge(Obj("""{"a": null}"""), Obj("""{"a": 1}"""));
        Assert.Equal(ChangeKind.Conflict, Change(r, "/a").Kind);
        Assert.Null(r.Merged["a"]);
    }

    // --- 충돌 교체 ---

    [Fact]
    public void OnlyTheChosenPointersAreReplaced()
    {
        var r = SettingsMerge.Merge(Obj("""{"a": 1, "b": 1, "c": 1}"""), Obj("""{"a": 2, "b": 2, "c": 1}"""), Set("/a"));
        Assert.Equal(2, r.Merged["a"]!.GetValue<int>());
        Assert.Equal(1, r.Merged["b"]!.GetValue<int>());
        Assert.True(Change(r, "/a").Replaced);
        Assert.False(Change(r, "/b").Replaced);
    }

    [Fact]
    public void ReplacementWorksOnNestedKeys()
    {
        var r = SettingsMerge.Merge(Obj("""{"m": {"k": 1}}"""), Obj("""{"m": {"k": 2}}"""), Set("/m/k"));
        Assert.Equal(2, r.Merged["m"]!["k"]!.GetValue<int>());
    }

    // --- 순서·식별자·입력 보존 ---

    [Fact]
    public void ExistingKeyOrderIsKeptAndNewKeysGoLast()
    {
        var r = SettingsMerge.Merge(Obj("""{"z": 1, "a": 1}"""), Obj("""{"m": 1, "a": 1}"""));
        Assert.Equal(["z", "a", "m"], r.Merged.Select(kv => kv.Key));
    }

    [Fact]
    public void PointersEscapeTildeAndSlash()
    {
        var r = SettingsMerge.Merge(new JsonObject(), Obj("""{"a/b~c": 1}"""));
        Assert.Equal("/a~1b~0c", r.Changes.Single().Pointer);
        Assert.Equal(["a/b~c"], JsonPointer.Segments("/a~1b~0c"));
        Assert.Equal(["files.watcherExclude", "**/build/**"], JsonPointer.Segments("/files.watcherExclude/**~1build~1**"));
        Assert.Empty(JsonPointer.Segments(""));
    }

    [Fact]
    public void InputsAreNotMutated()
    {
        var existing = Obj("""{"xs": ["a"], "m": {"k": 1}}""");
        var patch = Obj("""{"xs": ["b"], "m": {"j": 2}, "n": 3}""");
        var existingBefore = existing.ToJsonString();
        var patchBefore = patch.ToJsonString();
        var r = SettingsMerge.Merge(existing, patch);
        Assert.Equal(existingBefore, existing.ToJsonString());
        Assert.Equal(patchBefore, patch.ToJsonString());
        // 결과를 고쳐도 입력에 영향이 없다(노드를 복제했다).
        r.Merged["xs"]!.AsArray().Add("z");
        Assert.Equal(patchBefore, patch.ToJsonString());
    }
}

public class SettingsDiffTests
{
    private static SettingsDiff Ok(string? existing, string patch, params string[] replace)
    {
        var r = SettingsDiffer.Compute(existing, Obj(patch), replace.ToHashSet());
        Assert.True(r.Ok, r.Error?.ToString());
        return r.Value!;
    }

    [Fact]
    public void NoExistingFileEverythingIsAdded()
    {
        var d = Ok(null, """{"a": 1}""");
        Assert.False(d.ExistingFile);
        Assert.Single(d.Added);
        Assert.True(d.HasChanges);
        Assert.False(d.CommentsWillBeLost);
        Assert.Equal("{\n  \"a\": 1\n}\n", d.MergedJson);
    }

    [Fact]
    public void CommentsInTheExistingFileRaiseTheWarningFlag()
    {
        var d = Ok("{\n  // mine\n  \"a\": 1,\n}\n", """{"b": 2}""");
        Assert.True(d.CommentsWillBeLost);
        var merged = JsonNode.Parse(d.MergedJson)!;
        Assert.Equal(1, merged["a"]!.GetValue<int>());
        Assert.Equal(2, merged["b"]!.GetValue<int>());
    }

    [Fact]
    public void KeptConflictsAloneAreNotAChangeToWrite()
    {
        var kept = Ok("""{"a": 1}""", """{"a": 2}""");
        Assert.Single(kept.Conflicts);
        Assert.False(kept.HasChanges);

        var replaced = Ok("""{"a": 1}""", """{"a": 2}""", "/a");
        Assert.True(replaced.HasChanges);
        Assert.Equal(2, JsonNode.Parse(replaced.MergedJson)!["a"]!.GetValue<int>());
    }

    [Fact]
    public void EmptyExistingFileIsTreatedAsAnEmptyObject()
    {
        var d = Ok("", """{"a": 1}""");
        Assert.True(d.ExistingFile);
        Assert.Single(d.Added);
        Assert.False(d.CommentsWillBeLost);
    }

    [Fact]
    public void CommentOnlyExistingFileStillWarns()
    {
        var d = Ok("// just a note\n", """{"a": 1}""");
        Assert.True(d.CommentsWillBeLost);
    }

    [Fact]
    public void InvalidOrNonObjectExistingFileIsAnError()
    {
        Assert.False(SettingsDiffer.Compute("{oops", Obj("""{"a": 1}""")).Ok);
        Assert.False(SettingsDiffer.Compute("[1, 2]", Obj("""{"a": 1}""")).Ok);
        Assert.False(SettingsDiffer.Compute("5", Obj("""{"a": 1}""")).Ok);
    }

    [Fact]
    public void KoreanTextIsWrittenUnescaped()
    {
        var d = Ok(null, """{"k": "한글"}""");
        Assert.Contains("한글", d.MergedJson);
    }

    [Fact]
    public void OutputUsesTwoSpaceIndentAndLineFeedsOnly()
    {
        var d = Ok(null, """{"a": {"b": [1, 2]}}""");
        Assert.DoesNotContain('\r', d.MergedJson);
        Assert.Contains("\n  \"a\": {\n    \"b\": [", d.MergedJson);
        Assert.EndsWith("}\n", d.MergedJson);
    }
}
