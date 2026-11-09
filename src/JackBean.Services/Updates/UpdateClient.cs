using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using JackBean.Domain;

namespace JackBean.Services.Updates;

/// <summary>서버가 이해하는 <c>os</c>/<c>arch</c> 값 (<c>macos|windows|linux</c>, <c>x64|arm64</c>).</summary>
public sealed record UpdatePlatform(string Os, string Arch)
{
    public static UpdatePlatform Current()
    {
        var os = OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux"
            : throw new UpdateException(UpdateErrorKind.UnsupportedPlatform, "Unsupported OS");
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var other => throw new UpdateException(UpdateErrorKind.UnsupportedPlatform, $"Unsupported CPU: {other}"),
        };
        return new UpdatePlatform(os, arch);
    }
}

/// <summary>
/// 릴리스 서버에 새 버전이 있는지 묻고, 설치 파일을 내려받아 SHA-256으로 검증한 뒤 OS 설치기에 넘긴다
/// (Flutter 앱의 app_updater 패키지와 같은 계약). 안전 규칙은 호출자에게 맡기지 않고 여기서 강제한다:
/// 서버는 https(시험용 루프백만 http) · 내려받기 주소는 <see cref="DownloadUrlPrefixes"/>로 시작 ·
/// 크기와 SHA-256이 모두 맞는 파일만 돌려줌 · 서버가 준 SHA-256이 없으면 자동 설치하지 않음.
/// </summary>
public sealed partial class UpdateClient : IDisposable
{
    static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "::1", "[::1]"];

    readonly Uri _server;
    readonly SemanticVersion _current;
    readonly UpdatePlatform _platform;
    readonly HttpClient _http;
    readonly bool _ownsClient;
    readonly string? _tempRoot;
    readonly IUpdateInstaller? _installer;

    public UpdateClient(
        Uri server,
        string appId,
        string currentVersion,
        UpdatePlatform? platform = null,
        HttpClient? httpClient = null,
        IUpdateInstaller? installer = null,
        IReadOnlyList<string>? downloadUrlPrefixes = null,
        TimeSpan? requestTimeout = null,
        string? tempRoot = null)
    {
        if (!(server.Scheme == "https" || IsLoopbackHttp(server)))
            throw new UpdateException(UpdateErrorKind.InsecureUrl, $"The update server must be https (got {server.Scheme}://{server.Host})");
        _server = server;
        _current = SemanticVersion.TryParse(currentVersion) ?? throw new ArgumentException("not a version", nameof(currentVersion));
        if (!AppIdPattern().IsMatch(appId)) throw new ArgumentException("not a repository name", nameof(appId));
        AppId = appId;
        _platform = platform ?? UpdatePlatform.Current();
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _installer = installer;
        _tempRoot = tempRoot;
        DownloadUrlPrefixes = downloadUrlPrefixes ?? ["https://github.com/"];
        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex AppIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    /// <summary>서버가 이 앱을 아는 이름 = 저장소 이름.</summary>
    public string AppId { get; }
    public IReadOnlyList<string> DownloadUrlPrefixes { get; }
    public TimeSpan RequestTimeout { get; }

    static bool IsLoopbackHttp(Uri url) => url.Scheme == "http" && LoopbackHosts.Contains(url.Host);

    Uri CheckUri
    {
        get
        {
            var basePath = _server.AbsolutePath.TrimEnd('/');
            var query = $"app={Uri.EscapeDataString(AppId)}&version={Uri.EscapeDataString(_current.ToString())}"
                + $"&os={_platform.Os}&arch={_platform.Arch}";
            return new UriBuilder(_server) { Path = basePath + "/api/update", Query = query }.Uri;
        }
    }

    /// <summary>예상된 문제로는 던지지 않는다. 결과가 무슨 일이 있었는지 말해 준다.</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await GetJsonAsync(CheckUri, ct);
            return Interpret(doc.RootElement);
        }
        catch (UpdateException e)
        {
            return new UpdateCheckResult.Failed(e);
        }
    }

    /// <summary>
    /// 앱 시작용. 확인할 때가 아니거나 · 실패했거나 · 사용자가 그 버전을 건너뛰었으면 null(= 아무 말 안 함).
    /// 수동 "업데이트 확인"은 이 모든 것을 무시하는 <see cref="CheckAsync"/>를 쓴다.
    /// </summary>
    public async Task<UpdateCheckResult?> CheckAutomaticallyAsync(UpdatePolicy policy, CancellationToken ct = default)
    {
        if (!policy.IsDue()) return null;
        var result = await CheckAsync(ct);
        if (result is UpdateCheckResult.Failed) return null; // 다음 시작 때 다시
        policy.MarkChecked();
        var newer = result switch
        {
            UpdateCheckResult.Available a => a.Info.LatestVersion,
            UpdateCheckResult.Unavailable u => u.LatestVersion,
            _ => null,
        };
        if (newer is not null && policy.IsSkipped(newer)) return null;
        return result;
    }

    async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd($"app_updater/0.1 ({AppId})");
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new UpdateException(UpdateErrorKind.UnknownApp, $"The server does not know \"{AppId}\"");
            if (response.StatusCode != HttpStatusCode.OK)
                throw new UpdateException(UpdateErrorKind.Server, $"Update server answered HTTP {(int)response.StatusCode}");
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            try
            {
                var doc = JsonDocument.Parse(bytes);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
                doc.Dispose();
            }
            catch (JsonException e)
            {
                throw new UpdateException(UpdateErrorKind.BadResponse, "Update server sent invalid JSON", e);
            }
            throw new UpdateException(UpdateErrorKind.BadResponse, "Update server sent an unexpected JSON shape");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            throw new UpdateException(UpdateErrorKind.Network, $"Could not reach the update server: {e.Message}", e);
        }
    }

    static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    UpdateCheckResult Interpret(JsonElement json)
    {
        static UpdateException Bad(string what) => new(UpdateErrorKind.BadResponse, $"Update response: {what}");

        if (!json.TryGetProperty("updateAvailable", out var availableProp) || availableProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Bad("updateAvailable is missing");
        var latestText = Str(json, "latest");
        if (availableProp.ValueKind == JsonValueKind.False) return new UpdateCheckResult.UpToDate(latestText);
        if (latestText is null) throw Bad("latest is missing");
        var latest = SemanticVersion.TryParse(latestText) ?? throw Bad($"latest is not a version ({latestText})");
        // 서버가 뭐라고 하든 뒤로 가지 않는다.
        if (_current.CompareTo(latest) >= 0) return new UpdateCheckResult.UpToDate(latestText);

        var releaseUrl = HttpsUri(Str(json, "releaseUrl"));
        var reason = Str(json, "reason");
        if (!json.TryGetProperty("asset", out var asset) || asset.ValueKind == JsonValueKind.Null || reason == "no-asset-for-platform")
            return new UpdateCheckResult.Unavailable(UnavailableReason.NoAssetForPlatform, latest.ToString(), releaseUrl);
        if (asset.ValueKind != JsonValueKind.Object) throw Bad("asset is malformed");

        var name = Str(asset, "name");
        var url = HttpsUri(Str(asset, "url"));
        long size = 0;
        var hasSize = asset.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number && sizeProp.TryGetInt64(out size);
        if (name is null || url is null || !hasSize || size <= 0) throw Bad("asset is incomplete");
        // 체크섬이 없으면(또는 읽을 수 없으면) 그 파일을 보증할 수 없다.
        var sha = Str(asset, "sha256")?.ToLowerInvariant();
        if (sha is null || !Sha256Pattern().IsMatch(sha) || reason == "no-checksum")
            return new UpdateCheckResult.Unavailable(UnavailableReason.NoChecksum, latest.ToString(), releaseUrl);

        return new UpdateCheckResult.Available(new UpdateInfo(
            _current.ToString(),
            latest.ToString(),
            Str(json, "tag") ?? latestText,
            Str(json, "notes") ?? "",
            new UpdateAsset(name, url, size, sha),
            releaseUrl,
            DateTimeOffset.TryParse(Str(json, "publishedAt"), out var published) ? published : null));
    }

    /// <summary>https만 (루프백 http는 시험용).</summary>
    static Uri? HttpsUri(string? value)
    {
        if (value is null || !Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Host.Length == 0) return null;
        return u.Scheme == "https" || IsLoopbackHttp(u) ? u : null;
    }

    /// <summary>앱 업데이터가 내려받기에 허용하는 주소인가 (브라우저로 열기 전에도 이것으로 거른다).</summary>
    public bool IsDownloadUrlAllowed(Uri url) =>
        DownloadUrlPrefixes.Any(p => url.ToString().StartsWith(p, StringComparison.Ordinal))
        && (url.Scheme == "https" || IsLoopbackHttp(url));

    /// <summary>
    /// 설치 파일을 앱 전용 임시 폴더에 내려받아 크기와 SHA-256을 검증한다. 실패하면 임시 폴더를 지우고 <see cref="UpdateException"/>을 던진다.
    /// 사용자가 동의한 뒤에만 부른다.
    /// </summary>
    public async Task<DownloadedUpdate> DownloadAsync(UpdateInfo info, Action<long, long>? onProgress = null, CancellationToken ct = default)
    {
        var asset = info.Asset;
        if (!IsDownloadUrlAllowed(asset.Url))
            throw new UpdateException(UpdateErrorKind.InsecureUrl, $"Refusing to download from {asset.Url.Host} — not an allowed location");
        var fileName = Path.GetFileName(asset.Name);
        if (fileName != asset.Name || fileName.Length == 0 || fileName.StartsWith('.') || fileName.Length > 200 || fileName.Any(char.IsControl)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new UpdateException(UpdateErrorKind.BadResponse, $"Unsafe file name: {asset.Name}");

        var dir = Directory.CreateTempSubdirectory("app_updater_");
        if (_tempRoot is not null)
        {
            dir.Delete();
            dir = Directory.CreateDirectory(Path.Combine(_tempRoot, "app_updater_" + Guid.NewGuid().ToString("N")));
        }
        try
        {
            var part = Path.Combine(dir.FullName, fileName + ".part");
            await FetchVerifiedAsync(asset, part, onProgress, ct);
            var file = Path.Combine(dir.FullName, fileName);
            File.Move(part, file);
            return new DownloadedUpdate(info, file);
        }
        catch
        {
            DeleteQuietly(dir.FullName);
            throw;
        }
    }

    async Task FetchVerifiedAsync(UpdateAsset asset, string target, Action<long, long>? onProgress, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startTimeout.CancelAfter(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
            request.Headers.UserAgent.ParseAdd($"app_updater/0.1 ({AppId})");
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, startTimeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new UpdateException(UpdateErrorKind.Cancelled, "Download cancelled");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            throw new UpdateException(UpdateErrorKind.Network, $"Could not start the download: {e.Message}", e);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw new UpdateException(UpdateErrorKind.Server, $"Download answered HTTP {(int)response.StatusCode}");

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(target);
                var buffer = new byte[81920];
                while (true)
                {
                    // 시간 제한은 조각마다: 멈춘 연결은 실패하고 느린 연결은 실패하지 않는다.
                    using var chunkTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    chunkTimeout.CancelAfter(RequestTimeout);
                    var read = await input.ReadAsync(buffer, chunkTimeout.Token);
                    if (read == 0) break;
                    received += read;
                    if (received > asset.Size)
                        throw new UpdateException(UpdateErrorKind.SizeMismatch, $"The download is larger than the announced {asset.Size} bytes");
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    onProgress?.Invoke(received, asset.Size);
                }
            }
            catch (UpdateException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new UpdateException(UpdateErrorKind.Cancelled, "Download cancelled");
            }
            catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException)
            {
                throw new UpdateException(UpdateErrorKind.Network, $"The download was interrupted: {e.Message}", e);
            }

            if (received != asset.Size)
                throw new UpdateException(UpdateErrorKind.SizeMismatch, $"Expected {asset.Size} bytes, got {received}");
            if (!Convert.ToHexStringLower(hasher.GetHashAndReset()).Equals(asset.Sha256, StringComparison.Ordinal))
                throw new UpdateException(UpdateErrorKind.ChecksumMismatch, $"SHA-256 of {asset.Name} does not match the release");
        }
    }

    /// <summary>OS 설치기를 시작한다. <see cref="DownloadAsync"/>가 돌려준 것만 넘긴다.</summary>
    public InstallOutcome Install(DownloadedUpdate update) =>
        (_installer ?? throw new UpdateException(UpdateErrorKind.UnsupportedPlatform, $"No installer is configured for {_platform.Os}")).Install(update);

    /// <summary>내려받은 파일을 지운다 (마지막에 사용자가 취소한 경우 등).</summary>
    public void Discard(DownloadedUpdate update) => DeleteQuietly(update.Directory);

    static void DeleteQuietly(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* 임시 파일은 OS가 언젠가 치운다 */ }
    }

    /// <summary>이전 실행이 남긴 내려받기 폴더를 지운다 (Windows 설치 프로그램은 자기 파일을 못 지운다). 시작할 때 부른다.</summary>
    public void CleanUpOldDownloads(TimeSpan? olderThan = null)
    {
        var root = _tempRoot ?? Path.GetTempPath();
        var cutoff = DateTime.UtcNow - (olderThan ?? TimeSpan.FromDays(1));
        try
        {
            foreach (var d in new DirectoryInfo(root).EnumerateDirectories("app_updater_*"))
                if (d.LinkTarget is null && d.LastWriteTimeUtc < cutoff) DeleteQuietly(d.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* 볼 수 없으면 치울 것도 없다 */ }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
