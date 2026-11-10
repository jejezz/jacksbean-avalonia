using JackBean.Domain.Policy;
using JackBean.Domain.Drop;
// 떨어뜨린 항목을 정책에 따라 앱으로 보낸다: 판정(PolicyDocument.Decide) → 실행(AppLauncher).
// "물어보기"는 화면이 필요하므로 NeedsChoice 로 돌려주고, 사용자가 고르면 Choose 로 이어 간다.

using System.Text.Json.Nodes;

namespace JackBean.Services;

public sealed record RoutePlan(IReadOnlyList<RouteOutcome> Outcomes, PolicyDocument Policy)
{
    public IReadOnlyList<NeedsChoice> AskOutcomes => Outcomes.OfType<NeedsChoice>().ToList();
    public IReadOnlyList<DroppedItem> Asks => AskOutcomes.Select(o => o.Item).ToList();
    public IReadOnlyList<OpenFailed> Failures => Outcomes.OfType<OpenFailed>().ToList();

    /// <summary>
    /// 앱 고르기에 보여 줄 앱 — **그 항목의 규칙에 연결된 앱뿐**이다(폴더에 pdf 앱이 뜨지 않는다). 항목마다 연결된 앱이 다를 수 있어서
    /// 모두에게 연결된 앱(교집합, 규칙에 적힌 순서)만 보인다. 고른 앱 하나로 항목을 전부 여니까, 어느 항목에도 안 맞는 앱을 내놓지 않는다.
    /// 비어 있으면 함께 열 수 있는 앱이 없다(종류가 다른 항목을 같이 놓았다).
    /// </summary>
    public IReadOnlyList<AppEntry> ChoiceApps()
    {
        IReadOnlyList<AppEntry>? common = null;
        foreach (var ask in AskOutcomes)
        {
            var mine = ask.Candidates ?? [];
            common = common == null ? mine : common.Where(a => mine.Any(m => m.Id == a.Id)).ToList();
        }
        return common ?? [];
    }

}

public sealed record ChoiceResult(IReadOnlyList<RouteOutcome> Outcomes, IReadOnlyList<RuleSuggestion>? Saved = null, string? SaveError = null)
{
    public IReadOnlyList<RuleSuggestion> SavedRules => Saved ?? [];
    public IReadOnlyList<OpenFailed> Failures => Outcomes.OfType<OpenFailed>().ToList();
}

public sealed class DropRouter
{
    public DropRouter(PolicyStore store, AppLauncher? launcher = null)
    {
        Store = store;
        Launcher = launcher ?? new AppLauncher();
    }

    public PolicyStore Store { get; }
    public AppLauncher Launcher { get; }

    /// <summary>정책을 다시 읽고 항목마다 판정해서, 열 수 있는 것은 바로 연다.</summary>
    public async Task<RoutePlan> RouteAsync(IReadOnlyList<DroppedItem> items)
    {
        var policy = await Store.LoadAsync();
        var outcomes = new RouteOutcome?[items.Count];
        var opens = new List<(int Index, AppEntry App)>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (policy.Decide(item))
            {
                case OpenWith o: opens.Add((i, o.App)); break;
                case Ask a: outcomes[i] = new NeedsChoice(item, a.RuleId, a.Candidates); break;
                case MissingApp m: outcomes[i] = new AppNotInPolicy(item, m.AppId); break;
                default: outcomes[i] = new Skipped(item); break;
            }
        }
        // 같은 앱으로 열 항목은 묶는다 — 여러 항목을 한 번에 받는 앱(`{paths}`)은 한 번만 띄운다.
        foreach (var group in opens.GroupBy(o => o.App.Id))
        {
            var app = group.First().App;
            var results = await OpenManyAsync(app, group.Select(g => items[g.Index]).ToList());
            var k = 0;
            foreach (var g in group) outcomes[g.Index] = results[k++];
        }
        return new RoutePlan(outcomes.Select(o => o!).ToList(), policy);
    }

    /// <summary>사용자가 고른 앱으로 항목을 연다.</summary>
    public async Task<IReadOnlyList<RouteOutcome>> OpenWithAsync(AppEntry app, IEnumerable<DroppedItem> items)
    {
        return await OpenManyAsync(app, items.ToList());
    }

    /// <summary>항목들을 앱 하나로 연다: `{paths}` 를 받는 앱이면 한 번에, 아니면 항목마다.</summary>
    private async Task<IReadOnlyList<RouteOutcome>> OpenManyAsync(AppEntry app, IReadOnlyList<DroppedItem> items)
    {
        if (items.Count > 1 && AppLauncher.AcceptsMany(app))
        {
            var r = await Launcher.LaunchManyAsync(app, items.Select(i => i.Path).ToList());
            return items.Select(i => r.Ok ? (RouteOutcome)new Opened(i, app) : new OpenFailed(i, app, r.Error!)).ToList();
        }
        var list = new List<RouteOutcome>();
        foreach (var item in items) list.Add(await OpenAsync(app, item));
        return list;
    }

    /// <summary>
    /// 물어본 항목을 고른 앱으로 열고, remember 면 같은 종류는 앞으로 묻지 않도록 규칙을 저장한다.
    /// 규칙은 **전부 열렸을 때만** 저장한다 — 안 열렸는데 그 앱으로 영영 연결하면 곤란하다.
    /// </summary>
    public async Task<ChoiceResult> ChooseAsync(AppEntry app, IReadOnlyList<NeedsChoice> asks, bool remember = false)
    {
        var outcomes = await OpenWithAsync(app, asks.Select(a => a.Item));
        if (!remember || outcomes.Any(o => o is OpenFailed)) return new ChoiceResult(outcomes);
        var suggestions = RuleSuggester.Suggest(asks);
        if (suggestions.Count == 0) return new ChoiceResult(outcomes);
        var saved = await Store.RememberAsync(suggestions, app.Id);
        return saved.Ok ? new ChoiceResult(outcomes, suggestions) : new ChoiceResult(outcomes, SaveError: saved.Error);
    }

    private async Task<RouteOutcome> OpenAsync(AppEntry app, DroppedItem item)
    {
        var r = await Launcher.LaunchAsync(app, item.Path);
        return r.Ok ? new Opened(item, app) : new OpenFailed(item, app, r.Error!);
    }
}
