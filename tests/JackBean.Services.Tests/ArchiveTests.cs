using System.IO.Compression;
using JackBean.Services.Archive;

namespace JackBean.Services.Tests;

/// <summary>압축(zip 만들기)·풀기·내용 살펴보기.</summary>
public sealed class ArchiveTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("jb-archive-test").FullName;
    readonly string _left;
    readonly string _right;
    readonly ArchiveService _archive = new();

    public ArchiveTests()
    {
        _left = Directory.CreateDirectory(Path.Combine(_root, "left")).FullName;
        _right = Directory.CreateDirectory(Path.Combine(_root, "right")).FullName;
        File.WriteAllText(Path.Combine(_left, "a.txt"), "A");
        Directory.CreateDirectory(Path.Combine(_left, "dir", "sub"));
        File.WriteAllText(Path.Combine(_left, "dir", "inner.txt"), "I");
        Directory.CreateDirectory(Path.Combine(_left, "empty"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    string L(string name) => Path.Combine(_left, name);

    static string[] Names(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Select(e => e.FullName).Order().ToArray();
    }

    [Fact]
    public void DefaultNamesAndZipDetection()
    {
        Assert.Equal("a.txt.zip", ArchiveService.DefaultZipName([L("a.txt")]));
        Assert.Equal("dir.zip", ArchiveService.DefaultZipName([L("dir") + Path.DirectorySeparatorChar]));
        Assert.Equal("archive.zip", ArchiveService.DefaultZipName([L("a.txt"), L("dir")]));
        Assert.Equal("photos", ArchiveService.ExtractFolderName("photos.ZIP"));
        Assert.True(ArchiveService.IsZip("X.Zip"));
        Assert.False(ArchiveService.IsZip("x.tar"));
    }

    [Fact]
    public async Task FoldersAreAddedStartingWithTheirName()
    {
        var zip = Path.Combine(_right, "out.zip");
        await _archive.CompressAsync([L("a.txt"), L("dir"), L("empty")], zip);
        Assert.Equal(["a.txt", "dir/inner.txt", "dir/sub/", "empty/"], Names(zip));
    }

    [Fact]
    public async Task CompressThenExtractGivesTheSameContent()
    {
        var zip = Path.Combine(_right, "out.zip");
        await _archive.CompressAsync([L("a.txt"), L("dir")], zip);
        var dest = await _archive.ExtractAsync(zip, Path.Combine(_right, "out"));
        Assert.Equal("A", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("I", File.ReadAllText(Path.Combine(dest, "dir", "inner.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, "dir", "sub")));
    }

    [Fact]
    public async Task TheZipBeingMadeIsNotAddedToItself()
    {
        var zip = L("self.zip");
        await _archive.CompressAsync([L("a.txt"), L("dir")], zip);
        Assert.DoesNotContain("self.zip", Names(zip));
    }

    [Fact]
    public async Task ExistingNameIsRefusedAndTheOldFileUntouched()
    {
        File.WriteAllText(L("keep.zip"), "x");
        var e = await Assert.ThrowsAsync<ArchiveException>(() => _archive.CompressAsync([L("a.txt")], L("keep.zip")));
        Assert.Equal(ArchiveErrorKind.AlreadyExists, e.Kind);
        Assert.Equal("keep.zip", e.Detail);
        Assert.Equal("x", File.ReadAllText(L("keep.zip")));
    }

    [Fact]
    public async Task BadNamesAndNothingToAddAreRefused()
    {
        var bad = await Assert.ThrowsAsync<ArchiveException>(() => _archive.CompressAsync([L("a.txt")], Path.Combine(_right, "a?b.zip")));
        Assert.Equal(ArchiveErrorKind.BadName, bad.Kind);
        var none = await Assert.ThrowsAsync<ArchiveException>(() => _archive.CompressAsync([L("missing")], Path.Combine(_right, "x.zip")));
        Assert.Equal(ArchiveErrorKind.NothingToAdd, none.Kind);
        Assert.False(File.Exists(Path.Combine(_right, "x.zip")));
    }

    [Fact]
    public async Task EntriesLeavingTheFolderAreNotExtracted()
    {
        var zip = Path.Combine(_right, "evil.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("../escaped.txt").Open());
            w.Write("bad");
        }
        var e = await Assert.ThrowsAsync<ArchiveException>(() => _archive.ExtractAsync(zip, Path.Combine(_right, "evil")));
        Assert.Equal(ArchiveErrorKind.UnsafePath, e.Kind);
        Assert.False(File.Exists(Path.Combine(_right, "escaped.txt")));
    }

    [Fact]
    public async Task ExistingFilesAreNotOverwritten()
    {
        var zip = Path.Combine(_right, "out.zip");
        await _archive.CompressAsync([L("a.txt")], zip);
        var dest = Path.Combine(_right, "out");
        await _archive.ExtractAsync(zip, dest);
        File.WriteAllText(Path.Combine(dest, "a.txt"), "changed");
        var e = await Assert.ThrowsAsync<ArchiveException>(() => _archive.ExtractAsync(zip, dest));
        Assert.Equal(ArchiveErrorKind.AlreadyExists, e.Kind);
        Assert.Equal("changed", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task InspectCountsEntriesAndSizes()
    {
        var zip = Path.Combine(_right, "out.zip");
        await _archive.CompressAsync([L("a.txt"), L("dir")], zip);
        var info = await _archive.InspectAsync(zip);
        Assert.Equal(2, info.FileCount);          // a.txt, dir/inner.txt
        Assert.Equal(1, info.FolderCount);        // dir/sub/
        Assert.Equal(2, info.TotalSize);          // "A" + "I"
        var capped = await _archive.InspectAsync(zip, maxEntries: 1);
        Assert.Single(capped.Entries);
        Assert.Equal(2, capped.FileCount);        // 개수는 상한과 무관
    }

    [Fact]
    public async Task BrokenZipThrowsInvalidData()
    {
        File.WriteAllText(L("broken.zip"), "not a zip");
        await Assert.ThrowsAsync<InvalidDataException>(() => _archive.InspectAsync(L("broken.zip")));
    }
}
