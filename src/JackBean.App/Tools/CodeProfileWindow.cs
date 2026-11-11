using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JackBean.App.Icons;
using JackBean.App.Localization;
using JackBean.App.Notifications;
using JackBean.Domain.CodeProfiles;
using JackBean.Services.CodeProfiles;

namespace JackBean.App.Tools;

/// <summary>
/// VS Code 프로필 도구 창: 분석 결과 → 추천 편집 → 설정 비교 → 적용(→ VS Code 로 열기). 상태와 전이는 <see cref="CodeProfileFlow"/> 가 갖고,
/// 이 창은 그것을 읽어 그리고 버튼을 이어 줄 뿐이다. 상태가 바뀔 때마다 본문을 다시 만든다(프로필 이름을 치는 동안만 빼고 — 포커스를 잃지 않게).
/// 적용하는 동안(VS Code 프로세스가 도는 중)에는 창을 닫지 못한다.
/// </summary>
internal sealed class CodeProfileWindow : Window
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly CodeProfileFlow _flow;
    readonly DockPanel _root = new() { Margin = new Thickness(24) };
    readonly StackPanel _stepBar = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
    readonly Border _body = new();
    readonly StackPanel _footer = new() { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
    bool _typing;   // 프로필 이름을 치는 중 — 본문을 다시 만들면 포커스를 잃는다

    public CodeProfileWindow(CodeProfileFlow flow)
    {
        _flow = flow;
        var s = Strings.Current;
        Title = WindowTitle.Of(s.CodeToolName);
        Width = 760;
        Height = 700;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var heading = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 10) };
        var name = Path.GetFileName(flow.Folder.TrimEnd('/', '\\'));
        heading.Children.Add(Text(name.Length > 0 ? name : flow.Folder, bold: true, size: "FsTitle", user: true));
        heading.Children.Add(Text(flow.Folder, size: "FsSmall", mid: true, user: true));
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(_stepBar, Dock.Top);
        DockPanel.SetDock(_footer, Dock.Bottom);
        _root.Children.Add(heading);
        _root.Children.Add(_stepBar);
        _root.Children.Add(_footer);
        _root.Children.Add(_body);
        Content = _root;

        _flow.Changed += OnChanged;
        Opened += async (_, _) => await _flow.AnalyzeAsync();
        Closing += (_, e) =>
        {
            if (_flow.CanClose) return;
            if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) { _flow.Cancel(); return; }
            e.Cancel = true;   // 적용 중에는 닫지 못한다(중단 버튼은 따로 있다)
            Snackbar.Info(Strings.Current.CodeApplyRunning);
        };
        Closed += (_, _) => _flow.Changed -= OnChanged;
        Refresh();
    }

    void OnChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnChanged); return; }
        if (_typing) RefreshChrome(); else Refresh();
    }

    // ───── 공통 부품 ─────

    TextBlock Text(string text, bool bold = false, string? size = null, bool mid = false, bool mono = false, bool user = false, bool danger = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeight.Bold;
        if (size != null) t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable(size));
        if (mid) t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        if (danger) t.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppDanger"));
        if (mono) t.Classes.Add("mono");
        if (user) t.Classes.Add("user");
        return t;
    }

    SelectableTextBlock Block(string text, string? size = "FsSmall")
    {
        var t = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.Classes.Add("mono");
        if (size != null) t.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable(size));
        return t;
    }

    Border Card(Control child, bool danger = false)
    {
        var card = new Border { Child = child, Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable(danger ? "AppDanger" : "AppStroke"));
        return card;
    }

    static Control Section(Control title, params Control[] content)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(title);
        foreach (var c in content) panel.Children.Add(c);
        return panel;
    }

    Control Heading(string text) => Text(text, bold: true, size: "FsMid");

    Button Primary(string text, Func<Task> run, bool enabled = true, string? icon = null)
    {
        var b = new Button { Content = WithIcon(text, icon), IsEnabled = enabled };
        b.Classes.Add("accent");
        b.Click += async (_, _) => await run();
        return b;
    }

    Button Plain(string text, Func<Task> run, bool enabled = true, string? icon = null)
    {
        var b = new Button { Content = WithIcon(text, icon), IsEnabled = enabled };
        b.Click += async (_, _) => await run();
        return b;
    }

    static object WithIcon(string text, string? icon) => icon == null
        ? text
        : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new DuoIcon { Icon = icon }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };

    // ───── 다시 그리기 ─────

    void Refresh()
    {
        BuildStepBar();
        _body.Child = _flow.Step switch
        {
            FlowStep.Analysis => BuildAnalysis(),
            FlowStep.Recommendation => BuildRecommendation(),
            FlowStep.Diff => BuildDiff(),
            _ => BuildApply(),
        };
        BuildFooter();
    }

    /// <summary>이름을 치는 동안: 본문은 그대로 두고 이름 검사 문구와 버튼만 고친다.</summary>
    void RefreshChrome()
    {
        _nameProblem?.SetCurrentValue(TextBlock.TextProperty, NameProblemText() ?? "");
        BuildFooter();
    }

    static readonly FlowStep[] Steps = [FlowStep.Analysis, FlowStep.Recommendation, FlowStep.Diff, FlowStep.Apply];

    void BuildStepBar()
    {
        var s = Strings.Current;
        _stepBar.Children.Clear();
        string[] names = [s.CodeStepAnalysis, s.CodeStepRecommend, s.CodeStepDiff, s.CodeStepApply];
        var current = _flow.Step == FlowStep.Done ? FlowStep.Apply : _flow.Step;
        for (var i = 0; i < Steps.Length; i++)
        {
            var active = Steps[i] == current;
            var chip = new Border { Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
            chip.Bind(Border.BorderBrushProperty, this.GetResourceObservable(active ? "AppPrimary" : "AppStroke"));
            var label = Text($"{i + 1}  {names[i]}", bold: active, size: "FsSmall", mid: !active);
            if (active) label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppPrimary"));
            chip.Child = label;
            _stepBar.Children.Add(chip);
            if (i < Steps.Length - 1) _stepBar.Children.Add(new DuoIcon { Icon = "arrow-right", ColorKey = "AppTextMid", Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center });
        }
    }

    ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };

    // ───── 1. 분석 결과 ─────

    Control BuildAnalysis()
    {
        var s = Strings.Current;
        var panel = new StackPanel { Spacing = 10 };
        if (_flow.IsBusy && _flow.Analysis == null)
        {
            panel.Children.Add(new ProgressBar { IsIndeterminate = true });
            panel.Children.Add(Text(s.CodeAnalysisRunning, mid: true));
            return panel;
        }
        if (_flow.Analysis is not { } a)
        {
            if (_flow.Error != null) panel.Children.Add(Text($"{s.CodeAnalysisFailed}: {_flow.Error}", danger: true));
            return panel;
        }

        var result = a.Analysis;
        if (result.Types.Count == 0) panel.Children.Add(Card(new StackPanel { Spacing = 4, Children = { Text(s.CodeAnalysisNoTypes), Text(s.CodeNoTypesHint, size: "FsSmall", mid: true) } }));
        for (var i = 0; i < result.Types.Count; i++)
        {
            var t = result.Types[i];
            var list = new StackPanel { Spacing = 3 };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(Text(t.Label, bold: true));
            if (i == 0) title.Children.Add(Text(s.CodePrimaryBadge, size: "FsSmall", mid: true));
            title.Children.Add(Text(s.CodePriority(t.Priority), size: "FsSmall", mid: true));
            list.Children.Add(title);
            foreach (var ev in t.Evidence)
                list.Children.Add(Text("· " + ev.Description + (ev.RelativePath is { Length: > 0 } rp ? $"  ({rp})" : ""), size: "FsSmall", mid: true, user: true));
            panel.Children.Add(Card(list));
        }

        if (a.Competitors.Count > 0)
            panel.Children.Add(Card(new StackPanel
            {
                Spacing = 3,
                Children = { Text(s.CodeCompetitorsTitle, bold: true), Text(s.CodeCompetitorsBody(string.Join(", ", a.Competitors.Select(c => c.Label))), size: "FsSmall", mid: true) },
            }));

        var stats = new StackPanel { Spacing = 3 };
        stats.Children.Add(Text(s.CodeScanStats(result.Stats.FileCount), size: "FsSmall", mid: true));
        if (result.Stats.Truncated) stats.Children.Add(Text(s.CodeScanTruncated, size: "FsSmall", danger: true));
        if (result.Stats.LargeDirsPresent.Count > 0) stats.Children.Add(Text(s.CodeSkippedDirs(string.Join(", ", result.Stats.LargeDirsPresent)), size: "FsSmall", mid: true));
        panel.Children.Add(stats);

        if (result.Variables.Count > 0)
            panel.Children.Add(Section(Text(s.CodeVariables, bold: true), Block(string.Join("\n", result.Variables.Select(kv => $"{kv.Key} = {kv.Value}")))));

        return Scroll(panel);
    }

    // ───── 2. 추천 편집 ─────

    TextBox? _nameBox;
    TextBlock? _nameProblem;

    string? NameProblemText()
    {
        var s = Strings.Current;
        return _flow.NameProblem switch
        {
            ProfileNameProblem.Empty => s.CodeNameEmpty,
            ProfileNameProblem.TooLong => s.CodeNameTooLong(ProfileNames.MaxLength),
            ProfileNameProblem.UnsafeCharacter => s.CodeNameInvalid,
            _ => null,
        };
    }

    Control BuildRecommendation()
    {
        var s = Strings.Current;
        var panel = new StackPanel { Spacing = 4 };
        if (_flow.Recommendation is not { } rec || _flow.Analysis is not { } a)
        {
            panel.Children.Add(Text(s.CodeNothingToRecommend, mid: true));
            return panel;
        }

        // 주 유형: 둘 이상 판정됐을 때만 고른다(다른 프로필을 쓰는 유형이 주 유형이 되면 프로필·확장이 바뀐다).
        if (a.Analysis.Types.Count > 1)
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var t in a.Analysis.Types)
            {
                var id = t.TypeId;
                var chip = new ToggleButton { Content = t.Label, IsChecked = id == _flow.PrimaryTypeId, Margin = new Thickness(0, 0, 6, 6) };
                chip.Classes.Add("chip");
                chip.Click += (_, _) => { if (!_flow.SelectPrimary(id)) chip.IsChecked = id == _flow.PrimaryTypeId; };
                chips.Children.Add(chip);
            }
            panel.Children.Add(Section(Heading(s.CodePrimaryType), chips));
        }

        _nameBox = new TextBox { Text = _flow.ProfileName, MaxLength = 200 };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_nameBox.Text == _flow.ProfileName) return;
            _typing = true;
            try { _flow.SetProfileName(_nameBox.Text ?? ""); }
            finally { _typing = false; }
        };
        _nameProblem = Text(NameProblemText() ?? "", size: "FsSmall", danger: true);
        panel.Children.Add(Section(Heading(s.CodeProfileName), _nameBox, Text(s.CodeProfileNameHelp, size: "FsSmall", mid: true), _nameProblem));

        var required = _flow.Extensions.Where(e => !e.Optional).ToList();
        var optional = _flow.Extensions.Where(e => e.Optional).ToList();
        if (required.Count > 0) panel.Children.Add(Section(Heading(s.CodeExtRecommended), ExtensionList(required)));
        if (optional.Count > 0) panel.Children.Add(Section(Heading(s.CodeExtOptional), ExtensionList(optional)));

        var patchText = rec.SettingsPatch.ToJsonString(Indented);
        panel.Children.Add(Section(Heading($"{s.CodeSettingsPatch} · {s.CodeSettingsCount(rec.SettingsPatch.Count)}"), Card(Block(patchText))));

        if (rec.Notes.Count > 0)
            panel.Children.Add(Section(Heading(s.CodeNotes), new StackPanel { Spacing = 3, Children = { Block(string.Join("\n", rec.Notes.Select(n => "· " + n)), "FsSmall") } }));

        return Scroll(panel);
    }

    StackPanel ExtensionList(IReadOnlyList<ExtensionChoice> items)
    {
        var list = new StackPanel { Spacing = 2 };
        foreach (var e in items)
        {
            var id = e.Id;
            var box = new CheckBox { Content = new TextBlock { Text = id, FontFamily = (FontFamily)this.FindResource("AppMonoFont")! }, IsChecked = e.Selected };
            box.IsCheckedChanged += (_, _) => _flow.SetExtension(id, box.IsChecked == true);
            list.Children.Add(box);
        }
        return list;
    }

    // ───── 3. 설정 비교 ─────

    Control BuildDiff()
    {
        var s = Strings.Current;
        var panel = new StackPanel { Spacing = 4 };
        if (_flow.Diff is not { } d) return panel;

        panel.Children.Add(Text(d.ExistingFile ? (d.HasChanges ? "" : s.CodeDiffNoChange) : s.CodeDiffNewFile, mid: true));
        if (d.CommentsWillBeLost) panel.Children.Add(Card(Text(s.CodeDiffComments, danger: true), danger: true));
        panel.Children.Add(new Border { Height = 6 });

        if (d.Added.Count > 0)
            panel.Children.Add(Section(Heading(s.CodeDiffAdded(d.Added.Count)), ChangeRows(d.Added)));

        if (d.Conflicts.Count > 0)
        {
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            head.Children.Add(Heading(s.CodeDiffConflicts(d.Conflicts.Count)));
            var all = Plain(s.CodeDiffReplaceAll, () => _flow.SetAllReplaceAsync(true));
            var none = Plain(s.CodeDiffKeepAll, () => _flow.SetAllReplaceAsync(false));
            Grid.SetColumn(all, 1);
            Grid.SetColumn(none, 2);
            head.Children.Add(all);
            head.Children.Add(none);

            var rows = new StackPanel { Spacing = 6 };
            foreach (var c in d.Conflicts)
            {
                var pointer = c.Pointer;
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(Text(pointer, bold: true, mono: true, size: "FsSmall"));
                row.Children.Add(Text($"{s.CodeDiffExisting}: {c.Existing?.ToJsonString() ?? "null"}", mono: true, size: "FsSmall", mid: true));
                row.Children.Add(Text($"{s.CodeDiffPatch}: {c.Patch?.ToJsonString() ?? "null"}", mono: true, size: "FsSmall", mid: true));
                var box = new CheckBox { Content = s.CodeDiffReplace, IsChecked = c.Replaced, IsEnabled = !_flow.IsBusy };
                box.IsCheckedChanged += async (_, _) => { if ((box.IsChecked == true) != c.Replaced) await _flow.ToggleReplaceAsync(pointer); };
                row.Children.Add(box);
                rows.Children.Add(Card(row));
            }
            panel.Children.Add(Section(head, rows));
        }

        if (d.Unchanged.Count > 0)
        {
            var expander = new Expander { Header = s.CodeDiffUnchanged(d.Unchanged.Count), Content = ChangeRows(d.Unchanged) };
            panel.Children.Add(expander);
        }

        var preview = new Expander { Header = s.CodeDiffPreview, Content = Card(Block(d.MergedJson)), Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(preview);
        return Scroll(panel);
    }

    StackPanel ChangeRows(IReadOnlyList<SettingChange> changes)
    {
        var list = new StackPanel { Spacing = 2 };
        foreach (var c in changes)
            list.Children.Add(Text($"{c.Pointer} = {(c.Patch ?? c.Existing)?.ToJsonString() ?? "null"}", mono: true, size: "FsSmall"));
        return list;
    }

    // ───── 4. 적용 · 끝 ─────

    ScrollViewer? _logScroll;

    string StepName(ApplyStep step)
    {
        var s = Strings.Current;
        return step switch { ApplyStep.Profile => s.CodeStepProfile, ApplyStep.Extensions => s.CodeStepExtensions, _ => s.CodeStepSettings };
    }

    Control BuildApply()
    {
        var s = Strings.Current;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 10 };

        var steps = new StackPanel { Spacing = 6 };
        foreach (var step in Enum.GetValues<ApplyStep>())
        {
            var status = _flow.Status.GetValueOrDefault(step, StepStatus.Pending);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Control mark = status switch
            {
                StepStatus.Done => new DuoIcon { Icon = "check", ColorKey = "AppPrimary" },
                StepStatus.Failed => new DuoIcon { Icon = "warning", ColorKey = "AppDanger" },
                StepStatus.Running => new ProgressBar { IsIndeterminate = true, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center },
                _ => new Border { Width = 18, Height = 18 },
            };
            row.Children.Add(mark);
            row.Children.Add(Text(StepName(step), bold: status == StepStatus.Running, mid: status == StepStatus.Pending, danger: status == StepStatus.Failed));
            steps.Children.Add(row);
        }
        grid.Children.Add(steps);

        var summary = new StackPanel { Spacing = 3 };
        if (_flow.Step == FlowStep.Done)
        {
            summary.Children.Add(Text(s.CodeApplyDone, bold: true, size: "FsMid"));
            summary.Children.Add(Text(_flow.SettingsWritten == true ? s.CodeSettingsWritten : s.CodeSettingsUnchanged, size: "FsSmall", mid: true));
            if (_flow.BackupPath is { } bak) summary.Children.Add(Text(s.CodeSettingsBackup(bak), size: "FsSmall", mid: true, user: true));
        }
        else if (_flow.FailedStep is { } failed)
        {
            summary.Children.Add(Text(s.CodeApplyFailedStep(StepName(failed), FailureMessage()), danger: true));
        }
        else if (_flow.IsBusy)
        {
            summary.Children.Add(Text(s.CodeApplyRunning, size: "FsSmall", mid: true));
        }
        Grid.SetRow(summary, 1);
        grid.Children.Add(summary);

        var log = new StackPanel { Spacing = 1 };
        foreach (var line in _flow.Log.Where(l => l.Event != LogEvent.Failure)) log.Children.Add(LogRow(line));   // 실패 사유는 위 요약에 있다
        if (_flow.Log.Count == 0 && _flow.SelectedExtensions.Count == 0 && _flow.Step == FlowStep.Apply)
            log.Children.Add(Text(s.CodeNoExtensions, size: "FsSmall", mid: true));
        _logScroll = new ScrollViewer { Content = log, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var logCard = Card(_logScroll);
        Grid.SetRow(logCard, 2);
        grid.Children.Add(logCard);
        Dispatcher.UIThread.Post(() => _logScroll?.ScrollToEnd(), DispatcherPriority.Background);
        return grid;
    }

    /// <summary>실패 사유. 확장 설치 단계는 실패한 확장 id 목록이 사유다.</summary>
    string FailureMessage() => _flow.Error?.Message ?? "";

    Control LogRow(LogLine line)
    {
        var s = Strings.Current;
        var name = _flow.ProfileName.Trim();
        var text = line.Event switch
        {
            LogEvent.ProfileExists => s.CodeLogProfileExists(name),
            LogEvent.ProfileCreating => s.CodeLogProfileCreating(name),
            LogEvent.ProfileCreated => s.CodeLogProfileCreated(name),
            LogEvent.ExtensionSkipped => s.CodeLogExtSkipped(line.Text),
            LogEvent.ExtensionStarted => s.CodeLogExtInstalling(line.Text),
            LogEvent.ExtensionOk => s.CodeLogExtOk(line.Text),
            LogEvent.ExtensionFailed => s.CodeLogExtFailed(line.Text),
            LogEvent.Cancelled => s.CodeLogCancelled,
            _ => line.Text,   // VS Code 가 낸 줄·실패 사유는 그대로
        };
        var raw = line.Event is LogEvent.ExtensionOutput or LogEvent.Failure;
        // VS Code 는 성공할 때도 stderr 에 경고(Node DEP0169 등)를 낸다 — 그 줄은 적용이 실패했을 때만 빨갛게 한다.
        var showAsError = line.IsError && (line.Event != LogEvent.ExtensionOutput || _flow.FailedStep != null);
        var row = Text(raw ? "    " + text : text, mono: true, size: "FsSmall", mid: line.Event == LogEvent.ExtensionOutput && !showAsError, danger: showAsError);
        return row;
    }

    // ───── 아래 줄(오류·버튼) ─────

    void BuildFooter()
    {
        var s = Strings.Current;
        _footer.Children.Clear();
        if (_flow.Error != null && _flow.Step is FlowStep.Recommendation or FlowStep.Diff or FlowStep.Done)
            _footer.Children.Add(Text(_flow.Step == FlowStep.Done ? s.CodeOpenFailed(_flow.Error.Message) : $"{s.CodeDiffFailed}: {_flow.Error}", danger: true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        switch (_flow.Step)
        {
            case FlowStep.Analysis:
                if (_flow.Analysis == null && !_flow.IsBusy) buttons.Children.Add(Plain(s.CodeRetry, _flow.AnalyzeAsync, icon: "undo"));
                if (_flow.HasNoTypes)
                {
                    buttons.Children.Add(Plain(Strings.Current.AboutClose, () => { Close(); return Task.CompletedTask; }));
                    buttons.Children.Add(Primary(s.CodeOpenDefault, OpenDefaultAsync, enabled: !_flow.IsBusy, icon: "code"));
                    break;
                }
                buttons.Children.Add(Primary(s.CodeViewRecommendation, () => { _flow.GoToRecommendation(); return Task.CompletedTask; }, enabled: !_flow.IsBusy && _flow.Recommendation != null, icon: "arrow-right"));
                break;
            case FlowStep.Recommendation:
                buttons.Children.Add(Plain(s.CodeBack, () => { _flow.BackToAnalysis(); return Task.CompletedTask; }));
                buttons.Children.Add(Primary(s.CodeCompareSettings, () => _flow.GoToDiffAsync(), enabled: _flow.CanContinueToDiff, icon: "arrow-right"));
                break;
            case FlowStep.Diff:
                buttons.Children.Add(Plain(s.CodeBack, () => { _flow.BackToRecommendation(); return Task.CompletedTask; }, enabled: !_flow.IsBusy));
                buttons.Children.Add(Primary(s.CodeApply, _flow.ApplyAsync, enabled: !_flow.IsBusy, icon: "play"));
                break;
            case FlowStep.Apply:
                if (_flow.IsBusy) buttons.Children.Add(Plain(s.CodeStop, () => { _flow.Cancel(); return Task.CompletedTask; }, icon: "x"));
                else
                {
                    buttons.Children.Add(Plain(s.CodeBack, () => { _flow.BackToRecommendation(); return Task.CompletedTask; }));
                    buttons.Children.Add(Primary(s.CodeRetry, _flow.ApplyAsync, icon: "undo"));
                }
                break;
            case FlowStep.Done:
                buttons.Children.Add(Plain(Strings.Current.AboutClose, () => { Close(); return Task.CompletedTask; }));
                buttons.Children.Add(Primary(s.CodeOpenInVsCode, OpenAsync, enabled: !_flow.IsBusy, icon: "code"));
                break;
        }
        _footer.Children.Add(buttons);
    }

    async Task OpenDefaultAsync()
    {
        var s = Strings.Current;
        if (await _flow.OpenDefaultAsync())
        {
            Snackbar.Info(s.CodeOpenedDefault);
            Close();
        }
        else Snackbar.Error(s.CodeOpenFailed(_flow.Error!.Message), _flow.Error.Message);
    }

    async Task OpenAsync()
    {
        var s = Strings.Current;
        if (await _flow.OpenAsync()) Snackbar.Info(s.CodeOpened(_flow.ProfileName.Trim()));
        else Snackbar.Error(s.CodeOpenFailed(_flow.Error!.Message), _flow.Error.Message);
    }
}
