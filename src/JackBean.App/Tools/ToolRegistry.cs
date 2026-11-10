using Avalonia.Controls;
using Avalonia.Threading;

namespace JackBean.App.Tools;

/// <summary>
/// 앱 안에 들어 있는 도구(이미지 뷰어, 텍스트 에디터, PDF 뷰어, 압축 도구 …). 열 때마다 **자기 창을 하나** 만든다.
/// 정책 파일에서는 앱의 command 를 `jackbean:&lt;도구 id&gt;` 로 적으면 프로세스를 띄우지 않고 이 레지스트리가 연다.
/// 새 도구는 이 인터페이스를 구현해 <see cref="ToolRegistry.Register"/> 에 더하면 된다.
/// </summary>
public interface IBuiltInTool
{
    /// <summary>`jackbean:` 뒤에 오는 이름. 예: `image-viewer`.</summary>
    string Id { get; }

    /// <summary>path(파일·폴더·드라이브 경로)를 보여 줄 새 창을 만든다. 창을 띄우는 일은 레지스트리가 한다.</summary>
    Window CreateWindow(string path);
}

public sealed class ToolRegistry
{
    public const string Scheme = "jackbean:";

    private readonly Dictionary<string, IBuiltInTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Window> _open = [];
    private readonly Dictionary<(string Id, string Path), Window> _byFile = new();

    public IReadOnlyCollection<Window> OpenWindows => _open;

    public void Register(IBuiltInTool tool) => _tools[tool.Id] = tool;

    public static bool IsBuiltIn(string command) => command.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>`jackbean:image-viewer` 같은 명령을 연다. 모르는 도구면 예외(실행기가 실패로 독에 알린다).</summary>
    public Task OpenAsync(string command, string path)
    {
        var id = command[Scheme.Length..];
        if (!_tools.TryGetValue(id, out var tool)) throw new InvalidOperationException($"알 수 없는 내장 도구: {id}");
        var done = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // 같은 도구로 같은 파일을 다시 열면 열려 있는 창을 앞으로 가져온다(파일 하나당 창 하나).
                var key = (tool.Id.ToLowerInvariant(), OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path);
                if (_byFile.TryGetValue(key, out var existing))
                {
                    if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                    existing.Activate();
                    done.SetResult();
                    return;
                }
                var window = tool.CreateWindow(path);
                _open.Add(window);
                _byFile[key] = window;
                window.Closed += (_, _) => { _open.Remove(window); _byFile.Remove(key); };
                window.Show();
                window.Activate();
                done.SetResult();
            }
            catch (Exception e) { done.SetException(e); }
        });
        return done.Task;
    }
}
