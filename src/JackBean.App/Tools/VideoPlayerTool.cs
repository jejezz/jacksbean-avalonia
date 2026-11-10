using Avalonia.Controls;
using JackBean.Services;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 비디오 플레이어: 운영체제의 웹뷰가 파일을 직접 재생한다(<see cref="WebFileWindow"/>) — 재생 컨트롤도 웹뷰의 것이다.
/// 별도 재생 라이브러리를 싣지 않아 앱이 커지지 않지만, 웹뷰가 아는 형식(mp4 H.264, webm 등)만 재생된다. 안 되는 형식은
/// 위쪽 띠의 [기본 앱으로 열기]로 넘긴다. 창을 닫으면 재생도 멈춘다.
/// </summary>
public sealed class VideoPlayerTool : IBuiltInTool
{
    public string Id => "video-player";

    public Window CreateWindow(string path) =>
        WebFileWindow.Create(path, Strings.Current.VideoPlayerFailed, Strings.Current.VideoPlayerHint, width: 960, height: 600, detectFailure: false);
}
