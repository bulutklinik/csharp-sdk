using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bulutklinik.Sdk;

internal enum AuthMode
{
    Public,
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

/// <summary>
/// Low-level transport: builds requests, unwraps the response envelope and maps
/// failures to typed exceptions.
/// <para>
/// On a <c>401</c> / <c>resultType 4</c> it refreshes once and retries the
/// original request; the error surfaces only when there is no refresh token or
/// the refresh itself fails. Concurrent refreshes share one in-flight attempt.
/// </para>
/// </summary>
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
    private readonly ITokenStore _tokenStore;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    /// <summary>Used only when the injected store cannot persist the refresh token.</summary>
    private string? _fallbackRefreshToken;

    internal Transport(HttpClient http, string baseUrl, string lang, string? clientId,
        string? clientSecret, ITokenStore tokenStore)
    {
        _http = http;
        _baseUrl = baseUrl;
        _lang = lang;
        ClientId = clientId;
        ClientSecret = clientSecret;
        _tokenStore = tokenStore;
    }

    internal ITokenStore TokenStore => _tokenStore;

    internal string? ClientId { get; }

    internal string? ClientSecret { get; }

    /// <summary>Persist a freshly minted token pair.</summary>
    internal void SetTokens(string accessToken, string? refreshToken)
    {
        _tokenStore.SetToken(accessToken);
        if (_tokenStore is IRefreshTokenStore refreshable)
        {
            refreshable.SetRefreshToken(refreshToken);
        }
        else
        {
            _fallbackRefreshToken = refreshToken;
        }
    }

    internal string? GetRefreshToken() =>
        _tokenStore is IRefreshTokenStore refreshable
            ? refreshable.GetRefreshToken()
            : _fallbackRefreshToken;

    internal void ClearTokens()
    {
        _fallbackRefreshToken = null;
        _tokenStore.Clear();
    }

    /// <summary>Force a refresh using the stored refresh token. Throws on failure.</summary>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await TryRefreshAsync(null, cancellationToken).ConfigureAwait(false))
        {
            throw new AuthenticationException("Token refresh failed",
                new ApiErrorContext(401, null, null, default, "POST", "/general/refreshApi", null));
        }
    }

    internal async Task<JsonElement> SendAsync(HttpMethod method, string path, AuthMode auth,
        object? body, CancellationToken cancellationToken, bool isRetry = false)
    {
        string? staleAccess = auth == AuthMode.Partner ? _tokenStore.GetToken() : null;
        var (status, env, retryAfter) = await DispatchAsync(method, path, auth, body, cancellationToken)
            .ConfigureAwait(false);

        if (status is >= 200 and < 300 && env.ResultType == 0)
        {
            return env.Data;
        }

        bool expired = status == 401 || env.ResultType == 4;
        if (auth == AuthMode.Partner && expired && !isRetry
            && await TryRefreshAsync(staleAccess, cancellationToken).ConfigureAwait(false))
        {
            return await SendAsync(method, path, auth, body, cancellationToken, true).ConfigureAwait(false);
        }

        // A revoked session is worth forgetting; a merely expired access token is
        // not, since the caller may want to inspect it.
        if (env.ResultType == 2)
        {
            ClearTokens();
        }

        throw ToException(method, path, status, env, retryAfter);
    }

    private async Task<bool> TryRefreshAsync(string? staleAccess, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (staleAccess is not null && _tokenStore.GetToken() != staleAccess)
            {
                return true;
            }

            string? refreshToken = GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken)
                || string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
            {
                return false;
            }

            var (status, env, _) = await DispatchAsync(HttpMethod.Post, "/general/refreshApi",
                AuthMode.Public,
                new { refreshToken, clientId = ClientId, clientSecretKey = ClientSecret },
                cancellationToken).ConfigureAwait(false);

            if (status is < 200 or >= 300 || env.ResultType != 0
                || env.Data.ValueKind != JsonValueKind.Object
                || !env.Data.TryGetProperty("access_token", out var accessEl)
                || accessEl.ValueKind != JsonValueKind.String)
            {
                ClearTokens();
                return false;
            }

            string access = accessEl.GetString()!;
            string newRefresh = env.Data.TryGetProperty("refresh_token", out var refreshEl)
                && refreshEl.ValueKind == JsonValueKind.String
                ? refreshEl.GetString()!
                : refreshToken;
            SetTokens(access, newRefresh);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<(int Status, Envelope Envelope, string? RetryAfter)> DispatchAsync(
        HttpMethod method, string path, AuthMode auth, object? body, CancellationToken cancellationToken)
    {
        string? token = null;
        if (auth == AuthMode.Partner)
        {
            token = _tokenStore.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                // Dispatching anyway would only come back as an opaque 401.
                throw new AuthenticationException(
                    "No access token available. Call Auth.ConnectAsync, or set PartnerToken.",
                    new ApiErrorContext(0, null, null, default, method.Method, path, null));
            }
        }

        using var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("lang", _lang);

        if (body is not null && method != HttpMethod.Get)
        {
            string json = JsonSerializer.Serialize(body, SerializeOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
