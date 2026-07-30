namespace Bulutklinik.Sdk;

/// <summary>Base URL presets.</summary>
public enum BulutklinikEnvironment
{
    Production,
    Test,
    Local,
}

/// <summary>
/// API version segment. The <c>/outher</c> surface is route-for-route identical
/// on both, so switching is configuration rather than a code change.
/// </summary>
public enum BulutklinikApiVersion
{
    V3,
    V4,
}

internal static class EnvironmentExtensions
{
    /// <summary>API root for this environment. The base URL is <c>root/version</c>.</summary>
    public static string ApiRoot(this BulutklinikEnvironment environment) => environment switch
    {
        BulutklinikEnvironment.Production => "https://api.bulutklinik.com/api",
        BulutklinikEnvironment.Test => "https://apitest.bulutklinik.com/api",
        BulutklinikEnvironment.Local => "https://api-bulutklinik.test/api",
        _ => throw new ArgumentOutOfRangeException(nameof(environment)),
    };

    public static string Segment(this BulutklinikApiVersion version) => version switch
    {
        BulutklinikApiVersion.V3 => "v3",
        BulutklinikApiVersion.V4 => "v4",
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };

    public static string BaseUrl(this BulutklinikEnvironment environment, BulutklinikApiVersion version)
        => $"{environment.ApiRoot()}/{version.Segment()}";
}
