namespace JackBean.Domain;

/// <summary>앱 버전. 원본은 Directory.Build.props의 AppVersion / AppBuild.</summary>
public static class AppVersion
{
    /// <summary>예: <c>1.0.0-rc.1</c>. 메타데이터를 못 읽으면 로컬 빌드임이 보이도록 <c>0.0.0</c>.</summary>
    public static string Version { get; } = AppIdentity.Get("AppVersion") ?? "0.0.0";

    public static int Build { get; } = int.TryParse(AppIdentity.Get("AppBuild"), out var b) ? b : 0;

    /// <summary>정보 창 표시용: <c>1.0.0-rc.1 (1)</c></summary>
    public static string Display => $"{Version} ({Build})";
}
