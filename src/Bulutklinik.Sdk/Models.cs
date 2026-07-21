namespace Bulutklinik.Sdk;

/// <summary>
/// Result of <c>Auth.ConnectAsync</c>. When <see cref="TwoFactorRequired"/> is
/// true, pass <see cref="TwoFactorResponse"/> (with the SMS code) to
/// <c>ConnectWithTwoFactorAsync</c>.
/// </summary>
public sealed record LoginResult(bool TwoFactorRequired, string? TwoFactorResponse = null);

/// <summary>Plain card fields for SaveCard and inline payment.</summary>
public sealed record CardInfo(
    string CardHolder,
    string CardNumber,
    string CardExpMonth,
    string CardExpYear,
    string CardCvv);

/// <summary>Filtered doctor search parameters.</summary>
public sealed class SearchInput
{
    public IDictionary<string, object?>? SearchParams { get; set; }
    public IReadOnlyList<string>? OrderParams { get; set; }
    public IReadOnlyList<string>? OtherParams { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PerPageLimit { get; set; } = 20;
}

/// <summary>
/// Appointment payment parameters. Provide either <see cref="CardInfo"/> (a new
/// card) or <see cref="CardId"/> (a saved card).
/// </summary>
public sealed class PaymentInput
{
    public required object DoctorId { get; set; }
    public required string AppointmentDate { get; set; }
    public bool Is3D { get; set; }
    public bool TermsAccept { get; set; }
    public string AppointmentType { get; set; } = "interview";
    public CardInfo? CardInfo { get; set; }
    public object? CardId { get; set; }
    public int SaveCard { get; set; }
    public string DiscountCode { get; set; } = "";
    public string? CaseDetail { get; set; }
}

/// <summary>New-patient registration parameters.</summary>
public sealed class RegisterInput
{
    public required string Name { get; set; }
    public required string Surname { get; set; }
    public required string ApiUserName { get; set; }
    public required string PhoneNumber { get; set; }
    public required string Password { get; set; }
    public required string SmsVerificationCode { get; set; }
    public required string Response { get; set; }
    public int AcceptUserAgreement { get; set; } = 1;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

/// <summary>
/// Input for the registration verify step (<c>auth.VerifyRegistrationAsync</c>).
/// The endpoint requires a CAPTCHA token (<see cref="RecaptchaV2"/> or
/// <see cref="Captcha"/>) minted by a browser/human, and is authorized with the
/// configured partner token (it is behind <c>auth:apiusers</c>, not public).
/// </summary>
public sealed class VerifyRegistrationInput
{
    public required string Name { get; set; }
    public required string Surname { get; set; }
    /// <summary>Must start with <c>+</c> and country code, e.g. <c>+90 555 111 22 33</c>.</summary>
    public required string PhoneNumber { get; set; }
    /// <summary>Country dial code only, e.g. <c>+90</c> (matches <c>^\+\d{1,3}$</c>).</summary>
    public required string PhoneCode { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public int AcceptUserAgreement { get; set; } = 1;
    /// <summary>reCAPTCHA v2 token → sent as <c>g-recaptcha-response-v2</c>. Provide this or <see cref="Captcha"/>.</summary>
    public string? RecaptchaV2 { get; set; }
    /// <summary>Alternative CAPTCHA token → sent as <c>captcha</c>. Provide this or <see cref="RecaptchaV2"/>.</summary>
    public string? Captcha { get; set; }
    /// <summary>Optional structured agreement approvals, passed through verbatim.</summary>
    public object?[]? UserAgreements { get; set; }
}

/// <summary>Step 2 of the e-mail-branch registration (<c>auth.ConfirmRegistrationEmailAsync</c>).</summary>
public sealed class ConfirmRegistrationEmailInput
{
    public required string VerificationCode { get; set; }
    /// <summary>The <c>response</c> blob from <c>VerifyRegistration</c> (when <c>confirmationType</c> was <c>"email"</c>).</summary>
    public required string Response { get; set; }
    public object?[]? UserAgreements { get; set; }
}

/// <summary>Step 1 of social sign-up (public; no CAPTCHA and no partner token).</summary>
public sealed class VerifyRegistrationSocialInput
{
    public required string Name { get; set; }
    public required string Surname { get; set; }
    /// <summary>Must start with <c>+</c> and a country code; must not already be registered.</summary>
    public required string PhoneNumber { get; set; }
    public required string Password { get; set; }
    /// <summary>Social provider identifier (e.g. <c>"google"</c>, <c>"apple"</c>).</summary>
    public required string SocialType { get; set; }
    /// <summary>The social provider key/token identifying the user.</summary>
    public required string Key { get; set; }
    public string? Email { get; set; }
    public int AcceptUserAgreement { get; set; } = 1;
    public object?[]? UserAgreements { get; set; }
}

/// <summary>Step 2 of social sign-up. Does NOT mint tokens; log in via <c>Connect</c> (social) after.</summary>
public sealed class RegisterSocialInput
{
    public required string SmsVerificationCode { get; set; }
    /// <summary>The <c>response</c> blob from <c>VerifyRegistrationSocial</c>.</summary>
    public required string Response { get; set; }
    public object?[]? UserAgreements { get; set; }
}

/// <summary>Step 1 of password reset (<c>auth.ForgotPasswordAsync</c>).</summary>
public sealed class ForgotPasswordInput
{
    /// <summary>Must start with <c>+</c> and a country code; must be a registered number.</summary>
    public required string PhoneNumber { get; set; }
    /// <summary>Optional <c>YYYY-MM-DD</c>; required by installs that verify identity.</summary>
    public string? Birthdate { get; set; }
    /// <summary>reCAPTCHA v2 token → <c>g-recaptcha-response-v2</c>. Set this or <see cref="Captcha"/> (outside local env).</summary>
    public string? RecaptchaV2 { get; set; }
    public string? Captcha { get; set; }
}

/// <summary>Step 2 of password reset (<c>auth.ResetPasswordAsync</c>).</summary>
public sealed class ResetPasswordInput
{
    public required string SmsConfirmCode { get; set; }
    /// <summary>The <c>response</c> blob from <c>ForgotPassword</c>.</summary>
    public required string Response { get; set; }
    public required string Password { get; set; }
}

/// <summary>A new patient address (<c>addresses.AddAsync</c>).</summary>
public sealed class AddressInput
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    /// <summary>City id — the <c>location_id</c> from <c>Doctors.LocationsAsync</c>.</summary>
    public required object CityId { get; set; }
    /// <summary>District id — from <c>GET /getConfig</c> (<c>cities[].districts[]</c>).</summary>
    public required object DistrictId { get; set; }
    public required string Address { get; set; }
    public required string LocationLat { get; set; }
    public required string LocationLng { get; set; }
    /// <summary><c>1</c> makes this the default (the first address is always the default).</summary>
    public int? IsDefault { get; set; }
}

/// <summary>An address update by <see cref="Id"/> (<c>addresses.UpdateAsync</c>). Null fields are omitted.</summary>
public sealed class AddressUpdateInput
{
    public required object Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public object? CityId { get; set; }
    public object? DistrictId { get; set; }
    public string? Address { get; set; }
    public string? LocationLat { get; set; }
    public string? LocationLng { get; set; }
    public int? IsDefault { get; set; }
}

/// <summary>
/// AI meal-photo analysis parameters. Idiomatic names map to the API's snake_case
/// body (<c>portion_size</c>, <c>portion_grams</c>, <c>meal_type</c>).
/// <see cref="PortionGrams"/> is required when <see cref="PortionSize"/> is <c>"custom"</c>.
/// </summary>
public sealed class MealAnalyzeInput
{
    public required string Image { get; set; }
    public required string PortionSize { get; set; }
    public int? PortionGrams { get; set; }
    public required string MealType { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Laboratory test pre-order parameters. All three ids are required; they map to the
/// API's <c>testId</c>/<c>addressId</c>/<c>laboratoryId</c> body fields.
/// </summary>
public sealed record LabOrderInput(object TestId, object AddressId, object LaboratoryId);

/// <summary>Configuration for <see cref="BulutklinikClient"/>.</summary>
public sealed class BulutklinikClientOptions
{
    public BulutklinikEnvironment Environment { get; set; } = BulutklinikEnvironment.Production;
    public string? BaseUrl { get; set; }
    public string Lang { get; set; } = "tr";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? PartnerToken { get; set; }
    public ITokenStore? TokenStore { get; set; }
    public HttpClient? HttpClient { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
