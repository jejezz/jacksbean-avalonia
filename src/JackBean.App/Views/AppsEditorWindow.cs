// 앱 편집 창: 정책의 앱(`apps`)을 JSON 을 보지 않고 폼으로 추가·수정·삭제한다. 홈 창은 보기 전용이고 편집은 이 별도 창에서 한다.
// 왼쪽에 앱 목록, 오른쪽에 폼(이름·실행 파일·인자). 저장은 `PolicyStore` 가 임시 파일에 쓰고 바꿔 끼우며, 사용자가 손으로 적은 키는 보존한다.
// 단축키: 저장 Ctrl/⌘+S · 창 닫기 Ctrl/⌘+W (ui-ux.md §7). 앱에 하나만 열린다.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JackBean.App.Dialogs;
using JackBean.App.Icons;
using JackBean.App.Notifications;
using JackBean.Domain;
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.App.Views;

public sealed class AppsEditorWindow : Window
{
    private static AppsEditorWindow? _instance;

    /// <summary>앱 편집 창을 연다. 이미 열려 있으면 앞으로 가져온다.</summary>
    public static void Open(Window origin)
    {
        if (_instance is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new AppsEditorWindow();
        _instance = window;
        WindowPlacementHelper.OpenNear(origin, window);
    }

    private readonly DockPanel _leftPanel = new();
    private readonly StackPanel _list = new() { Spacing = 4 };
    private readonly ScrollViewer _listScroll;
    private readonly ContentControl _form = new();
    private PolicyDocument? _policy;
    private string? _selectedId;   // null 이면 새 앱을 만드는 중(또는 아무것도 고르지 않음)
    private bool _isNew;
    private bool _saving;

    // 폼 상태
    private TextBox _idBox = null!, _commandBox = null!, _argsBox = null!;
    private TextBlock _errorText = null!, _noPathText = null!;
    private Button _saveButton = null!, _revertButton = null!;
    private string _origCommand = "", _origArgs = "";
    private bool _building;

    private AppsEditorWindow()
    {
        Width = 880; Height = 560; MinWidth = 640; MinHeight = 420;
        Title = WindowTitle.Of(Strings.Current.AppsEditorTitle);

        var divider = new Border { Width = 1 };
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppStroke"));
        _listScroll = new ScrollViewer { Content = _list };
        _leftPanel.Margin = new Thickness(16, 20, 16, 16);
        var scroll = new ScrollViewer { Content = _form, Padding = new Thickness(32, 24) };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("240,1,*") };
        Grid.SetColumn(_leftPanel, 0); Grid.SetColumn(divider, 1); Grid.SetColumn(scroll, 2);
        root.Children.Add(_leftPanel);
        root.Children.Add(divider);
        root.Children.Add(scroll);
        Content = root;

        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Strings.Changed += OnLanguageChanged;
        App.PolicyChanged += OnPolicyChanged;
        Closed += (_, _) =>
        {
            Strings.Changed -= OnLanguageChanged;
            App.PolicyChanged -= OnPolicyChanged;
            _instance = null;
        };
        Opened += async (_, _) => await ReloadAsync(selectFirst: true);
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(() => { Title = WindowTitle.Of(Strings.Current.AppsEditorTitle); BuildList(); BuildForm(); });

    /// <summary>다른 곳(홈 창의 복구 등)이 정책을 바꿨다. 내 저장이 불러낸 알림은 무시하고, 고치던 내용이 있으면 폼은 그대로 둔다.</summary>
    private void OnPolicyChanged() => Dispatcher.UIThread.Post(async () =>
    {
        if (_saving) return;
        await ReloadAsync(selectFirst: false, keepForm: IsDirty);
    });

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mod = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != mod) return;
        if (e.Key == Key.S) { _ = SaveAsync(); e.Handled = true; }
        else if (e.Key == Key.W) { Close(); e.Handled = true; }
    }

    // --- 불러오기 ---

    private async Task ReloadAsync(bool selectFirst, string? select = null, bool keepForm = false)
    {
        _policy = await App.PolicyStore.LoadAsync();
        if (select != null) { _selectedId = select; _isNew = false; }
        else if (selectFirst && _selectedId == null && !_isNew) _selectedId = _policy.Apps.Keys.FirstOrDefault();
        if (_selectedId != null && !_policy.Apps.ContainsKey(_selectedId) && !_isNew) _selectedId = _policy.Apps.Keys.FirstOrDefault();
        BuildList();
        if (!keepForm) BuildForm();
    }

    private bool IsDirty =>
        _isNew ? (_idBox?.Text?.Length > 0 || _commandBox?.Text?.Length > 0 || _argsBox?.Text != AppRules.PathPlaceholder)
               : _commandBox != null && (_commandBox.Text != _origCommand || _argsBox.Text != _origArgs);

    // --- 목록 ---

    private void BuildList()
    {
        var s = Strings.Current;
        _leftPanel.Children.Clear();
        _list.Children.Clear();

        var title = new TextBlock { Text = s.AppsEditorList, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsMid"));
        var add = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = "plus" }, new TextBlock { Text = s.AppsEditorAdd, VerticalAlignment = VerticalAlignment.Center } } } };
        add.Click += (_, _) => StartNew();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(title);
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        DockPanel.SetDock(header, Dock.Top);
        _leftPanel.Children.Add(header);

        if (_isNew) _list.Children.Add(ListItem(s.AppsEditorNewApp, selected: true, () => { }));
        foreach (var id in _policy?.Apps.Keys ?? [])
        {
            var captured = id;
            _list.Children.Add(ListItem(id, selected: !_isNew && id == _selectedId, () => Select(captured)));
        }
        if (_list.Children.Count == 0)
        {
            var none = new TextBlock { Text = s.HomeNoApps, TextWrapping = TextWrapping.Wrap };
            none.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            _list.Children.Add(none);
        }
        _leftPanel.Children.Add(_listScroll);
    }

    private static ToggleButton ListItem(string label, bool selected, Action click)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Classes.Add("user"); // ID 는 사용자가 정한 이름 — 시스템 글꼴 (fonts.md §3)
        var button = new ToggleButton
        {
            Classes = { "nav" }, IsChecked = selected, Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private async void Select(string id)
    {
        if (!_isNew && id == _selectedId) { BuildList(); return; }
        if (!await ConfirmDiscardAsync()) { BuildList(); return; }
        _selectedId = id;
        _isNew = false;
        BuildList();
        BuildForm();
    }

    private async void StartNew()
    {
        if (_isNew) return;
        if (!await ConfirmDiscardAsync()) return;
        _selectedId = null;
        _isNew = true;
        BuildList();
        BuildForm();
    }

    /// <summary>고치던 내용이 있는데 다른 곳으로 옮기려 하면 한 번 묻는다. 계속하면 true.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty) return true;
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(this, s.AppsEditorTitle, s.AppDiscardConfirm, [s.AppDiscard, s.CommonCancel], defaultIndex: 1, cancelIndex: 1);
        return choice == 0;
    }

    // --- 폼 ---

    private static TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold };
        return t;
    }

    private TextBlock Hint(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.Classes.Add("small");
        t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        return t;
    }

    private TextBox Box(string text, bool multiline = false)
    {
        var box = new TextBox { Text = text, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.NoWrap : TextWrapping.NoWrap };
        if (multiline) { box.MinHeight = 96; box.Height = 112; }
        box.Bind(TextBox.FontFamilyProperty, this.GetResourceObservable("AppUserFont")); // 경로·인자는 사용자 문자열
        box.TextChanged += (_, _) => { if (!_building) Validate(); };
        return box;
    }

    private void BuildForm()
    {
        var s = Strings.Current;
        _building = true;
        if (_policy == null || (!_isNew && _selectedId == null))
        {
            _form.Content = EmptyForm();
            _building = false;
            return;
        }

        AppEntry? entry = _isNew ? null : _policy.Apps[_selectedId!];
        _origCommand = entry?.Command ?? "";
        _origArgs = AppRules.FormatArgs(entry?.Args ?? [AppRules.PathPlaceholder]);

        _idBox = Box(entry?.Id ?? "");
        _idBox.IsReadOnly = !_isNew;
        _idBox.Opacity = _isNew ? 1 : 0.6; // 저장한 뒤에는 바꿀 수 없다는 것이 보이게
        _idBox.MaxLength = AppRules.MaxIdLength;
        _commandBox = Box(_origCommand);
        _argsBox = Box(_origArgs, multiline: true);

        var browse = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = "folder" }, new TextBlock { Text = s.FieldAppBrowse, VerticalAlignment = VerticalAlignment.Center } } } };
        browse.Click += async (_, _) => await BrowseAsync();
        var commandRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        commandRow.Children.Add(_commandBox);
        Grid.SetColumn(browse, 1);
        commandRow.Children.Add(browse);

        _errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _errorText.Classes.Add("failed");
        _noPathText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _noPathText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));

        _saveButton = new Button { Content = s.AppSave };
        _saveButton.Classes.Add("accent");
        _saveButton.Click += async (_, _) => await SaveAsync();
        _revertButton = new Button { Content = s.AppRevert };
        _revertButton.Click += (_, _) => BuildForm();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { _saveButton, _revertButton } };
        if (!_isNew)
        {
            var delete = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = "trash" }, new TextBlock { Text = s.AppDelete, VerticalAlignment = VerticalAlignment.Center } } }, Margin = new Thickness(24, 0, 0, 0) };
            delete.Click += async (_, _) => await DeleteAsync();
            buttons.Children.Add(delete);
        }

        var form = new StackPanel { Spacing = 6, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(Label(s.FieldAppId));
        form.Children.Add(_idBox);
        if (_isNew) form.Children.Add(Hint(s.FieldAppIdHint));
        form.Children.Add(Spaced(Label(s.FieldAppCommand)));
        form.Children.Add(commandRow);
        form.Children.Add(Hint(s.FieldAppCommandHint));
        form.Children.Add(Spaced(Label(s.FieldAppArgs)));
        form.Children.Add(_argsBox);
        form.Children.Add(Hint(s.FieldAppArgsHint));
        form.Children.Add(_noPathText);
        form.Children.Add(_errorText);
        form.Children.Add(buttons);
        _form.Content = form;
        _building = false;
        Validate();
        Dispatcher.UIThread.Post(() => (_isNew ? _idBox : _commandBox).Focus());
    }

    private static Control Spaced(Control c) { c.Margin = new Thickness(0, 12, 0, 0); return c; }

    private Control EmptyForm()
    {
        var s = Strings.Current;
        var icon = new DuoIcon { Icon = "tray", ColorKey = "AppTextMid", Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        var line = new TextBlock { Text = s.HomeNoApps, HorizontalAlignment = HorizontalAlignment.Center };
        line.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var add = new Button { Content = s.AppsEditorAdd, HorizontalAlignment = HorizontalAlignment.Center };
        add.Classes.Add("accent");
        add.Click += (_, _) => StartNew();
        return new StackPanel { Spacing = 12, Margin = new Thickness(0, 80), Children = { icon, line, add } };
    }

    private async Task BrowseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.Current.FieldAppCommand, AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) _commandBox.Text = path;
    }

    // --- 검증과 저장 ---

    private AppFieldError CurrentError() =>
        _policy == null ? AppFieldError.None
        : AppRules.Validate(_isNew ? _idBox.Text : _selectedId, _commandBox.Text, _policy.Apps.Keys, _isNew);

    private void Validate()
    {
        if (_idBox == null || _policy == null) return;
        var s = Strings.Current;
        var error = CurrentError();
        // 처음 열었을 때(아직 아무것도 안 쳤을 때)부터 빨갛게 보이지 않게, 쓰던 칸이 있을 때만 이유를 보인다.
        var touched = IsDirty;
        _errorText.Text = !touched ? "" : error switch
        {
            AppFieldError.IdEmpty => s.AppErrIdEmpty,
            AppFieldError.IdInvalid => s.AppErrIdInvalid,
            AppFieldError.IdTaken => s.AppErrIdTaken,
            AppFieldError.CommandEmpty => s.AppErrCommandEmpty,
            _ => "",
        };
        _errorText.IsVisible = _errorText.Text.Length > 0;
        _noPathText.Text = s.AppArgsNoPath;
        _noPathText.IsVisible = AppRules.LacksPathPlaceholder(AppRules.ParseArgs(_argsBox.Text));
        var blocked = _policy.IsFallback; // 파일을 못 읽은 상태에서는 저장하지 않는다(덮어쓰지 않는다)
        _saveButton.IsEnabled = touched && error == AppFieldError.None && !blocked;
        _revertButton.IsEnabled = touched;
        if (blocked) { _errorText.Text = s.HomeFallbackNote; _errorText.IsVisible = true; }
    }

    private async Task SaveAsync()
    {
        if (_saveButton is not { IsEnabled: true } || _saving) return;
        _saving = true;
        try
        {
            var id = (_isNew ? _idBox.Text!.Trim() : _selectedId)!;
            var result = await App.PolicyStore.UpsertAppAsync(new AppUpsert(id, _commandBox.Text!.Trim(), AppRules.ParseArgs(_argsBox.Text)));
            if (!result.Ok) { Snackbar.Error(Strings.Current.AppSaveFailed(result.Error!)); return; }
            _isNew = false;
            await ReloadAsync(selectFirst: false, select: id);
            App.NotifyPolicyChanged();
            PolicyNotices.Saved(Strings.Current.AppSaved(id));
        }
        finally { _saving = false; }
    }

    private async Task DeleteAsync()
    {
        if (_isNew || _selectedId is not { } id || _policy == null) return;
        var s = Strings.Current;
        var users = _policy.RulesUsing(id).Count;
        var choice = await MessageDialog.ShowAsync(this, s.AppDeleteTitle, s.AppDeleteConfirm(id, users), [s.AppDelete, s.CommonCancel], defaultIndex: 1, cancelIndex: 1);
        if (choice != 0) return;
        _saving = true;
        try
        {
            var result = await App.PolicyStore.RemoveAppAsync(id, ReferencedRules.MakeAsk);
            if (!result.Ok) { Snackbar.Error(s.AppDeleteFailed(result.Error!)); return; }
            _selectedId = null;
            await ReloadAsync(selectFirst: true);
            App.NotifyPolicyChanged();
            PolicyNotices.Saved(s.AppDeleted(id));
        }
        finally { _saving = false; }
    }
}
