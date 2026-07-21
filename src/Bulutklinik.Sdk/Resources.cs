using System.Text.Json;

namespace Bulutklinik.Sdk;

/// <summary>Login, 2FA, token refresh, registration and logout.</summary>
public sealed class AuthResource
{
    private readonly Transport _t;

    internal AuthResource(Transport transport) => _t = transport;

    public async Task<LoginResult> ConnectAsync(string apiUserName, string? apiUserPassword, string loginMode,
        string? clientId = null, string? clientSecret = null, string? withPhoneNumber = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["apiUserName"] = apiUserName,
            ["apiUserPassword"] = apiUserPassword,
            ["apiClientId"] = clientId ?? _t.ClientId,
            ["apiSecretKey"] = clientSecret ?? _t.ClientSecret,
            ["loginMode"] = loginMode,
        };
        if (withPhoneNumber is not null)
        {
            body["withPhoneNumber"] = withPhoneNumber;
        }

        var data = await _t.SendAsync(HttpMethod.Post, "/general/connectApi", AuthMode.Public, body, cancellationToken)
            .ConfigureAwait(false);
        return FinishLogin(data);
    }

    public async Task ConnectWithTwoFactorAsync(string smsVerificationCode, string response,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["smsVerificationCode"] = smsVerificationCode,
            ["response"] = response,
        };
        StoreTokens(await _t.SendAsync(HttpMethod.Post, "/general/connectApiWithTwoFactor", AuthMode.Public, body, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Step 1 of registration: send the SMS/e-mail verification code and return the
    /// raw <c>data</c> containing the encrypted <c>response</c> blob. Uses the
    /// configured partner token (the endpoint is behind <c>auth:apiusers</c>, not
    /// public). A CAPTCHA token (<c>RecaptchaV2</c> or <c>Captcha</c>), minted by a
    /// browser/human, is required. Feed the returned <c>response</c> (and the code the
    /// user receives) into <see cref="RegisterAsync"/>.
    /// </summary>
    public Task<JsonElement> VerifyRegistrationAsync(VerifyRegistrationInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = input.Name,
            ["surname"] = input.Surname,
            ["phoneNumber"] = input.PhoneNumber,
            ["phone_code"] = input.PhoneCode,
            ["email"] = input.Email,
            ["password"] = input.Password,
            ["passwordAgain"] = input.Password,
            ["acceptUserAgreement"] = input.AcceptUserAgreement == 0 ? 1 : input.AcceptUserAgreement,
        };
        if (input.RecaptchaV2 is not null) body["g-recaptcha-response-v2"] = input.RecaptchaV2;
        if (input.Captcha is not null) body["captcha"] = input.Captcha;
        if (input.UserAgreements is not null) body["userAgreements"] = input.UserAgreements;
        return _t.SendAsync(HttpMethod.Post, "/patients/verifyAddingNewPatient", AuthMode.Partner, body, cancellationToken);
    }

    public async Task RegisterAsync(RegisterInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = input.Name,
            ["surname"] = input.Surname,
            ["apiUserName"] = input.ApiUserName,
            ["phoneNumber"] = input.PhoneNumber,
            ["password"] = input.Password,
            ["smsVerificationCode"] = input.SmsVerificationCode,
            ["response"] = input.Response,
            ["acceptUserAgreement"] = input.AcceptUserAgreement == 0 ? 1 : input.AcceptUserAgreement,
            ["apiClientId"] = input.ClientId ?? _t.ClientId,
            ["apiSecretKey"] = input.ClientSecret ?? _t.ClientSecret,
        };
        StoreTokens(await _t.SendAsync(HttpMethod.Post, "/patients/addNewPatient", AuthMode.Public, body, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Step 2 of e-mail-branch registration. When <c>VerifyRegistrationAsync</c> returned
    /// <c>confirmationType "email"</c>, confirm the e-mailed code here with the same
    /// <c>response</c> blob; the server sends an SMS code and returns a fresh <c>response</c>
    /// blob (<c>confirmationType "sms"</c>) to feed into <c>RegisterAsync</c>. Public.
    /// </summary>
    public Task<JsonElement> ConfirmRegistrationEmailAsync(ConfirmRegistrationEmailInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["verificationCode"] = input.VerificationCode,
            ["response"] = input.Response,
        };
        if (input.UserAgreements is not null) body["userAgreements"] = input.UserAgreements;
        return _t.SendAsync(HttpMethod.Post, "/patients/emailConfirmationRegister", AuthMode.Public, body, cancellationToken);
    }

    /// <summary>
    /// Step 1 of social sign-up: send the SMS code and return the raw <c>data</c> holding
    /// the <c>response</c> blob. Public — no CAPTCHA and no partner token. Feed
    /// <c>response</c> + the SMS code into <c>RegisterSocialAsync</c>.
    /// </summary>
    public Task<JsonElement> VerifyRegistrationSocialAsync(VerifyRegistrationSocialInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = input.Name,
            ["surname"] = input.Surname,
            ["phoneNumber"] = input.PhoneNumber,
            ["password"] = input.Password,
            ["passwordAgain"] = input.Password,
            ["socialType"] = input.SocialType,
            ["key"] = input.Key,
            ["acceptUserAgreement"] = input.AcceptUserAgreement == 0 ? 1 : input.AcceptUserAgreement,
        };
        if (input.Email is not null) body["email"] = input.Email;
        if (input.UserAgreements is not null) body["userAgreements"] = input.UserAgreements;
        return _t.SendAsync(HttpMethod.Post, "/patients/verifyAddingNewPatientSocial", AuthMode.Public, body, cancellationToken);
    }

    /// <summary>
    /// Step 2 of social sign-up: create the social patient. Unlike <c>RegisterAsync</c> this
    /// does NOT log in — call <c>ConnectAsync</c> with <c>loginMode "social"</c> afterwards. Public.
    /// </summary>
    public async Task RegisterSocialAsync(RegisterSocialInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["smsVerificationCode"] = input.SmsVerificationCode,
            ["response"] = input.Response,
        };
        if (input.UserAgreements is not null) body["userAgreements"] = input.UserAgreements;
        await _t.SendAsync(HttpMethod.Post, "/patients/addNewPatientWithSocial", AuthMode.Public, body, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Step 1 of password reset: send the SMS confirm code to a registered phone and return
    /// the raw <c>data</c> holding the <c>response</c> blob. A CAPTCHA token (<c>RecaptchaV2</c>
    /// or <c>Captcha</c>) is required outside the local environment. Public.
    /// </summary>
    public Task<JsonElement> ForgotPasswordAsync(ForgotPasswordInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["phoneNumber"] = input.PhoneNumber };
        if (input.Birthdate is not null) body["birthdate"] = input.Birthdate;
        if (input.RecaptchaV2 is not null) body["g-recaptcha-response-v2"] = input.RecaptchaV2;
        if (input.Captcha is not null) body["captcha"] = input.Captcha;
        return _t.SendAsync(HttpMethod.Post, "/patients/forgotPassword", AuthMode.Public, body, cancellationToken);
    }

    /// <summary>
    /// Step 2 of password reset: set the new password using the SMS confirm code and the
    /// <c>response</c> blob from <c>ForgotPasswordAsync</c>. Public.
    /// </summary>
    public async Task ResetPasswordAsync(ResetPasswordInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["smsConfirmCode"] = input.SmsConfirmCode,
            ["response"] = input.Response,
            ["password"] = input.Password,
            ["passwordAgain"] = input.Password,
        };
        await _t.SendAsync(HttpMethod.Put, "/patients/forgotPassword", AuthMode.Public, body, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => _t.RefreshAsync(cancellationToken);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _t.SendAsync(HttpMethod.Post, "/general/disconnectApi", AuthMode.Bearer,
                new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _t.TokenStore.Clear();
        }
    }

    private LoginResult FinishLogin(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("access_token", out var accessEl) && accessEl.ValueKind == JsonValueKind.String)
        {
            StoreTokens(data);
            return new LoginResult(false);
        }
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("response", out var responseEl) && responseEl.ValueKind == JsonValueKind.String)
        {
            return new LoginResult(true, responseEl.GetString());
        }
        return new LoginResult(false);
    }

    private void StoreTokens(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("access_token", out var accessEl) || accessEl.ValueKind != JsonValueKind.String)
        {
            throw new BulutklinikException("Login response did not contain an access token");
        }
        string access = accessEl.GetString()!;
        string? refresh = data.TryGetProperty("refresh_token", out var refreshEl) && refreshEl.ValueKind == JsonValueKind.String
            ? refreshEl.GetString()
            : null;
        _t.TokenStore.SetTokens(access, refresh);
    }
}

/// <summary>Branches, locations, search and doctor detail.</summary>
public sealed class DoctorsResource
{
    private readonly Transport _t;

    internal DoctorsResource(Transport transport) => _t = transport;

    public Task<JsonElement> BranchesAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/patients/allBranches", AuthMode.Bearer, null, cancellationToken);

    public Task<JsonElement> LocationsAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/patients/allLocations", AuthMode.Bearer, null, cancellationToken);

    public Task<JsonElement> QuickSearchAsync(string searchText, string? listType = null, string? location = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["searchText"] = searchText,
            ["listType"] = listType,
            ["location"] = location,
        };
        return _t.SendAsync(HttpMethod.Post, "/patients/quickSearch", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> SearchAsync(SearchInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["searchParams"] = input.SearchParams ?? new Dictionary<string, object?>(),
            ["orderParams"] = input.OrderParams ?? Array.Empty<string>(),
            ["otherParams"] = input.OtherParams ?? Array.Empty<string>(),
            ["currentPage"] = input.CurrentPage > 0 ? input.CurrentPage : 1,
            ["perPageLimit"] = input.PerPageLimit > 0 ? input.PerPageLimit : 20,
        };
        return _t.SendAsync(HttpMethod.Post, "/patients/filteredSearch", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> DetailAsync(object id, object? corporate = null, CancellationToken cancellationToken = default)
    {
        string path = $"/patients/doctorDetail/{id}" + (corporate is not null ? $"/{corporate}" : "");
        return _t.SendAsync(HttpMethod.Get, path, AuthMode.Bearer, null, cancellationToken);
    }
}

/// <summary>Doctor availability (materialized slots).</summary>
public sealed class SlotsResource
{
    private readonly Transport _t;

    internal SlotsResource(Transport transport) => _t = transport;

    public Task<JsonElement> ScheduleAsync(object doctorId, string listType, string? scheduleDate = null,
        object? scheduleStep = null, object? schedulePage = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["doctorId"] = doctorId,
            ["scheduleDate"] = scheduleDate,
            ["scheduleStep"] = scheduleStep ?? 7,
            ["schedulePage"] = schedulePage ?? 1,
            ["listType"] = listType,
        };
        return _t.SendAsync(HttpMethod.Post, "/patients/doctorScheduler", AuthMode.Bearer, body, cancellationToken);
    }
}

/// <summary>Online reservation, physical appointment and cancellation.</summary>
public sealed class AppointmentsResource
{
    private readonly Transport _t;

    internal AppointmentsResource(Transport transport) => _t = transport;

    public Task<JsonElement> ReserveInterviewAsync(object doctorId, string appointmentDate,
        string appointmentType = "interview", CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["doctorId"] = doctorId,
            ["appointmentDate"] = appointmentDate,
            ["appointmentType"] = appointmentType,
        };
        return _t.SendAsync(HttpMethod.Post, "/patients/addInterviewDateReservation", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> AddPhysicalAsync(object doctorId, string appointmentDate, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["doctorId"] = doctorId,
            ["appointmentDate"] = appointmentDate,
        };
        return _t.SendAsync(HttpMethod.Post, "/patients/addNewAppointment", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> CancelAsync(object eventId, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Delete, $"/patients/deleteUserAppointment/{eventId}", AuthMode.Bearer, null, cancellationToken);

    /// <summary>
    /// The patient's appointments (<c>{ foundAppointmentsCount, foundAppointments }</c>). Each
    /// item's <c>event_id</c> is the id for <c>CancelAsync</c>; rows with <c>event_id "0"</c> are
    /// paid-order/refund entries (not cancellable). Server paging is disabled, so <paramref name="page"/>
    /// &lt;= 1 (the default) returns the full list; pass <c>null</c> to omit the page segment.
    /// </summary>
    public Task<JsonElement> ListAsync(object? page = null, CancellationToken cancellationToken = default)
    {
        var path = page is null ? "/patients/userAppointments" : $"/patients/userAppointments/{page}";
        return _t.SendAsync(HttpMethod.Get, path, AuthMode.Bearer, null, cancellationToken);
    }

    /// <summary>The patient's active online-slot reservation holds (with a <c>minute_diff</c> countdown).</summary>
    public Task<JsonElement> ReservationsAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/patients/userReservations", AuthMode.Bearer, null, cancellationToken);
}

/// <summary>
/// The patient's saved addresses. Required by <c>Laboratory.OrderAsync</c> (which needs an
/// <c>addressId</c>). <c>Add</c>/<c>Update</c> take a <c>CityId</c> (from <c>Doctors.LocationsAsync</c>)
/// and a <c>DistrictId</c> (from <c>GET /getConfig</c> — <c>cities[].districts[]</c>).
/// </summary>
public sealed class AddressesResource
{
    private readonly Transport _t;

    internal AddressesResource(Transport transport) => _t = transport;

    /// <summary>List saved addresses (default first). Each item's <c>id</c> is the <c>addressId</c>.</summary>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/patients/userAddress", AuthMode.Bearer, null, cancellationToken);

    /// <summary>Add an address. Success → <c>{ addressId }</c>. The first address is always the default.</summary>
    public Task<JsonElement> AddAsync(AddressInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = input.Title,
            ["cityId"] = input.CityId,
            ["districtId"] = input.DistrictId,
            ["address"] = input.Address,
            ["locationLat"] = input.LocationLat,
            ["locationLng"] = input.LocationLng,
        };
        if (input.Description is not null) body["description"] = input.Description;
        if (input.IsDefault is not null) body["isDefault"] = input.IsDefault;
        return _t.SendAsync(HttpMethod.Post, "/patients/userAddress", AuthMode.Bearer, body, cancellationToken);
    }

    /// <summary>
    /// Update an address by <c>Id</c>. Send <c>{ Id, IsDefault = 1 }</c> to only flip the default
    /// flag, or the other fields to edit it (null fields are omitted).
    /// </summary>
    public Task<JsonElement> UpdateAsync(AddressUpdateInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["id"] = input.Id };
        if (input.Title is not null) body["title"] = input.Title;
        if (input.Description is not null) body["description"] = input.Description;
        if (input.CityId is not null) body["cityId"] = input.CityId;
        if (input.DistrictId is not null) body["districtId"] = input.DistrictId;
        if (input.Address is not null) body["address"] = input.Address;
        if (input.LocationLat is not null) body["locationLat"] = input.LocationLat;
        if (input.LocationLng is not null) body["locationLng"] = input.LocationLng;
        if (input.IsDefault is not null) body["isDefault"] = input.IsDefault;
        return _t.SendAsync(HttpMethod.Put, "/patients/userAddress", AuthMode.Bearer, body, cancellationToken);
    }

    /// <summary>
    /// Delete an address by id (sent in the body). The default address cannot be deleted
    /// (reassign the default via <c>UpdateAsync</c> first), nor can one already used on an order.
    /// </summary>
    public Task<JsonElement> DeleteAsync(object id, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Delete, "/patients/userAddress", AuthMode.Bearer,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
}

/// <summary>Discount check, saved cards and the 3DS payment entrypoint.</summary>
public sealed class PaymentsResource
{
    private readonly Transport _t;

    internal PaymentsResource(Transport transport) => _t = transport;

    public Task<JsonElement> CheckDiscountCodeAsync(string checkType, string discountCode, object? doctorId = null,
        object? orderId = null, object? specialServiceId = null, string? programSlug = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["checkType"] = checkType,
            ["discountCode"] = discountCode,
        };
        if (doctorId is not null)
        {
            body["doctorId"] = doctorId;
        }
        if (orderId is not null)
        {
            body["orderId"] = orderId;
        }
        if (specialServiceId is not null)
        {
            body["specialServiceId"] = specialServiceId;
        }
        if (programSlug is not null)
        {
            body["programSlug"] = programSlug;
        }
        return _t.SendAsync(HttpMethod.Post, "/patients/checkDiscountCode", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> GetCardsAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/payments/getCards", AuthMode.Bearer, null, cancellationToken);

    public Task<JsonElement> SaveCardAsync(CardInfo card, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Post, "/payments/saveCard", AuthMode.Bearer, card, cancellationToken);

    public Task<JsonElement> PayAsync(PaymentInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["doctorId"] = input.DoctorId,
            ["appointmentDate"] = input.AppointmentDate,
            ["appointmentType"] = input.AppointmentType,
            ["is3D"] = input.Is3D,
            ["termsAccept"] = input.TermsAccept,
            ["saveCard"] = input.SaveCard,
            ["discountCode"] = input.DiscountCode,
        };
        if (input.CardId is not null)
        {
            body["cardId"] = input.CardId;
        }
        if (input.CardInfo is not null)
        {
            body["cardInfo"] = input.CardInfo;
        }
        if (input.CaseDetail is not null)
        {
            body["caseDetail"] = input.CaseDetail;
        }
        return _t.SendAsync(HttpMethod.Post, "/payments/interviewPayment", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> DeleteCardAsync(object cardId, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Delete, $"/payments/deleteCard/{cardId}", AuthMode.Bearer, null, cancellationToken);
}

/// <summary>Health measurement CRUD, listing, graph and partner submission.</summary>
public sealed class MeasuresResource
{
    private readonly Transport _t;

    internal MeasuresResource(Transport transport) => _t = transport;

    public Task<JsonElement> AddListAsync(IEnumerable<IDictionary<string, object?>> records, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["data"] = records };
        return _t.SendAsync(HttpMethod.Post, "/patients/addNewUserMeasures", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> AddAsync(string measureType, IDictionary<string, object?> fields, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Post, $"/patients/addNewUserMeasures/{measureType}", AuthMode.Bearer, fields, cancellationToken);

    public Task<JsonElement> UpdateAsync(string measureType, IDictionary<string, object?> fields, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Put, $"/patients/updateUserMeasures/{measureType}", AuthMode.Bearer, fields, cancellationToken);

    public Task<JsonElement> DeleteAsync(string measureType, object id, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["id"] = id };
        return _t.SendAsync(HttpMethod.Delete, $"/patients/deleteUserMeasures/{measureType}", AuthMode.Bearer, body, cancellationToken);
    }

    public Task<JsonElement> LastAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/patients/measuresList", AuthMode.Bearer, null, cancellationToken);

    public Task<JsonElement> ListAsync(string measureType, object page, int? glucoseType = null, CancellationToken cancellationToken = default)
    {
        string path = $"/patients/userMeasuresList/{measureType}/{page}" + (glucoseType is not null ? $"/{glucoseType}" : "");
        return _t.SendAsync(HttpMethod.Get, path, AuthMode.Bearer, null, cancellationToken);
    }

    public Task<JsonElement> GraphAsync(string measureType, int period, object page, int? glucoseType = null, CancellationToken cancellationToken = default)
    {
        string path = $"/patients/userMeasuresGraph/{measureType}/{period}/{page}" + (glucoseType is not null ? $"/{glucoseType}" : "");
        return _t.SendAsync(HttpMethod.Get, path, AuthMode.Bearer, null, cancellationToken);
    }

    public Task<JsonElement> PartnerHealthInformationAsync(string? identity, string? phoneNumber,
        IEnumerable<IDictionary<string, object?>> data, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["identity"] = identity,
            ["phoneNumber"] = phoneNumber,
            ["data"] = data,
        };
        return _t.SendAsync(HttpMethod.Post, "/outher/healthInformation", AuthMode.Partner, body, cancellationToken);
    }
}

/// <summary>"Cildimde Neyim Var" — AI skin-lesion analysis.</summary>
public sealed class SkinResource
{
    private readonly Transport _t;

    internal SkinResource(Transport transport) => _t = transport;

    /// <summary>
    /// Analyze one or more skin photos. Each item is <c>{ "image": "&lt;base64&gt;", "branch_id"?: &lt;int&gt; }</c>
    /// (<c>branch_id</c> optional); mirrors <c>Measures.AddListAsync</c> — a loose array of records. The
    /// returned <c>data</c> is passed through verbatim, including the opaque <c>case_detail</c> blob, which
    /// can be forwarded as a payment's <c>caseDetail</c>.
    /// </summary>
    public Task<JsonElement> AnalyzeAsync(IEnumerable<IDictionary<string, object?>> images, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["images"] = images };
        return _t.SendAsync(HttpMethod.Post, "/patients/imageCheck", AuthMode.Bearer, body, cancellationToken);
    }
}

/// <summary>AI meal-photo calorie/nutrition estimation (sibling of <c>Skin</c>).</summary>
public sealed class MealsResource
{
    private readonly Transport _t;

    internal MealsResource(Transport transport) => _t = transport;

    /// <summary>
    /// Estimate calories and nutrition from a meal photo. Idiomatic input names map to the API's
    /// snake_case body (<c>portion_size</c>, <c>portion_grams</c>, <c>meal_type</c>); <c>portion_grams</c>
    /// and <c>note</c> are sent only when non-null.
    /// </summary>
    public Task<JsonElement> AnalyzeAsync(MealAnalyzeInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["image"] = input.Image,
            ["portion_size"] = input.PortionSize,
            ["meal_type"] = input.MealType,
        };
        if (input.PortionGrams is not null)
        {
            body["portion_grams"] = input.PortionGrams;
        }
        if (input.Note is not null)
        {
            body["note"] = input.Note;
        }
        return _t.SendAsync(HttpMethod.Post, "/patients/imageAnalyzeMeal", AuthMode.Bearer, body, cancellationToken);
    }
}

/// <summary>The patient's lab results, the orderable test catalog, and test pre-ordering.</summary>
public sealed class LaboratoryResource
{
    private readonly Transport _t;

    internal LaboratoryResource(Transport transport) => _t = transport;

    /// <summary>The patient's completed/in-progress lab results. <paramref name="page"/> defaults to 1 server-side when omitted.</summary>
    public Task<JsonElement> ResultsAsync(int? page = null, CancellationToken cancellationToken = default)
    {
        string path = "/patients/userLabTestList" + (page is not null ? $"/{page}" : "");
        return _t.SendAsync(HttpMethod.Get, path, AuthMode.Bearer, null, cancellationToken);
    }

    /// <summary>
    /// One lab result's detail. <paramref name="testId"/> is a <b>string</b> — pass the id from a
    /// <see cref="ResultsAsync"/> item verbatim (a plain id like <c>"123"</c> or a TMC-lab id like <c>"4821-lab"</c>).
    /// </summary>
    public Task<JsonElement> ResultDetailAsync(string testId, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, $"/patients/userLabTestDetail/{testId}", AuthMode.Bearer, null, cancellationToken);

    /// <summary>The orderable laboratory test-group catalog.</summary>
    public Task<JsonElement> CatalogAsync(CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, "/patients/allLaboratoryTests", AuthMode.Bearer, null, cancellationToken);

    /// <summary>One catalog test group.</summary>
    public Task<JsonElement> CatalogDetailAsync(string id, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, $"/patients/laboratoryTestDetail/{id}", AuthMode.Bearer, null, cancellationToken);

    /// <summary>Pre-order a laboratory test. All three ids are required; success returns <c>{ preOrderId }</c>.</summary>
    public Task<JsonElement> OrderAsync(LabOrderInput input, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["testId"] = input.TestId,
            ["addressId"] = input.AddressId,
            ["laboratoryId"] = input.LaboratoryId,
        };
        return _t.SendAsync(HttpMethod.Post, "/patients/addNewLaboratoryTest", AuthMode.Bearer, body, cancellationToken);
    }
}

/// <summary>The patient's diet lists (a dietitian's "Diyet Listesi").</summary>
public sealed class DietsResource
{
    private readonly Transport _t;

    internal DietsResource(Transport transport) => _t = transport;

    /// <summary>The patient's diet lists. <paramref name="page"/> defaults to 1 server-side when omitted (page size fixed to 10).</summary>
    public Task<JsonElement> ListAsync(int? page = null, CancellationToken cancellationToken = default)
    {
        string path = "/patients/dietLists" + (page is not null ? $"/{page}" : "");
        return _t.SendAsync(HttpMethod.Get, path, AuthMode.Bearer, null, cancellationToken);
    }

    /// <summary>One diet list's detail (an array of meal-time groups). <paramref name="listId"/> is a <c>list_id</c> from a <see cref="ListAsync"/> item.</summary>
    public Task<JsonElement> DetailAsync(string listId, CancellationToken cancellationToken = default) =>
        _t.SendAsync(HttpMethod.Get, $"/patients/diet/{listId}", AuthMode.Bearer, null, cancellationToken);
}
