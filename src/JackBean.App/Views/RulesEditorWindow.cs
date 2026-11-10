// 규칙 편집 창: 정책의 규칙을 JSON 을 보지 않고 고친다. 홈 창은 보기 전용이고 편집은 이 별도 창에서 한다.
// 사용자가 보는 것은 조건 목록이 아니라 **네 가지 대상**이다 — 드라이브 · 폴더 · 파일 형식(확장자 묶음) · 그 밖의 파일 —
// 대상마다 앱을 0~10개 연결한다(1개면 바로 열고, 2개 이상이면 그 앱들만 골라서 묻는다). 규칙 순서는 저장할 때 알아서 정해진다.
// 단축키: 저장 Ctrl/⌘+S · 창 닫기 Ctrl/⌘+W (ui-ux.md §7). 앱에 하나만 열린다.

using System.Text.RegularExpressions;
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
using JackBean.Domain.Policy;
using JackBean.Services;

namespace JackBean.App.Views;

/// <summary>규칙 편집 창의 구역(처음 어느 구역을 보여 줄지).</summary>
public enum RulesEditorSection { Drive, Folder, Files, Default }

public sealed partial class RulesEditorWindow : Window
{
    private static RulesEditorWindow? _instance;

    /// <summary>
    /// 규칙 편집 창을 연다. 이미 열려 있으면 앞으로 가져온다. <paramref name="start"/> = 보여 줄 구역,
    /// <paramref name="ruleId"/> = 파일 형식 구역에서 고를 규칙(홈 창에서 그 규칙 줄을 눌러 열 때 — 없으면 첫 규칙).
    /// </summary>
    public static void Open(Window origin, RulesEditorSection start = RulesEditorSection.Drive, string? ruleId = null)
    {
        if (_instance is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            if (ruleId != null || (Section)start != open._section) open.GoTo((Section)start, ruleId);
            return;
        }
        var window = new RulesEditorWindow((Section)start) { _initialExt = ruleId };
        _instance = window;
        WindowPlacementHelper.OpenNear(origin, window);
    }

    private enum Section { Drive, Folder, Files, Default }   // RulesEditorSection 과 같은 순서

    [GeneratedRegex(@"[,\s;]+")]
    private static partial Regex ExtSeparators();

    private readonly StackPanel _nav = new() { Spacing = 4 };
    private readonly TextBlock _countText = new();
    private readonly ContentControl _page = new();
    private PolicyDocument? _policy;
    private Section _section;
    private string? _extRuleId;   // 파일 형식 구역에서 고른 규칙(없으면 null)
    private string? _initialExt;  // 열 때 고를 파일 형식 규칙(홈 창에서 그 줄을 눌렀을 때)
    private bool _isNewExt;       // 파일 형식 구역에서 새 규칙을 만드는 중
    private bool _saving;
    private bool _syncing;

    // 고치는 중인 내용
    private List<string> _apps = [];
    private string _extText = "";
    private string _origKey = "";
    private readonly Dictionary<string, AppUpsert> _pending = [];   // 찾아본 새 앱: 규칙을 저장할 때 정책의 apps 에 등록한다

    // 폼 컨트롤
    private ContentControl _appsHost = new();   // 페이지를 다시 만들 때마다 새로 만든다(이전 페이지의 자식으로 남아 있으면 다시 붙일 수 없다)
    private readonly List<(ExtensionGroup Group, ToggleButton Chip)> _chips = [];
    private TextBox? _extBox;
    private TextBlock _errorText = new();
    private Button _saveButton = new(), _revertButton = new();

    private RulesEditorWindow(Section start)
    {
        _section = start;
        Width = 920; Height = 640; MinWidth = 700; MinHeight = 460;
        Title = WindowTitle.Of(Strings.Current.RulesEditorTitle);

        var divider = new Border { Width = 1 };
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppStroke"));
        _countText.Classes.Add("small");
        _countText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var left = new DockPanel { Margin = new Thickness(16, 20, 16, 16) };
        DockPanel.SetDock(_countText, Dock.Bottom);
        left.Children.Add(_countText);
        left.Children.Add(_nav);
        var scroll = new ScrollViewer { Content = _page, Padding = new Thickness(32, 24) };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,1,*") };
        Grid.SetColumn(left, 0); Grid.SetColumn(divider, 1); Grid.SetColumn(scroll, 2);
        root.Children.Add(left);
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
        Opened += async (_, _) => await ReloadAsync(first: true);
    }

    private void OnLanguageChanged() => Dispatcher.UIThread.Post(() =>
    {
        Title = WindowTitle.Of(Strings.Current.RulesEditorTitle);
        BuildNav();
        BuildPage();
    });

    /// <summary>다른 곳(앱 편집 창, 홈 창의 복구, 독의 "항상 이렇게 열기")이 정책을 바꿨다. 고치던 내용이 있으면 폼은 그대로 둔다.</summary>
    private void OnPolicyChanged() => Dispatcher.UIThread.Post(async () =>
    {
        if (_saving) return;
        await ReloadAsync(first: false, keepForm: IsDirty);
    });

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mod = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != mod) return;
        if (e.Key == Key.S) { _ = SaveAsync(); e.Handled = true; }
        else if (e.Key == Key.W) { Close(); e.Handled = true; }
    }

    // --- 불러오기와 선택 ---

    private async Task ReloadAsync(bool first, string? selectExt = null, bool keepForm = false)
    {
        _policy = await App.PolicyStore.LoadAsync();
        if (selectExt != null) { _extRuleId = selectExt; _isNewExt = false; }
        if (_section == Section.Files && !_isNewExt && (first || _extRuleId == null || ExtRules().All(r => r.Id != _extRuleId)))
        {
            // 처음 열 때는 눌러서 온 규칙을, 없으면 첫 규칙을 고른다.
            _extRuleId = first && _initialExt != null && ExtRules().Any(r => r.Id == _initialExt) ? _initialExt : ExtRules().FirstOrDefault()?.Id;
            _initialExt = null;
        }
        BuildNav();
        if (keepForm) return;
        LoadDraft();
        BuildPage();
    }

    private IEnumerable<Rule> ExtRules() => _policy?.Rules.Where(r => r.When.Kind == RuleKind.Extension) ?? [];

    private static RuleKind KindOf(Section s) => s switch
    {
        Section.Drive => RuleKind.Drive,
        Section.Folder => RuleKind.Folder,
        Section.Files => RuleKind.Extension,
        _ => RuleKind.File,
    };

    private Rule? CurrentRule() => _policy == null ? null
        : _section == Section.Files ? (_isNewExt ? null : _policy.Rules.FirstOrDefault(r => r.Id == _extRuleId))
        : _policy.Rules.FirstOrDefault(r => r.When.Kind == KindOf(_section));

    private void LoadDraft()
    {
        _pending.Clear();
        var rule = CurrentRule();
        _apps = rule?.Choices.ToList() ?? [];
        _extText = rule is { When.Kind: RuleKind.Extension } ? string.Join(", ", rule.When.Extensions) : "";
        _origKey = StateKey();
    }

    private static List<string> ParseExt(string? text) =>
        ExtSeparators().Split(text ?? "").Select(ExtensionGroups.Normalize).Where(e => e.Length > 0).Distinct().ToList();

    private string StateKey() => string.Join("|", _apps) + "#" + (_section == Section.Files ? string.Join(",", ParseExt(_extText)) : "");

    private bool IsDirty => _policy != null && StateKey() != _origKey;

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty) return true;
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(this, s.RulesEditorTitle, s.AppDiscardConfirm, [s.AppDiscard, s.CommonCancel], defaultIndex: 1, cancelIndex: 1);
        return choice == 0;
    }

    private async void SelectSection(Section section)
    {
        if (section == _section) { BuildNav(); return; }
        if (!await ConfirmDiscardAsync()) { BuildNav(); return; }
        _section = section;
        _isNewExt = false;
        _extRuleId = section == Section.Files ? ExtRules().FirstOrDefault()?.Id : null;
        BuildNav();
        LoadDraft();
        BuildPage();
    }

    /// <summary>이미 열린 창을 다른 구역·규칙으로 옮긴다(고치던 내용이 있으면 버릴지 묻는다).</summary>
    private async void GoTo(Section section, string? ruleId)
    {
        var target = section == Section.Files ? (ruleId != null && ExtRules().Any(r => r.Id == ruleId) ? ruleId : null) : null;
        if (section == _section && (section != Section.Files || target == null || (!_isNewExt && target == _extRuleId))) return;
        if (!await ConfirmDiscardAsync()) return;
        _section = section;
        _isNewExt = false;
        _extRuleId = section == Section.Files ? target ?? ExtRules().FirstOrDefault()?.Id : null;
        BuildNav();
        LoadDraft();
        BuildPage();
    }

    private async void SelectExt(string id)
    {
        if (!_isNewExt && id == _extRuleId) return;
        if (!await ConfirmDiscardAsync()) return;
        _extRuleId = id;
        _isNewExt = false;
        LoadDraft();
        BuildPage();
    }

    private async void StartNewExt()
    {
        if (_isNewExt) return;
        if (!await ConfirmDiscardAsync()) return;
        _isNewExt = true;
        _extRuleId = null;
        LoadDraft();
        BuildPage();
    }

    // --- 이름 ---

    private string RuleName(Rule rule)
    {
        var s = Strings.Current;
        switch (rule.When.Kind)
        {
            case RuleKind.Drive: return s.RuleSectionDrive;
            case RuleKind.Folder: return s.RuleSectionFolder;
            case RuleKind.File: return s.RuleSectionDefault;
        }
        var exts = rule.When.Extensions;
        var group = ExtensionGroups.All.FirstOrDefault(g => g.Extensions.Count == exts.Count && g.Extensions.All(exts.Contains));
        return group != null ? s.ExtensionGroupName(group.Id) : string.Join(", ", exts.Take(3)) + (exts.Count > 3 ? "…" : "");
    }

    private string NameOfRuleId(string? id) => _policy?.Rules.FirstOrDefault(r => r.Id == id) is { } r ? RuleName(r) : id ?? "";

    // --- 왼쪽: 대상 목록 ---

    private void BuildNav()
    {
        var s = Strings.Current;
        _nav.Children.Clear();
        (Section Section, string Label)[] items =
        [
            (Section.Drive, s.RuleSectionDrive), (Section.Folder, s.RuleSectionFolder),
            (Section.Files, s.RuleSectionFiles), (Section.Default, s.RuleSectionDefault),
        ];
        foreach (var (section, label) in items)
        {
            var captured = section;
            var button = new ToggleButton
            {
                Classes = { "nav" }, IsChecked = section == _section, Content = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => SelectSection(captured);
            _nav.Children.Add(button);
        }
        _countText.Text = _policy == null ? "" : s.RuleCount(_policy.Rules.Count, PolicyLimits.MaxRules);
    }

    // --- 오른쪽: 선택한 대상 ---

    private TextBlock PageTitle(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeight.Bold };
        t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        return t;
    }

    private TextBlock Muted(string text, bool small = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (small) t.Classes.Add("small");
        t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        return t;
    }

    private void BuildPage()
    {
        if (_policy == null) { _page.Content = null; return; }
        var s = Strings.Current;
        _chips.Clear();
        _extBox = null;
        _errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _errorText.Classes.Add("failed");
        _saveButton = new Button { Content = s.AppSave };
        _saveButton.Classes.Add("accent");
        _saveButton.Click += async (_, _) => await SaveAsync();
        _revertButton = new Button { Content = s.AppRevert };
        _revertButton.Click += (_, _) => { LoadDraft(); BuildPage(); };

        var (heading, desc) = _section switch
        {
            Section.Drive => (s.RuleSectionDrive, s.RuleDescDrive),
            Section.Folder => (s.RuleSectionFolder, s.RuleDescFolder),
            Section.Files => (s.RuleSectionFiles, s.RuleDescFiles),
            _ => (s.RuleSectionDefault, s.RuleDescDefault),
        };
        var page = new StackPanel { Spacing = 12, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(PageTitle(heading));
        page.Children.Add(Muted(desc));
        page.Children.Add(_section == Section.Files ? FilesBody() : RuleForm(showExtensions: false));
        _page.Content = page;
        Validate();
    }

    /// <summary>파일 형식 구역: 왼쪽에 확장자 규칙 목록, 오른쪽에 고르고 있는 규칙의 폼.</summary>
    private Control FilesBody()
    {
        var s = Strings.Current;
        var list = new StackPanel { Spacing = 4 };
        var add = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = "plus" }, new TextBlock { Text = s.RuleExtAdd, VerticalAlignment = VerticalAlignment.Center } } }, HorizontalAlignment = HorizontalAlignment.Stretch };
        add.Click += (_, _) => StartNewExt();
        list.Children.Add(add);
        if (_isNewExt) list.Children.Add(ListItem(s.RuleExtNew, selected: true, () => { }));
        foreach (var rule in ExtRules())
        {
            var id = rule.Id;
            list.Children.Add(ListItem(RuleName(rule), selected: !_isNewExt && id == _extRuleId, () => SelectExt(id)));
        }

        Control right = !_isNewExt && _extRuleId == null ? Muted(s.RuleExtEmpty) : RuleForm(showExtensions: true);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,24,*"), HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(list, 0); Grid.SetColumn(right, 2);
        grid.Children.Add(list);
        grid.Children.Add(right);
        return grid;
    }

    private static ToggleButton ListItem(string label, bool selected, Action click)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var button = new ToggleButton
        {
            Classes = { "nav" }, IsChecked = selected, Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private Control RuleForm(bool showExtensions)
    {
        var s = Strings.Current;
        var form = new StackPanel { Spacing = 8 };
        if (showExtensions)
        {
            form.Children.Add(Label(s.RuleExtGroups));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var group in ExtensionGroups.All)
            {
                var g = group;
                var chip = new ToggleButton { Classes = { "chip" }, Height = 36, VerticalContentAlignment = VerticalAlignment.Center, Content = s.ExtensionGroupName(g.Id) };
                chip.Click += (_, _) => ToggleGroup(g);
                _chips.Add((g, chip));
                wrap.Children.Add(chip);
            }
            form.Children.Add(wrap);

            form.Children.Add(Spaced(Label(s.RuleExtList)));
            _extBox = new TextBox { Text = _extText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72 };
            _extBox.Bind(TextBox.FontFamilyProperty, this.GetResourceObservable("AppUserFont"));   // 사용자가 적는 글
            _extBox.TextChanged += (_, _) =>
            {
                if (_syncing) return;
                _extText = _extBox.Text ?? "";
                RefreshChips();
                Validate();
            };
            form.Children.Add(_extBox);
            form.Children.Add(Muted(s.RuleExtHint, small: true));
            RefreshChips();
        }

        _appsHost = new ContentControl { Content = AppsEditor() };
        _appsHost.Margin = new Thickness(0, showExtensions ? 12 : 0, 0, 0);
        form.Children.Add(_appsHost);
        form.Children.Add(_errorText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { _saveButton, _revertButton } };
        if (showExtensions && !_isNewExt)
        {
            var delete = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = "trash" }, new TextBlock { Text = s.RuleDelete, VerticalAlignment = VerticalAlignment.Center } } }, Margin = new Thickness(24, 0, 0, 0) };
            delete.Click += async (_, _) => await DeleteAsync();
            buttons.Children.Add(delete);
        }
        form.Children.Add(buttons);
        return form;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    private static Control Spaced(Control c)
    {
        c.Margin = new Thickness(0, 12, 0, 0);
        return c;
    }

    // --- 확장자 묶음 ---

    private void ToggleGroup(ExtensionGroup group)
    {
        var current = ParseExt(_extText);
        var allThere = group.Extensions.All(current.Contains);
        var next = allThere
            ? current.Where(e => !group.Extensions.Contains(e)).ToList()
            : current.Concat(group.Extensions.Where(e => !current.Contains(e))).ToList();
        _extText = string.Join(", ", next);
        _syncing = true;
        if (_extBox != null) _extBox.Text = _extText;
        _syncing = false;
        RefreshChips();
        Validate();
    }

    private void RefreshChips()
    {
        var current = ParseExt(_extText);
        foreach (var (group, chip) in _chips) chip.IsChecked = group.Extensions.All(current.Contains);
    }

    // --- 앱 목록(0~10개, 순서 있음) ---

    private Control AppsEditor()
    {
        var s = Strings.Current;
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Label(s.RuleAppsTitle(_apps.Count, PolicyLimits.MaxAppsPerRule)));
        for (var i = 0; i < _apps.Count; i++) panel.Children.Add(AppRow(i));

        if (_apps.Count < PolicyLimits.MaxAppsPerRule)
        {
            var free = (_policy?.Apps.Keys ?? []).Concat(_pending.Keys).Where(id => !_apps.Contains(id)).ToList();
            var addRow = new WrapPanel { Orientation = Orientation.Horizontal };   // 좁으면 줄을 바꾼다
            if (free.Count > 0)
            {
                var combo = new ComboBox { PlaceholderText = s.RuleAppsAdd, ItemsSource = free, MinWidth = 240, Margin = new Thickness(0, 0, 8, 6) };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not string id) return;
                    _apps.Add(id);
                    RebuildApps();
                };
                addRow.Children.Add(combo);
            }
            // 등록해 둔 앱에 없는 앱도 파일에서 직접 골라 연결할 수 있다(저장할 때 정책에 새 앱으로 등록된다).
            var browse = new Button { Margin = new Thickness(0, 0, 0, 6), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = "folder" }, new TextBlock { Text = s.RuleAppsBrowse, VerticalAlignment = VerticalAlignment.Center } } } };
            browse.Click += async (_, _) => await BrowseAppAsync();
            addRow.Children.Add(browse);
            panel.Children.Add(addRow);
        }
        panel.Children.Add(Muted(_apps.Count == 0 ? s.RuleAppsNoneNote : _apps.Count == 1 ? s.RuleAppsOneNote : s.RuleAppsManyNote, small: true));
        return panel;
    }

    private Control AppRow(int index)
    {
        var s = Strings.Current;
        var id = _apps[index];
        var pending = _pending.TryGetValue(id, out var pendingApp);
        var known = pending || _policy?.Apps.ContainsKey(id) == true;

        var number = Muted((index + 1).ToString());
        number.VerticalAlignment = VerticalAlignment.Center;
        number.Width = 20;
        var name = new TextBlock { Text = pending ? s.RuleAppPending(id) : known ? id : s.RuleAppMissing(id), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Classes.Add("user");   // 앱 이름은 사용자가 정했다 — 시스템 글꼴
        if (!known) name.Classes.Add("failed");
        if (pending) ToolTip.SetTip(name, pendingApp!.Command);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var up = SmallButton("arrow-up", s.RuleAppMoveUp, () => Move(index, -1)); up.IsEnabled = index > 0;
        var down = SmallButton("arrow-down", s.RuleAppMoveDown, () => Move(index, 1)); down.IsEnabled = index < _apps.Count - 1;
        buttons.Children.Add(up);
        buttons.Children.Add(down);
        buttons.Children.Add(SmallButton("x", s.RuleAppRemove, () => { _apps.RemoveAt(index); RebuildApps(); }));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(name, 1); Grid.SetColumn(buttons, 2);
        grid.Children.Add(number);
        grid.Children.Add(name);
        grid.Children.Add(buttons);
        var row = new Border { Padding = new Thickness(12, 2, 4, 2), CornerRadius = new CornerRadius(8), Child = grid };
        row.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        return row;
    }

    private static Button SmallButton(string icon, string tooltip, Action click)
    {
        var b = new Button { Classes = { "tool" }, Content = new DuoIcon { Icon = icon } };
        ToolTip.SetTip(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    private void Move(int index, int delta)
    {
        var to = index + delta;
        if (to < 0 || to >= _apps.Count) return;
        (_apps[index], _apps[to]) = (_apps[to], _apps[index]);
        RebuildApps();
    }

    private void RebuildApps()
    {
        _appsHost.Content = AppsEditor();
        Validate();
    }

    // --- 검증 ---

    private RuleCondition? Condition() => _section switch
    {
        Section.Drive => RuleCondition.Drive,
        Section.Folder => RuleCondition.Folder,
        Section.Default => RuleCondition.File,
        _ => RuleCondition.Extension(ParseExt(_extText)),
    };

    private bool IsNewRule => _section == Section.Files ? _isNewExt : CurrentRule() == null;

    /// <summary>저장할 규칙의 ID: 있는 규칙이면 그대로, 새 규칙이면 정한다(묶음과 똑같으면 묶음 이름, 아니면 첫 확장자).</summary>
    private string RuleIdForSave(RuleCondition cond)
    {
        if (_section != Section.Files) return CurrentRule()?.Id ?? _section switch { Section.Drive => "drives", Section.Folder => "folders", _ => "files" };
        if (!_isNewExt && _extRuleId != null) return _extRuleId;
        var exts = cond.Extensions;
        var group = ExtensionGroups.All.FirstOrDefault(g => g.Extensions.Count == exts.Count && g.Extensions.All(exts.Contains));
        var baseId = group?.Id ?? "ext-" + exts[0];
        var id = baseId;
        for (var i = 2; _policy!.Rules.Any(r => r.Id == id); i++) id = $"{baseId}-{i}";
        return id;
    }

    private string Describe(RuleProblem p)
    {
        var s = Strings.Current;
        return p.Kind switch
        {
            RuleProblemKind.RuleLimit => s.RuleErrLimit,
            RuleProblemKind.TooManyApps => s.RuleErrTooMany,
            RuleProblemKind.UnknownApp => s.RuleErrUnknownApp(p.Detail ?? ""),
            RuleProblemKind.ExtensionTaken => s.RuleErrExtTaken(p.Detail ?? "", NameOfRuleId(p.Other)),
            _ => p.Kind.ToString(),
        };
    }

    private void Validate()
    {
        if (_policy == null) return;
        var s = Strings.Current;
        var dirty = IsDirty;
        var cond = Condition();
        var messages = new List<string>();
        if (cond == null) { if (dirty || _isNewExt) messages.Add(s.RuleErrNoExt); }
        else messages.AddRange(RuleCheck.Validate(new Rule(RuleIdForSave(cond), cond, _apps), PolicyWithPending(), IsNewRule).Select(Describe));
        if (_policy.IsFallback) messages.Add(s.HomeFallbackNote);   // 파일을 못 읽은 상태에서는 저장하지 않는다(덮어쓰지 않는다)

        _errorText.Text = string.Join("\n", messages.Distinct());
        _errorText.IsVisible = _errorText.Text.Length > 0;
        _saveButton.IsEnabled = dirty && cond != null && messages.Count == 0;
        _revertButton.IsEnabled = dirty;
    }

    // --- 저장과 삭제 ---

    /// <summary>찾아본 새 앱까지 등록된 것으로 본 정책(저장 전 검사용 — "정책에 없는 앱" 오류를 내지 않게).</summary>
    private PolicyDocument PolicyWithPending()
    {
        var apps = _policy!.Apps.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (id, app) in _pending) apps[id] = new AppEntry(id, app.Command, app.Args);
        return new PolicyDocument(apps, _policy.Rules, _policy.Warnings, _policy.IsFallback, _policy.Os);
    }

    /// <summary>파일을 골라 앱으로 연결한다. 같은 실행 파일이 이미 등록되어 있으면 그 앱을 쓰고, 아니면 새 앱(이름은 파일 이름에서)으로 올려 둔다.</summary>
    private async Task BrowseAppAsync()
    {
        if (_policy == null || _apps.Count >= PolicyLimits.MaxAppsPerRule) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Strings.Current.RuleAppsBrowseTitle, AllowMultiple = false });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

        var existing = _policy.Apps.Values.FirstOrDefault(a => string.Equals(a.Command, path, StringComparison.OrdinalIgnoreCase))?.Id
                       ?? _pending.Values.FirstOrDefault(a => string.Equals(a.Command, path, StringComparison.OrdinalIgnoreCase))?.Id;
        var id = existing ?? AppRules.SuggestId(path, _policy.Apps.Keys.Concat(_pending.Keys));
        if (existing == null) _pending[id] = new AppUpsert(id, path, [AppRules.PathPlaceholder]);
        if (!_apps.Contains(id)) _apps.Add(id);
        RebuildApps();
    }

    private async Task SaveAsync()
    {
        if (_saveButton is not { IsEnabled: true } || _saving || Condition() is not { } cond) return;
        _saving = true;
        try
        {
            var id = RuleIdForSave(cond);
            // 찾아본 새 앱 중 이 규칙이 쓰는 것을 먼저 정책에 등록한다.
            foreach (var appId in _apps.Where(_pending.ContainsKey).ToList())
            {
                var registered = await App.PolicyStore.UpsertAppAsync(_pending[appId]);
                if (!registered.Ok) { Snackbar.Error(Strings.Current.AppSaveFailed(registered.Error!)); return; }
            }
            var result = await App.PolicyStore.UpsertRuleAsync(new RuleEdit(id, cond, _apps.ToList()));
            if (!result.Ok) { Snackbar.Error(Strings.Current.RuleSaveFailed(result.Error!)); return; }
            _isNewExt = false;
            if (_section == Section.Files) _extRuleId = id;
            await ReloadAsync(first: false, selectExt: _section == Section.Files ? id : null);
            App.NotifyPolicyChanged();
            PolicyNotices.Saved(Strings.Current.RuleSaved(NameOfRuleId(id)));
        }
        finally { _saving = false; }
    }

    private async Task DeleteAsync()
    {
        if (_isNewExt || _extRuleId is not { } id || _policy == null) return;
        var s = Strings.Current;
        var name = NameOfRuleId(id);
        var choice = await MessageDialog.ShowAsync(this, s.RuleDeleteTitle, s.RuleDeleteConfirm(name), [s.RuleDelete, s.CommonCancel], defaultIndex: 1, cancelIndex: 1);
        if (choice != 0) return;
        _saving = true;
        try
        {
            var result = await App.PolicyStore.RemoveRuleAsync(id);
            if (!result.Ok) { Snackbar.Error(s.RuleDeleteFailed(result.Error!)); return; }
            _extRuleId = null;
            await ReloadAsync(first: true);
            App.NotifyPolicyChanged();
            PolicyNotices.Saved(s.RuleDeleted(name));
        }
        finally { _saving = false; }
    }
}
