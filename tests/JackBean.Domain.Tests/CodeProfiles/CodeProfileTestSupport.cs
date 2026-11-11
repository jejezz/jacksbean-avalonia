using System.Text.Json.Nodes;
using JackBean.Domain.CodeProfiles;

namespace JackBean.Domain.Tests.CodeProfiles;

/// <summary>메모리에 둔 파일 내용을 돌려주는 가짜 읽기(파일 시스템 없이 `fileContains` 를 시험한다).</summary>
internal sealed class FakeFileReader(Dictionary<string, string>? files = null) : IFileTextReader
{
    public string? ReadHead(string relativePath) => files != null && files.TryGetValue(relativePath, out var text) ? text : null;
}

internal static class CodeProfileTestSupport
{
    public static ProfileRules DefaultRules() => ProfileRulesParser.Parse(DefaultProfileRules.Json).Value!;

    public static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    /// <summary>파일 이름 목록으로 만든 스캔 결과. 디렉터리는 파일 경로의 상위 폴더에서 자동으로 채운다.</summary>
    public static ScanSnapshot Snapshot(IEnumerable<string> files, IEnumerable<string>? extraDirs = null, IEnumerable<string>? skipped = null, bool truncated = false)
    {
        var fileList = files.ToList();
        var dirs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in fileList)
        {
            var parts = f.Split('/');
            for (var i = 1; i < parts.Length; i++) dirs.Add(string.Join('/', parts.Take(i)));
        }
        foreach (var d in extraDirs ?? []) dirs.Add(d);
        return new ScanSnapshot("/root", fileList, dirs.ToList(), (skipped ?? []).ToList(), truncated);
    }

    public static AnalysisResult Classify(ScanSnapshot snapshot, IReadOnlyDictionary<string, string>? variables = null, Dictionary<string, string>? contents = null, ProfileRules? rules = null) =>
        ProjectClassifier.Classify(snapshot, variables ?? new Dictionary<string, string>(), rules ?? DefaultRules(), new FakeFileReader(contents), DateTimeOffset.UnixEpoch);

    public static IReadOnlyList<string> Ids(AnalysisResult a) => a.Types.Select(t => t.TypeId).ToList();
}
