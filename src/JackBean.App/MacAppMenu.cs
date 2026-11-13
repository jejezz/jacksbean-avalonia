// From daylight-compare-avalonia @ faf8fcc (MacAppMenu.cs) — 앱 메뉴를 앱 언어로. 고친 곳은 원본에도 반영할 것.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using JackBean.App.Dialogs;
using JackBean.App.Localization;
using JackBean.App.Views;
using JackBean.Domain;

namespace JackBean.App;

/// <summary>
/// macOS 메뉴 막대의 앱 메뉴(앱 이름 메뉴). Avalonia 기본 항목은 영어로 고정이라 끄고(<c>DisableDefaultApplicationMenuItems</c>),
/// 같은 자리·같은 단축키의 항목을 앱 언어로 만든다. 언어를 바꾸면 다시 만든다. "서비스" 하위 메뉴는 Avalonia 메뉴로 만들 수 없어 빠진다.
/// </summary>
public static partial class MacAppMenu
{
    /// <param name="owner">정보·설정 창의 주인 (독 창 — 다른 창이 모두 닫혀도 떠 있다).</param>
    /// <param name="quit">앱 종료 (<c>App.Quit</c> — 독 앱이라 종료는 명시적으로만 한다).</param>
    public static void Install(Application app, Window owner, Action quit)
    {
        if (!OperatingSystem.IsMacOS()) return;
        void Build() => NativeMenu.SetMenu(app, Create(Strings.Current,
            about: () => AboutDialog.Open(owner),
            settings: () => SettingsWindow.Open(owner),
            quit: quit));
        Build();
        Strings.Changed += () => Dispatcher.UIThread.Post(Build);
    }

    /// <summary>앱 메뉴 항목: 정보 | 설정… (⌘,) | 가리기 (⌘H) · 기타 가리기 (⌥⌘H) · 모두 보기 | 종료 (⌘Q) — macOS 표준 순서.</summary>
    public static NativeMenu Create(IAppStrings s, Action about, Action settings, Action quit)
    {
        var name = AppIdentity.DisplayName;
        NativeMenuItem Item(string header, Action run, KeyGesture? key = null)
        {
            var item = new NativeMenuItem(header) { Gesture = key };
            item.Click += (_, _) => run();
            return item;
        }
        return new NativeMenu
        {
            Item(s.MacMenuAbout(name), about),
            new NativeMenuItemSeparator(),
            Item(s.MacMenuSettings, settings, new KeyGesture(Key.OemComma, KeyModifiers.Meta)),
            new NativeMenuItemSeparator(),
            Item(s.MacMenuHide(name), () => Send("hide:"), new KeyGesture(Key.H, KeyModifiers.Meta)),
            Item(s.MacMenuHideOthers, () => Send("hideOtherApplications:"), new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt)),
            Item(s.MacMenuShowAll, () => Send("unhideAllApplications:")),
            new NativeMenuItemSeparator(),
            Item(s.MacMenuQuit(name), quit, new KeyGesture(Key.Q, KeyModifiers.Meta)),
        };
    }

    // --- [NSApp hide:nil] 등: Avalonia에 해당 API가 없어 Objective-C 런타임을 직접 부른다 ---

    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void MsgSend(IntPtr receiver, IntPtr selector, IntPtr arg);

    static void Send(string selector)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var app = MsgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        MsgSend(app, sel_registerName(selector), IntPtr.Zero);
    }
}
