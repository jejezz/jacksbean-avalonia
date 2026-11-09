using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using JackBean.App.Dialogs;
using JackBean.Services.Updates;

namespace JackBean.App.Updates;

/// <summary>내려받는 동안의 진행률 창. 동의를 받은 뒤에만 연다. 취소하거나 창을 닫으면 내려받기를 멈춘다.</summary>
sealed class UpdateDownloadDialog : DialogBase
{
    readonly CancellationTokenSource _cts = new();
    readonly TextBlock _title;
    readonly ProgressBar _bar;
    readonly TextBlock _progress;

    /// <summary>끝나면 검증을 마친 파일, 취소하면 null. 실패하면 <see cref="UpdateException"/>.</summary>
    public static async Task<DownloadedUpdate?> RunAsync(Window owner, UpdateClient client, UpdateInfo info)
    {
        var dialog = new UpdateDownloadDialog(info);
        _ = dialog.ShowDialog(owner);
        try
        {
            var done = await client.DownloadAsync(info, dialog.Report, dialog._cts.Token);
            if (dialog._cts.IsCancellationRequested)
            {
                client.Discard(done);
                return null;
            }
            return done;
        }
        catch (UpdateException e) when (e.Kind == UpdateErrorKind.Cancelled)
        {
            return null;
        }
        finally
        {
            dialog.CloseFromState();
        }
    }

    UpdateDownloadDialog(UpdateInfo info)
    {
        var s = Strings.Current;
        Title = s.UpdateDownloading;
        MinWidth = 440;
        MaxWidth = 440;
        _title = new TextBlock { Text = s.UpdateDownloading, FontWeight = Avalonia.Media.FontWeight.Bold };
        _title.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FsTitle"));
        _bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, info.Asset.Size) };
        _progress = new TextBlock { Text = s.UpdateDownloadProgress(UpdateText.Bytes(0), UpdateText.Bytes(info.Asset.Size)) };
        _progress.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppTextMid"));
        var cancel = new Button { Content = s.UpdateCancel, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => _cts.Cancel();
        Content = new StackPanel { Margin = new Thickness(24), Spacing = 10, Children = { _title, _bar, _progress, cancel } };
        Dismissed += () => _cts.Cancel();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _cts.Cancel(); e.Handled = true; }
        };
    }

    void Report(long received, long total) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        var s = Strings.Current;
        _bar.Value = received;
        var verifying = received >= total;
        _bar.IsIndeterminate = verifying;
        _title.Text = verifying ? s.UpdateVerifying : s.UpdateDownloading;
        _progress.Text = s.UpdateDownloadProgress(UpdateText.Bytes(received), UpdateText.Bytes(total));
    });
}

/// <summary>"확인하는 중…" — 수동 확인이 서버를 기다리는 동안. 확인이 끝나면 프로그램이 닫는다.</summary>
sealed class UpdateCheckingDialog : DialogBase
{
    public UpdateCheckingDialog()
    {
        Title = Strings.Current.UpdateChecking;
        MinWidth = 320;
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = Strings.Current.UpdateChecking },
                new ProgressBar { IsIndeterminate = true },
            },
        };
        KeyDown += (_, e) => e.Handled = true;
    }
}

static class UpdateText
{
    /// <summary>12.3 MB 처럼.</summary>
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{(value >= 10 || i == 0 ? Math.Round(value).ToString("0") : value.ToString("0.0"))} {units[i]}";
    }

    /// <summary>실패 종류별 안내 문구. 서버 주소나 내부 오류 문자열은 사용자에게 보이지 않는다.</summary>
    public static string ErrorMessage(UpdateErrorKind kind)
    {
        var s = Strings.Current;
        return kind switch
        {
            UpdateErrorKind.Network => s.UpdateErrorNetwork,
            UpdateErrorKind.ChecksumMismatch or UpdateErrorKind.SizeMismatch => s.UpdateErrorChecksum,
            UpdateErrorKind.InstallFailed => s.UpdateErrorInstall,
            _ => s.UpdateErrorGeneric,
        };
    }
}
