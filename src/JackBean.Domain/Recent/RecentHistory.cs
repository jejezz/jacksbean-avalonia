// 최근 작업(독에 놓아 앱으로 연 것)의 기록. 독 알약에 마우스를 올리면 보이고, 누르면 같은 앱으로 다시 연다.
// 이 PC 전용(경로는 PC 마다 다르다) — 정책처럼 동기화하지 않는다. 저장은 Services 의 RecentStore.

using JackBean.Domain.Drop;
using JackBean.Domain.Policy;

namespace JackBean.Domain.Recent;

/// <summary>한 번의 작업: 항목들(경로)을 앱 하나로 열었다. 여러 항목을 함께 놓았으면 한 줄로 묶인다.</summary>
/// <param name="Kind">항목들의 공통 종류. 종류가 섞였으면 null.</param>
public sealed record RecentEntry(IReadOnlyList<string> Paths, DropKind? Kind, string AppId, DateTimeOffset At)
{
    /// <summary>같은 작업인가(같은 앱, 같은 경로들 — 순서는 따지지 않는다). Windows 는 경로 대소문자를 구분하지 않는다.</summary>
    public bool SameWork(RecentEntry other, bool ignoreCase)
    {
        if (!string.Equals(AppId, other.AppId, StringComparison.Ordinal) || Paths.Count != other.Paths.Count) return false;
        var cmp = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return new HashSet<string>(Paths, cmp).SetEquals(other.Paths);
    }

    /// <summary>한 줄에 보일 이름 — 항목이 하나면 그 이름(드라이브는 `D:\`), 여럿이면 null(화면이 "파일 3개"처럼 적는다).</summary>
    public string? SingleName => Paths.Count == 1 ? new DroppedItem(Paths[0], Kind ?? DropKind.File).Name : null;
}

public static class RecentHistory
{
    /// <summary>기록은 최근 이만큼만 남긴다.</summary>
    public const int MaxEntries = 10;

    /// <summary>맨 앞에 더한다. 같은 작업이 이미 있으면 그것을 지우고 새로 앞에 둔다(한 줄만 남는다). 넘치면 오래된 것부터 버린다.</summary>
    public static IReadOnlyList<RecentEntry> Add(IReadOnlyList<RecentEntry> list, RecentEntry entry, bool ignoreCase, int max = MaxEntries)
    {
        var result = new List<RecentEntry>(list.Count + 1) { entry };
        result.AddRange(list.Where(e => !e.SameWork(entry, ignoreCase)));
        return result.Take(max).ToList();
    }

    /// <summary>앱으로 열린 항목들(<see cref="Opened"/>)을 앱마다 한 줄씩 기록할 작업으로 만든다(드롭 한 번에 앱이 둘이면 두 줄).</summary>
    public static IReadOnlyList<RecentEntry> FromOutcomes(IEnumerable<RouteOutcome> outcomes, DateTimeOffset at) =>
        outcomes.OfType<Opened>()
            .GroupBy(o => o.App.Id)
            .Select(g =>
            {
                var items = g.Select(o => o.Item).ToList();
                var first = items[0].Kind;
                return new RecentEntry(items.Select(i => i.Path).ToList(), items.All(i => i.Kind == first) ? first : null, g.Key, at);
            })
            .ToList();

    /// <summary>
    /// 긴 이름을 가운데에서 줄인다(`report-final-…-2026.pdf`) — 앞부분과 확장자가 남아야 무엇인지 알 수 있다.
    /// <paramref name="max"/> 글자 이하면 그대로 둔다.
    /// </summary>
    public static string Shorten(string name, int max = 32)
    {
        if (max < 5 || name.Length <= max) return name;
        var keep = max - 1;   // 말줄임표 한 글자
        var tail = Math.Min(keep / 2, Math.Max(0, keep - 4));
        var dot = name.LastIndexOf('.');
        if (dot > 0 && name.Length - dot <= 8) tail = Math.Max(tail, name.Length - dot + 3);   // 확장자와 그 앞 몇 글자는 남긴다
        tail = Math.Min(tail, keep - 4);
        return name[..(keep - tail)] + "…" + name[^tail..];
    }
}
