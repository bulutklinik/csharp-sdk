using System.Net;
using System.Text;
using System.Text.Json;
using Bulutklinik.Sdk;
using Xunit;

namespace Bulutklinik.Sdk.Tests;

internal sealed class MockHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, (HttpStatusCode Status, string Body)> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public MockHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : string.Empty;
        Requests.Add(request);
        Bodies.Add(body);
        var (status, payload) = _responder(request, body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }
}

public class TransportTests
{
    private static (BulutklinikClient Client, MockHandler Handler) Make(
        Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder,
        ITokenStore? tokenStore = null, string? clientId = null, string? clientSecret = null, string? partnerToken = null)
    {
        var handler = new MockHandler(responder);
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            BaseUrl = "http://localhost",
            HttpClient = new HttpClient(handler),
            TokenStore = tokenStore,
            ClientId = clientId,
            ClientSecret = clientSecret,
            PartnerToken = partnerToken,
        });
        return (client, handler);
    }

    [Fact]
    public async Task UnwrapsDataAndSendsHeaders()
    {
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"searchedDoctors\":[]}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Doctors.QuickSearchAsync("kardiyo");

        Assert.Equal(JsonValueKind.Array, data.GetProperty("searchedDoctors").ValueKind);
        Assert.Equal("/patients/quickSearch", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Contains("\"searchText\":\"kardiyo\"", handler.Bodies[0]);
    }

    [Fact]
    public async Task RequestEscapeHatchCallsArbitraryPathWithBearer()
    {
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"ok\":true}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.RequestAsync(HttpMethod.Get, "/patients/customEndpoint");

        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("/patients/customEndpoint", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task RequestEscapeHatchSendsPublicPostBody()
    {
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"id\":7}}"));

        var data = await client.RequestAsync(HttpMethod.Post, "/general/somePublicEndpoint", "public",
            new { foo = "bar" });

        Assert.Equal(7, data.GetProperty("id").GetInt32());
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Contains("\"foo\":\"bar\"", handler.Bodies[0]);
    }

    [Fact]
    public async Task MapsValidation()
    {
        var (client, _) = Make((_, _) => (HttpStatusCode.UnprocessableEntity, "{\"resultType\":1,\"errorType\":\"validation\"}"),
            new InMemoryTokenStore("a"));
        await Assert.ThrowsAsync<ValidationException>(() => client.Doctors.BranchesAsync());
    }

    [Fact]
    public async Task MapsNumeric404ToNotFound()
    {
        var (client, _) = Make((_, _) => (HttpStatusCode.NotFound, "{\"resultType\":1,\"errorType\":1,\"errorMessage\":\"Bilinmeyen\"}"),
            new InMemoryTokenStore("a"));
        await Assert.ThrowsAsync<NotFoundException>(() => client.Doctors.QuickSearchAsync("x"));
    }

    [Fact]
    public async Task RefreshesOnceThenRetries()
    {
        int dataCalls = 0;
        var store = new InMemoryTokenStore("old", "r");
        var (client, _) = Make((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/general/refreshApi")
            {
                return (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"access_token\":\"new\",\"refresh_token\":\"r2\"}}");
            }
            dataCalls++;
            return dataCalls == 1
                ? (HttpStatusCode.Unauthorized, "{\"resultType\":4}")
                : (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"ok\":true}}");
        }, store, "c", "s");

        var data = await client.Measures.LastAsync();

        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("new", store.GetAccessToken());
    }

    [Fact]
    public async Task LogoutClearsStore()
    {
        var store = new InMemoryTokenStore("a", "r");
        var (client, _) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":2,\"errorMessage\":\"logged out\"}"), store);

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Measures.LastAsync());
        Assert.Null(store.GetAccessToken());
    }

    [Fact]
    public async Task ConnectStoresTokensAndFillsCredentials()
    {
        var store = new InMemoryTokenStore();
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"access_token\":\"t\",\"refresh_token\":\"r\"}}"),
            store, "c", "s");

        var result = await client.Auth.ConnectAsync("u", "p", "email");

        Assert.False(result.TwoFactorRequired);
        Assert.Equal("t", store.GetAccessToken());
        Assert.Contains("\"apiClientId\":\"c\"", handler.Bodies[0]);
    }

    [Fact]
    public async Task UsesPartnerToken()
    {
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":null}"),
            new InMemoryTokenStore("a"), partnerToken: "PT");

        await client.Measures.PartnerHealthInformationAsync(null, "5551112233", new IDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["type"] = "pulse", ["date_time"] = "2026-06-17 09:00", ["pulse"] = 72 },
        });

        Assert.Equal("Bearer PT", handler.Requests[^1].Headers.Authorization!.ToString());
    }
}
