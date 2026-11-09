using System.Reflection;

namespace JackBean.Domain;

/// <summary>
/// 앱 정체성(이름·식별자·게시자·저작권)을 코드에 반영하는 유일한 곳.
/// 값의 원본은 Directory.Build.props의 App* 속성이고 어셈블리 메타데이터로 들어온다.
/// </summary>
public static class AppIdentity
{
    static readonly Dictionary<string, string> Meta = typeof(AppIdentity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(a => a.Value is not null)
        .ToDictionary(a => a.Key, a => a.Value!);

    internal static string? Get(string key) => Meta.GetValueOrDefault(key);

    static string Require(string key) =>
        Get(key) ?? throw new InvalidOperationException($"어셈블리 메타데이터 '{key}'가 없다 (Directory.Build.props 확인)");

    /// <summary>표시 이름. 번역하지 않는다.</summary>
    public static string DisplayName { get; } = Require("AppDisplayName");

    /// <summary>릴리스 산출물과 설정 폴더에 쓰는 공백 없는 이름.</summary>
    public static string FileName { get; } = Require("AppFileName");

    /// <summary>번들 ID / 설치 식별자. 릴리스 후 바꾸지 않는다.</summary>
    public static string Id { get; } = Require("AppId");

    public static string CopyrightHolder { get; } = Require("AppCopyrightHolder");

    /// <summary>첫 릴리스 연도. 해마다 바꾸지 않는다.</summary>
    public static int FirstReleaseYear { get; } = int.Parse(Require("AppFirstReleaseYear"));

    public static string Copyright => $"Copyright © {FirstReleaseYear} {CopyrightHolder}";

    public const string RepositoryUrl = "https://github.com/jejezz/jackbean-avalonia";

    public const string LicenseName = "MIT License";

    /// <summary>업데이트 서버 주소. 빈 값이면 업데이트 확인을 끈다. 원본은 Directory.Build.props의 AppUpdateServer.</summary>
    public static string UpdateServerUrl { get; } = Get("AppUpdateServer")?.Trim() ?? "";

    /// <summary>서버가 앱을 아는 이름 = 저장소 이름 (<see cref="RepositoryUrl"/>의 마지막 경로).</summary>
    public static string UpdateAppId { get; } = RepositoryUrl.TrimEnd('/').Split('/')[^1];
}
