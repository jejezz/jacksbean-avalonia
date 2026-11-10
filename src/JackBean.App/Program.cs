using Avalonia;

namespace JackBean.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 앱은 하나만 뜬다. `--quit`: 떠 있는 앱에 종료를 알린다(설치 프로그램이 업그레이드 전에 쓴다). 확인용 인자(--snapshot --tool)는 따로 돌 수 있다.
        var quit = args.Contains("--quit");
        var dev = args.Contains("--snapshot") || args.Contains("--tool");
        if (quit || !dev)
        {
            var primary = !quit && SingleInstance.TryBecomePrimary();
            if (!primary)
            {
                SingleInstance.SignalPrimary(quit);   // 이미 떠 있다 — 독이 숨어 있으면 보이게 하고 끝낸다
                return;
            }
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
