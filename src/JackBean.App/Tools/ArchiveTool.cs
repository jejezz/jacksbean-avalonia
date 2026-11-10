using Avalonia.Controls;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 압축 도구(zip 풀기·압축하기). 앱의 `args` 가 `{paths}` 라 여러 항목을 한 번에 받는다 — 하나의 .zip 이면 풀기, 그 밖에는 압축하기.
/// 쓰는 것은 표준 라이브러리(System.IO.Compression)뿐이라 앱이 커지지 않는다.
/// </summary>
public sealed class ArchiveTool : IBuiltInTool
{
    public string Id => "archive";

    public Window CreateWindow(string path) => new ArchiveWindow([path]);

    public Window CreateWindow(IReadOnlyList<string> paths) => new ArchiveWindow(paths);
}
