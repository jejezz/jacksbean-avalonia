// "항상 이렇게 열기": 물어본 항목에서 규칙 하나를 짓는다. 항목 종류별로 가장 좁은 조건을 고른다.
//   확장자 있는 파일 → 그 확장자 / Git 폴더 → hasGit / Git 없는 폴더 → hasGit:false / 드라이브 → 전부
// 확장자 없는 파일처럼 알맞은 조건이 없으면 제안하지 않는다(계속 물어본다).

using System.Text.Json.Nodes;
using JackBean.Domain.Drop;

namespace JackBean.Domain.Policy;

public enum SuggestionKind { Extension, GitFolder, PlainFolder, Drive }

/// <param name="Arg">확장자(`zip`). 확장자 규칙이 아니면 빈 문자열.</param>
/// <param name="Id">같은 조건이면 늘 같은 id — 다시 고르면 새 규칙이 쌓이지 않고 앱만 바뀐다.</param>
/// <param name="BeforeRuleId">그 항목을 물어보게 만든 "물어보기" 규칙의 id. 새 규칙은 그 바로 위에 넣는다.</param>
public sealed record RuleSuggestion(SuggestionKind Kind, string Id, JsonObject When, string Arg = "", string? BeforeRuleId = null);

public static class RuleSuggester
{
    /// <summary>물어본 항목들에서 만들 수 있는 규칙. 같은 조건은 한 번만(처음 항목 기준).</summary>
    public static IReadOnlyList<RuleSuggestion> Suggest(IEnumerable<NeedsChoice> asks)
    {
        var byId = new Dictionary<string, RuleSuggestion>();
        foreach (var ask in asks)
        {
            var s = SuggestOne(ask.Item, ask.RuleId);
            if (s != null) byId.TryAdd(s.Id, s);
        }
        return byId.Values.ToList();
    }

    private static RuleSuggestion? SuggestOne(DroppedItem item, string? before) => item.Kind switch
    {
        DropKind.File when item.Extension.Length == 0 => null,
        DropKind.File => new(SuggestionKind.Extension, $"auto-ext-{item.Extension}",
            new JsonObject { ["kind"] = "extension", ["values"] = new JsonArray(item.Extension) }, item.Extension, before),
        DropKind.Folder => item.HasGit
            ? new(SuggestionKind.GitFolder, "auto-git-folder", new JsonObject { ["kind"] = "hasGit" }, BeforeRuleId: before)
            : new(SuggestionKind.PlainFolder, "auto-plain-folder", new JsonObject { ["kind"] = "hasGit", ["value"] = false }, BeforeRuleId: before),
        DropKind.Drive => new(SuggestionKind.Drive, "auto-drive", new JsonObject { ["kind"] = "kind", ["value"] = "drive" }, BeforeRuleId: before),
        _ => null,
    };
}
