namespace Bulutklinik.Sdk;

/// <summary>
/// Pluggable source for the partner access token.
/// <para>
/// The token is read on <b>every</b> request, so pointing this at a file, cache,
/// database or secret manager lets a long-running process pick up a newly issued
/// token without being rebuilt. A null return means "no token"; the transport then
/// fails before dispatching rather than sending an anonymous request.
/// </para>
/// <para>Implementations must be thread-safe.</para>
/// </summary>
public interface ITokenStore
{
    string? GetToken();

    void SetToken(string? token);

    void Clear();
}

/// <summary>
/// Optional extension: a store that also persists the refresh token.
/// <para>
/// Implementing this is not required — an <see cref="ITokenStore"/> written
/// against spec 1.0.x keeps working. When the injected store does not implement
/// it, the SDK holds the refresh token in memory for the client's lifetime; the
/// only consequence is that a process restart needs <c>Auth.ConnectAsync</c>
/// rather than <c>Auth.RefreshAsync</c>.
/// </para>
/// </summary>
public interface IRefreshTokenStore : ITokenStore
{
    string? GetRefreshToken();

    void SetRefreshToken(string? token);
}

/// <summary>Default, thread-safe in-memory token store.</summary>
public sealed class InMemoryTokenStore : IRefreshTokenStore
{
    private readonly object _lock = new();
    private string? _token;
    private string? _refreshToken;

    public InMemoryTokenStore(string? token = null, string? refreshToken = null)
    {
        _token = token;
        _refreshToken = refreshToken;
    }

    public string? GetToken()
    {
        lock (_lock)
        {
            return _token;
        }
    }

    public void SetToken(string? token)
    {
        lock (_lock)
        {
            _token = token;
        }
    }

    public string? GetRefreshToken()
    {
        lock (_lock)
        {
            return _refreshToken;
        }
    }

    public void SetRefreshToken(string? token)
    {
        lock (_lock)
        {
            _refreshToken = token;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _token = null;
            _refreshToken = null;
        }
    }
}
