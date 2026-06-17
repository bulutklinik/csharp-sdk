# Bulutklinik.Sdk — Bulutklinik API SDK for .NET

Official Bulutklinik API SDK for .NET (net8.0). Async (`HttpClient`),
nullable-enabled, `System.Text.Json`.

Covers the patient flow: **auth, doctor search, slots, appointments, payments,
and health measures**. See [`DESIGN.md`](./DESIGN.md) for the full wire contract.

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
| `client.Auth`          | `Connect`, `ConnectWithTwoFactor`, `Register`, `Refresh`, `Disconnect` |
| `client.Doctors`       | `Branches`, `Locations`, `QuickSearch`, `Search`, `Detail` |
| `client.Slots`         | `Schedule` |
| `client.Appointments`  | `ReserveInterview`, `AddPhysical`, `Cancel` |
| `client.Payments`      | `CheckDiscountCode`, `GetCards`, `SaveCard`, `Pay`, `DeleteCard` |
| `client.Measures`      | `AddList`, `Add`, `Update`, `Delete`, `Last`, `List`, `Graph`, `PartnerHealthInformation` |

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

## Development

```bash
dotnet test tests/Bulutklinik.Sdk.Tests/Bulutklinik.Sdk.Tests.csproj -c Release
```

## License

MIT
