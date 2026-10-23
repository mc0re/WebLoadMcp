using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebLoadMcp.Configuration;

namespace WebLoadMcp.Services;

/// <summary>
/// Checks which country the public IP (as seen from the Internet) belongs to.
/// </summary>
public sealed class ReadyChecker
{
    public const string HttpClientName = "GeoIp";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReadyOptions _options;
    private readonly ILogger<ReadyChecker> _logger;

    public ReadyChecker(IHttpClientFactory httpClientFactory, IOptions<ReadyOptions> options, ILogger<ReadyChecker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns <c>true</c> if the public IP is NOT in the configured country,
    /// <c>false</c> if it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The country is not configured or could not be determined.</exception>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Country))
        {
            throw new InvalidOperationException($"'{ReadyOptions.SectionName}:Country' is not configured.");
        }

        var actual = await GetPublicIpCountryAsync(cancellationToken);
        var ready = !string.Equals(actual, _options.Country.Trim(), StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation("Public IP country is {Country}; ready = {Ready}", actual, ready);
        return ready;
    }

    private async Task<string> GetPublicIpCountryAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(_options.GeoIpUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty(_options.CountryProperty, out var country) &&
            country.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(country.GetString()))
        {
            return country.GetString()!.Trim();
        }

        throw new InvalidOperationException(
            $"Geo-IP response from '{_options.GeoIpUrl}' has no '{_options.CountryProperty}' property.");
    }
}
