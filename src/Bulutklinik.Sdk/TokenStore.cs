namespace Bulutklinik.Sdk;

/// <summary>
/// Pluggable source for the partner token.
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

/// <summary>Default, thread-safe in-memory token store.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly object _lock = new();
    private string? _token;

    public InMemoryTokenStore(string? token = null) => _token = token;

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

    public void Clear()
    {
        lock (_lock)
        {
            _token = null;
        }
    }
}
