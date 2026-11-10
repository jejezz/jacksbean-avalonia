using Avalonia.Controls;
using JackBean.Services;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 PDF 뷰어: 운영체제의 웹뷰(Windows WebView2 · macOS WKWebView)에 파일을 그대로 열어 보여 준다(<see cref="WebFileWindow"/>).
/// 별도 PDF 렌더러를 싣지 않으므로 앱이 거의 커지지 않는다.
/// </summary>
public sealed class PdfViewerTool : IBuiltInTool
{
    public string Id => "pdf-viewer";

    public Window CreateWindow(string path) => WebFileWindow.Create(path, Strings.Current.PdfViewerFailed);
}
