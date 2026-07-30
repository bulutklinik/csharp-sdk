# Changelog

All notable changes to `Bulutklinik.Sdk` are documented here. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0]

Restores the `auth` group. 1.0.x removed it on a mistaken premise: that a partner
token could only be issued out of band. It cannot be minted by a
*client-credentials* grant — there is no `oauth/token` route — but it **is** minted
by the password grant at `connectApi`, using the client id, client secret and
service identity the Developer Platform issues per application. That is exactly
what the portal's own quick-start shows.

This release is **additive**. All 28 data methods keep their paths, bodies and
signatures; a 1.0.x integration that supplies `PartnerToken` keeps working.

### Added

- **`client.Auth`** with three methods:
  - `ConnectAsync` — `POST /general/connectApi`, public. Exchanges the portal
    credentials for an access + refresh token pair and stores both. Returns a
    login result; if the account has SMS 2FA enabled it reports
    `TwoFactorRequired` with the server's challenge instead of throwing.
  - `RefreshAsync` — `POST /general/refreshApi`, public. Rotates both tokens.
  - `DisconnectAsync` — `POST /general/disconnectApi`. Revokes the access token and
    its refresh tokens, then clears the store. Sent with an empty body on
    purpose: the endpoint's optional `device` mapping has no default branch
    server-side.
- **`ClientId` / `ClientSecret`** client options, used by `ConnectAsync` and by the
  silent refresh.
- **Silent refresh + single retry** on `401` / `resultType 4`, concurrency-safe —
  simultaneous failures share one in-flight refresh rather than stampeding.
- **Optional refresh-token persistence** on the token store. Implementing it is
  not required: a store written against the 1.0.x interface keeps working, and
  the SDK then holds the refresh token in memory for the client's lifetime (a
  process restart needs `Auth.ConnectAsync` rather than a refresh).
- `IRefreshTokenStore`, the optional `ITokenStore` extension that carries it;
  `InMemoryTokenStore` implements it and now takes an optional refresh token.

### Changed

- `resultType 4` no longer terminates immediately. It triggers the refresh path;
  the error surfaces only when there is no refresh token or the refresh itself
  fails, and its message now points at `Auth.ConnectAsync`.

## [1.0.1]

Documentation and contract corrections found by auditing the SDKs against the
API source before release. No wire change.

### Fixed

- `doctors.search` no longer lets `searchParams` default to an empty map. The
  server rule is `required|array` and PHP's `required` rejects an empty array, so
  `{}` was a guaranteed `422` rather than an unfiltered search.
- Corrected the `measures.HealthInformationAsync` note. The defect it described — the API
  nulling `identity` before the patient lookup — was fixed API-side on
  2026-07-21. What actually remains is looser and worth knowing: the lookup is
  `identity OR phoneNumber` against the global user table and takes the first
  row, so a phone number alone can resolve a person whose TCKN differs from the
  one you sent.

## [1.0.0]

The SDK becomes **partner-only**. Everything that required a patient login is
gone; the company-scoped `/outher` surface that shipped under `client.Partner`
in 0.6.0 is now the client root. See `DESIGN.md` §12 for the full migration.

### Changed — BREAKING

- **`client.Partner.<Group>` → `client.<Group>`.** The six partner groups
  (`Doctors`, `Slots`, `Appointments`, `Measures`, `Laboratory`, `Diets`) moved
  to the root. Their paths, bodies and behaviour are unchanged — this is a
  rename. Resource classes lost the `Partner` prefix (`PartnerDoctorsResource` →
  `DoctorsResource`); `PartnerNamespace` is gone.
- **`ITokenStore` now holds one partner token**: `GetToken()` /
  `SetToken(string?)` / `Clear()` replace `GetAccessToken()` /
  `GetRefreshToken()` / `SetTokens()`. `InMemoryTokenStore` takes the token as
  its single constructor argument.
- **`PartnerToken` is now the client's credential** and is required for every
  call. Setting both `PartnerToken` and `TokenStore` throws `ArgumentException`
  at construction rather than silently picking one.
- **No silent refresh.** A `401` / `resultType 4` throws `AuthenticationException`
  with no retry — a partner token is issued out of band and cannot be renewed
  from here. Install a newly issued token in the token store instead.
- **A missing token fails before dispatch** with `AuthenticationException`,
  rather than sending an anonymous request that returns an opaque `401`.
- **`RequestAsync` `auth` defaults to `"partner"`**; the `"bearer"` mode no
  longer exists. `"public"` remains, for unauthenticated endpoints outside the
  surface.
- `Measures.PartnerHealthInformationAsync` → `Measures.HealthInformationAsync`.
- `Doctors.SearchAsync` no longer accepts `otherParams` / `perPageLimit`, and
  `orderParams` no longer accepts `point` — the `/outher` search has neither.

### Added

- **`BulutklinikApiVersion` (`V3` / `V4`) and `BulutklinikClientOptions.ApiVersion`.**
  Every path is version-agnostic, so targeting v4 is configuration, not a code
  change. Default stays `V3`.

### Removed

- `client.Auth` (all 11 methods), `client.Payments` (5), `client.Skin`,
  `client.Meals`, `client.Addresses` (4) — no company-scoped equivalent exists.
- The patient-persona `Doctors` / `Slots` / `Appointments` / `Measures` /
  `Laboratory` / `Diets` that lived at the root in 0.6.0.
- `BulutklinikClientOptions.ClientId` / `.ClientSecret`.
- The `LoginResult`, `ConnectInput`, `RegisterInput`, `VerifyRegistrationInput`,
  `ConfirmRegistrationEmailInput`, `VerifyRegistrationSocialInput`,
  `RegisterSocialInput`, `ForgotPasswordInput`, `ResetPasswordInput`,
  `AddressInput`, `AddressUpdateInput`, `SearchInput`, `ScheduleInput`,
  `DiscountInput`, `CardInfo`, `PaymentInput`, `MealInput` and `LabOrderInput`
  types.

## [0.6.0]

### Added

- `client.Auth.ConfirmRegistrationEmailAsync(input)` — the **required** e-mail-branch middle
  step of registration (`POST /patients/emailConfirmationRegister`). A headerless SDK
  caller always gets `confirmationType "email"` from `VerifyRegistrationAsync`; confirm the
  e-mailed code here to receive the SMS blob that `RegisterAsync` consumes (without it,
  `RegisterAsync` returns 501).
- Social sign-up: `client.Auth.VerifyRegistrationSocialAsync(input)` +
  `client.Auth.RegisterSocialAsync(input)` (both public; `RegisterSocialAsync` does not
  auto-login — call `ConnectAsync` with loginMode `social` after).
- Password reset: `client.Auth.ForgotPasswordAsync(input)` + `client.Auth.ResetPasswordAsync(input)`.
- `client.Appointments.ListAsync(page?)` (`GET /patients/userAppointments`) — the source of the
  `event_id` that `CancelAsync` requires — and `client.Appointments.ReservationsAsync()`.
- New `client.Addresses` group (`ListAsync`/`AddAsync`/`UpdateAsync`/`DeleteAsync`) over
  `/patients/userAddress`, required by `Laboratory.OrderAsync` (which needs an `addressId`).
- Types: `ConfirmRegistrationEmailInput`, `VerifyRegistrationSocialInput`,
  `RegisterSocialInput`, `ForgotPasswordInput`, `ResetPasswordInput`, `AddressInput`,
  `AddressUpdateInput`.

## [0.5.0]

### Added

- `client.Auth.VerifyRegistrationAsync(input)` — step 1 of registration
  (`POST /patients/verifyAddingNewPatient`): sends the verification code and returns
  the raw `data` (`JsonElement`) holding the encrypted `response` blob to pass to
  `RegisterAsync`. Uses the configured partner token (`auth:apiusers`, not public)
  and requires a browser-minted CAPTCHA token (`RecaptchaV2` or `Captcha`).
- Type: `VerifyRegistrationInput`.

## [0.4.0]

### Added

- `client.Laboratory` — the patient's lab results, orderable test catalog and test
  pre-ordering: `ResultsAsync(page?)` (`GET /patients/userLabTestList/{page?}`),
  `ResultDetailAsync(testId)` (`GET /patients/userLabTestDetail/{testId}`, `testId` is a
  string — may carry a `-lab` suffix), `CatalogAsync` (`GET /patients/allLaboratoryTests`),
  `CatalogDetailAsync(id)` (`GET /patients/laboratoryTestDetail/{id}`) and
  `OrderAsync(input)` (`POST /patients/addNewLaboratoryTest`).
- `client.Diets` — the patient's diet lists: `ListAsync(page?)` (`GET /patients/dietLists/{page?}`)
  and `DetailAsync(listId)` (`GET /patients/diet/{listId}`).
- Type: `LabOrderInput`.

## [0.3.0]

### Added

- `client.Skin.AnalyzeAsync(images)` — "Cildimde Neyim Var" AI skin-lesion analysis
  (`POST /patients/imageCheck`). Returns per-image lesion `label`, a Turkish AI
  `comment`, `confidence`, `possible_icd` and an opaque `case_detail` blob (which
  can be forwarded as a payment's `caseDetail`).
- `client.Meals.AnalyzeAsync(input)` — AI meal-photo calorie/nutrition estimation
  (`POST /patients/imageAnalyzeMeal`).
- Type: `MealAnalyzeInput`.

## [0.2.0]

### Added

- `client.RequestAsync(...)` escape hatch for calling any endpoint not yet covered
  by a typed resource method (DESIGN.md §7.2).

## [0.1.0]

### Added

- Initial release: `Auth`, `Doctors`, `Slots`, `Appointments`, `Payments`,
  `Measures` service groups over a shared transport with silent token refresh.
