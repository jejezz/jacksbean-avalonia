using JackBean.Services.Startup;

namespace JackBean.Services.Tests;

public class StartupTests
{
    private static readonly LaunchCommand Published = new(@"C:\Program Files\Jack's Bean\JacksBean.exe", []);
    private static readonly LaunchCommand Dev = new("dotnet", [@"C:\work\bin\JacksBean.dll"]);

    [Fact]
    public void CommandLineQuotesOnlyWhenNeeded()
    {
        Assert.Equal("\"C:\\Program Files\\Jack's Bean\\JacksBean.exe\"", Published.ToCommandLine());
        Assert.Equal("dotnet C:\\work\\bin\\JacksBean.dll", Dev.ToCommandLine());
        Assert.Equal("\"a b\" \"x\\\"y\"", new LaunchCommand("a b", ["x\"y"]).ToCommandLine());
    }

    [Fact]
    public void MacLaunchAgentRunsAtLoadWithEscapedArguments()
    {
        var plist = MacLaunchAgent.Content("art.zoomon.jacksbean.avalonia", new LaunchCommand("/Applications/Jack's Bean & Co.app/Contents/MacOS/JacksBean", []));
        Assert.Contains("<key>Label</key>", plist);
        Assert.Contains("<string>art.zoomon.jacksbean.avalonia</string>", plist);
        Assert.Contains("<string>/Applications/Jack&apos;s Bean &amp; Co.app/Contents/MacOS/JacksBean</string>", plist);   // XML 로 안전하게
        Assert.Contains("<key>RunAtLoad</key>", plist);
        Assert.EndsWith("/Library/LaunchAgents/art.zoomon.jacksbean.avalonia.plist", MacLaunchAgent.Path("art.zoomon.jacksbean.avalonia", "/Users/x").Replace('\\', '/'));
    }

    [Fact]
    public void LinuxAutostartEntry()
    {
        var entry = LinuxAutostart.Content("Jack’s Bean", new LaunchCommand("/opt/jacksbean/JacksBean", ["--x=100%"]));
        Assert.StartsWith("[Desktop Entry]\nType=Application\n", entry);
        Assert.Contains("Name=Jack’s Bean\n", entry);
        Assert.Contains("Exec=/opt/jacksbean/JacksBean --x=100%%\n", entry);   // % 는 .desktop 에서 특별한 글자라 겹쳐 쓴다
        Assert.EndsWith("/autostart/jacksbean.desktop", LinuxAutostart.Path("JacksBean", "/home/x").Replace('\\', '/'));
    }

    [Fact]
    public void FileBasedRegistrationCreatesAndRemovesTheFile()
    {
        if (OperatingSystem.IsWindows()) return;   // Windows 는 레지스트리(아래 시험)
        using var t = new TempDir();
        var reg = StartupRegistrations.Create("JacksBeanTest", "art.test.jacksbean", "Jack’s Bean", Published, homeOverride: t.Path);
        Assert.True(reg.IsSupported);
        Assert.False(reg.IsEnabled());
        Assert.True(reg.SetEnabled(true, out var e1), e1);
        Assert.True(reg.IsEnabled());
        Assert.True(reg.SetEnabled(false, out var e2), e2);
        Assert.False(reg.IsEnabled());
        Assert.True(reg.SetEnabled(false, out _));   // 이미 없어도 괜찮다
    }

    [Fact]
    public void WindowsRegistryRoundTripUsesAnIsolatedValueName()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "JacksBeanTest-" + Guid.NewGuid().ToString("N");   // 진짜 등록(JacksBean)과 겹치지 않는 이름
        var reg = StartupRegistrations.Create(name, "art.test.jacksbean", "Jack’s Bean", Published);
        try
        {
            Assert.True(reg.IsSupported);
            Assert.False(reg.IsEnabled());
            Assert.True(reg.SetEnabled(true, out var e1), e1);
            Assert.True(reg.IsEnabled());
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                Assert.Equal(Published.ToCommandLine(), key!.GetValue(name));   // 공백이 있는 경로가 따옴표로 감싸여 들어간다
            Assert.True(reg.SetEnabled(false, out var e2), e2);
            Assert.False(reg.IsEnabled());
        }
        finally { reg.SetEnabled(false, out _); }
    }
}
