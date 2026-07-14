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
