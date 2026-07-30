using System.Text.Json;

namespace Bulutklinik.Sdk;

/// <summary>
/// Client configuration.
/// <para>
/// Set <see cref="PartnerToken"/> <b>or</b> <see cref="TokenStore"/>, not both —
/// either the literal or the store is the source of truth for the credential, and
/// guessing which one the caller meant is how credential bugs get shipped.
/// </para>
/// </summary>
public sealed class BulutklinikClientOptions
{
    /// <summary>
    /// OAuth client id from your portal application. Used by
    /// <c>Auth.ConnectAsync</c> and by the silent refresh.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret from your portal application.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Named environment preset. Ignored when <see cref="BaseUrl"/> is set.</summary>
    public BulutklinikEnvironment Environment { get; set; } = BulutklinikEnvironment.Production;

    /// <summary>
    /// API version segment. Ignored when <see cref="BaseUrl"/> is set. The
    /// <c>/outher</c> surface is route-for-route identical on both versions.
    /// </summary>
    public BulutklinikApiVersion ApiVersion { get; set; } = BulutklinikApiVersion.V3;

    /// <summary>Explicit base URL; overrides <see cref="Environment"/> + <see cref="ApiVersion"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Default <c>lang</c> header.</summary>
    public string Lang { get; set; } = "tr";

    /// <summary>
    /// An already-minted access token, for callers who do not want the SDK to log
    /// in. Seeds the default in-memory token store. Mutually exclusive with
    /// <see cref="TokenStore"/>.
    /// </summary>
    public string? PartnerToken { get; set; }

    /// <summary>
    /// Pluggable token source, read on every request so a long-running process can
    /// rotate the credential without being rebuilt. Mutually exclusive with
    /// <see cref="PartnerToken"/>.
    /// </summary>
    public ITokenStore? TokenStore { get; set; }

    public HttpClient? HttpClient { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Result of <c>Auth.ConnectAsync</c>.
/// <para>
/// When <see cref="TwoFactorRequired"/> is true no tokens were stored and
/// <see cref="TwoFactorResponse"/> carries the server's challenge blob.
/// </para>
/// </summary>
public sealed record LoginResult(
    bool TwoFactorRequired,
    string? TwoFactorResponse = null,
    JsonElement? PasswordPolicy = null);
