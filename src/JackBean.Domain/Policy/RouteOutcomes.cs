using JackBean.Domain.Drop;

namespace JackBean.Domain.Policy;

// 떨어뜨린 항목 하나를 처리한 결과.
public abstract record RouteOutcome(DroppedItem Item);
public sealed record Opened(DroppedItem Item, AppEntry App) : RouteOutcome(Item);
public sealed record OpenFailed(DroppedItem Item, AppEntry App, string Error) : RouteOutcome(Item);

/// <summary>어떤 앱으로 열지 물어야 한다. RuleId = 이 항목을 "물어보기"로 보낸 규칙(어느 규칙에도 안 맞았으면 null). Candidates = 고를 앱(null 이면 등록된 앱 전부).</summary>
public sealed record NeedsChoice(DroppedItem Item, string? RuleId = null, IReadOnlyList<AppEntry>? Candidates = null) : RouteOutcome(Item);
public sealed record AppNotInPolicy(DroppedItem Item, string AppId) : RouteOutcome(Item);
public sealed record Skipped(DroppedItem Item) : RouteOutcome(Item);

