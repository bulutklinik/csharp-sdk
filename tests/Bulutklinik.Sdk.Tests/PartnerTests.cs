using System.Net;
using System.Text.Json;
using Bulutklinik.Sdk;
using Xunit;

namespace Bulutklinik.Sdk.Tests;

public class PartnerTests
{
    private const string Base = "https://apitest.bulutklinik.com/api/v3";

    /// <summary>
    /// A client with BOTH a patient access token and a partner token configured.
    /// Partner calls must ignore the patient one.
    /// </summary>
    private static (BulutklinikClient Client, MockHandler Handler) Make()
    {
        var handler = new MockHandler((_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":null}"));
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            Environment = BulutklinikEnvironment.Test,
            PartnerToken = "PT",
            TokenStore = new InMemoryTokenStore("PATIENT"),
            HttpClient = new HttpClient(handler),
        });
        return (client, handler);
    }

    private static JsonElement BodyOf(MockHandler handler, int index)
        => JsonDocument.Parse(handler.Bodies[index]).RootElement;

    [Fact]
    public async Task SendsPartnerTokenNeverPatientToken()
    {
        var (client, handler) = Make();

        await client.Partner.Doctors.BranchesAsync();
        await client.Partner.Measures.LastAsync(new Patient { IdentityNumber = "12345678901" });

        foreach (var request in handler.Requests)
        {
            Assert.Equal("Bearer PT", request.Headers.Authorization!.ToString());
        }
    }

    [Fact]
    public async Task PatientSurfaceKeepsPatientToken()
    {
        var (client, handler) = Make();

        await client.Doctors.BranchesAsync();

        Assert.Equal("Bearer PATIENT", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Equal($"{Base}/patients/allBranches", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task BuildsDiscoveryPaths()
    {
        var (client, handler) = Make();

        await client.Partner.Doctors.LocationsAsync();
        await client.Partner.Doctors.DetailAsync(42);
        await client.Partner.Laboratory.CatalogAsync();
        await client.Partner.Laboratory.CatalogDetailAsync(18246);
        await client.Partner.Slots.ScheduleAsync(7, "2026-08-01");

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

        await client.Partner.Diets.ListAsync(patient, 2);
        await client.Partner.Measures.ListAsync(patient, "glucose", 1, 0);
        await client.Partner.Laboratory.ResultsAsync(patient);

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

        await client.Partner.Laboratory.ResultDetailAsync(patient, "1234-lab");
        Assert.Equal("1234-lab", BodyOf(handler, 0).GetProperty("testId").GetString());

        await client.Partner.Laboratory.ResultDetailAsync(patient, 1234);
        Assert.Equal("1234", BodyOf(handler, 1).GetProperty("testId").GetString());
    }

    [Fact]
    public async Task MeasureWriteVerbsAndPaths()
    {
        var (client, handler) = Make();
        var writePatient = new Patient { Name = "Ada", Surname = "Lovelace", PhoneNumber = "+905551112233" };
        var reference = new Patient { IdentityNumber = "12345678901" };

        await client.Partner.Measures.AddListAsync(writePatient, new IDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["type"] = "pulse", ["date_time"] = "2026-06-17 09:00", ["pulse"] = 72 },
        });
        await client.Partner.Measures.AddAsync(writePatient, "tension", new Dictionary<string, object?>
        {
            ["date_time"] = "2026-06-17 09:00", ["hypertension"] = 120, ["hypotension"] = 80,
        });
        await client.Partner.Measures.UpdateAsync(reference, "tension", 9, new Dictionary<string, object?>
        {
            ["date_time"] = "2026-06-17 10:00", ["hypertension"] = 125, ["hypotension"] = 85,
        });
        await client.Partner.Measures.DeleteAsync(reference, "tension", 9);

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

        await client.Partner.Appointments.ReserveAsync(1, 2, user);
        await client.Partner.Appointments.CreateAsync("h", 5);
        await client.Partner.Appointments.ListAsync("+905551112233");
        await client.Partner.Appointments.CancelWithoutSlotAsync(
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
}
