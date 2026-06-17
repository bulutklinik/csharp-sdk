using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bulutklinik.Sdk;

internal enum AuthMode
{
    Public,
    Bearer,
    Partner,
}

internal sealed class Envelope
{
    [JsonPropertyName("resultType")]
    public int? ResultType { get; init; }

    [JsonPropertyName("errorType")]
    public JsonElement ErrorType { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }
}

internal sealed class Transport
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _lang;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string? _partnerToken;
    private readonly ITokenStore _tokenStore;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    internal Transport(HttpClient http, string baseUrl, string lang, string? clientId,
        string? clientSecret, string? partnerToken, ITokenStore tokenStore)
    {
        _http = http;
        _baseUrl = baseUrl;
        _lang = lang;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _partnerToken = partnerToken;
        _tokenStore = tokenStore;
    }

    internal ITokenStore TokenStore => _tokenStore;

    internal string? ClientId => _clientId;

    internal string? ClientSecret => _clientSecret;

    internal async Task<JsonElement> SendAsync(HttpMethod method, string path, AuthMode auth,
        object? body, CancellationToken cancellationToken, bool isRetry = false)
    {
        string? staleAccess = auth == AuthMode.Bearer ? _tokenStore.GetAccessToken() : null;
        var (status, env, retryAfter) = await DispatchAsync(method, path, auth, body, cancellationToken)
            .ConfigureAwait(false);

        if (status is >= 200 and < 300 && env.ResultType == 0)
        {
            return env.Data;
        }

        bool expired = status == 401 || env.ResultType == 4;
        if (auth == AuthMode.Bearer && expired && !isRetry
            && await TryRefreshAsync(staleAccess, cancellationToken).ConfigureAwait(false))
        {
            return await SendAsync(method, path, auth, body, cancellationToken, true).ConfigureAwait(false);
        }

        if (env.ResultType == 2)
        {
            _tokenStore.Clear();
        }

        throw ToException(method, path, status, env, retryAfter);
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await TryRefreshAsync(null, cancellationToken).ConfigureAwait(false))
        {
            throw ApiException.Create("token refresh failed",
                new ApiErrorContext(401, null, null, default, "POST", "/general/refreshApi", null));
        }
    }

    private async Task<(int Status, Envelope Envelope, string? RetryAfter)> DispatchAsync(
        HttpMethod method, string path, AuthMode auth, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("lang", _lang);

        if (body is not null && method != HttpMethod.Get)
        {
            string json = JsonSerializer.Serialize(body, SerializeOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (auth == AuthMode.Bearer)
        {
            string? token = _tokenStore.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
        else if (auth == AuthMode.Partner && !string.IsNullOrEmpty(_partnerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _partnerToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new TransportException($"bulutklinik: {method.Method} {path}: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransportException($"bulutklinik: timeout {method.Method} {path}", e);
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string? retryAfter = response.Headers.TryGetValues("Retry-After", out var values)
                ? values.FirstOrDefault()
                : null;
            return ((int)response.StatusCode, ParseEnvelope(text), retryAfter);
        }
    }

    private static Envelope ParseEnvelope(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new Envelope();
        }
        try
        {
            return JsonSerializer.Deserialize<Envelope>(text, EnvelopeOptions) ?? new Envelope();
        }
        catch (JsonException)
        {
            return new Envelope { ErrorMessage = text };
        }
    }

    private async Task<bool> TryRefreshAsync(string? staleAccess, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (staleAccess is not null && _tokenStore.GetAccessToken() != staleAccess)
            {
                return true;
            }

            string? refreshToken = _tokenStore.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            {
                return false;
            }

            var (status, env, _) = await DispatchAsync(HttpMethod.Post, "/general/refreshApi", AuthMode.Public,
                new { refreshToken, clientId = _clientId, clientSecretKey = _clientSecret }, cancellationToken)
                .ConfigureAwait(false);

            if (status is < 200 or >= 300 || env.ResultType != 0 || env.Data.ValueKind != JsonValueKind.Object
                || !env.Data.TryGetProperty("access_token", out var accessEl)
                || accessEl.ValueKind != JsonValueKind.String)
            {
                _tokenStore.Clear();
                return false;
            }

            string access = accessEl.GetString()!;
            string newRefresh = env.Data.TryGetProperty("refresh_token", out var refreshEl)
                && refreshEl.ValueKind == JsonValueKind.String
                ? refreshEl.GetString()!
                : refreshToken;
            _tokenStore.SetTokens(access, newRefresh);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static ApiException ToException(HttpMethod method, string path, int status, Envelope env, string? retryAfter)
    {
        string message = string.IsNullOrEmpty(env.ErrorMessage)
            ? $"Bulutklinik API request failed: {method.Method} {path} (HTTP {status})"
            : env.ErrorMessage!;

        int? ra = int.TryParse(retryAfter, out int parsed) ? parsed : null;

        object? errorType = env.ErrorType.ValueKind switch
        {
            JsonValueKind.String => env.ErrorType.GetString(),
            JsonValueKind.Number => env.ErrorType.GetDouble(),
            _ => null,
        };

        return ApiException.Create(message,
            new ApiErrorContext(status, env.ResultType, errorType, env.Data, method.Method, path, ra));
    }
}
