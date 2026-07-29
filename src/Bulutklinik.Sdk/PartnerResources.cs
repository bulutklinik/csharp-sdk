using System.Text.Json;

namespace Bulutklinik.Sdk;

/// <summary>
/// Identifies a patient on the partner surface.
/// <para>
/// Reads need only <see cref="IdentityNumber"/> (primary) or <see cref="PhoneNumber"/>
/// (accepted solely when it matches exactly one patient in your company — the column
/// is not unique, and the server fails closed rather than guessing).
/// </para>
/// <para>
/// Writes need <see cref="Name"/>, <see cref="Surname"/> and <see cref="PhoneNumber"/>
/// as well: if no matching patient exists in your company the server creates one.
/// </para>
/// </summary>
public sealed class Patient
{
    public string? Name { get; set; }
    public string? Surname { get; set; }
    public string? PhoneNumber { get; set; }
    public string? IdentityNumber { get; set; }
    public string? Email { get; set; }
    /// <summary><c>Y-m-d</c>.</summary>
    public string? Birthdate { get; set; }
    /// <summary>ISO country code present in <c>bas_com_countries.code</c>.</summary>
    public string? Nationality { get; set; }
    public decimal? Price { get; set; }

    internal Dictionary<string, object?> ToBody()
    {
        var body = new Dictionary<string, object?>();
        if (Name is not null) body["name"] = Name;
        if (Surname is not null) body["surname"] = Surname;
        if (PhoneNumber is not null) body["phoneNumber"] = PhoneNumber;
        if (IdentityNumber is not null) body["identityNumber"] = IdentityNumber;
        if (Email is not null) body["email"] = Email;
        if (Birthdate is not null) body["birthdate"] = Birthdate;
        if (Nationality is not null) body["nationality"] = Nationality;
        if (Price is not null) body["price"] = Price;
        return body;
    }
}

/// <summary>
/// Addresses one appointment either by its process (<see cref="Hash"/> +
/// <see cref="OutherProcessId"/>) or by its coordinates (<see cref="DoctorId"/> +
/// <see cref="AppointmentDate"/> + <see cref="IsOutherDoctor"/>). Supply one pair
/// or the other.
/// </summary>
public sealed class AppointmentLookup
{
    public string? Hash { get; set; }
    public object? OutherProcessId { get; set; }
    public object? DoctorId { get; set; }
    /// <summary><c>Y-m-d H:i</c>.</summary>
    public string? AppointmentDate { get; set; }
    public int? IsOutherDoctor { get; set; }

    internal Dictionary<string, object?> ToBody()
    {
        var body = new Dictionary<string, object?>();
        if (Hash is not null) body["hash"] = Hash;
        if (OutherProcessId is not null) body["outherProcessId"] = OutherProcessId;
        if (DoctorId is not null) body["doctorId"] = DoctorId;
        if (AppointmentDate is not null) body["appointmentDate"] = AppointmentDate;
        if (IsOutherDoctor is not null) body["isOutherDoctor"] = IsOutherDoctor;
        return body;
    }
}

/// <summary>
/// The company-scoped partner surface (<c>/outher</c>), exposed as
/// <c>client.Partner</c>.
/// <para>
/// A <b>second persona</b>, not a replacement for the patient one: requests use
/// the configured <c>PartnerToken</c>, data is limited to your own company, and
/// the patient is named inline on each call. The silent access-token refresh does
/// not apply here.
/// </para>
/// <para>
/// Patient login/registration, the card vault, 3-D Secure payment, self-service
/// AI and address CRUD have no partner equivalent and stay on the patient surface
/// by design.
/// </para>
/// </summary>
public sealed class PartnerNamespace
{
    public PartnerDoctorsResource Doctors { get; }
    public PartnerSlotsResource Slots { get; }
    public PartnerAppointmentsResource Appointments { get; }
    public PartnerDietsResource Diets { get; }
    public PartnerLaboratoryResource Laboratory { get; }
    public PartnerMeasuresResource Measures { get; }

    internal PartnerNamespace(Transport transport)
    {
        Doctors = new PartnerDoctorsResource(transport);
        Slots = new PartnerSlotsResource(transport);
        Appointments = new PartnerAppointmentsResource(transport);
        Diets = new PartnerDietsResource(transport);
        Laboratory = new PartnerLaboratoryResource(transport);
        Measures = new PartnerMeasuresResource(transport);
    }
}

/// <summary>
/// Doctor discovery on the partner surface. Results are scoped to the doctors
/// enabled for your integration, so a doctor returned here is one you can book.
/// </summary>
public sealed class PartnerDoctorsResource
{
    private readonly Transport _t;

    internal PartnerDoctorsResource(Transport transport) => _t = transport;

    public Task<JsonElement> SearchAsync(IDictionary<string, object?> searchParams, int currentPage = 1,
        IEnumerable<string>? orderParams = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["searchParams"] = searchParams,
            ["orderParams"] = orderParams ?? Array.Empty<string>(),
            ["currentPage"] = currentPage,
        };
        return _t.SendAsync(HttpMethod.Post, "/outher/search", AuthMode.Partner, body, cancellationToken);
    }

    public Task<JsonElement> BranchesAsync(CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Get, "/outher/branches", AuthMode.Partner, null, cancellationToken);

    public Task<JsonElement> DetailAsync(object doctorId, CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Get, $"/outher/doctorInfos/{doctorId}", AuthMode.Partner, null, cancellationToken);

    /// <summary>City list. Global catalogue — not scoped to your company.</summary>
    public Task<JsonElement> LocationsAsync(CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Get, "/outher/locations", AuthMode.Partner, null, cancellationToken);
}

/// <summary>Doctor availability on the partner surface.</summary>
public sealed class PartnerSlotsResource
{
    private readonly Transport _t;

    internal PartnerSlotsResource(Transport transport) => _t = transport;

    /// <summary>
    /// Bookable slots for a doctor. Either pass <paramref name="scheduleDate"/>
    /// (<c>Y-m-d</c>), or page with <paramref name="scheduleStep"/> +
    /// <paramref name="schedulePage"/>; the server requires one of the two forms.
    /// </summary>
    public Task<JsonElement> ScheduleAsync(object doctorId, string? scheduleDate = null,
        int? scheduleStep = null, int? schedulePage = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["doctorId"] = doctorId,
            ["scheduleDate"] = scheduleDate,
            ["scheduleStep"] = scheduleStep,
            ["schedulePage"] = schedulePage,
        };
        return _t.SendAsync(HttpMethod.Post, "/outher/doctorSlots", AuthMode.Partner, body, cancellationToken);
    }
}

/// <summary>
/// Appointment lifecycle on the partner surface. The patient is supplied inline;
/// the server materialises it inside your company on write.
/// <para>
/// <b>Payment is not taken through the API.</b> <see cref="ReserveAsync"/> returns
/// a process settled through the hosted web checkout.
/// </para>
/// </summary>
public sealed class PartnerAppointmentsResource
{
    private readonly Transport _t;

    internal PartnerAppointmentsResource(Transport transport) => _t = transport;

    public Task<JsonElement> ReserveAsync(object slotId, object doctorId, Patient user,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["slotId"] = slotId, ["doctorId"] = doctorId, ["user"] = user.ToBody() };
        return _t.SendAsync(HttpMethod.Post, "/outher/reservation", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary>As <see cref="ReserveAsync"/>, for integrations that collect the agreements themselves.</summary>
    public Task<JsonElement> ReserveWithoutAgreementAsync(object slotId, object doctorId, Patient user,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["slotId"] = slotId, ["doctorId"] = doctorId, ["user"] = user.ToBody() };
        return _t.SendAsync(HttpMethod.Post, "/outher/reservationWithoutAgreement", AuthMode.Partner, body, cancellationToken);
    }

    public Task<JsonElement> InstantReserveAsync(Patient user, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["user"] = user.ToBody() };
        return _t.SendAsync(HttpMethod.Post, "/outher/instantReservation", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary>Turn a reservation into a confirmed appointment.</summary>
    public Task<JsonElement> CreateAsync(string hash, object outherProcessId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["hash"] = hash, ["outherProcessId"] = outherProcessId };
        return _t.SendAsync(HttpMethod.Post, "/outher/appointment", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary>Book a free-form time range without going through a slot. Dates are <c>Y-m-d H:i</c>.</summary>
    public Task<JsonElement> CreateWithoutSlotAsync(object doctorId, string startDate, string finishDate,
        Patient user, int? isOutherDoctor = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["doctorId"] = doctorId,
            ["startDate"] = startDate,
            ["finishDate"] = finishDate,
            ["isOutherDoctor"] = isOutherDoctor,
            ["user"] = user.ToBody(),
        };
        return _t.SendAsync(HttpMethod.Post, "/outher/appointmentWithoutSlot", AuthMode.Partner, body, cancellationToken);
    }

    public Task<JsonElement> CancelWithoutSlotAsync(AppointmentLookup lookup, CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Delete, "/outher/appointmentWithoutSlot", AuthMode.Partner, lookup.ToBody(), cancellationToken);

    /// <summary>The appointments you created for the given phone number.</summary>
    public Task<JsonElement> ListAsync(string phoneNumber, object? page = null, string? type = null,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["phoneNumber"] = phoneNumber, ["page"] = page, ["type"] = type };
        return _t.SendAsync(HttpMethod.Post, "/outher/appointments", AuthMode.Partner, body, cancellationToken);
    }

    public Task<JsonElement> InfoAsync(AppointmentLookup lookup, CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Post, "/outher/appointmentInfo", AuthMode.Partner, lookup.ToBody(), cancellationToken);

    public Task<JsonElement> CheckDoctorAsync(object doctorId, int isOutherDoctor, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["doctorId"] = doctorId, ["isOutherDoctor"] = isOutherDoctor };
        return _t.SendAsync(HttpMethod.Post, "/outher/checkDoctor", AuthMode.Partner, body, cancellationToken);
    }
}

/// <summary>
/// Diet lists recorded for a patient <b>inside your own company</b>. Lists written
/// by other clinics are not visible here.
/// </summary>
public sealed class PartnerDietsResource
{
    private readonly Transport _t;

    internal PartnerDietsResource(Transport transport) => _t = transport;

    public Task<JsonElement> ListAsync(Patient patient, object? page = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody(), ["currentPage"] = page };
        return _t.SendAsync(HttpMethod.Post, "/outher/dietLists", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary>Meal breakdown of one diet list. <paramref name="listId"/> comes from <see cref="ListAsync"/>.</summary>
    public Task<JsonElement> DetailAsync(Patient patient, object listId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody(), ["listId"] = listId };
        return _t.SendAsync(HttpMethod.Post, "/outher/diet", AuthMode.Partner, body, cancellationToken);
    }
}

/// <summary>
/// Laboratory catalogue (global, static) and results (your company only, merging
/// the clinic's HBYS lab requests and TmcLab order groups).
/// </summary>
public sealed class PartnerLaboratoryResource
{
    private readonly Transport _t;

    internal PartnerLaboratoryResource(Transport transport) => _t = transport;

    public Task<JsonElement> CatalogAsync(CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Get, "/outher/laboratoryCatalog", AuthMode.Partner, null, cancellationToken);

    /// <summary>
    /// One catalogue package. Prices are the plain list prices — the patient-side
    /// discount pass does not apply on the partner surface.
    /// </summary>
    public Task<JsonElement> CatalogDetailAsync(object testId, CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Get, $"/outher/laboratoryCatalog/{testId}", AuthMode.Partner, null, cancellationToken);

    /// <summary>
    /// Paginated results. Each item's <c>id</c> is accepted verbatim by
    /// <see cref="ResultDetailAsync"/>; a <c>-lab</c> suffix marks a TmcLab group.
    /// </summary>
    public Task<JsonElement> ResultsAsync(Patient patient, object? page = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody(), ["currentPage"] = page };
        return _t.SendAsync(HttpMethod.Post, "/outher/laboratoryResults", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary>One result. Pass the id from <see cref="ResultsAsync"/> unchanged.</summary>
    public Task<JsonElement> ResultDetailAsync(Patient patient, object testId, CancellationToken cancellationToken = default)
    {
        // Sent as a string on purpose so a "-lab" suffix survives the round trip.
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody(), ["testId"] = testId.ToString() };
        return _t.SendAsync(HttpMethod.Post, "/outher/laboratoryResult", AuthMode.Partner, body, cancellationToken);
    }
}

/// <summary>
/// Health measurements on the partner surface.
/// <para>
/// <b>Scope:</b> written into and read from <b>your own company</b>. Values the
/// patient entered in the Bulutklinik mobile app live in the consumer tenant and
/// are not visible here — a consequence of tenant isolation, not a bug.
/// </para>
/// </summary>
public sealed class PartnerMeasuresResource
{
    private readonly Transport _t;

    internal PartnerMeasuresResource(Transport transport) => _t = transport;

    public Task<JsonElement> LastAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody() };
        return _t.SendAsync(HttpMethod.Post, "/outher/lastMeasures", AuthMode.Partner, body, cancellationToken);
    }

    public Task<JsonElement> ListAsync(Patient patient, string measureType, object? page = null,
        int? glucoseType = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["patient"] = patient.ToBody(),
            ["currentPage"] = page,
            ["glucoseType"] = glucoseType,
        };
        return _t.SendAsync(HttpMethod.Post, $"/outher/measuresList/{measureType}", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary><paramref name="period"/>: 1=day, 2=week, 3=month, 4=year.</summary>
    public Task<JsonElement> GraphAsync(Patient patient, string measureType, int period, object? page = null,
        int? glucoseType = null, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["patient"] = patient.ToBody(),
            ["currentPage"] = page,
            ["glucoseType"] = glucoseType,
        };
        return _t.SendAsync(HttpMethod.Post, $"/outher/measuresGraph/{measureType}/{period}", AuthMode.Partner, body, cancellationToken);
    }

    /// <summary>Write several measurements of mixed types in one transaction. The server caps a call at 200 rows.</summary>
    public Task<JsonElement> AddListAsync(Patient patient, IEnumerable<IDictionary<string, object?>> data,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody(), ["data"] = data };
        return _t.SendAsync(HttpMethod.Post, "/outher/measures", AuthMode.Partner, body, cancellationToken);
    }

    public Task<JsonElement> AddAsync(Patient patient, string measureType, IDictionary<string, object?> fields,
        CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Post, $"/outher/measure/{measureType}", AuthMode.Partner,
            WithPatient(patient, fields, null), cancellationToken);

    /// <summary>Update one measurement row. <paramref name="id"/> comes from <see cref="ListAsync"/>.</summary>
    public Task<JsonElement> UpdateAsync(Patient patient, string measureType, object id,
        IDictionary<string, object?> fields, CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Put, $"/outher/measure/{measureType}", AuthMode.Partner,
            WithPatient(patient, fields, id), cancellationToken);

    public Task<JsonElement> DeleteAsync(Patient patient, string measureType, object id,
        CancellationToken cancellationToken = default)
        => _t.SendAsync(HttpMethod.Delete, $"/outher/measure/{measureType}", AuthMode.Partner,
            WithPatient(patient, null, id), cancellationToken);

    /// <summary>
    /// Legacy teusan bulk submission.
    /// </summary>
    /// <remarks>
    /// Deprecated: writes into the shared consumer tenant rather than your own
    /// company, so the values are not readable through <see cref="LastAsync"/> or
    /// <see cref="ListAsync"/>. Prefer <see cref="AddListAsync"/>. Kept for
    /// existing teusan integrations.
    /// </remarks>
    [Obsolete("Writes into the shared consumer tenant; prefer AddListAsync.")]
    public Task<JsonElement> HealthInformationAsync(string? identity, string? phoneNumber,
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

    /// <summary>
    /// Flattens measure fields next to the patient reference, optionally adding an
    /// id. The server expects the columns at the top level, not nested.
    /// </summary>
    private static Dictionary<string, object?> WithPatient(Patient patient, IDictionary<string, object?>? fields, object? id)
    {
        var body = new Dictionary<string, object?> { ["patient"] = patient.ToBody() };
        if (id is not null) body["id"] = id;
        if (fields is not null)
        {
            foreach (var pair in fields) body[pair.Key] = pair.Value;
        }
        return body;
    }
}
