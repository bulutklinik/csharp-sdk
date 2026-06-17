namespace Bulutklinik.Sdk;

/// <summary>Base URL presets.</summary>
public enum BulutklinikEnvironment
{
    Production,
    Test,
    Local,
}

internal static class EnvironmentExtensions
{
    public static string BaseUrl(this BulutklinikEnvironment environment) => environment switch
    {
        BulutklinikEnvironment.Production => "https://api.bulutklinik.com/api/v3",
        BulutklinikEnvironment.Test => "https://apitest.bulutklinik.com/api/v3",
        BulutklinikEnvironment.Local => "https://api-bulutklinik.test/api/v3",
        _ => throw new ArgumentOutOfRangeException(nameof(environment)),
    };
}
