# Bulutklinik.Sdk — Bulutklinik API SDK for .NET

Official Bulutklinik API SDK for .NET (net8.0). Async (`HttpClient`),
nullable-enabled, `System.Text.Json`.

Covers the patient flow: **auth, doctor search, slots, appointments, payments,
health measures, AI image analysis, lab results, and diet lists**. See
[`DESIGN.md`](./DESIGN.md) for the full wire contract.

## Install

```bash
dotnet add package Bulutklinik.Sdk
```

## Quick start

```csharp
using Bulutklinik.Sdk;

var client = new BulutklinikClient(new BulutklinikClientOptions
{
    Environment = BulutklinikEnvironment.Production, // Production | Test | Local
    ClientId = "clientId",
    ClientSecret = "clientSecret",
});

// 1) Log in (tokens are stored automatically)
var login = await client.Auth.ConnectAsync("patient@example.com", "•••••••", "email");
if (login.TwoFactorRequired)
{
    await client.Auth.ConnectWithTwoFactorAsync("123456", login.TwoFactorResponse!);
}

// 2) Search — returns a System.Text.Json JsonElement (the "data" payload)
var result = await client.Doctors.SearchAsync(new SearchInput
{
    SearchParams = new Dictionary<string, object?> { ["withFreeText"] = "kardiyoloji" },
    OrderParams = new[] { "slot" },
    OtherParams = new[] { "isInterviewable" },
});

// 3) Slots, then 4) reserve ("yyyy-MM-dd HH:mm")
var doctorId = result.GetProperty("foundDoctors")[0].GetProperty("doctor_id").GetInt32();
var slots = await client.Slots.ScheduleAsync(doctorId, "interview");
await client.Appointments.ReserveInterviewAsync(doctorId, "2026-06-20 14:30");
```

## Services

| Property               | Methods (all `…Async`) |
|------------------------|------------------------|
| `client.Auth`          | `Connect`, `ConnectWithTwoFactor`, `VerifyRegistration`, `Register`, `Refresh`, `Disconnect` |
| `client.Doctors`       | `Branches`, `Locations`, `QuickSearch`, `Search`, `Detail` |
| `client.Slots`         | `Schedule` |
| `client.Appointments`  | `ReserveInterview`, `AddPhysical`, `Cancel` |
| `client.Payments`      | `CheckDiscountCode`, `GetCards`, `SaveCard`, `Pay`, `DeleteCard` |
| `client.Measures`      | `AddList`, `Add`, `Update`, `Delete`, `Last`, `List`, `Graph`, `PartnerHealthInformation` |
| `client.Skin`          | `Analyze` |
| `client.Meals`         | `Analyze` |
| `client.Laboratory`    | `Results`, `ResultDetail`, `Catalog`, `CatalogDetail`, `Order` |
| `client.Diets`         | `List`, `Detail` |

Data methods return `System.Text.Json.JsonElement`. All accept a `CancellationToken`.

## Authentication & tokens

- `ConnectAsync` / `ConnectWithTwoFactorAsync` / `RegisterAsync` store tokens automatically.
- On a `401` (or `resultType 4`), the SDK silently refreshes once and retries
  (thread-safe, single shared refresh).
- Inject a custom store via `BulutklinikClientOptions.TokenStore` (implement `ITokenStore`).

## Errors

All extend `BulutklinikException`: `TransportException` and `ApiException` →
`ValidationException` (422), `AuthenticationException` (401 / logout),
`AuthorizationException` (403), `NotFoundException` (404), `RateLimitException`
(429). Details live on `ApiException.Context`.

```csharp
try
{
    await client.Payments.PayAsync(input);
}
catch (RateLimitException e)
{
    Console.WriteLine($"retry after {e.Context.RetryAfter}");
}
catch (ValidationException e)
{
    Console.WriteLine($"invalid: {e.Context.Data}");
}
```

## Payments (3-D Secure)

`Payments.PayAsync` returns data containing `payment3DUrl` on a 3DS flow — a
browser URL to open. The bank → server callback completes the capture.

## AI image analysis

`Skin.AnalyzeAsync` ("Cildimde Neyim Var") classifies one or more skin photos;
`Meals.AnalyzeAsync` estimates calories/nutrition from a meal photo. Both return
the `data` payload verbatim as a `JsonElement`.

```csharp
// Skin — a loose array of images (branch_id optional), like Measures.AddList
var skin = await client.Skin.AnalyzeAsync(new IDictionary<string, object?>[]
{
    new Dictionary<string, object?> { ["image"] = base64Jpeg, ["branch_id"] = 42 },
});
// The opaque case_detail blob can be forwarded verbatim as a payment's caseDetail.

// Meals — typed input mapped to the API's snake_case body (portion_grams/note
// are sent only when set; PortionGrams is required when PortionSize == "custom")
var meal = await client.Meals.AnalyzeAsync(new MealAnalyzeInput
{
    Image = base64Jpeg,
    PortionSize = "custom",
    PortionGrams = 300,
    MealType = "lunch",
    Note = "az yağlı",
});
```

## Development

```bash
dotnet test tests/Bulutklinik.Sdk.Tests/Bulutklinik.Sdk.Tests.csproj -c Release
```

## License

MIT
