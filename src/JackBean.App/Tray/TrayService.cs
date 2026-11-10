using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using JackBean.App.Notifications;
using JackBean.App.Views;
using JackBean.App.Windows;
using JackBean.Domain;
using JackBean.Services.Hotkeys;

namespace JackBean.App.Tray;

/// <summary>
/// 알림 영역(트레이, macOS 는 메뉴 막대) 아이콘. 독이 항상 떠 있는 앱이라 숨겼을 때도 되찾을 수 있는 곳이 필요하다.
/// 아이콘을 누르면 독을 숨기거나 보이고, 메뉴에는 독 숨기기·보이기 / 홈 창 / 설정 / 정보 / 종료가 있다.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly DockWindow _dock;
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _toggle = new();
    private readonly NativeMenuItem _home = new();
    private readonly NativeMenuItem _settings = new();
    private readonly NativeMenuItem _about = new();
    private readonly NativeMenuItem _quit = new();

    public TrayService(Application app, DockWindow dock)
    {
        _dock = dock;
        using var png = AssetLoader.Open(new Uri("avares://JacksBean/Assets/app_icon.png"));
        _icon = new TrayIcon { Icon = new WindowIcon(png), ToolTipText = AppIdentity.DisplayName, IsVisible = true };
        _icon.Menu = new NativeMenu { _toggle, new NativeMenuItemSeparator(), _home, _settings, _about, new NativeMenuItemSeparator(), _quit };
        _icon.Clicked += (_, _) => Dispatcher.UIThread.Post(App.ToggleDock);

        _toggle.Click += (_, _) => App.ToggleDock();
        _home.Click += (_, _) => App.OpenHome();
        _settings.Click += (_, _) => SettingsWindow.Open(_dock);
        _about.Click += (_, _) => Dialogs.AboutDialog.Open(_dock);
        _quit.Click += (_, _) => App.Quit();

        TrayIcon.SetIcons(app, new TrayIcons { _icon });
        _dock.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty) UpdateText(); };
        Strings.Changed += () => Dispatcher.UIThread.Post(UpdateText);
        UpdateText();
    }

    private void UpdateText()
    {
        var s = Strings.Current;
        _toggle.Header = _dock.IsVisible ? s.TrayHideDock : s.TrayShowDock;
        _home.Header = s.DockMenuOpenWindow;
        _settings.Header = s.SettingsTooltip;
        _about.Header = s.AboutTooltip;
        _quit.Header = s.DockMenuQuit;
    }

    public void Dispose() => _icon.Dispose();
}

/// <summary>
/// 전역 단축키(독 숨기기·보이기)를 설정에 맞춰 켜고 끈다. 이 OS 에서 쓸 수 없으면(<see cref="IsSupported"/> false) 아무것도 하지 않고
/// 설정 창도 그 항목을 숨긴다. 등록에 실패하면(다른 앱이 이미 쓰고 있음) 알리고 설정을 다시 끈다.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly IGlobalHotkey _hotkey = GlobalHotkeys.Create();
    private bool _registered;

    public bool IsSupported => _hotkey.IsSupported;

    public static string Combo => HotkeyGesture.ToggleDock.Display(OperatingSystem.IsMacOS());

    public void Apply(bool enabled)
    {
        if (!IsSupported || enabled == _registered) return;
        if (!enabled)
        {
            _hotkey.Unregister();
            _registered = false;
            return;
        }
        if (_hotkey.Register(HotkeyGesture.ToggleDock, () => Dispatcher.UIThread.Post(App.ToggleDock), out var error))
        {
            _registered = true;
            return;
        }
        Snackbar.Error(Strings.Current.HotkeyFailed(Combo), error);
        App.Settings.Update(x => x with { GlobalHotkey = false });   // 켜지 못했으니 설정도 꺼 둔다(다시 켜 보게)
    }

    public void Dispose() => _hotkey.Dispose();
}
