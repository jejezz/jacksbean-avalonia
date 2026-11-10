using Avalonia.Controls;
using JackBean.Services;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 오디오 플레이어: 비디오 플레이어와 같은 방식으로 운영체제의 웹뷰가 파일을 직접 재생한다(<see cref="WebFileWindow"/>) — 컨트롤도 웹뷰의 것.
/// 웹뷰가 아는 형식(mp3·wav·flac·aac·m4a·ogg·opus 등)만 재생되고, 안 되는 형식은 위쪽 띠의 [기본 앱으로 열기]로 넘긴다. 창을 닫으면 소리도 멈춘다.
/// </summary>
public sealed class AudioPlayerTool : IBuiltInTool
{
    public string Id => "audio-player";

    public Window CreateWindow(string path) =>
        WebFileWindow.Create(path, Strings.Current.AudioPlayerFailed, Strings.Current.AudioPlayerHint, width: 560, height: 260, detectFailure: false);
}
