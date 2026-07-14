using System.Net;
using System.Text.Json;
using Bulutklinik.Sdk;
using Xunit;

namespace Bulutklinik.Sdk.Tests;

public class AiTests
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
    public async Task SkinAnalyzePostsImagesWithBearer()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"status\":[{\"id\":1,\"label\":\"nevus\",\"case_detail\":\"blob\"}]}}"),
            new InMemoryTokenStore("abc"));

        var data = await client.Skin.AnalyzeAsync(new IDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["image"] = "BASE64", ["branch_id"] = 42 },
        });

        Assert.Equal("nevus", data.GetProperty("status")[0].GetProperty("label").GetString());
        Assert.Equal("/patients/imageCheck", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var images = body.RootElement.GetProperty("images");
        Assert.Equal(1, images.GetArrayLength());
        Assert.Equal("BASE64", images[0].GetProperty("image").GetString());
        Assert.Equal(42, images[0].GetProperty("branch_id").GetInt32());
    }

    [Fact]
    public async Task MealsAnalyzeMapsToSnakeCaseBodyWithOptionalFields()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"status\":{\"comment\":\"{}\"}}}"),
            new InMemoryTokenStore("abc"));

        await client.Meals.AnalyzeAsync(new MealAnalyzeInput
        {
            Image = "BASE64",
            PortionSize = "custom",
            PortionGrams = 300,
            MealType = "lunch",
            Note = "az yağlı",
        });

        Assert.Equal("/patients/imageAnalyzeMeal", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("Bearer abc", handler.Requests[0].Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal("BASE64", root.GetProperty("image").GetString());
        Assert.Equal("custom", root.GetProperty("portion_size").GetString());
        Assert.Equal("lunch", root.GetProperty("meal_type").GetString());
        Assert.Equal(300, root.GetProperty("portion_grams").GetInt32());
        Assert.Equal("az yağlı", root.GetProperty("note").GetString());
    }

    [Fact]
    public async Task MealsAnalyzeOmitsOptionalFieldsWhenNull()
    {
        var (client, handler) = Make(
            (_, _) => (HttpStatusCode.OK, "{\"resultType\":0,\"data\":{\"status\":{\"comment\":\"{}\"}}}"),
            new InMemoryTokenStore("abc"));

        await client.Meals.AnalyzeAsync(new MealAnalyzeInput
        {
            Image = "BASE64",
            PortionSize = "medium",
            MealType = "snack",
        });

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var root = body.RootElement;
        Assert.Equal("BASE64", root.GetProperty("image").GetString());
        Assert.Equal("medium", root.GetProperty("portion_size").GetString());
        Assert.Equal("snack", root.GetProperty("meal_type").GetString());
        Assert.False(root.TryGetProperty("portion_grams", out _));
        Assert.False(root.TryGetProperty("note", out _));
    }
}
