using System.Net;
using System.Text.Json;
using Bulutklinik.Sdk;
using Xunit;

namespace Bulutklinik.Sdk.Tests;

public class ResourceTests
{
    private const string Base = "https://apitest.bulutklinik.com/api/v3";

    private static (BulutklinikClient Client, MockHandler Handler) Make()
    {
        var handler = new MockHandler((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":null}"));
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            Environment = BulutklinikEnvironment.Test,
            PartnerToken = "PT",
            HttpClient = new HttpClient(handler),
        });
        return (client, handler);
    }

    private static JsonElement BodyOf(MockHandler handler, int index)
        => JsonDocument.Parse(handler.Bodies[index]).RootElement;

    [Fact]
    public async Task EveryCallUsesThePartnerToken()
    {
        var (client, handler) = Make();

        await client.Doctors.BranchesAsync();
        await client.Measures.LastAsync(new Patient { IdentityNumber = "12345678901" });

        foreach (var request in handler.Requests)
        {
            Assert.Equal("Bearer PT", request.Headers.Authorization!.ToString());
        }
    }

    [Fact]
    public async Task BuildsDiscoveryPaths()
    {
        var (client, handler) = Make();

        await client.Doctors.LocationsAsync();
        await client.Doctors.DetailAsync(42);
        await client.Laboratory.CatalogAsync();
        await client.Laboratory.CatalogDetailAsync(18246);
        await client.Slots.ScheduleAsync(7, "2026-08-01");

        var expected = new[]
        {
            $"{Base}/outher/locations",
            $"{Base}/outher/doctorInfos/42",
            $"{Base}/outher/laboratoryCatalog",
            $"{Base}/outher/laboratoryCatalog/18246",
            $"{Base}/outher/doctorSlots",
        };

        Assert.Equal(expected, handler.Requests.Select(r => r.RequestUri!.ToString()).ToArray());
    }

    [Fact]
    public async Task PatientReferenceTravelsInTheBodyNotThePath()
    {
        var (client, handler) = Make();
        var patient = new Patient { IdentityNumber = "12345678901" };

        await client.Diets.ListAsync(patient, 2);
        await client.Measures.ListAsync(patient, "glucose", 1, 0);
        await client.Laboratory.ResultsAsync(patient);

        // The identity number must never leak into a URL — it would land in access
        // logs, proxy logs and error breadcrumbs.
        foreach (var request in handler.Requests)
        {
            Assert.DoesNotContain("12345678901", request.RequestUri!.ToString());
        }

        Assert.Equal($"{Base}/outher/dietLists", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("12345678901",
            BodyOf(handler, 0).GetProperty("patient").GetProperty("identityNumber").GetString());

        Assert.Equal($"{Base}/outher/measuresList/glucose", handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(0, BodyOf(handler, 1).GetProperty("glucoseType").GetInt32());
    }

    [Fact]
    public async Task LabResultIdRoundTripsWithItsSuffix()
    {
        var (client, handler) = Make();
        var patient = new Patient { IdentityNumber = "12345678901" };

        await client.Laboratory.ResultDetailAsync(patient, "1234-lab");
        Assert.Equal("1234-lab", BodyOf(handler, 0).GetProperty("testId").GetString());

        await client.Laboratory.ResultDetailAsync(patient, 1234);
        Assert.Equal("1234", BodyOf(handler, 1).GetProperty("testId").GetString());
    }

    [Fact]
    public async Task MeasureWriteVerbsAndPaths()
    {
        var (client, handler) = Make();
        var writePatient = new Patient { Name = "Ada", Surname = "Lovelace", PhoneNumber = "+905551112233" };
        var reference = new Patient { IdentityNumber = "12345678901" };

        await client.Measures.AddListAsync(writePatient, new IDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["type"] = "pulse", ["date_time"] = "2026-06-17 09:00", ["pulse"] = 72 },
        });
        await client.Measures.AddAsync(writePatient, "tension", new Dictionary<string, object?>
        {
            ["date_time"] = "2026-06-17 09:00", ["hypertension"] = 120, ["hypotension"] = 80,
        });
        await client.Measures.UpdateAsync(reference, "tension", 9, new Dictionary<string, object?>
        {
            ["date_time"] = "2026-06-17 10:00", ["hypertension"] = 125, ["hypotension"] = 85,
        });
        await client.Measures.DeleteAsync(reference, "tension", 9);

        var seen = handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.ToString())).ToArray();

        Assert.Equal(new[]
        {
            ("POST", $"{Base}/outher/measures"),
            ("POST", $"{Base}/outher/measure/tension"),
            ("PUT", $"{Base}/outher/measure/tension"),
            ("DELETE", $"{Base}/outher/measure/tension"),
        }, seen);

        // Measure fields are flattened alongside `patient`, matching the server shape.
        Assert.Equal(120, BodyOf(handler, 1).GetProperty("hypertension").GetInt32());
        Assert.Equal(9, BodyOf(handler, 3).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task AppointmentLifecycle()
    {
        var (client, handler) = Make();
        var user = new Patient { Name = "Ada", Surname = "Lovelace", PhoneNumber = "+905551112233" };

        await client.Appointments.ReserveAsync(1, 2, user);
        await client.Appointments.CreateAsync("h", 5);
        await client.Appointments.ListAsync("+905551112233");
        await client.Appointments.CancelWithoutSlotAsync(
            new AppointmentLookup { Hash = "h", OutherProcessId = 5 });

        var seen = handler.Requests.Select(r => (r.Method.Method, r.RequestUri!.ToString())).ToArray();

        Assert.Equal(new[]
        {
            ("POST", $"{Base}/outher/reservation"),
            ("POST", $"{Base}/outher/appointment"),
            ("POST", $"{Base}/outher/appointments"),
            ("DELETE", $"{Base}/outher/appointmentWithoutSlot"),
        }, seen);

        Assert.Equal(1, BodyOf(handler, 0).GetProperty("slotId").GetInt32());
    }

    [Fact]
    public async Task RemainingAppointmentEndpoints()
    {
        var (client, handler) = Make();
        var user = new Patient { Name = "Ada", Surname = "Lovelace", PhoneNumber = "+905551112233" };

        await client.Appointments.CheckDoctorAsync(2, 0);
        await client.Appointments.ReserveWithoutAgreementAsync(1, 2, user);
        await client.Appointments.InstantReserveAsync(user);
        await client.Appointments.CreateWithoutSlotAsync(2, "2026-08-01 09:00", "2026-08-01 09:30", user);
        await client.Appointments.InfoAsync(new AppointmentLookup { Hash = "h", OutherProcessId = 5 });

        Assert.Equal(new[]
        {
            $"{Base}/outher/checkDoctor",
            $"{Base}/outher/reservationWithoutAgreement",
            $"{Base}/outher/instantReservation",
            $"{Base}/outher/appointmentWithoutSlot",
            $"{Base}/outher/appointmentInfo",
        }, handler.Requests.Select(r => r.RequestUri!.ToString()).ToArray());
    }

    [Fact]
    public async Task MeasuresGraphPathAndLegacyTeusanShape()
    {
        var (client, handler) = Make();

        await client.Measures.GraphAsync(new Patient { PhoneNumber = "+905551112233" }, "weight", 3);
        Assert.Equal($"{Base}/outher/measuresGraph/weight/3", handler.Requests[0].RequestUri!.ToString());

#pragma warning disable CS0618 // deliberately exercising the deprecated legacy endpoint
        await client.Measures.HealthInformationAsync("12345678901", "+905551112233",
            new IDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["type"] = "pulse", ["date_time"] = "2026-06-17 09:00", ["pulse"] = 72 },
            });
#pragma warning restore CS0618

        Assert.Equal($"{Base}/outher/healthInformation", handler.Requests[1].RequestUri!.ToString());
        // No `patient` wrapper here — this endpoint predates that contract.
        Assert.False(BodyOf(handler, 1).TryGetProperty("patient", out _));
        Assert.Equal("12345678901", BodyOf(handler, 1).GetProperty("identity").GetString());
    }

    [Fact]
    public async Task DietDetailAndCatalogDetailPaths()
    {
        var (client, handler) = Make();
        var reference = new Patient { IdentityNumber = "12345678901" };

        await client.Diets.DetailAsync(reference, 77);
        await client.Laboratory.CatalogDetailAsync(18246);

        Assert.Equal($"{Base}/outher/diet", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(77, BodyOf(handler, 0).GetProperty("listId").GetInt32());
        Assert.Equal($"{Base}/outher/laboratoryCatalog/18246", handler.Requests[1].RequestUri!.ToString());
    }
}
