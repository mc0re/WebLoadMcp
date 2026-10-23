namespace WebLoadMcp.Configuration;

/// <summary>
/// Settings for the "ready" check. Bound from the "Ready" configuration section.
/// </summary>
public sealed class ReadyOptions
{
    public const string SectionName = "Ready";

    /// <summary>
    /// ISO 3166-1 alpha-2 code (e.g. "DK") of the country the public IP must NOT be in.
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Geo-IP service returning a JSON object describing the caller's public IP.</summary>
    public string GeoIpUrl { get; set; } = "https://ipinfo.io/json";

    /// <summary>Name of the JSON property holding the country code in the geo-IP response.</summary>
    public string CountryProperty { get; set; } = "country";

    public int TimeoutSeconds { get; set; } = 10;
}
