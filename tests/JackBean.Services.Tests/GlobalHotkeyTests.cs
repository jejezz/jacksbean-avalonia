using System.Runtime.InteropServices;
using JackBean.Services.Hotkeys;

namespace JackBean.Services.Tests;

public class GlobalHotkeyTests
{
    // 다른 앱이 쓰지 않을 조합(Ctrl+Alt+Shift+F13 ~ F15)으로 시험한다.
    private static readonly HotkeyGesture TestKey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7C);
    private static readonly HotkeyGesture OtherKey = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7D);

    [Fact]
    public void DisplayNames()
    {
        Assert.Equal("Ctrl+Alt+J", HotkeyGesture.ToggleDock.Display());
        Assert.Equal("⌃⌥J", HotkeyGesture.ToggleDock.Display(mac: true));
        Assert.Equal("Ctrl+Alt+Shift+0x7C", TestKey.Display());
    }

    [Fact]
    public void UnsupportedOsReportsItAndNeverRegisters()
    {
        if (OperatingSystem.IsWindows()) return;   // 이 시험은 Windows 가 아닌 OS 에서만 의미가 있다
        using var hotkey = GlobalHotkeys.Create();
        Assert.False(hotkey.IsSupported);
        Assert.False(hotkey.Register(TestKey, () => { }, out _));
    }

    [Fact]
    public void SecondRegistrationOfTheSameKeyFailsUntilTheFirstIsReleased()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var first = GlobalHotkeys.Create();
        using var second = GlobalHotkeys.Create();
        Assert.True(first.IsSupported);

        Assert.True(first.Register(TestKey, () => { }, out var e1), e1);
        Assert.False(second.Register(TestKey, () => { }, out var conflict));   // 이미 쓰고 있다 — 이유를 돌려준다
        Assert.False(string.IsNullOrWhiteSpace(conflict));

        first.Unregister();
        Assert.True(second.Register(TestKey, () => { }, out var e2), e2);       // 풀면 다른 쪽이 쓸 수 있다
    }

    [Fact]
    public void RegisteringAgainReplacesTheOldKey()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var hotkey = GlobalHotkeys.Create();
        using var probe = GlobalHotkeys.Create();
        Assert.True(hotkey.Register(TestKey, () => { }, out _));
        Assert.True(hotkey.Register(OtherKey, () => { }, out _));               // 같은 객체에서 다시 등록하면 이전 것은 풀린다
        Assert.True(probe.Register(TestKey, () => { }, out var e), e);          // 그래서 이전 조합은 비어 있다
    }

    [Fact]
    public void PressingTheKeyCallsBackOnce()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var hotkey = GlobalHotkeys.Create();
        var presses = 0;
        var signal = new ManualResetEventSlim();
        Assert.True(hotkey.Register(TestKey, () => { Interlocked.Increment(ref presses); signal.Set(); }, out var error), error);

        if (!SendCombo()) return;   // 키 입력을 흉내 낼 수 없는 환경(대화형 데스크톱이 없음)에서는 여기까지만 확인한다
        Assert.True(signal.Wait(3000), "단축키를 눌렀는데 콜백이 불리지 않았다");
        Thread.Sleep(200);
        Assert.Equal(1, presses);
    }

    // --- SendInput 으로 Ctrl+Alt+Shift+F13 을 누른다 ---

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    private static INPUT Key(ushort vk, bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } } };

    private static bool SendCombo()
    {
        const ushort Ctrl = 0x11, Alt = 0x12, Shift = 0x10, F13 = 0x7C;
        INPUT[] seq = [Key(Ctrl, false), Key(Alt, false), Key(Shift, false), Key(F13, false), Key(F13, true), Key(Shift, true), Key(Alt, true), Key(Ctrl, true)];
        return SendInput((uint)seq.Length, seq, Marshal.SizeOf<INPUT>()) == seq.Length;
    }
}
