using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WebLoadMcp.Configuration;
using WebLoadMcp.Models;

namespace WebLoadMcp.Services;

/// <summary>
/// Turns HTML into a <see cref="PageResult"/> using the CSS selectors from configuration.
/// </summary>
public sealed class PageParser
{
    private readonly WebLoadOptions _options;
    private readonly PathResolver _resolver;
    private readonly HtmlParser _htmlParser = new();

    public PageParser(WebLoadOptions options, PathResolver resolver)
    {
        _options = options;
        _resolver = resolver;
    }

    public PageResult Parse(string html, Uri pageUri)
    {
        using var document = _htmlParser.ParseDocument(html);
        var path = _resolver.ToRelative(pageUri) ?? pageUri.PathAndQuery;
        var title = Normalize(document.Title);

        IReadOnlyList<IElement> listElements = string.IsNullOrWhiteSpace(_options.List.ItemSelector)
            ? []
            : document.QuerySelectorAll(_options.List.ItemSelector);

        if (listElements.Count > 0)
        {
            return new PageResult
            {
                Kind = PageKind.List,
                Path = path,
                Url = pageUri.ToString(),
                Title = title,
                Items = listElements.Select(e => ExtractFields(e, _options.List.Fields, pageUri)).ToList(),
            };
        }

        return new PageResult
        {
            Kind = PageKind.Item,
            Path = path,
            Url = pageUri.ToString(),
            Title = title,
            Item = ExtractFields(document.DocumentElement, _options.Detail.Fields, pageUri),
        };
    }

    private Dictionary<string, object?> ExtractFields(IElement scope, Dictionary<string, FieldOptions> fields, Uri pageUri)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, field) in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Selector))
            {
                continue;
            }

            if (field.IsList)
            {
                IEnumerable<IElement> elements = field.Multiple
                    ? scope.QuerySelectorAll(field.Selector)
                    : scope.QuerySelector(field.Selector) is { } first ? [first] : [];

                result[name] = elements
                    .Select(e => ReadValue(e, field, pageUri))
                    .SelectMany(v => Split(v, field.Separator))
                    .ToList();
            }
            else
            {
                var element = scope.QuerySelector(field.Selector);
                result[name] = element is null ? null : ReadValue(element, field, pageUri);
            }
        }

        return result;
    }

    private string? ReadValue(IElement element, FieldOptions field, Uri pageUri)
    {
        if (string.IsNullOrEmpty(field.Attribute))
        {
            return Normalize(GetText(element));
        }

        var value = element.GetAttribute(field.Attribute);
        return IsUrlAttribute(field.Attribute) ? MakeLink(value, pageUri) : Normalize(value);
    }

    /// <summary>
    /// Links to the base server are returned as relative paths, so they can be
    /// passed straight back to the load tool; other links stay absolute.
    /// </summary>
    private string? MakeLink(string? value, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(pageUri, value.Trim(), out var target))
        {
            return Normalize(value);
        }

        return _resolver.ToRelative(target) ?? target.ToString();
    }

    private static IEnumerable<string> Split(string? value, string? separator)
    {
        if (value is null)
        {
            return [];
        }

        return string.IsNullOrEmpty(separator)
            ? [value]
            : value.Split(separator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Joins text nodes with spaces so that adjacent block elements do not run together,
    /// skipping script and style content.
    /// </summary>
    private static string GetText(IElement element) =>
        string.Join(' ', element.Descendants<IText>()
            .Where(t => t.ParentElement?.LocalName is not ("script" or "style" or "noscript" or "template"))
            .Select(t => t.Data));

    private static bool IsUrlAttribute(string attribute) =>
        attribute.Equals("href", StringComparison.OrdinalIgnoreCase) ||
        attribute.Equals("src", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
