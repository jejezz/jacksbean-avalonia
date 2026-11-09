namespace JackBean.Services.Updates;

/// <summary>서버가 이 OS·아키텍처용으로 고른 설치 파일.</summary>
public sealed record UpdateAsset(string Name, Uri Url, long Size, string Sha256);

/// <summary>자동으로 내려받아 검증할 수 있는 새 버전.</summary>
public sealed record UpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string Tag,
    string Notes,
    UpdateAsset Asset,
    Uri? ReleaseUrl,
    DateTimeOffset? PublishedAt);

public enum UnavailableReason
{
    /// <summary>이 플랫폼 파일은 있지만 SHA-256이 없다 → 자동 설치 금지.</summary>
    NoChecksum,

    /// <summary>이 OS·아키텍처용 파일이 릴리스에 없다.</summary>
    NoAssetForPlatform,
}

public abstract record UpdateCheckResult
{
    /// <summary>더 새로운 버전이 없다 (앱이 서버의 최신보다 새로운 경우도 포함).</summary>
    public sealed record UpToDate(string? LatestVersion) : UpdateCheckResult;

    public sealed record Available(UpdateInfo Info) : UpdateCheckResult;

    /// <summary>새 버전이 있지만 앱이 설치할 수 없다. <see cref="ReleaseUrl"/>로 직접 받게 안내한다.</summary>
    public sealed record Unavailable(UnavailableReason Reason, string LatestVersion, Uri? ReleaseUrl) : UpdateCheckResult;

    public sealed record Failed(UpdateException Error) : UpdateCheckResult;
}

public enum UpdateErrorKind
{
    Network,
    Server,
    BadResponse,
    UnknownApp,
    InsecureUrl,
    SizeMismatch,
    ChecksumMismatch,
    Cancelled,
    InstallFailed,
    UnsupportedPlatform,
}

public sealed class UpdateException(UpdateErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public UpdateErrorKind Kind { get; } = kind;
}

/// <summary>내려받아 검증을 마친 설치 파일. <see cref="Directory"/>는 통째로 지워도 되는 앱 전용 임시 폴더.</summary>
public sealed record DownloadedUpdate(UpdateInfo Info, string File)
{
    public string Directory => Path.GetDirectoryName(File)!;
}

public enum InstallOutcome
{
    /// <summary>설치 프로세스를 시작했다. 실행 중인 파일을 바꿀 수 있게 앱은 곧 종료해야 한다.</summary>
    InstallerStarted,

    /// <summary>파일을 사용자가 마무리하도록 열었다 (macOS: DMG 창). 앱은 계속 실행된다.</summary>
    OpenedForUser,
}

/// <summary>OS 명령 실행. 시험에서는 가짜로 바꾼다.</summary>
public interface ICommandRunner
{
    /// <summary>끝날 때까지 실행하고 종료 코드를 돌려준다.</summary>
    int Run(string executable, IReadOnlyList<string> arguments);

    /// <summary>이 앱이 끝난 뒤에도 계속 도는 프로세스를 시작한다.</summary>
    void StartDetached(string executable, IReadOnlyList<string> arguments);
}

/// <summary>마지막 자동 확인 시각과 건너뛴 버전, 이 두 값만 저장한다.</summary>
public interface IUpdateStateStore
{
    string? Read(string key);
    void Write(string key, string value);
}
