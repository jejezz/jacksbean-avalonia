// 최근 작업을 남기고 다시 연다. 독(드롭·앱 고르기)과 최근 작업 목록 창이 같이 쓴다.

using JackBean.App.Notifications;
using JackBean.Domain.Policy;
using JackBean.Domain.Recent;
using JackBean.Services;

namespace JackBean.App.Recent;

/// <summary>목록 한 줄을 다시 열 수 있는지. 못 열면 <see cref="Reason"/> 이 이유(흐리게 보이고 툴팁으로 알린다).</summary>
internal sealed record RecentRow(RecentEntry Entry, AppEntry? App, string? Reason)
{
    public bool CanOpen => Reason == null;
}

internal static class RecentActions
{
    /// <summary>앱으로 열린 항목을 기록한다(설정에서 껐으면 남기지 않는다). 실패해도 드롭 흐름은 그대로 간다.</summary>
    public static void Remember(IEnumerable<RouteOutcome> outcomes)
    {
        if (!App.Settings.Value.RecentHistory) return;
        var entries = RecentHistory.FromOutcomes(outcomes, DateTimeOffset.Now);
        if (entries.Count == 0) return;
        _ = RememberAsync(entries);
    }

    private static async Task RememberAsync(IReadOnlyList<RecentEntry> entries)
    {
        try { await App.Recent.AddAsync(entries); }
        catch (Exception) { /* 기록을 못 남겨도 연 것은 연 것이다 — 알림으로 방해하지 않는다 */ }
    }

    /// <summary>목록에 보일 줄들: 지금 정책에서 앱을 찾고, 항목이 아직 있는지 본다.</summary>
    public static async Task<IReadOnlyList<RecentRow>> RowsAsync()
    {
        var entries = await App.Recent.ListAsync();
        if (entries.Count == 0) return [];
        var policy = await App.PolicyStore.LoadAsync();
        return entries.Select(e =>
        {
            var app = FindApp(policy, e.AppId);
            string? reason = null;
            if (app == null) reason = Strings.Current.DockAppMissing(e.AppId);
            else if (!e.Paths.All(Exists)) reason = Strings.Current.RecentItemMissing;
            return new RecentRow(e, app, reason);
        }).ToList();
    }

    private static AppEntry? FindApp(PolicyDocument policy, string id)
    {
        if (App.IsHiddenApp(id)) return null;
        if (policy.Apps.TryGetValue(id, out var app)) return app;
        return id == "default" ? policy.DefaultApp : null;   // 운영체제 기본 방식은 정책에 없어도 늘 쓸 수 있다
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>그때와 같은 앱으로 다시 연다. 열면 맨 위로 올라간다.</summary>
    public static async Task ReopenAsync(RecentRow row)
    {
        if (row.App is not { } app) return;
        try
        {
            var items = row.Entry.Paths.Select(p => PathClassifier.Classify(p)).ToList();
            var outcomes = await App.Router.OpenWithAsync(app, items);
            if (outcomes.OfType<OpenFailed>().FirstOrDefault() is { } failure) Snackbar.Error(Strings.Current.DockOpenFailed(failure.Error));
            Remember(outcomes);
        }
        catch (Exception e)
        {
            Snackbar.Error(Strings.Current.DockOpenFailed(e.Message), e.ToString());
        }
    }
}
