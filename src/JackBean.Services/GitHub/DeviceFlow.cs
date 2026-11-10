using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Services.GitHub;

/// <summary>브라우저 로그인(Device Flow)이 끝나지 못했다(코드 만료, 사용자가 거절, 설정 문제).</summary>
public sealed class DeviceFlowException(string message) : Exception(message);

/// <param name="UserCode">사용자가 GitHub 페이지에 입력할 코드.</param>
/// <param name="VerificationUri">그 페이지 주소.</param>
public sealed record DeviceCode(string DeviceCodeValue, string UserCode, string VerificationUri, int IntervalSeconds, int ExpiresInSeconds);

/// <summary>
/// GitHub OAuth Device Flow — 서버 없이 데스크톱 앱에서 로그인한다. 앱이 코드를 보여 주고 브라우저를 열면 사용자가 승인하고,
/// 앱은 그동안 토큰이 나올 때까지 물어본다. 로그인마다 *이 앱·이 PC 전용* 토큰이 나와서 사용자가 키를 만들거나 보관할 필요가 없다.
/// Client ID 는 비밀이 아니다(OAuth App 등록에서 "Enable Device Flow" 를 켜야 한다).
/// </summary>
public sealed class DeviceFlow : IDisposable
{
    private readonly string _clientId;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">시험에서 기다리지 않게 바꿔 끼운다.</param>
    public DeviceFlow(string clientId, HttpMessageHandler? handler = null, string baseUrl = "https://github.com", Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _clientId = clientId;
        _http = handler == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("JacksBean");
        _baseUrl = baseUrl.TrimEnd('/');
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonObject> PostAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var res = await _http.PostAsync(_baseUrl + path, new FormUrlEncodedContent(form), ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            return JsonNode.Parse(text) as JsonObject ?? throw new DeviceFlowException($"예상 밖의 응답입니다 ({(int)res.StatusCode})");
        }
        catch (JsonException) { throw new DeviceFlowException("GitHub 응답을 읽지 못했습니다"); }
        catch (HttpRequestException e) { throw new DeviceFlowException($"네트워크에 연결하지 못했습니다: {e.Message}"); }
    }

    public async Task<DeviceCode> StartAsync(string scope = "repo", CancellationToken ct = default)
    {
        var j = await PostAsync("/login/device/code", new() { ["client_id"] = _clientId, ["scope"] = scope }, ct);
        if (j["error"] != null) throw new DeviceFlowException((string?)j["error_description"] ?? (string?)j["error"] ?? "로그인을 시작하지 못했습니다");
        return new DeviceCode((string)j["device_code"]!, (string)j["user_code"]!, (string)j["verification_uri"]!, (int?)j["interval"] ?? 5, (int?)j["expires_in"] ?? 900);
    }

    /// <summary>사용자가 브라우저에서 승인할 때까지 기다려 토큰을 돌려준다. 취소하면 <see cref="OperationCanceledException"/>.</summary>
    public async Task<string> AwaitTokenAsync(DeviceCode code, CancellationToken ct = default)
    {
        var interval = code.IntervalSeconds;
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresInSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await _delay(TimeSpan.FromSeconds(interval), ct);
            JsonObject j;
            try
            {
                j = await PostAsync("/login/oauth/access_token", new()
                {
                    ["client_id"] = _clientId, ["device_code"] = code.DeviceCodeValue, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }, ct);
            }
            catch (DeviceFlowException) { continue; }   // 브라우저를 오가는 사이 네트워크가 잠깐 끊긴다 — 다음 확인에서 다시 시도
            if ((string?)j["access_token"] is { Length: > 0 } token) return token;
            switch ((string?)j["error"])
            {
                case "authorization_pending": break;
                case "slow_down": interval = (int?)j["interval"] ?? interval + 5; break;
                case "expired_token": throw new DeviceFlowException("코드가 만료되었습니다. 다시 시도하세요");
                case "access_denied": throw new DeviceFlowException("승인이 거절되었습니다");
                default: throw new DeviceFlowException((string?)j["error_description"] ?? (string?)j["error"] ?? "알 수 없는 오류");
            }
        }
        throw new DeviceFlowException("코드가 만료되었습니다. 다시 시도하세요");
    }
}
