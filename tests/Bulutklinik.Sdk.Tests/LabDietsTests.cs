using System.Net;
using System.Text.Json;
using Bulutklinik.Sdk;
using Xunit;

namespace Bulutklinik.Sdk.Tests;

public class LabDietsTests
{
    private static (BulutklinikClient Client, MockHandler Handler) Make(
        Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder, ITokenStore? tokenStore = null)
    {
        var handler = new MockHandler(responder);
        var client = new BulutklinikClient(new BulutklinikClientOptions
        {
            BaseUrl = "http://localhost",
            HttpClient = new HttpClient(handler),
            TokenStore = tokenStore,
        });
        return (client, handler);
    }

    [Fact]
    public async Task LabResultsGetsListWithBearerAndNoPageSegment()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"foundTestsCount\":1,\"foundTests\":[{\"id\":\"4821-lab\"}]}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Laboratory.ResultsAsync();

        Assert.Equal("4821-lab", data.GetProperty("foundTests")[0].GetProperty("id").GetString());
        Assert.Equal("/patients/userLabTestList", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Null(handler.Requests[0].Content);
    }

    [Fact]
    public async Task LabResultsAppendsPageSegmentWhenProvided()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"foundTests\":[]}}"),
            new InMemoryTokenStore("abc"));

        await client.Laboratory.ResultsAsync(2);

        Assert.Equal("/patients/userLabTestList/2", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task LabResultDetailInterpolatesStringTestIdVerbatim()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"test_name\":\"Hemogram\"}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Laboratory.ResultDetailAsync("4821-lab");

        Assert.Equal("Hemogram", data.GetProperty("test_name").GetString());
        Assert.Equal("/patients/userLabTestDetail/4821-lab", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task LabCatalogGetsCatalogWithBearer()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"test_groups\":[]}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Laboratory.CatalogAsync();

        Assert.Equal(JsonValueKind.Array, data.GetProperty("test_groups").ValueKind);
        Assert.Equal("/patients/allLaboratoryTests", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task LabCatalogDetailInterpolatesId()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"id\":7}}"),
            new InMemoryTokenStore("abc"));

        await client.Laboratory.CatalogDetailAsync("7");

        Assert.Equal("/patients/laboratoryTestDetail/7", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task LabOrderPostsBodyWithBearer()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"preOrderId\":99}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Laboratory.OrderAsync(new LabOrderInput(123, 45, 6));

        Assert.Equal(99, data.GetProperty("preOrderId").GetInt32());
        Assert.Equal("/patients/addNewLaboratoryTest", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal(123, root.GetProperty("testId").GetInt32());
        Assert.Equal(45, root.GetProperty("addressId").GetInt32());
        Assert.Equal(6, root.GetProperty("laboratoryId").GetInt32());
    }

    [Fact]
    public async Task DietsListGetsListWithBearerAndNoPageSegment()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"foundDietsCount\":1,\"foundDiets\":[{\"list_id\":10}]}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Diets.ListAsync();

        Assert.Equal(10, data.GetProperty("foundDiets")[0].GetProperty("list_id").GetInt32());
        Assert.Equal("/patients/dietLists", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
        Assert.Null(handler.Requests[0].Content);
    }

    [Fact]
    public async Task DietsListAppendsPageSegmentWhenProvided()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"foundDiets\":[]}}"),
            new InMemoryTokenStore("abc"));

        await client.Diets.ListAsync(3);

        Assert.Equal("/patients/dietLists/3", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task DietDetailInterpolatesListId()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":[{\"time\":\"08:00\"}]}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Diets.DetailAsync("10");

        Assert.Equal("08:00", data[0].GetProperty("time").GetString());
        Assert.Equal("/patients/diet/10", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());
    }
}
