// "항상 이렇게 열기": 물어본 항목에서 규칙 하나를 짓는다. 항목 종류별로 가장 좁은 조건을 고른다.
//   확장자 있는 파일 → 그 확장자 / 폴더 → 폴더 전부 / 드라이브 → 드라이브 전부
// 확장자 없는 파일처럼 알맞은 조건이 없으면 제안하지 않는다(계속 물어본다).
// 폴더는 Git 저장소인지로 가르지 않는다 — 어떤 앱을 지정할지는 다른 대상과 같고, 저장소를 어떻게 다룰지는 그 앱이 정한다.

using JackBean.Domain.Drop;

namespace JackBean.Domain.Policy;

public enum SuggestionKind { Extension, Folder, Drive }

/// <param name="Arg">확장자(`zip`). 확장자 규칙이 아니면 빈 문자열.</param>
public sealed record RuleSuggestion(SuggestionKind Kind, string Arg = "");

public static class RuleSuggester
{
    /// <summary>물어본 항목들에서 만들 수 있는 규칙. 같은 조건은 한 번만.</summary>
    public static IReadOnlyList<RuleSuggestion> Suggest(IEnumerable<NeedsChoice> asks) =>
        asks.Select(a => SuggestOne(a.Item)).OfType<RuleSuggestion>().Distinct().ToList();

    private static RuleSuggestion? SuggestOne(DroppedItem item) => item.Kind switch
    {
        DropKind.File when item.Extension.Length == 0 => null,
        DropKind.File => new(SuggestionKind.Extension, item.Extension),
        DropKind.Folder => new(SuggestionKind.Folder),
        DropKind.Drive => new(SuggestionKind.Drive),
        _ => null,
    };
}
