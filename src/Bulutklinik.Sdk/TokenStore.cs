namespace Bulutklinik.Sdk;

/// <summary>
/// Pluggable token persistence. The default is in-memory; implement this to
/// persist tokens elsewhere. A null return means "no token".
/// </summary>
public interface ITokenStore
{
    string? GetAccessToken();

    string? GetRefreshToken();

    void SetTokens(string accessToken, string? refreshToken);

    void Clear();
}

/// <summary>Default, thread-safe in-memory token store.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly object _lock = new();
    private string? _accessToken;
    private string? _refreshToken;

    public InMemoryTokenStore(string? accessToken = null, string? refreshToken = null)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
    }

    public string? GetAccessToken()
    {
        lock (_lock)
        {
            return _accessToken;
        }
    }

    public string? GetRefreshToken()
    {
        lock (_lock)
        {
            return _refreshToken;
        }
    }

    public void SetTokens(string accessToken, string? refreshToken)
    {
        lock (_lock)
        {
            _accessToken = accessToken;
            _refreshToken = refreshToken;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _accessToken = null;
            _refreshToken = null;
        }
    }
}
