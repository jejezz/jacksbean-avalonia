// 정책 파일 포팅이 "아는" 정보: 논리 앱 ID → OS별 실행 방법.
//
// 여기에 없는 앱(형제 앱 Taskbench·Daylight PDF처럼 설치 경로가 기기마다 다른 것, 사용자가 직접 추가한 앱)과
// null 로 둔 OS는 "모르는 정보"라서 포팅할 때 건너뛴다. 확실한 것만 적는다 — 추측으로 채우지 않는다.

using System.Text.Json.Nodes;

namespace JackBean.Domain.Policy;

/// <summary>정책 파일이 쓰는 OS 이름. 파일 이름(`policy.&lt;os&gt;.json`)과 같다.</summary>
public enum PolicyOs
{
    Windows,
    Macos,
    Linux,
}

public static class PolicyOsExtensions
{
    /// <summary>JSON·파일 이름에 쓰는 소문자 이름.</summary>
    public static string JsonName(this PolicyOs os) => os.ToString().ToLowerInvariant();

    /// <summary>지금 실행 중인 OS. 지원하지 않는 OS 면 null.</summary>
    public static PolicyOs? Current =>
        OperatingSystem.IsWindows() ? PolicyOs.Windows
        : OperatingSystem.IsMacOS() ? PolicyOs.Macos
        : OperatingSystem.IsLinux() ? PolicyOs.Linux
        : null;

    public static PolicyOs? Parse(string? name)
    {
        foreach (var os in Enum.GetValues<PolicyOs>())
            if (os.JsonName() == name) return os;
        return null;
    }
}

/// <summary>앱을 여는 방법. `{path}`는 떨어뜨린 대상의 경로로 바뀐다.</summary>
public sealed record LaunchSpec(string Command, IReadOnlyList<string>? ArgList = null)
{
    public IReadOnlyList<string> Args => ArgList ?? ["{path}"];

    public JsonObject ToJson() => new()
    {
        ["command"] = Command,
        ["args"] = new JsonArray(Args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
    };
}

/// <summary>논리 ID로 부르는 앱 하나와, OS별로 아는 실행 방법. 모르는 OS는 null.</summary>
public sealed record KnownApp(LaunchSpec? Windows = null, LaunchSpec? Macos = null, LaunchSpec? Linux = null)
{
    public LaunchSpec? On(PolicyOs os) => os switch
    {
        PolicyOs.Windows => Windows,
        PolicyOs.Macos => Macos,
        PolicyOs.Linux => Linux,
        _ => null,
    };
}

public static class KnownApps
{
    /// <summary>
    /// 앱 안에 들어 있는 도구(`jackbean:` 명령). 정책 파일의 `apps` 에 적지 않아도 늘 쓸 수 있다(<see cref="PolicyDocument.Apps"/> 가 더해 준다) —
    /// 운영체제 기본 방식(`default`)처럼. 파일에 같은 이름이 있으면 파일이 이긴다.
    /// </summary>
    public static readonly IReadOnlyList<string> BuiltInTools = ["image-viewer", "text-editor", "pdf-viewer"];

    /// <summary>기본 제공 카탈로그. 새로 확실히 아는 앱이 생기면 여기에 더한다.</summary>
    public static readonly IReadOnlyDictionary<string, KnownApp> Catalog = new Dictionary<string, KnownApp>
    {
        // 파일 관리자로 연다 (드라이브·폴더).
        ["explorer"] = new(new LaunchSpec("explorer.exe"), new LaunchSpec("open"), new LaunchSpec("xdg-open")),
        // 시스템 기본 앱으로 연다.
        ["default"] = new(new LaunchSpec("explorer.exe"), new LaunchSpec("open"), new LaunchSpec("xdg-open")),
        // 앱 안에 들어 있는 이미지 뷰어(별도 창으로 뜬다). `jackbean:` 로 시작하는 명령은 프로세스를 띄우지 않고
        // 앱이 직접 처리한다 — 어느 OS 에서나 같다.
        ["image-viewer"] = new(new LaunchSpec("jackbean:image-viewer"), new LaunchSpec("jackbean:image-viewer"), new LaunchSpec("jackbean:image-viewer")),
        // 앱 안에 들어 있는 PDF 뷰어(운영체제의 웹뷰로 연다 — Linux 는 웹뷰가 없을 수 있어 기본 정책에는 넣지 않는다).
        ["pdf-viewer"] = new(new LaunchSpec("jackbean:pdf-viewer"), new LaunchSpec("jackbean:pdf-viewer"), new LaunchSpec("jackbean:pdf-viewer")),
        // 앱 안에 들어 있는 텍스트 에디터.
        ["text-editor"] = new(new LaunchSpec("jackbean:text-editor"), new LaunchSpec("jackbean:text-editor"), new LaunchSpec("jackbean:text-editor")),
        // 터미널: 리눅스는 배포판마다 달라서 모른다(건너뜀).
        ["terminal"] = new(
            new LaunchSpec("wt.exe", ["-d", "{path}"]),
            new LaunchSpec("open", ["-a", "Terminal", "{path}"])),
    };
}
