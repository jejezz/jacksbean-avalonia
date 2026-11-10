namespace JackBean.App.Tools;

/// <summary>
/// 내장 텍스트 에디터. 파일 하나당 창 하나(같은 파일을 다시 열면 열려 있는 창을 앞으로 가져온다 — <see cref="ToolRegistry"/>).
/// 구문 강조는 열 때 처음 불러 쓰므로 에디터를 열지 않는 동안은 메모리·시작 시간에 영향이 없다.
/// </summary>
public sealed class TextEditorTool : IBuiltInTool
{
    public string Id => "text-editor";

    public Avalonia.Controls.Window CreateWindow(string path) => new EditorWindow(path);
}
