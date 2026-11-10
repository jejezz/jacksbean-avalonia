using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using JackBean.App.Dialogs;
using JackBean.Services.Text;
using TextMateSharp.Grammars;
using TextSearch = JackBean.Services.Text.TextSearch;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 텍스트 에디터 창(파일 하나당 창 하나). 형제 앱 DaylightCommander 에디터에서 마크다운 미리보기·원격 파일 연동을 뺀 이식.
/// UTF-8(BOM·줄바꿈 종류는 저장할 때 그대로 되돌림), 구문 강조(TextMate), 저장·다른 이름으로 저장, 닫을 때 저장 여부 확인,
/// 찾기·바꾸기(대소문자·정규식), 줄 이동. 바이너리이거나 UTF-8이 아닌 파일은 읽기 전용, 5MB 를 넘는 파일은 열지 않는다.
/// Ctrl+S 저장 · Ctrl+Shift+S 다른 이름으로 · Ctrl+F 찾기 · Ctrl+H 바꾸기 · Ctrl+G 줄 이동 · Esc (찾기가 열려 있으면 찾기만 닫고, 아니면 닫기).
/// </summary>
internal sealed class EditorWindow : Window
{
    /// <summary>이 크기를 넘으면 구문 강조를 끈다 (원본과 같은 4MB).</summary>
    const int HighlightMaxBytes = 4 * 1024 * 1024;

    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, Monaco, DejaVu Sans Mono, monospace");
    string _path;
    EditorTextFile? _file;
    bool _readOnly;
    bool _closeApproved;
    TextMate.Installation? _textMate;
    string _language = "plaintext";

    readonly TextEditor _editor = new();
    readonly Border _menuHost = new();
    MenuItem? _wrapItem;
    ToggleButton? _wrapToggle;
    readonly TextBlock _title = new();
    readonly Border _banner = new() { IsVisible = false };
    readonly TextBlock _positionText = new();
    readonly TextBlock _infoText = new();
    readonly TextBlock _notice = new() { IsVisible = false };

    // 찾기 바
    readonly Border _findBar = new() { IsVisible = false };
    readonly TextBox _findBox = new();
    readonly TextBox _replaceBox = new();
    readonly Grid _replaceRow = new() { IsVisible = false };
    readonly TextBlock _matchCount = new();
    readonly ToggleButton _matchCase = new() { Classes = { "tool" } };
    readonly ToggleButton _regex = new() { Classes = { "tool" } };
    IReadOnlyList<TextMatch> _matches = [];

    static bool _stylesLoaded;

    /// <summary>AvaloniaEdit 기본 스타일은 에디터를 처음 열 때만 불러온다(앱 시작 시간에 영향을 주지 않으려고 App.axaml 에 넣지 않는다).</summary>
    static void EnsureStyles()
    {
        if (_stylesLoaded || Application.Current is not { } app) return;
        _stylesLoaded = true;
        app.Styles.Add(new EditorStyles());
    }

    public EditorWindow(string path)
    {
        EnsureStyles();
        _path = path;
        Width = 1000;
        Height = 700;
        MinWidth = 520;
        MinHeight = 320;
        UpdateTitle();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
        var bar = BuildBar();
        var banner = BuildBanner();
        Grid.SetRow(bar, 0);
        Grid.SetRow(banner, 1);
        Grid.SetRow(_menuHost, 2);
        Grid.SetRow(_findBar, 3);
        var statusBar = BuildStatusBar();
        Grid.SetRow(statusBar, 5);
        BuildFindBar();
        BuildMenu();
        ConfigureEditor();
        var body = BuildBody();
        Grid.SetRow(body, 4);
        root.Children.Add(bar);
        root.Children.Add(banner);
        root.Children.Add(_menuHost);
        root.Children.Add(_findBar);
        root.Children.Add(body);
        root.Children.Add(statusBar);
        Content = root;

        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) => _textMate?.Dispose();
        Opened += async (_, _) => await LoadAsync();
    }

    // --- 화면 만들기 ---

    static Button IconButton(string iconKey, string tip, Action click)
    {
        var icon = new Icons.DuoIcon { Icon = iconKey };
        var button = new Button { Classes = { "tool" }, Content = icon };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    static ToggleButton IconToggle(string iconKey, string tip, Action<bool> changed)
    {
        var icon = new Icons.DuoIcon { Icon = iconKey };
        var button = new ToggleButton { Classes = { "tool" }, Content = icon };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => changed(button.IsChecked == true);
        return button;
    }

    Control BuildBar()
    {
        var s = Strings.Current;
        _title.FontWeight = FontWeight.Bold;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(IconButton("undo", s.EditorUndo, () => _editor.Undo()));
        buttons.Children.Add(IconButton("redo", s.EditorRedo, () => _editor.Redo()));
        buttons.Children.Add(IconButton("search", s.EditorFind, () => ShowFind(replace: false)));
        buttons.Children.Add(IconButton("replace", s.EditorReplace, () => ShowFind(replace: true)));
        buttons.Children.Add(IconButton("goto-line", s.EditorGoToLine, () => _ = GoToLineAsync()));
        _wrapToggle = IconToggle("wrap", s.EditorWordWrap, on => SetWordWrap(on));
        buttons.Children.Add(_wrapToggle);
        buttons.Children.Add(IconButton("save-as", s.EditorSaveAs, () => _ = SaveAsPromptAsync())); // 원격 파일은 다른 이름으로 저장하지 않는다
        buttons.Children.Add(IconButton("save", s.EditorSave, () => _ = SaveAsync()));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(_title);
        grid.Children.Add(buttons);
        var bar = UiScale.Enable(new Border { Height = 44, Padding = new Thickness(16, 0, 8, 0), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        return bar;
    }

    Control BuildBody() => _editor;

    void SetWordWrap(bool on)
    {
        _editor.WordWrap = on;
        if (_wrapToggle is not null) _wrapToggle.IsChecked = on;
        if (_wrapItem is not null) _wrapItem.IsChecked = on;
    }

    /// <summary>메뉴 막대: 파일 · 편집 · 찾기 · 보기. 각 항목 옆에 단축키가 보인다 (표시용 — 키 처리는 창과 에디터가 한다).</summary>
    void BuildMenu()
    {
        var s = Strings.Current;
        MenuItem Item(string header, string? gesture, Action click)
        {
            var item = new MenuItem { Header = header };
            if (gesture is not null) item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => click();
            return item;
        }
        MenuItem Top(string header, IEnumerable<Control> items)
        {
            var top = new MenuItem { Header = header };
            foreach (var i in items) top.Items.Add(i);
            return top;
        }
        MenuItem TopOf(string header, params Control[] items) => Top(header, items);
        static Separator Sep() => new();

        _wrapItem = new MenuItem { Header = s.EditorWordWrap, ToggleType = MenuItemToggleType.CheckBox };
        _wrapItem.Click += (_, _) => SetWordWrap(_wrapItem.IsChecked);
        var fileItems = new List<Control>
        {
            Item(s.EditorSave, "Ctrl+S", () => _ = SaveAsync()),
            Item(s.EditorSaveAs, "Ctrl+Shift+S", () => _ = SaveAsPromptAsync()),
        };
        fileItems.Add(Sep());
        fileItems.Add(Item(s.EditorScClose, "Escape", Close));

        var menu = new Menu
        {
            Items =
            {
                Top(s.EditorGroupFile, fileItems),
                TopOf(s.EditorGroupEdit,
                    Item(s.EditorUndo, "Ctrl+Z", () => _editor.Undo()),
                    Item(s.EditorRedo, "Ctrl+Y", () => _editor.Redo()),
                    Sep(),
                    Item(s.EditorCut, "Ctrl+X", () => _editor.Cut()),
                    Item(s.EditorCopy, "Ctrl+C", () => _editor.Copy()),
                    Item(s.EditorPaste, "Ctrl+V", () => _editor.Paste()),
                    Sep(),
                    Item(s.EditorScSelectAll, "Ctrl+A", () => _editor.SelectAll()),
                    Item(s.EditorScDeleteLine, "Ctrl+D", DeleteLine)),
                TopOf(s.EditorGroupFind,
                    Item(s.EditorFind, "Ctrl+F", () => ShowFind(replace: false)),
                    Item(s.EditorReplace, "Ctrl+H", () => ShowFind(replace: true)),
                    Item(s.EditorGoToLine, "Ctrl+G", () => _ = GoToLineAsync()),
                    Sep(),
                    Item(s.EditorNextMatch, null, () => GoToMatch(true, false)),
                    Item(s.EditorPrevMatch, null, () => GoToMatch(false, false))),
                TopOf(s.EditorMenuView, _wrapItem),
            },
        };
        _menuHost.BorderThickness = new Thickness(0, 0, 0, 1);
        _menuHost.Padding = new Thickness(8, 0);
        _menuHost.Child = menu;
        _menuHost.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        _menuHost.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
    }

    /// <summary>커서가 있는 줄을 지운다 (Ctrl+D). 에디터 자체의 같은 키와 같은 일을 한다.</summary>
    void DeleteLine()
    {
        if (_readOnly || _editor.Document is not { } doc) return;
        var line = doc.GetLineByNumber(_editor.TextArea.Caret.Line);
        doc.Remove(line.Offset, line.TotalLength);
    }

    Control BuildBanner()
    {
        var text = new TextBlock { Text = Strings.Current.EditorReadOnlyBinary, TextWrapping = TextWrapping.Wrap };
        text.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppDanger"));
        _banner.Padding = new Thickness(16, 8);
        _banner.Child = text;
        _banner.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        return _banner;
    }

    Control BuildStatusBar()
    {
        foreach (var t in new[] { _positionText, _infoText, _notice })
        {
            t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
            t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
            t.VerticalAlignment = VerticalAlignment.Center;
        }
        _notice.Margin = new Thickness(16, 0, 0, 0);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { _positionText, _notice } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_infoText, 1);
        grid.Children.Add(left);
        grid.Children.Add(_infoText);
        var bar = new Border { Padding = new Thickness(16, 6), BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        bar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurface"));
        bar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
        return bar;
    }

    void BuildFindBar()
    {
        var s = Strings.Current;
        _findBox.PlaceholderText = s.EditorFindHint;
        _findBox.MinWidth = 220;
        _findBox.TextChanged += (_, _) => { RefreshMatches(); GoToMatch(forward: true, fromSelectionStart: true); };
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            GoToMatch(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), fromSelectionStart: false);
            e.Handled = true;
        };

        _matchCount.VerticalAlignment = VerticalAlignment.Center;
        _matchCount.Margin = new Thickness(8, 0);
        _matchCount.MinWidth = 60;
        _matchCount.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));

        _matchCase.Content = new TextBlock { Text = "Aa" };
        ToolTip.SetTip(_matchCase, s.EditorMatchCase);
        _matchCase.Click += (_, _) => { RefreshMatches(); GoToMatch(true, true); };
        _regex.Content = new TextBlock { Text = ".*" };
        ToolTip.SetTip(_regex, s.EditorRegex);
        _regex.Click += (_, _) => { RefreshMatches(); GoToMatch(true, true); };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(_findBox);
        row.Children.Add(_matchCount);
        row.Children.Add(IconButton("up", s.EditorPrevMatch, () => GoToMatch(false, false)));
        row.Children.Add(IconButton("down", s.EditorNextMatch, () => GoToMatch(true, false)));
        row.Children.Add(_matchCase);
        row.Children.Add(_regex);
        row.Children.Add(IconButton("close", s.CommonCancel, HideFind));

        _replaceBox.PlaceholderText = s.EditorReplaceHint;
        _replaceBox.MinWidth = 220;
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ReplaceCurrent();
            e.Handled = true;
        };
        var replace = new Button { Content = s.EditorReplace };
        replace.Click += (_, _) => ReplaceCurrent();
        var replaceAll = new Button { Content = s.EditorReplaceAll };
        replaceAll.Click += (_, _) => ReplaceAll();
        _replaceRow.ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto");
        _replaceRow.ColumnSpacing = 8;
        _replaceRow.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetColumn(replace, 1);
        Grid.SetColumn(replaceAll, 2);
        _replaceRow.Children.Add(_replaceBox);
        _replaceRow.Children.Add(replace);
        _replaceRow.Children.Add(replaceAll);

        _findBar.Padding = new Thickness(12, 8);
        _findBar.BorderThickness = new Thickness(0, 0, 0, 1);
        _findBar.Child = new StackPanel { Children = { row, _replaceRow } };
        _findBar.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppSurfaceHi"));
        _findBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppStroke"));
    }

    void ConfigureEditor()
    {
        _editor.FontFamily = Mono;
        _editor.Bind(TextEditor.FontSizeProperty, this.GetResourceObservable("FsBody"));
        _editor.ShowLineNumbers = true;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Padding = new Thickness(4, 8);
        _editor.Background = Brushes.Transparent;
        _editor.Bind(TextEditor.ForegroundProperty, this.GetResourceObservable("AppTextHi"));
        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdatePosition();
        _editor.TextArea.SelectionChanged += (_, _) => UpdatePosition();
    }

    // --- 불러오기 ---

    async Task LoadAsync()
    {
        var s = Strings.Current;
        EditorLoadResult result;
        try
        {
            result = await TextFiles.LoadAsync(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, Path.GetFileName(_path), s.EditorLoadFailed(e.Message), [s.CommonConfirm], 0, 0);
            _closeApproved = true;
            Close();
            return;
        }
        if (result.File is not { } file)
        {
            await MessageDialog.ShowAsync(this, Path.GetFileName(_path),
                s.EditorTooLarge(Path.GetFileName(_path)), [s.CommonConfirm], 0, 0);
            _closeApproved = true;
            Close();
            return;
        }

        _file = file;
        _readOnly = file.Binary;
        // 편집 화면은 LF만 쓴다 (저장할 때 원래 줄바꿈으로 되돌린다). 읽기 전용 바이너리는 읽은 그대로 보여 준다.
        var text = file.Binary ? file.Text : TextFiles.NormalizeLineBreaks(file.Text);
        _editor.Document = new TextDocument(text);
        _editor.Document.UndoStack.MarkAsOriginalFile();
        _editor.Document.TextChanged += (_, _) =>
        {
            UpdateTitle();
            if (_findBar.IsVisible) RefreshMatches();
        };
        _editor.IsReadOnly = _readOnly;
        _banner.IsVisible = _readOnly;
        ApplyHighlight();
        UpdateInfo();
        UpdatePosition();
        UpdateTitle();
        _editor.Focus();
    }

    /// <summary>파일 이름에 맞는 문법으로 구문 강조를 건다. 큰 파일이거나 문법이 없으면 강조 없이 둔다. 강조가 실패해도 편집은 된다.</summary>
    void ApplyHighlight()
    {
        _notice.IsVisible = false;
        _language = "plaintext";
        _textMate?.Dispose();
        _textMate = null;
        if (_file is null) return;
        if (_file.Length > HighlightMaxBytes)
        {
            _notice.Text = Strings.Current.EditorHighlightOff;
            _notice.IsVisible = true;
            return;
        }
        if (EditorLanguages.ExtensionFor(Path.GetFileName(_path)) is not { } ext) return;
        try
        {
            var options = new RegistryOptions(ThemeService.IsDark(Application.Current!) ? ThemeName.DarkPlus : ThemeName.LightPlus);
            var language = options.GetLanguageByExtension(ext);
            if (language is null) return;
            _textMate = _editor.InstallTextMate(options);
            _textMate.SetGrammar(options.GetScopeByLanguageId(language.Id));
            _language = language.Id;
            // 글자 색(토큰)은 강조 테마를 따르되, 바탕과 기본 글자색은 앱 색 구성을 따른다.
            _editor.Background = Brushes.Transparent;
        }
        catch (Exception)
        {
            // 강조 엔진이 못 뜨는 환경에서도 에디터는 떠야 한다.
            _textMate?.Dispose();
            _textMate = null;
            _language = "plaintext";
        }
    }

    // --- 상태 표시 ---

    bool IsDirty => !_readOnly && _editor.Document is { } doc && !doc.UndoStack.IsOriginalFile;

    void UpdateTitle()
    {
        var name = Path.GetFileName(_path);
        _title.Text = IsDirty ? $"{name}  ●" : name;
        Title = IsDirty ? $"● {name}" : name;
    }

    void UpdatePosition()
    {
        var s = Strings.Current;
        var text = s.EditorStatusPosition(_editor.TextArea.Caret.Line, _editor.TextArea.Caret.Column);
        if (_editor.SelectionLength > 0) text += "  ·  " + s.EditorStatusSelected(_editor.SelectionLength);
        _positionText.Text = text;
    }

    void UpdateInfo()
    {
        if (_file is null) return;
        var encoding = _file.Binary ? "Latin-1" : _file.HasBom ? "UTF-8 BOM" : "UTF-8";
        var lineBreak = _file.Binary ? "" : _file.LineBreak == "\r\n" ? "  ·  CRLF" : "  ·  LF";
        _infoText.Text = $"{_language}  ·  {encoding}{lineBreak}";
    }

    // --- 키 ---

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = Mods.Primary(e.KeyModifiers);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && e.Key == Key.S)
        {
            if (shift) _ = SaveAsPromptAsync(); else _ = SaveAsync();
        }
        else if (ctrl && e.Key == Key.F) ShowFind(replace: false);
        else if (ctrl && e.Key == Key.H) ShowFind(replace: true);
        else if (ctrl && e.Key == Key.G) _ = GoToLineAsync();
        else if (ctrl && !shift && e.Key == Key.D && _editor.TextArea.IsFocused) DeleteLine(); // 찾기 상자에서 누르면 지우지 않는다
        else if (e.Key == Key.Escape && !ctrl)
        {
            // 찾기가 열려 있으면 찾기만 닫고, 아니면 에디터를 닫는다.
            if (_findBar.IsVisible) HideFind(); else Close();
        }
        else return;
        e.Handled = true;
    }

    // --- 저장 ---

    async Task<bool> SaveAsync(string? asPath = null)
    {
        var s = Strings.Current;
        if (_file is null || _readOnly) return false;
        var target = asPath ?? _path;
        try
        {
            if (asPath is null && TextFiles.ChangedOnDisk(target, _file))
            {
                var choice = await MessageDialog.ShowAsync(this, s.EditorExternalChangedTitle, s.EditorExternalChangedBody,
                    [s.EditorOverwrite, s.CommonCancel], 1, 1);
                if (choice != 0) return false;
            }
            _file = await TextFiles.SaveAsync(target, _editor.Document.Text, _file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, Path.GetFileName(target), s.EditorSaveFailed(e.Message), [s.CommonConfirm], 0, 0);
            return false;
        }

        _editor.Document.UndoStack.MarkAsOriginalFile();
        if (asPath is not null)
        {
            _path = target;
            ApplyHighlight(); // 이름이 바뀌었으니 문법도 다시 정한다
            UpdateInfo();
        }
        UpdateTitle();
        return true;
    }

    /// <summary>다른 이름으로 저장: 경로를 입력받는다(이름만 쓰면 같은 폴더). 이미 있으면 덮어쓸지 묻고, 저장한 뒤에는 새 파일을 계속 편집한다.</summary>
    Task SaveAsPromptAsync()
    {
        var s = Strings.Current;
        if (_file is null || _readOnly) return Task.CompletedTask;
        var dialog = new TextPromptDialog(s.EditorSaveAs, s.EditorSaveAsHint, _path, 0, _path.Length, s.EditorSave);
        dialog.Confirmed += async text =>
        {
            var input = text.Trim();
            if (input.Length == 0) return;
            string target;
            try
            {
                target = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(Path.GetDirectoryName(_path) ?? "", input));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                dialog.SetError(s.EditorSaveFailed(e.Message));
                return;
            }
            if (File.Exists(target) && !string.Equals(target, _path, StringComparison.OrdinalIgnoreCase))
            {
                var choice = await MessageDialog.ShowAsync(dialog, s.EditorSaveAs, s.EditorOverwriteExistingBody(Path.GetFileName(target)),
                    [s.EditorOverwrite, s.CommonCancel], 1, 1);
                if (choice != 0) return;
            }
            dialog.CloseFromState();
            await SaveAsync(target);
        };
        _ = dialog.ShowDialog(this);
        return Task.CompletedTask;
    }

    // --- 닫기 ---

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || !IsDirty) return;
        e.Cancel = true; // 저장 여부를 묻는 동안 닫기를 미룬다
        _ = ConfirmCloseAsync();
    }

    async Task ConfirmCloseAsync()
    {
        var s = Strings.Current;
        var choice = await MessageDialog.ShowAsync(this, s.EditorUnsavedTitle, s.EditorUnsavedBody(Path.GetFileName(_path)),
            [s.EditorSave, s.EditorDontSave, s.CommonCancel], 0, 2);
        if (choice == 2) return;
        if (choice == 0 && !await SaveAsync()) return;
        _closeApproved = true;
        Close();
    }

    // --- 찾기·바꾸기·줄 이동 ---

    void ShowFind(bool replace)
    {
        _findBar.IsVisible = true;
        _replaceRow.IsVisible = replace && !_readOnly;
        // 선택한 글이 있으면 그것을 검색어로 쓴다.
        if (_editor.SelectionLength > 0 && !_editor.SelectedText.Contains('\n')) _findBox.Text = _editor.SelectedText;
        _findBox.Focus();
        _findBox.SelectAll();
        RefreshMatches();
    }

    void HideFind()
    {
        _findBar.IsVisible = false;
        _editor.Focus();
    }

    void RefreshMatches()
    {
        var query = _findBox.Text ?? "";
        _matches = TextSearch.FindAll(_editor.Document.Text, query, _matchCase.IsChecked == true, _regex.IsChecked == true);
        UpdateMatchCount();
    }

    void UpdateMatchCount()
    {
        var s = Strings.Current;
        if ((_findBox.Text ?? "").Length == 0) { _matchCount.Text = ""; return; }
        if (_matches.Count == 0) { _matchCount.Text = s.EditorNoMatches; return; }
        var current = -1;
        for (var i = 0; i < _matches.Count; i++)
            if (_matches[i].Index == _editor.SelectionStart && _matches[i].Length == _editor.SelectionLength) { current = i; break; }
        _matchCount.Text = s.EditorMatchCount(current + 1, _matches.Count);
    }

    /// <summary>다음·이전 결과로 가서 선택한다. 끝에 닿으면 반대편으로 돌아간다.</summary>
    void GoToMatch(bool forward, bool fromSelectionStart)
    {
        if (_matches.Count == 0) { UpdateMatchCount(); return; }
        var from = forward
            ? (fromSelectionStart ? _editor.SelectionStart : _editor.SelectionStart + _editor.SelectionLength)
            : _editor.SelectionStart;
        var index = TextSearch.IndexOfNext(_matches, from, forward);
        if (index < 0) return;
        var match = _matches[index];
        _editor.Select(match.Index, match.Length);
        var location = _editor.Document.GetLocation(match.Index);
        _editor.ScrollTo(location.Line, location.Column);
        UpdateMatchCount();
    }

    void ReplaceCurrent()
    {
        if (_readOnly) return;
        var query = _findBox.Text ?? "";
        var matchCase = _matchCase.IsChecked == true;
        var regex = _regex.IsChecked == true;
        // 지금 선택이 결과 하나와 정확히 같을 때만 바꾸고, 아니면 다음 결과로 간다.
        var current = _matches.FirstOrDefault(m => m.Index == _editor.SelectionStart && m.Length == _editor.SelectionLength);
        if (current.Length > 0)
        {
            var text = TextSearch.ReplacementFor(_editor.Document.Text, current, query, _replaceBox.Text ?? "", matchCase, regex);
            _editor.Document.Replace(current.Index, current.Length, text);
            RefreshMatches();
            _editor.Select(current.Index + text.Length, 0);
        }
        GoToMatch(forward: true, fromSelectionStart: true);
    }

    void ReplaceAll()
    {
        if (_readOnly || _matches.Count == 0) return;
        var doc = _editor.Document;
        var replaced = TextSearch.ReplaceAll(doc.Text, _findBox.Text ?? "", _replaceBox.Text ?? "", _matchCase.IsChecked == true, _regex.IsChecked == true);
        if (replaced == doc.Text) return;
        doc.Replace(0, doc.TextLength, replaced); // 한 번에 바꿔서 실행 취소도 한 번이다
        RefreshMatches();
    }

    Task GoToLineAsync()
    {
        var s = Strings.Current;
        var max = _editor.Document.LineCount;
        var dialog = new TextPromptDialog(s.EditorGoToLine, s.EditorGoToLineHint(max), _editor.TextArea.Caret.Line.ToString(), 0,
            _editor.TextArea.Caret.Line.ToString().Length, s.CommonConfirm);
        dialog.Confirmed += text =>
        {
            if (!int.TryParse(text.Trim(), out var line) || line < 1 || line > max)
            {
                dialog.SetError(s.EditorGoToLineHint(max));
                return;
            }
            dialog.CloseFromState();
            _editor.TextArea.Caret.Line = line;
            _editor.TextArea.Caret.Column = 1;
            _editor.ScrollTo(line, 1);
            Dispatcher.UIThread.Post(() => _editor.Focus());
        };
        _ = dialog.ShowDialog(this);
        return Task.CompletedTask;
    }
}
