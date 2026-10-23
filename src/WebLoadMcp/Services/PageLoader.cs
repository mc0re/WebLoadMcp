using Microsoft.Extensions.Logging;
using WebLoadMcp.Models;

namespace WebLoadMcp.Services;

/// <summary>
/// Downloads a page from the predefined server and parses it.
/// </summary>
public sealed class PageLoader
{
    public const string HttpClientName = "WebLoad";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PathResolver _resolver;
    private readonly PageParser _parser;
    private readonly ILogger<PageLoader> _logger;

    public PageLoader(IHttpClientFactory httpClientFactory, PathResolver resolver, PageParser parser, ILogger<PageLoader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _parser = parser;
        _logger = logger;
    }

    public async Task<PageResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var uri = _resolver.Resolve(path);
        _logger.LogInformation("Loading {Url}", uri);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Loading '{uri}' failed with {(int)response.StatusCode} {response.ReasonPhrase}.",
                null,
                response.StatusCode);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // Follow redirects: parse relative to where the page actually came from.
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        return _parser.Parse(html, finalUri);
    }
}
