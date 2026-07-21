# Changelog

All notable changes to `Bulutklinik.Sdk` are documented here. The format is based
on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
