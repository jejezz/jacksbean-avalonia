using Avalonia.Input;

namespace JackBean.App;

/// <summary>키 보조키의 OS 별 차이: 단축키의 "기본" 보조키는 Ctrl, macOS 는 ⌘ 도.</summary>
internal static class Mods
{
    public static bool Primary(KeyModifiers m) =>
        m.HasFlag(KeyModifiers.Control) || (OperatingSystem.IsMacOS() && m.HasFlag(KeyModifiers.Meta));
}
