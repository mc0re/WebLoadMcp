namespace WebLoadMcp.Services;

/// <summary>
/// Resolves relative paths against the configured base URL and makes sure
/// the result never leaves that server.
/// </summary>
public sealed class PathResolver
{
    private readonly Uri _baseUri;

    public PathResolver(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Base URL '{baseUrl}' must be an absolute http(s) URL.");
        }

        // Without a trailing slash the last segment would be replaced when combining.
        _baseUri = baseUri.AbsolutePath.EndsWith('/')
            ? baseUri
            : new Uri(baseUri.GetLeftPart(UriPartial.Path) + "/" + baseUri.Query);
    }

    public Uri BaseUri => _baseUri;

    /// <summary>Converts a relative path into an absolute URL on the base server.</summary>
    /// <exception cref="ArgumentException">The path is absolute or escapes the base URL.</exception>
    public Uri Resolve(string? relativePath)
    {
        var path = (relativePath ?? string.Empty).Trim();

        if (path.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out var abs) && !string.IsNullOrEmpty(abs.Scheme) && path.Contains(':'))
        {
            throw new ArgumentException($"Path '{path}' must be relative to the base URL.");
        }

        var uri = new Uri(_baseUri, path.TrimStart('/'));
        if (!IsUnderBase(uri))
        {
            throw new ArgumentException($"Path '{path}' resolves outside of the base URL.");
        }

        return uri;
    }

    /// <summary>
    /// Returns the path of <paramref name="uri"/> relative to the base URL,
    /// or <c>null</c> if it points elsewhere.
    /// </summary>
    public string? ToRelative(Uri uri)
    {
        if (!IsUnderBase(uri))
        {
            return null;
        }

        var relative = uri.PathAndQuery[_baseUri.AbsolutePath.Length..];
        return relative + uri.Fragment;
    }

    private bool IsUnderBase(Uri uri) =>
        Uri.Compare(uri, _baseUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0 &&
        uri.AbsolutePath.StartsWith(_baseUri.AbsolutePath, StringComparison.Ordinal);
}
