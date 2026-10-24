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

    /// <remarks>
    /// Error messages mention only the relative path, never the base server's address.
    /// </remarks>
    public async Task<PageResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var uri = _resolver.Resolve(path);
        var relativePath = _resolver.ToRelative(uri);
        _logger.LogInformation("Loading {Url}", uri);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Loading {Url} failed", uri);
            throw new HttpRequestException($"Loading '{relativePath}' failed: the server could not be reached.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Loading {Url} timed out", uri);
            throw new HttpRequestException($"Loading '{relativePath}' failed: the request timed out.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Loading '{relativePath}' failed with {(int)response.StatusCode} {response.ReasonPhrase}.",
                    null,
                    response.StatusCode);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Follow redirects: parse relative to where the page actually came from.
            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            var result = _parser.Parse(html, finalUri);
            foreach (var warning in result.Warnings ?? [])
            {
                _logger.LogWarning("{Url}: {Warning}", finalUri, warning);
            }

            return result;
        }
    }
}
