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
/// There is no silent refresh: a partner token is issued out of band and cannot
/// be renewed from here, so an expired one (<c>401</c> / <c>resultType 4</c>)
/// surfaces as an <see cref="AuthenticationException"/> instead of being retried.
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

    internal Transport(HttpClient http, string baseUrl, string lang, ITokenStore tokenStore)
    {
        _http = http;
        _baseUrl = baseUrl;
        _lang = lang;
        _tokenStore = tokenStore;
    }

    internal ITokenStore TokenStore => _tokenStore;

    internal async Task<JsonElement> SendAsync(HttpMethod method, string path, AuthMode auth,
        object? body, CancellationToken cancellationToken)
    {
        var (status, env, retryAfter) = await DispatchAsync(method, path, auth, body, cancellationToken)
            .ConfigureAwait(false);

        if (status is >= 200 and < 300 && env.ResultType == 0)
        {
            return env.Data;
        }

        // A revoked token is worth forgetting; an expired one is not, since the
        // caller may want to inspect it while installing a replacement.
        if (env.ResultType == 2)
        {
            _tokenStore.Clear();
        }

        throw ToException(method, path, status, env, retryAfter);
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
                throw new AuthenticationException("No partner token configured.",
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
