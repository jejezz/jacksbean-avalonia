using System.Text;
using JackBean.Services.Text;

namespace JackBean.Services.Text.Tests;

public sealed class TextFilesTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("dc-editor-file-test").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static EditorTextFile Ok(EditorLoadResult r) => Assert.IsType<EditorTextFile>(r.File);

    [Fact]
    public void 줄바꿈_종류는_많은_쪽이다()
    {
        Assert.Equal("\n", TextFiles.DetectLineBreak("a\nb\nc"));
        Assert.Equal("\r\n", TextFiles.DetectLineBreak("a\r\nb\r\nc"));
        Assert.Equal("\r\n", TextFiles.DetectLineBreak("a\r\nb\r\nc\n"));
        Assert.Equal("\n", TextFiles.DetectLineBreak("a\r\nb\nc\n"));
        Assert.Equal("\n", TextFiles.DetectLineBreak("한 줄뿐"));
    }

    [Fact]
    public void 줄바꿈_정규화와_되돌리기()
    {
        Assert.Equal("a\nb\nc\nd", TextFiles.NormalizeLineBreaks("a\r\nb\rc\nd"));
        Assert.Equal("a\r\nb", TextFiles.ApplyLineBreak("a\nb", "\r\n"));
        Assert.Equal("a\nb", TextFiles.ApplyLineBreak("a\nb", "\n"));
    }

    [Fact]
    public void UTF8_본문과_BOM을_읽는다()
    {
        var plain = Ok(TextFiles.Decode(Encoding.UTF8.GetBytes("안녕\r\n세상"), DateTime.UnixEpoch));
        Assert.Equal("안녕\r\n세상", plain.Text);
        Assert.False(plain.HasBom);
        Assert.False(plain.Binary);
        Assert.Equal("\r\n", plain.LineBreak);

        var bom = Ok(TextFiles.Decode([0xEF, 0xBB, 0xBF, .. "x"u8], DateTime.UnixEpoch));
        Assert.Equal("x", bom.Text);
        Assert.True(bom.HasBom);
    }

    [Fact]
    public void UTF8이_아니면_글자_하나씩_보여_주는_읽기_전용이다()
    {
        var f = Ok(TextFiles.Decode([0x63, 0x61, 0x66, 0xE9], DateTime.UnixEpoch));
        Assert.Equal("café", f.Text);
        Assert.True(f.Binary);
        Assert.False(f.HasBom);
    }

    [Fact]
    public void NUL이_있으면_바이너리다()
    {
        Assert.True(Ok(TextFiles.Decode([0x41, 0x00, 0x42], DateTime.UnixEpoch)).Binary);
        Assert.True(TextFiles.LooksBinary([1, 0]));
        Assert.False(TextFiles.LooksBinary("abc"u8));
    }

    [Fact]
    public void 한도를_넘으면_열지_않는다()
    {
        var r = TextFiles.Decode(new byte[TextFiles.EditorMaxBytes + 1], DateTime.UnixEpoch);
        Assert.Equal(EditorLoadFailure.TooLarge, r.Failure);
        Assert.Null(r.File);
    }

    [Fact]
    public void 저장_바이트는_BOM을_앞에_붙인다()
    {
        Assert.Equal("가"u8.ToArray(), TextFiles.Encode("가", hasBom: false));
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "가"u8], TextFiles.Encode("가", hasBom: true));
    }

    [Fact]
    public async Task 저장하면_원래_줄바꿈과_BOM을_되살리고_새_기준값을_돌려준다()
    {
        var path = Path.Combine(_dir, "a.txt");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. "one\r\ntwo"u8]);
        var opened = Ok(await TextFiles.LoadAsync(path));
        Assert.True(opened.HasBom);
        Assert.Equal("\r\n", opened.LineBreak);

        // 편집 화면은 LF만 쓴다
        var saved = await TextFiles.SaveAsync(path, "one\ntwo\nthree", opened);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\nthree"u8], await File.ReadAllBytesAsync(path));
        Assert.Equal(new FileInfo(path).Length, saved.Length);
        Assert.False(TextFiles.ChangedOnDisk(path, saved));
    }

    [Fact]
    public async Task 디스크에서_바뀌었는지_수정_시각과_크기로_안다()
    {
        var path = Path.Combine(_dir, "b.txt");
        await File.WriteAllTextAsync(path, "hello");
        var opened = Ok(await TextFiles.LoadAsync(path));
        Assert.False(TextFiles.ChangedOnDisk(path, opened));

        await File.WriteAllTextAsync(path, "hello world");
        Assert.True(TextFiles.ChangedOnDisk(path, opened));

        File.Delete(path);
        Assert.False(TextFiles.ChangedOnDisk(path, opened)); // 없으면 새로 쓰면 되므로 충돌이 아니다
    }

    [Fact]
    public async Task 큰_파일은_읽기_전에_거른다()
    {
        var path = Path.Combine(_dir, "big.txt");
        await using (var s = File.Create(path)) s.SetLength(TextFiles.EditorMaxBytes + 10);
        var r = await TextFiles.LoadAsync(path);
        Assert.Equal(EditorLoadFailure.TooLarge, r.Failure);
    }
}
