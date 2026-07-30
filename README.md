# Bulutklinik.Sdk — Bulutklinik partner API SDK for .NET

Official Bulutklinik **partner** API SDK for .NET (net8.0). Async
(`HttpClient`), nullable-enabled, `System.Text.Json`.

This is a single-persona SDK: every call runs on the company-scoped `/outher`
surface with the partner token issued for your integration. You act on the
patients of **your own company**, and the patient is named inline on each
request — there is no login and no session. See [`DESIGN.md`](./DESIGN.md) for
the full wire contract.

> **1.0.0 is a breaking release.** The patient persona (login, registration,
> payments, AI analysis, address book) has been removed and the former
> `client.Partner.*` namespace was lifted to the client root. See
> [CHANGELOG.md](./CHANGELOG.md) and DESIGN.md §12 for the migration.

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
    ApiVersion = BulutklinikApiVersion.V3,           // V3 (default) | V4
    PartnerToken = Environment.GetEnvironmentVariable("BK_PARTNER_TOKEN"),
});

// 1) Find a doctor you can book
var result = await client.Doctors.SearchAsync(
    new Dictionary<string, object?> { ["withFreeText"] = "kardiyoloji" },
    currentPage: 1,
    orderParams: new[] { "slot" });
var doctorId = result.GetProperty("foundDoctors")[0].GetProperty("doctor_id").GetInt32();

// 2) Free slots
var schedule = await client.Slots.ScheduleAsync(doctorId, scheduleDate: "2026-08-01");

// 3) Hold one for a patient — named inline, no session
var held = await client.Appointments.ReserveWithoutAgreementAsync(slotId, doctorId, new Patient
{
    Name = "Ada",
    Surname = "Lovelace",
    PhoneNumber = "+905551112233",
});

// 4) Confirm before held.reservationExpired passes
await client.Appointments.CreateAsync(
    held.GetProperty("hash").GetString()!,
    held.GetProperty("outherProcessId").GetInt32());
```

Every method returns the unwrapped `data` payload as a `JsonElement`.

## Services

28 endpoints across six groups.

| Group                 | Methods |
|-----------------------|---------|
| `client.Doctors`      | `SearchAsync`, `BranchesAsync`, `DetailAsync`, `LocationsAsync` |
| `client.Slots`        | `ScheduleAsync` |
| `client.Appointments` | `ReserveAsync`, `ReserveWithoutAgreementAsync`, `InstantReserveAsync`, `CreateAsync`, `CreateWithoutSlotAsync`, `CancelWithoutSlotAsync`, `ListAsync`, `InfoAsync`, `CheckDoctorAsync` |
| `client.Measures`     | `LastAsync`, `ListAsync`, `GraphAsync`, `AddListAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `HealthInformationAsync` |
| `client.Laboratory`   | `CatalogAsync`, `CatalogDetailAsync`, `ResultsAsync`, `ResultDetailAsync` |
| `client.Diets`        | `ListAsync`, `DetailAsync` |

## Naming a patient

There is no session, so every patient-scoped call carries the patient in its
body — never in the URL, since a TCKN in a path segment would land in access
logs, proxy logs and error breadcrumbs.

**Reads** need only the reference fields. The server looks solely inside your own
company and never creates anything:

```csharp
await client.Measures.LastAsync(new Patient { IdentityNumber = "12345678901" });
await client.Diets.ListAsync(new Patient { PhoneNumber = "+905551112233" });
```

`IdentityNumber` is primary; `PhoneNumber` is a fallback accepted only when it
matches exactly one patient (the column is not unique — family members share
numbers). A patient you have never treated resolves to "not found", with the same
message as "not yours" so the endpoint cannot be used to probe for TCKNs.

**Writes** need `Name`, `Surname` and `PhoneNumber` too, because the patient is
created inside your company if absent:

```csharp
await client.Measures.AddListAsync(
    new Patient { Name = "Ada", Surname = "Lovelace", PhoneNumber = "+905551112233" },
    new IDictionary<string, object?>[]
    {
        new Dictionary<string, object?> { ["type"] = "pulse", ["date_time"] = "2026-06-17 09:31", ["pulse"] = 72 },
    });
```

## Booking

Two flows, depending on who collects the agreements and the payment:

```csharp
// (A) Hand off to the patient — returns a browser url for agreements + payment.
var held = await client.Appointments.ReserveAsync(slotId, doctorId, user);

// (B) You already collected them — returns a hash to confirm yourself.
var held = await client.Appointments.ReserveWithoutAgreementAsync(slotId, doctorId, user);
await client.Appointments.CreateAsync(hash, outherProcessId);
```

**Payment is never taken through the API.** No partner endpoint produces a
financial record; the browser hand-off in (A) is where payment happens. The SDK
returns the url verbatim and never opens or follows it.

`CreateWithoutSlotAsync` books a free-form range outside the slot grid, for
integrations running their own calendar; `CancelWithoutSlotAsync` reverses it —
and only it.

## Authentication

The partner token is **issued out of band** through the Bulutklinik Developer
Platform. It behaves like an API key: there is no login method, and the SDK
cannot renew it.

The token is read from an `ITokenStore` on **every** request, so a long-running
process can pick up a newly issued one without being rebuilt:

```csharp
public sealed class VaultTokenStore : ITokenStore
{
    public string? GetToken() => /* … */;
    public void SetToken(string? token) { /* … */ }
    public void Clear() { /* … */ }
}

var client = new BulutklinikClient(new BulutklinikClientOptions
{
    TokenStore = new VaultTokenStore(),
});

// …or rotate the default in-memory store in place:
client.TokenStore.SetToken(newlyIssuedToken);
```

Set `PartnerToken` **or** `TokenStore`, not both — the constructor throws
`ArgumentException` rather than guessing which one you meant.

### When the token expires

Tokens last about 30 days. An expired one comes back as `401` / `resultType 4`;
the SDK throws `AuthenticationException` and does **not** retry — there is
nothing to refresh. Recovery is operational: obtain a newly issued token and
write it into the store.

> This is the one behaviour that changed meaning in 1.0.0. On the patient SDK
> `resultType 4` meant "the SDK will fix this silently". Here it means the opposite.

An `AuthorizationException` (403) means the credential itself is wrong — either
the token lacks the `apiouther` scope, or it resolves to a user with no company.
The company boundary comes from the token, never from request input, so retrying
with different body parameters will not help.

## Health measures

```csharp
var reference = new Patient { IdentityNumber = "12345678901" };

// Write several measurements at once (max 200 per call, one transaction)
await client.Measures.AddListAsync(patient, new IDictionary<string, object?>[]
{
    new Dictionary<string, object?>
    {
        ["type"] = "tension", ["date_time"] = "2026-06-17 09:30",
        ["hypertension"] = 120, ["hypotension"] = 80,
    },
});

await client.Measures.LastAsync(reference);
await client.Measures.ListAsync(reference, "glucose", page: 1, glucoseType: 0); // 0=fasting, 1=postprandial
await client.Measures.GraphAsync(reference, "tension", period: 2);              // 2 = weekly
```

> Measurements are written to **your own company**. A value you write does not
> appear in the patient's Bulutklinik mobile app, and values they entered there
> are not visible to you. That is tenant isolation working as intended.

`Measures.HealthInformationAsync` is the legacy `teusan` bulk endpoint, marked
`[Obsolete]` and kept for existing integrations: it needs the `teusan` scope
instead of `apiouther`, takes a flat identity + phone number instead of a patient
object, and writes into the shared consumer tenant. Its patient matching is an **OR**, and it is loose: the lookup is
`identity OR phoneNumber` against the *global* user table and takes the first
row, so a phone number alone can resolve someone whose TCKN differs from the one
you sent. Send both, but do not assume they are checked as a pair — the
`apiouther` reads above do the opposite, scoping to your company and failing
closed on ambiguity. Prefer `AddListAsync` for anything new.

## Escape hatch

Not every endpoint has a typed method. `RequestAsync` reuses the same transport,
so headers, envelope unwrapping and typed exceptions all still apply:

```csharp
var data = await client.RequestAsync(HttpMethod.Get, "/outher/somethingNew");

// "public" reaches unauthenticated endpoints outside the partner surface,
// e.g. the city/district catalogue that feeds address forms.
var config = await client.RequestAsync(HttpMethod.Get, "/general/getConfig", "public");
```

## Errors

All exceptions derive from `BulutklinikException`:

`TransportException` (network) · `ApiException` → `ValidationException` (422),
`AuthenticationException` (401 / revoked / expired), `AuthorizationException`
(403), `NotFoundException` (404), `RateLimitException` (429).
Every `ApiException` carries a `Context` with `HttpStatus`, `ResultType`,
`ErrorType`, `Data`, `Method`, `Path` and `RetryAfter`.

```csharp
try
{
    await client.Measures.LastAsync(reference);
}
catch (RateLimitException e)
{
    Console.WriteLine($"retry after {e.Context.RetryAfter}");
}
catch (ValidationException e)
{
    Console.WriteLine(e.Context.Data);
}
```

Note that `/outher` reports most business-rule failures as HTTP **`501`** with
`resultType 1` — "patient not found in your company", "slot no longer free",
"doctor not bookable through your integration". It is not a server crash; read
the message.

## Development

```bash
dotnet build
dotnet test
```

## License

MIT
