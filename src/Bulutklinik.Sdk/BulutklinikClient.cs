using System.Text.Json;

namespace Bulutklinik.Sdk;

/// <summary>
/// The Bulutklinik API client. Construct once and reuse; service groups are
/// exposed as properties. Thread-safe.
/// </summary>
/// <example>
/// <code>
/// var client = new BulutklinikClient(new BulutklinikClientOptions
/// {
///     Environment = BulutklinikEnvironment.Test,
///     ClientId = "…",
///     ClientSecret = "…",
/// });
/// var login = await client.Auth.ConnectAsync("patient@example.com", "•••", "email");
/// var result = await client.Doctors.QuickSearchAsync("kardiyo");
/// </code>
/// </example>
public sealed class BulutklinikClient
{
    public AuthResource Auth { get; }
    public DoctorsResource Doctors { get; }
    public SlotsResource Slots { get; }
    public AppointmentsResource Appointments { get; }
    public PaymentsResource Payments { get; }
    public MeasuresResource Measures { get; }
    public SkinResource Skin { get; }
    public MealsResource Meals { get; }
    public LaboratoryResource Laboratory { get; }
    public DietsResource Diets { get; }
    public AddressesResource Addresses { get; }

    /// <summary>
    /// The company-scoped partner surface (<c>/outher</c>). Uses the configured
    /// <c>PartnerToken</c> instead of a patient login; data is limited to your own
    /// company and the patient is named inline on each call.
    /// </summary>
    public PartnerNamespace Partner { get; }

    /// <summary>The active token store.</summary>
    public ITokenStore TokenStore { get; }

    private readonly Transport _transport;

    public BulutklinikClient(BulutklinikClientOptions? options = null)
    {
        options ??= new BulutklinikClientOptions();
        string baseUrl = (options.BaseUrl ?? options.Environment.BaseUrl()).TrimEnd('/');
        ITokenStore store = options.TokenStore ?? new InMemoryTokenStore();
        HttpClient http = options.HttpClient ?? new HttpClient { Timeout = options.Timeout };

        var transport = new Transport(http, baseUrl, options.Lang, options.ClientId,
            options.ClientSecret, options.PartnerToken, store);

        _transport = transport;
        TokenStore = store;
        Auth = new AuthResource(transport);
        Doctors = new DoctorsResource(transport);
        Slots = new SlotsResource(transport);
        Appointments = new AppointmentsResource(transport);
        Payments = new PaymentsResource(transport);
        Measures = new MeasuresResource(transport);
        Skin = new SkinResource(transport);
        Meals = new MealsResource(transport);
        Laboratory = new LaboratoryResource(transport);
        Diets = new DietsResource(transport);
        Addresses = new AddressesResource(transport);
        Partner = new PartnerNamespace(transport);
    }

    /// <summary>
    /// Escape hatch: call any Bulutklinik API endpoint that does not yet have a
    /// typed resource method. The request still goes through the shared transport,
    /// so default headers, the chosen <paramref name="auth"/> mode (<c>bearer</c>
    /// by default), silent token refresh + retry, envelope unwrapping and the typed
    /// exception hierarchy all apply. Returns the unwrapped <c>data</c> payload as a
    /// <c>JsonElement</c>, exactly like the typed resource methods. Prefer a typed
    /// resource method when one exists — reach for this only for endpoints the SDK
    /// does not cover yet.
    /// </summary>
    /// <param name="method">HTTP method (<c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>DELETE</c>).</param>
    /// <param name="path">Path relative to the configured base URL, leading slash included (e.g. <c>/patients/allBranches</c>).</param>
    /// <param name="auth">Auth mode: <c>"public"</c>, <c>"bearer"</c> (default) or <c>"partner"</c>; case-insensitive, blank/unknown maps to bearer.</param>
    /// <param name="body">Optional JSON payload; omitted on <c>GET</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <example>
    /// <code>
    /// var branches = await client.RequestAsync(HttpMethod.Get, "/patients/allBranches");
    /// var created = await client.RequestAsync(HttpMethod.Post, "/patients/someNewEndpoint",
    ///     body: new { foo = "bar" });
    /// </code>
    /// </example>
    public Task<JsonElement> RequestAsync(HttpMethod method, string path, string auth = "bearer",
        object? body = null, CancellationToken cancellationToken = default)
    {
        AuthMode mode = auth?.Trim().ToLowerInvariant() switch
        {
            "public" => AuthMode.Public,
            "partner" => AuthMode.Partner,
            _ => AuthMode.Bearer,
        };
        return _transport.SendAsync(method, path, mode, body, cancellationToken);
    }
}
