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

    /// <summary>The active token store.</summary>
    public ITokenStore TokenStore { get; }

    public BulutklinikClient(BulutklinikClientOptions? options = null)
    {
        options ??= new BulutklinikClientOptions();
        string baseUrl = (options.BaseUrl ?? options.Environment.BaseUrl()).TrimEnd('/');
        ITokenStore store = options.TokenStore ?? new InMemoryTokenStore();
        HttpClient http = options.HttpClient ?? new HttpClient { Timeout = options.Timeout };

        var transport = new Transport(http, baseUrl, options.Lang, options.ClientId,
            options.ClientSecret, options.PartnerToken, store);

        TokenStore = store;
        Auth = new AuthResource(transport);
        Doctors = new DoctorsResource(transport);
        Slots = new SlotsResource(transport);
        Appointments = new AppointmentsResource(transport);
        Payments = new PaymentsResource(transport);
        Measures = new MeasuresResource(transport);
    }
}
