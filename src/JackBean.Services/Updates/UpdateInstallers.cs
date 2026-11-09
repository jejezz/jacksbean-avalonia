using System.Diagnostics;

namespace JackBean.Services.Updates;

/// <summary>OS별 설치를 시작한다. 시험에서 프로세스 실행을 가짜로 바꿀 수 있게 OS마다 따로 둔다.</summary>
public interface IUpdateInstaller
{
    InstallOutcome Install(DownloadedUpdate update);
}

public sealed class SystemCommandRunner : ICommandRunner
{
    public int Run(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in arguments) info.ArgumentList.Add(a);
            using var process = Process.Start(info) ?? throw new UpdateException(UpdateErrorKind.InstallFailed, $"Could not run {executable}");
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new UpdateException(UpdateErrorKind.InstallFailed, $"Could not run {executable}: {e.Message}", e);
        }
    }

    public void StartDetached(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in arguments) info.ArgumentList.Add(a);
            using var process = Process.Start(info);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new UpdateException(UpdateErrorKind.InstallFailed, $"Could not start {executable}: {e.Message}", e);
        }
    }
}

public static class UpdateInstallers
{
    /// <summary>
    /// 현재 OS의 설치기. macOS와 Windows는 내려받은 파일 자체가 설치 프로그램(DMG, setup.exe)이라 OS 방식으로 열어 주기만 하고,
    /// 압축을 풀어야 하는 Linux만 코드가 있다.
    /// </summary>
    public static IUpdateInstaller ForPlatform(UpdatePlatform platform, ICommandRunner? runner = null)
    {
        runner ??= new SystemCommandRunner();
        return platform.Os switch
        {
            "macos" => new MacosInstaller(runner),
            "windows" => new WindowsInstaller(runner),
            "linux" => new LinuxInstaller(runner),
            var other => throw new UpdateException(UpdateErrorKind.UnsupportedPlatform, $"No installer for {other}"),
        };
    }
}

/// <summary>DMG를 연다. 사용자가 앱을 Applications로 끌어다 놓는다 (실행 중인 앱을 몰래 교체하지 않는다).</summary>
public sealed class MacosInstaller(ICommandRunner runner) : IUpdateInstaller
{
    public InstallOutcome Install(DownloadedUpdate update)
    {
        var code = runner.Run("open", [update.File]);
        if (code != 0) throw new UpdateException(UpdateErrorKind.InstallFailed, $"\"open\" exited with {code}");
        return InstallOutcome.OpenedForUser;
    }
}

/// <summary>
/// Inno Setup 설치 프로그램을 연다. .exe를 직접 띄우지 않고 셸(explorer)로 연다: 설치 프로그램이 관리자 권한을 요구하는데,
/// 일반 프로세스가 CreateProcess로 그런 파일을 띄우면 오류 740으로 시작되지 않고 셸은 UAC를 띄워 준다.
/// Inno의 고정 AppId 덕분에 제자리 업그레이드가 된다.
/// </summary>
public sealed class WindowsInstaller(ICommandRunner runner) : IUpdateInstaller
{
    public InstallOutcome Install(DownloadedUpdate update)
    {
        // 서버가 같은 점수의 portable zip을 고르면 explorer가 압축 폴더만 열고 끝난다. 설치 프로그램이 아니면 설치 실패로 알린다.
        if (!update.File.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateErrorKind.InstallFailed, $"{Path.GetFileName(update.File)} is not a setup program");
        runner.StartDetached("explorer.exe", [update.File]);
        return InstallOutcome.InstallerStarted;
    }
}

/// <summary>
/// tar.gz를 풀고 <b>이 앱이 종료된 뒤</b> install.sh(~/.local에 설치, 권한 불필요)를 돌린 다음 앱을 다시 시작한다.
/// 확인할 수 있는 것(압축이 풀림 · 최상위 폴더 하나 · 그 안에 install.sh)은 앱이 아직 떠 있어 오류를 보여 줄 수 있을 때 모두 검사한다.
/// 설치 자체만 종료 뒤에 일어난다.
/// </summary>
public sealed class LinuxInstaller(ICommandRunner runner, int? currentPid = null) : IUpdateInstaller
{
    // $1 = 기다릴 pid, $2 = install.sh, $3 = 앱 ID(비어 있을 수 있음), $4 = 지울 임시 폴더.
    const string Helper = """
        n=0
        while kill -0 "$1" 2>/dev/null && [ "$n" -lt 150 ]; do sleep 0.2; n=$((n+1)); done
        if bash "$2" >/dev/null 2>&1; then
          if [ -n "$3" ]; then gtk-launch "$3" >/dev/null 2>&1; fi
        fi
        rm -rf "$4"
        """;

    public InstallOutcome Install(DownloadedUpdate update)
    {
        var unpacked = Directory.CreateDirectory(Path.Combine(update.Directory, "unpacked")).FullName;
        var code = runner.Run("tar", ["-xzf", update.File, "-C", unpacked]);
        if (code != 0)
            throw new UpdateException(UpdateErrorKind.InstallFailed, $"Could not unpack {Path.GetFileName(update.File)} (tar exit {code})");

        var tops = Directory.EnumerateDirectories(unpacked).Where(d => File.Exists(Path.Combine(d, "install.sh"))).ToList();
        if (tops.Count != 1)
            throw new UpdateException(UpdateErrorKind.InstallFailed, "The archive does not hold exactly one folder with an install.sh");
        var top = tops[0];
        var script = Path.Combine(top, "install.sh");
        var resolved = new FileInfo(script).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(script);
        var root = Path.GetFullPath(unpacked).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root, StringComparison.Ordinal))
            throw new UpdateException(UpdateErrorKind.InstallFailed, "install.sh points outside the archive");

        // .desktop 파일 이름이 앱 ID다 (conventions/packaging.md §4).
        var apps = Path.Combine(top, "share", "applications");
        var desktops = Directory.Exists(apps) ? Directory.GetFiles(apps, "*.desktop") : [];
        var appId = desktops.Length == 1 ? Path.GetFileNameWithoutExtension(desktops[0]) : "";

        runner.StartDetached("sh", ["-c", Helper, "sh", (currentPid ?? Environment.ProcessId).ToString(), resolved, appId, update.Directory]);
        return InstallOutcome.InstallerStarted;
    }
}
