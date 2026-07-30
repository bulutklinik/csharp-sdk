using System.Text.Json;

namespace Bulutklinik.Sdk;

/// <summary>
/// The Bulutklinik partner API client. Construct once and reuse; service groups
/// are exposed as properties. Thread-safe.
/// <para>
/// Every data call runs on the company-scoped <c>/outher</c> surface: you act on
/// the patients of <b>your own company</b>, and the patient is named inline on
/// each request — there is no patient session.
/// </para>
/// <para>Already holding a token? Set <c>PartnerToken</c> and skip the login.</para>
/// </summary>
/// <example>
/// <code>
/// var client = new BulutklinikClient(new BulutklinikClientOptions
/// {
///     Environment = BulutklinikEnvironment.Test,
///     ClientId = Environment.GetEnvironmentVariable("BK_CLIENT_ID"),
///     ClientSecret = Environment.GetEnvironmentVariable("BK_CLIENT_SECRET"),
/// });
/// await client.Auth.ConnectAsync("svc@your-app.bulutklinik", "your-portal-password");
/// var branches = await client.Doctors.BranchesAsync();
/// var latest = await client.Measures.LastAsync(new Patient { IdentityNumber = "12345678901" });
/// </code>
/// </example>
public sealed class BulutklinikClient
{
    /// <summary>Obtain, refresh and revoke the access token.</summary>
    public AuthResource Auth { get; }

    /// <summary>Doctor discovery: search, branches, detail, city list.</summary>
    public DoctorsResource Doctors { get; }

    /// <summary>Doctor availability (materialized slots).</summary>
    public SlotsResource Slots { get; }

    /// <summary>Reserve, confirm, free-form booking, cancel, list, lookup.</summary>
    public AppointmentsResource Appointments { get; }

    /// <summary>Health measurements for a named patient, read and write.</summary>
    public MeasuresResource Measures { get; }

    /// <summary>Lab results for a named patient plus the orderable test catalog.</summary>
    public LaboratoryResource Laboratory { get; }

    /// <summary>Diet lists written by a dietitian, for a named patient.</summary>
    public DietsResource Diets { get; }

    /// <summary>
    /// The active token store. Write a newly issued partner token here to rotate
    /// the credential without rebuilding the client.
    /// </summary>
    public ITokenStore TokenStore { get; }

    private readonly Transport _transport;

    public BulutklinikClient(BulutklinikClientOptions? options = null)
    {
        options ??= new BulutklinikClientOptions();

        if (options.PartnerToken is not null && options.TokenStore is not null)
        {
            throw new ArgumentException(
                "Set either PartnerToken or TokenStore, not both. Seed your own store "
                + "with the token if you need custom persistence.",
                nameof(options));
        }

        string baseUrl = (options.BaseUrl ?? options.Environment.BaseUrl(options.ApiVersion)).TrimEnd('/');
        ITokenStore store = options.TokenStore ?? new InMemoryTokenStore(options.PartnerToken);
        HttpClient http = options.HttpClient ?? new HttpClient { Timeout = options.Timeout };

        var transport = new Transport(http, baseUrl, options.Lang, options.ClientId,
            options.ClientSecret, store);

        _transport = transport;
        TokenStore = store;
        Auth = new AuthResource(transport);
        Doctors = new DoctorsResource(transport);
        Slots = new SlotsResource(transport);
        Appointments = new AppointmentsResource(transport);
        Measures = new MeasuresResource(transport);
        Laboratory = new LaboratoryResource(transport);
        Diets = new DietsResource(transport);
    }

    /// <summary>
    /// Escape hatch: call any Bulutklinik API endpoint that does not yet have a
    /// typed resource method. The request still goes through the shared transport,
    /// so default headers, the chosen <paramref name="auth"/> mode (<c>partner</c>
    /// by default), envelope unwrapping and the typed exception hierarchy all
    /// apply. Returns the unwrapped <c>data</c> payload as a <c>JsonElement</c>,
    /// exactly like the typed resource methods. Prefer a typed resource method when
    /// one exists — reach for this only for endpoints the SDK does not cover yet.
    /// </summary>
    /// <param name="method">HTTP method (<c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>DELETE</c>).</param>
    /// <param name="path">Path relative to the configured base URL, leading slash included (e.g. <c>/outher/branches</c>).</param>
    /// <param name="auth">Auth mode: <c>"partner"</c> (default) or <c>"public"</c>; case-insensitive, blank/unknown maps to partner.</param>
    /// <param name="body">Optional JSON payload; omitted on <c>GET</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <example>
    /// <code>
    /// var branches = await client.RequestAsync(HttpMethod.Get, "/outher/branches");
    /// // "public" reaches unauthenticated endpoints outside the partner surface
    /// var config = await client.RequestAsync(HttpMethod.Get, "/general/getConfig", "public");
    /// </code>
    /// </example>
    public Task<JsonElement> RequestAsync(HttpMethod method, string path, string auth = "partner",
        object? body = null, CancellationToken cancellationToken = default)
    {
        AuthMode mode = string.Equals(auth?.Trim(), "public", StringComparison.OrdinalIgnoreCase)
            ? AuthMode.Public
            : AuthMode.Partner;
        return _transport.SendAsync(method, path, mode, body, cancellationToken);
    }
}
