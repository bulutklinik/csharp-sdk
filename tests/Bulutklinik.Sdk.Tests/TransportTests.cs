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
    internal static readonly Patient Ref = new() { IdentityNumber = "12345678901" };

    /// <summary>
    /// A client wired to a mock handler. Unless <paramref name="tokenStore"/>
    /// overrides it, the credential is the partner token "PT".
    /// </summary>
    internal static (BulutklinikClient Client, MockHandler Handler) Make(
        Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder,
        ITokenStore? tokenStore = null)
    {
        var handler = new MockHandler(responder);
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            BaseUrl = "http://localhost",
            HttpClient = new HttpClient(handler),
            TokenStore = tokenStore ?? new InMemoryTokenStore("PT"),
        });
        return (client, handler);
    }

    private static (HttpStatusCode, string) Ok(HttpRequestMessage _, string __)
        => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"ok\":true}}");

    [Fact]
    public async Task UnwrapsDataAndSendsPartnerTokenAndLang()
    {
        var (client, handler) = Make((_, _) =>
            (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"foundDoctors\":[]}}"));

        var data = await client.Doctors.SearchAsync(
            new Dictionary<string, object?> { ["withFreeText"] = "kardiyoloji" }, 1, new[] { "slot" });

        Assert.Equal(JsonValueKind.Array, data.GetProperty("foundDoctors").ValueKind);
        Assert.Equal("/outher/search", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer PT", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Equal("tr", handler.Requests[0].Headers.GetValues("lang").Single());
        Assert.Contains("\"currentPage\":1", handler.Bodies[0]);
    }

    [Theory]
    [InlineData(BulutklinikApiVersion.V3, "https://apitest.bulutklinik.com/api/v3/outher/branches")]
    [InlineData(BulutklinikApiVersion.V4, "https://apitest.bulutklinik.com/api/v4/outher/branches")]
    public async Task ApiVersionChangesOnlyTheBaseUrl(BulutklinikApiVersion version, string expected)
    {
        var handler = new MockHandler(Ok);
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            Environment = BulutklinikEnvironment.Test,
            ApiVersion = version,
            PartnerToken = "PT",
            HttpClient = new HttpClient(handler),
        });

        await client.Doctors.BranchesAsync();

        Assert.Equal(expected, handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public void PartnerTokenAndTokenStoreTogetherIsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => new BulutklinikClient(new BulutklinikClientOptions
        {
            PartnerToken = "PT",
            TokenStore = new InMemoryTokenStore("OTHER"),
        }));
        Assert.Contains("not both", ex.Message);
    }

    [Fact]
    public void PartnerTokenSeedsTheDefaultStore()
    {
        var client = new BulutklinikClient(new BulutklinikClientOptions { PartnerToken = "PT" });
        Assert.Equal("PT", client.TokenStore.GetToken());
    }

    [Fact]
    public async Task MissingTokenFailsBeforeDispatch()
    {
        int dispatched = 0;
        var (client, _) = Make((req, body) =>
        {
            dispatched++;
            return Ok(req, body);
        }, new InMemoryTokenStore());

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Doctors.BranchesAsync());
        Assert.Equal(0, dispatched);
    }

    [Fact]
    public async Task TokenIsReadFromTheStoreOnEveryCall()
    {
        var store = new InMemoryTokenStore("first");
        var (client, handler) = Make(Ok, store);

        await client.Doctors.BranchesAsync();
        store.SetToken("second");
        await client.Doctors.BranchesAsync();

        Assert.Equal("Bearer first", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Equal("Bearer second", handler.Requests[1].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task RequestEscapeHatchDefaultsToPartner()
    {
        var (client, handler) = Make(Ok);

        var data = await client.RequestAsync(HttpMethod.Get, "/outher/customEndpoint");

        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("/outher/customEndpoint", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer PT", handler.Requests[0].Headers.Authorization!.ToString());
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
        var (client, _) = Make((_, _) =>
            (HttpStatusCode.UnprocessableEntity, "{\"resultType\":1,\"errorType\":\"validation\"}"));
        await Assert.ThrowsAsync<ValidationException>(() => client.Doctors.BranchesAsync());
    }

    [Fact]
    public async Task MapsForbiddenToAuthorization()
    {
        var (client, _) = Make((_, _) => (HttpStatusCode.Forbidden, "{\"resultType\":1}"));
        await Assert.ThrowsAsync<AuthorizationException>(() => client.Doctors.BranchesAsync());
    }

    [Fact]
    public async Task MapsNumeric404ToNotFound()
    {
        var (client, _) = Make((_, _) =>
            (HttpStatusCode.NotFound, "{\"resultType\":1,\"errorType\":1,\"errorMessage\":\"Bilinmeyen\"}"));
        await Assert.ThrowsAsync<NotFoundException>(() => client.Doctors.BranchesAsync());
    }

    [Fact]
    public async Task ExpiredTokenIsNotRetriedWithoutARefreshToken()
    {
        int attempts = 0;
        var store = new InMemoryTokenStore("expired");
        var (client, _) = Make((_, _) =>
        {
            attempts++;
            return (HttpStatusCode.Unauthorized, "{\"resultType\":4,\"errorMessage\":\"You must log in.\"}");
        }, store);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => client.Measures.LastAsync(Ref));

        Assert.Contains("could not be refreshed", ex.Message);
        Assert.Equal(1, attempts);
        // An expired token is kept: the caller may want to inspect it while
        // installing the replacement. Only a revoked one is cleared.
        Assert.Equal("expired", store.GetToken());
    }

    [Fact]
    public async Task LogoutClearsStore()
    {
        var store = new InMemoryTokenStore("revoked");
        var (client, _) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":2,\"errorMessage\":\"logged out\"}"), store);

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Measures.LastAsync(Ref));
        Assert.Null(store.GetToken());
    }
}
