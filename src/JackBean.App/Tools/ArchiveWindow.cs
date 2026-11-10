using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using JackBean.Domain;
using JackBean.Services;
using JackBean.Services.Archive;

namespace JackBean.App.Tools;

/// <summary>
/// 내장 압축 도구 창. 하나의 .zip 을 놓으면 **풀기**(내용 목록 · 풀 위치 · [압축 풀기]), 그 밖의 것(파일·폴더 하나 또는 여럿)을 놓으면
/// **압축하기**(이름 · 저장 위치 · [압축하기])다. 이미 있는 파일은 덮어쓰지 않고 멈춘다. 창을 닫으면 진행 중인 작업을 취소한다.
/// </summary>
internal sealed class ArchiveWindow : Window
{
    const int ListMax = 500;

    readonly ArchiveService _archive = new();
    readonly CancellationTokenSource _cts = new();
    readonly IReadOnlyList<string> _paths;
    readonly bool _extractMode;

    readonly TextBox _destBox = new();
    readonly TextBox _nameBox = new();
    readonly Button _action = new() { Classes = { "accent" } };
    readonly Button _browse = new();
    readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _openFolder = new() { IsVisible = false };
    string? _resultFolder;

    public ArchiveWindow(IReadOnlyList<string> paths)
    {
        _paths = paths;
        _extractMode = paths.Count == 1 && ArchiveService.IsZip(paths[0]) && File.Exists(paths[0]);
        var s = Strings.Current;
        Title = WindowTitle.Of(_extractMode ? Path.GetFileName(paths[0]) : s.ArchiveCompressTitle);
        Width = 560;
        Height = _extractMode ? 560 : 520;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = Text(_extractMode ? Path.GetFileName(paths[0]) : s.ArchiveCompressTitle, bold: true, titleSize: true);
        DockPanel.SetDock(heading, Dock.Top);
        heading.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(heading);

        var bottom = BuildBottom();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        root.Children.Add(_extractMode ? BuildExtractTop() : BuildCompressTop());
        Content = root;

        _action.Click += async (_, _) => await (_extractMode ? ExtractAsync() : CompressAsync());
        _browse.Click += async (_, _) => await BrowseAsync();
        _openFolder.Click += (_, _) => OpenResultFolder();
        Closing += (_, _) => _cts.Cancel();
        Closed += (_, _) => _cts.Dispose();
        if (_extractMode) Opened += async (_, _) => await LoadListingAsync();
    }

    // --- 화면 ---

    TextBlock Text(string text, bool bold = false, bool titleSize = false, bool small = false, bool mid = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeight.Bold;
        if (titleSize) t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        else if (small) t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        if (mid) t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        return t;
    }

    ListBox BuildList(IEnumerable<string> items)
    {
        var list = new ListBox { ItemsSource = items.ToList(), MinHeight = 80 };
        list.Classes.Add("user");   // 파일 이름은 시스템 글꼴(fonts 규약)
        return list;
    }

    readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap };
    ListBox? _entries;
    readonly Border _listHost = new();

    Control BuildExtractTop()
    {
        var s = Strings.Current;
        _info.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        _info.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _info.Text = "";
        _destBox.Text = Path.Combine(Path.GetDirectoryName(_paths[0]) ?? "", ArchiveService.ExtractFolderName(Path.GetFileName(_paths[0])));
        _info.Margin = new Thickness(0, 0, 0, 8);
        var top = new DockPanel();
        DockPanel.SetDock(_info, Dock.Top);
        top.Children.Add(_info);
        top.Children.Add(_listHost);
        _action.Content = s.ArchiveExtract;
        _browse.Content = s.ArchiveBrowse;
        return top;
    }

    Control BuildCompressTop()
    {
        var s = Strings.Current;
        var info = Text(s.ArchiveItemCount(_paths.Count), small: true, mid: true);
        info.Margin = new Thickness(0, 0, 0, 8);
        var top = new DockPanel();
        DockPanel.SetDock(info, Dock.Top);
        top.Children.Add(info);
        top.Children.Add(BuildList(_paths.Select(p => Path.GetFileName(p.TrimEnd((char)47, (char)92)) is { Length: > 0 } n ? n : p)));   // 이름만(저장 위치는 아래 칸)
        _nameBox.Text = ArchiveService.DefaultZipName(_paths);
        _destBox.Text = Path.GetDirectoryName(_paths[0].TrimEnd('/', '\\')) ?? "";
        _action.Content = s.ArchiveCompress;
        _browse.Content = s.ArchiveBrowse;
        return top;
    }

    Control BuildBottom()
    {
        var s = Strings.Current;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        if (!_extractMode)
        {
            panel.Children.Add(Text(s.ArchiveZipName, small: true, mid: true));
            panel.Children.Add(_nameBox);
        }
        panel.Children.Add(Text(_extractMode ? s.ArchiveExtractTo : s.ArchiveSaveIn, small: true, mid: true));
        var destRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(_browse, 1);
        destRow.Children.Add(_destBox);
        destRow.Children.Add(_browse);
        panel.Children.Add(destRow);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { _action, _openFolder } };
        _openFolder.Content = s.ArchiveOpenFolder;
        _status.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsSmall"));
        panel.Children.Add(buttons);
        panel.Children.Add(_progress);
        panel.Children.Add(_status);
        return panel;
    }

    // --- 풀기: 내용 목록 ---

    async Task LoadListingAsync()
    {
        var s = Strings.Current;
        try
        {
            var info = await _archive.InspectAsync(_paths[0], ListMax, _cts.Token);
            _info.Text = s.ArchiveListingInfo(info.FileCount, info.FolderCount, FormatSize(info.TotalSize))
                + (info.FileCount + info.FolderCount > ListMax ? "  ·  " + s.ArchiveListTruncated(ListMax) : "");
            _entries = BuildList(info.Entries);
            _listHost.Child = _entries;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _info.Text = "";
            _action.IsEnabled = false;
            ShowError(s.ArchiveBroken(e.Message));
        }
    }

    // --- 동작 ---

    async Task BrowseAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) _destBox.Text = path;
    }

    void Busy(bool busy, string? message = null)
    {
        _progress.IsVisible = busy;
        _action.IsEnabled = !busy;
        _browse.IsEnabled = !busy;
        _destBox.IsEnabled = !busy;
        _nameBox.IsEnabled = !busy;
        _openFolder.IsVisible = false;
        _status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        _status.Text = message ?? "";
    }

    void ShowError(string message)
    {
        _progress.IsVisible = false;
        _status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppDanger"));
        _status.Text = message;
    }

    void ShowDone(string message, string folder)
    {
        Busy(false);
        _resultFolder = folder;
        _status.Text = message;
        _openFolder.IsVisible = true;
    }

    string Describe(Exception e)
    {
        var s = Strings.Current;
        return e switch
        {
            ArchiveException { Kind: ArchiveErrorKind.AlreadyExists } a => s.ArchiveErrAlreadyExists(a.Detail),
            ArchiveException { Kind: ArchiveErrorKind.UnsafePath } a => s.ArchiveErrUnsafe(a.Detail),
            ArchiveException { Kind: ArchiveErrorKind.BadName } => s.ArchiveErrBadName,
            ArchiveException { Kind: ArchiveErrorKind.NothingToAdd } => s.ArchiveErrNothing,
            _ => s.ArchiveFailed(e.Message),
        };
    }

    async Task ExtractAsync()
    {
        var s = Strings.Current;
        var dest = (_destBox.Text ?? "").Trim();
        if (dest.Length == 0) { ShowError(s.ArchiveErrNoFolder); return; }
        Busy(true, s.ArchiveExtracting);
        try
        {
            var folder = await _archive.ExtractAsync(_paths[0], dest, _cts.Token);
            ShowDone(s.ArchiveExtracted(folder), folder);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is ArchiveException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            Busy(false);
            ShowError(Describe(e));
        }
    }

    async Task CompressAsync()
    {
        var s = Strings.Current;
        var dir = (_destBox.Text ?? "").Trim();
        var name = (_nameBox.Text ?? "").Trim();
        if (dir.Length == 0) { ShowError(s.ArchiveErrNoFolder); return; }
        if (name.Length > 0 && !ArchiveService.IsZip(name)) name += ".zip";   // .zip 은 자동으로 붙는다
        Busy(true, s.ArchiveCompressing);
        try
        {
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, name);
            await _archive.CompressAsync(_paths, zip, _cts.Token);
            ShowDone(s.ArchiveCompressed(zip), dir);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is ArchiveException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Busy(false);
            ShowError(Describe(e));
        }
    }

    void OpenResultFolder()
    {
        if (_resultFolder is null) return;
        try { new OpenWithDefaultApp().Open(_resultFolder); }
        catch (Exception e) { ShowError(Strings.Current.ArchiveFailed(e.Message)); }
    }

    static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.#} GB",
    };
}
