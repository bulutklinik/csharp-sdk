using System.Net;
using System.Text.Json;
using Bulutklinik.Sdk;
using Xunit;

namespace Bulutklinik.Sdk.Tests;

public class AuthTests
{
    private const string TokensJson = "{\"resultType\":0,\"data\":{\"access_token\":\"AT\",\"refresh_token\":\"RT\"}}";

    private static readonly Patient Ref = new() { IdentityNumber = "12345678901" };

    /// <summary>A client carrying portal client credentials.</summary>
    private static (BulutklinikClient Client, MockHandler Handler) Make(
        Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder,
        ITokenStore? tokenStore = null)
    {
        var handler = new MockHandler(responder);
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            BaseUrl = "http://localhost",
            HttpClient = new HttpClient(handler),
            ClientId = "cid",
            ClientSecret = "csecret",
            TokenStore = tokenStore ?? new InMemoryTokenStore(),
        });
        return (client, handler);
    }

    [Fact]
    public async Task ConnectPostsPortalCredentialsAndStoresBothTokens()
    {
        var store = new InMemoryTokenStore();
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, TokensJson), store);

        var result = await client.Auth.ConnectAsync("svc@app.bulutklinik", "hunter2");

        Assert.False(result.TwoFactorRequired);
        Assert.Equal("/general/connectApi", handler.Requests[0].RequestUri!.AbsolutePath);
        // The login call is public — it is what produces the credential.
        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Contains("\"apiClientId\":\"cid\"", handler.Bodies[0]);
        Assert.Contains("\"apiUserName\":\"svc@app.bulutklinik\"", handler.Bodies[0]);
        Assert.Contains("\"loginMode\":\"email\"", handler.Bodies[0]);
        Assert.Equal("AT", store.GetToken());
        Assert.Equal("RT", store.GetRefreshToken());
    }

    [Fact]
    public async Task ConnectSurfacesTwoFactorChallengeAsAResult()
    {
        var store = new InMemoryTokenStore();
        var (client, _) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"response\":\"BLOB\"}}"), store);

        var result = await client.Auth.ConnectAsync("svc", "p");

        Assert.True(result.TwoFactorRequired);
        Assert.Equal("BLOB", result.TwoFactorResponse);
        Assert.Null(store.GetToken());
    }

    [Fact]
    public async Task ConnectRequiresClientCredentials()
    {
        var handler = new MockHandler((_, _) => (HttpStatusCode.OK, TokensJson));
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            BaseUrl = "http://localhost",
            HttpClient = new HttpClient(handler),
            PartnerToken = "PT",
        });

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.Auth.ConnectAsync("svc", "p"));
        Assert.Contains("ClientId and ClientSecret are required", ex.Message);
    }

    [Fact]
    public async Task RefreshesOnceThenRetriesWithTheNewToken()
    {
        int dataCalls = 0;
        var store = new InMemoryTokenStore("AT", "RT");
        var (client, handler) = Make((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/general/refreshApi")
            {
                return (HttpStatusCode.OK,
                    "{\"resultType\":0,\"data\":{\"access_token\":\"AT2\",\"refresh_token\":\"RT2\"}}");
            }
            dataCalls++;
            return dataCalls == 1
                ? (HttpStatusCode.Unauthorized, "{\"resultType\":4}")
                : (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"ok\":true}}");
        }, store);

        var data = await client.Measures.LastAsync(Ref);

        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("AT2", store.GetToken());
        Assert.Equal("RT2", store.GetRefreshToken());
        Assert.Contains("\"refreshToken\":\"RT\"", handler.Bodies[1]);
        Assert.Contains("\"clientSecretKey\":\"csecret\"", handler.Bodies[1]);
        Assert.Equal("Bearer AT2", handler.Requests[^1].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task RetriesAtMostOnceAndClearsOnFailedRefresh()
    {
        int refreshCalls = 0;
        var store = new InMemoryTokenStore("AT", "RT");
        var (client, _) = Make((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/general/refreshApi")
            {
                refreshCalls++;
                return (HttpStatusCode.Unauthorized, "{\"resultType\":1}");
            }
            return (HttpStatusCode.Unauthorized, "{\"resultType\":4}");
        }, store);

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Measures.LastAsync(Ref));
        Assert.Equal(1, refreshCalls);
        Assert.Null(store.GetToken());
    }

    [Fact]
    public async Task NoRefreshAttemptWithoutARefreshToken()
    {
        int refreshCalls = 0;
        var store = new InMemoryTokenStore("AT");
        var (client, _) = Make((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/general/refreshApi")
            {
                refreshCalls++;
            }
            return (HttpStatusCode.Unauthorized, "{\"resultType\":4}");
        }, store);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => client.Doctors.BranchesAsync());
        Assert.Contains("could not be refreshed", ex.Message);
        Assert.Equal(0, refreshCalls);
    }

    /// <summary>A store written against spec 1.0.x: access token only.</summary>
    private sealed class LegacyStore : ITokenStore
    {
        public string? Token;

        public string? GetToken() => Token;

        public void SetToken(string? token) => Token = token;

        public void Clear() => Token = null;
    }

    [Fact]
    public async Task StoreWithoutRefreshSupportStillRefreshesInMemory()
    {
        int dataCalls = 0;
        var legacy = new LegacyStore();
        var (client, _) = Make((req, _) =>
        {
            switch (req.RequestUri!.AbsolutePath)
            {
                case "/general/connectApi":
                    return (HttpStatusCode.OK, TokensJson);
                case "/general/refreshApi":
                    return (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"access_token\":\"AT2\"}}");
            }
            dataCalls++;
            return dataCalls == 1
                ? (HttpStatusCode.Unauthorized, "{\"resultType\":4}")
                : (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"ok\":true}}");
        }, legacy);

        await client.Auth.ConnectAsync("svc", "p");
        var data = await client.Measures.LastAsync(Ref);

        Assert.True(data.GetProperty("ok").GetBoolean());
        Assert.Equal("AT2", legacy.Token);
    }

    [Fact]
    public async Task DisconnectSendsAnEmptyBodyAndClearsTheStore()
    {
        var store = new InMemoryTokenStore("AT", "RT");
        var (client, handler) = Make((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":null}"), store);

        await client.Auth.DisconnectAsync();

        Assert.Equal("/general/disconnectApi", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer AT", handler.Requests[0].Headers.Authorization!.ToString());
        // The device-cleanup fields are deliberately not sent: the server's `device`
        // mapping has no default branch.
        Assert.Equal("{}", handler.Bodies[0]);
        Assert.Null(store.GetToken());
        Assert.Null(store.GetRefreshToken());
    }
}
