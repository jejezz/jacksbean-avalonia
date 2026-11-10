using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace JackBean.Services.Startup;

/// <summary>컴퓨터에 로그인할 때 앱을 자동으로 시작하게 등록한다(시작 프로그램). 등록 여부는 OS 에 있는 등록 자체가 원본이다.</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }

    /// <summary>지금 로그인할 때 자동으로 시작하게 등록되어 있다.</summary>
    bool IsEnabled();

    /// <summary>등록하거나(`true`) 지운다(`false`). 못 하면 false 와 이유.</summary>
    bool SetEnabled(bool enabled, out string? error);
}

/// <summary>자동 시작 때 실행할 명령: 실행 파일과 인자.</summary>
public sealed record LaunchCommand(string Executable, IReadOnlyList<string> Args)
{
    /// <summary>
    /// 지금 실행 중인 앱의 명령. 게시본은 실행 파일 하나이고, 개발 중 `dotnet run` 은 `dotnet.exe` 가 앱 dll 을 여는 모양이라
    /// 그때는 `dotnet "…/JacksBean.dll"` 로 한다(`dotnet.exe` 만 등록하면 아무 앱도 못 연다).
    /// </summary>
    public static LaunchCommand Current()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 알 수 없습니다");
        var name = Path.GetFileNameWithoutExtension(exe);
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && Environment.GetCommandLineArgs() is { Length: > 0 } args
            && args[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return new LaunchCommand(exe, [Path.GetFullPath(args[0])]);
        return new LaunchCommand(exe, []);
    }

    /// <summary>공백이 있는 경로도 한 덩어리로 읽히게 따옴표로 감싼 한 줄(Windows 레지스트리·Linux `Exec` 용).</summary>
    public string ToCommandLine() => string.Join(' ', new[] { Executable }.Concat(Args).Select(Quote));

    private static string Quote(string s) => s.Length > 0 && s.All(c => !char.IsWhiteSpace(c) && c != '"') ? s : "\"" + s.Replace("\"", "\\\"") + "\"";
}

public static class StartupRegistrations
{
    /// <param name="name">등록 이름(레지스트리 값 이름, `.desktop` 파일 이름). 시험에서는 진짜 등록과 안 겹치게 다른 이름을 쓴다.</param>
    public static IStartupRegistration Create(string name, string id, string displayName, LaunchCommand? command = null, string? homeOverride = null)
    {
        var launch = command ?? LaunchCommand.Current();
        if (OperatingSystem.IsWindows()) return new WindowsStartup(name, launch);
        if (OperatingSystem.IsMacOS()) return new FileStartup(MacLaunchAgent.Path(id, homeOverride), MacLaunchAgent.Content(id, launch));
        if (OperatingSystem.IsLinux()) return new FileStartup(LinuxAutostart.Path(name, homeOverride), LinuxAutostart.Content(displayName, launch));
        return new UnsupportedStartup();
    }
}

internal sealed class UnsupportedStartup : IStartupRegistration
{
    public bool IsSupported => false;
    public bool IsEnabled() => false;
    public bool SetEnabled(bool enabled, out string? error) { error = "unsupported"; return false; }
}

/// <summary>Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 에 값 하나(관리자 권한이 필요 없다).</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsStartup(string name, LaunchCommand command) : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsSupported => true;

    public bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) is string;
    }

    public bool SetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled) key.SetValue(name, command.ToCommandLine());   // 이미 있어도 지금 경로로 고쳐 쓴다(앱을 옮긴 경우)
            else key.DeleteValue(name, throwOnMissingValue: false);
            error = null;
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            error = e.Message;
            return false;
        }
    }
}

/// <summary>macOS 와 Linux: 정해진 폴더에 파일 하나를 만들고 지운다.</summary>
internal sealed class FileStartup(string path, string content) : IStartupRegistration
{
    public bool IsSupported => true;

    public bool IsEnabled() => File.Exists(path);

    public bool SetEnabled(bool enabled, out string? error)
    {
        try
        {
            if (enabled)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
            else if (File.Exists(path)) File.Delete(path);
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return false;
        }
    }
}

public static class MacLaunchAgent
{
    public static string Path(string id, string? home = null) =>
        System.IO.Path.Combine(home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", id + ".plist");

    /// <summary>`~/Library/LaunchAgents/&lt;id&gt;.plist`: 로그인할 때 한 번 실행(<c>RunAtLoad</c>), 앱이 끝나도 다시 띄우지 않는다.</summary>
    public static string Content(string id, LaunchCommand command)
    {
        var args = string.Concat(new[] { command.Executable }.Concat(command.Args).Select(a => $"        <string>{SecurityElement.Escape(a)}</string>\n"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{SecurityElement.Escape(id)}</string>
                <key>ProgramArguments</key>
                <array>
            {args.TrimEnd('\n')}
                </array>
                <key>RunAtLoad</key>
                <true/>
            </dict>
            </plist>

            """;
    }
}

public static class LinuxAutostart
{
    public static string Path(string name, string? home = null)
    {
        var config = home != null ? System.IO.Path.Combine(home, ".config")
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return System.IO.Path.Combine(config, "autostart", name.ToLowerInvariant() + ".desktop");
    }

    /// <summary>`~/.config/autostart/&lt;이름&gt;.desktop` (XDG Autostart).</summary>
    public static string Content(string displayName, LaunchCommand command) =>
        $"[Desktop Entry]\nType=Application\nName={displayName}\nExec={command.ToCommandLine().Replace("%", "%%")}\nX-GNOME-Autostart-enabled=true\nTerminal=false\n";
}
