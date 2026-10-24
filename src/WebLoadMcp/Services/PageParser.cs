using System.Text;
using System.Text.RegularExpressions;
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
    private const int MaxWholeWordStem = 3;

    private readonly WebLoadOptions _options;
    private readonly PathResolver _resolver;
    private readonly MagnetLinkRewriter _magnet;
    private readonly List<KeyValuePair<string, string>> _languageStems;
    private readonly Dictionary<string, string> _languageByCountry;
    private readonly HtmlParser _htmlParser = new();

    public PageParser(WebLoadOptions options, PathResolver resolver)
    {
        _options = options;
        _resolver = resolver;
        _magnet = new MagnetLinkRewriter(options.Magnet);

        // Longest stems first, so a specific stem wins over a shorter one it starts with.
        _languageStems = options.Languages
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .OrderByDescending(p => p.Key.Length)
            .ToList();

        _languageByCountry = options.Countries.Values
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Language))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Language!, StringComparer.OrdinalIgnoreCase);
    }

    public PageResult Parse(string html, Uri pageUri)
    {
        using var document = _htmlParser.ParseDocument(html);
        var path = _resolver.ToRelative(pageUri) ?? pageUri.PathAndQuery;

        IReadOnlyList<IElement> listElements = string.IsNullOrWhiteSpace(_options.List.ItemSelector)
            ? []
            : document.QuerySelectorAll(_options.List.ItemSelector);

        if (listElements.Count > 0)
        {
            return new PageResult
            {
                Kind = PageKind.List,
                Path = path,
                Items = listElements.Select(e => ExtractFields(e, _options.List.Fields, pageUri)).ToList(),
            };
        }

        var item = ExtractFields(document.DocumentElement, _options.Detail.Fields, pageUri);
        var warnings = new List<string>();
        _magnet.Rewrite(item, warnings);

        return new PageResult
        {
            Kind = PageKind.Item,
            Path = path,
            Item = item,
            Warnings = warnings.Count > 0 ? warnings : null,
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

            var elements = scope.QuerySelectorAll(field.Selector)
                .Where(e => string.IsNullOrEmpty(field.Label) || HasLabel(e, field.Label));
            var values = (field.Multiple ? elements : elements.Take(1))
                .Select(e => ApplyPattern(ReadValue(e, field, pageUri), field.Pattern));

            if (field.IsList)
            {
                var list = values.SelectMany(v => Split(v, field.Separator));
                if (field.Country)
                {
                    list = list.Select(ToCountry).OfType<string>();
                }

                if (field.Language)
                {
                    list = list.Select(v => ToLanguage(v, field.Default)).OfType<string>().Where(v => v.Length > 0);
                }

                result[name] = list.ToList();
            }
            else
            {
                var value = values.FirstOrDefault();
                if (field.Country)
                {
                    value = value is null ? null : ToCountry(value);
                }

                result[name] = field.Language ? ToLanguage(value, field.Default) : value;
            }
        }

        ResolveOriginalLanguage(result, fields);

        foreach (var (name, field) in fields)
        {
            if (field.Distinct && result.GetValueOrDefault(name) is List<string> list)
            {
                result[name] = list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        return result;
    }

    private string? ReadValue(IElement element, FieldOptions field, Uri pageUri)
    {
        if (!string.IsNullOrEmpty(field.Label))
        {
            return TextAfterLabel(element, field.Selector);
        }

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

        var relative = _resolver.ToRelative(target);
        if (relative is not null)
        {
            return relative;
        }

        // Keep absolute links exactly as written (e.g. magnet links with encoded parameters).
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out _) ? value.Trim() : target.ToString();
    }

    /// <summary>
    /// Finds the language named in <paramref name="value"/>: the first word that starts with
    /// one of the configured language stems. Returns <paramref name="defaultLanguage"/> when no
    /// language is named, and an empty string for stems mapped to "" (e.g. "нет").
    /// Short stems (codes like "eng", abbreviations like "рус") must match a whole word,
    /// so that e.g. "per" does not match "performance".
    /// </summary>
    private string? ToLanguage(string? value, string? defaultLanguage)
    {
        if (value is null)
        {
            return null;
        }

        foreach (var word in Regex.Split(value, @"[^\p{L}]+"))
        {
            foreach (var (stem, language) in _languageStems)
            {
                var matches = stem.Length <= MaxWholeWordStem
                    ? word.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    : word.StartsWith(stem, StringComparison.OrdinalIgnoreCase);
                if (matches)
                {
                    return language;
                }
            }
        }

        return defaultLanguage;
    }

    /// <summary>Maps a country name as written on the site to its configured name, or <c>null</c> if it is not a known country.</summary>
    private string? ToCountry(string value) =>
        _options.Countries.TryGetValue(value.Trim(), out var country) && !string.IsNullOrWhiteSpace(country.Name)
            ? country.Name
            : null;

    /// <summary>
    /// Replaces <see cref="WebLoadOptions.OriginalLanguage"/> in language fields with the language of the
    /// first production country, or with "Original" when that country's language is not known.
    /// </summary>
    private void ResolveOriginalLanguage(Dictionary<string, object?> item, Dictionary<string, FieldOptions> fields)
    {
        var firstCountry = fields
            .Where(f => f.Value.Country)
            .Select(f => item.GetValueOrDefault(f.Key))
            .Select(v => v is List<string> list ? list.FirstOrDefault() : v as string)
            .FirstOrDefault(c => c is not null);
        var original = firstCountry is not null && _languageByCountry.TryGetValue(firstCountry, out var language)
            ? language
            : "Original";

        foreach (var (name, field) in fields.Where(f => f.Value.Language))
        {
            switch (item.GetValueOrDefault(name))
            {
                case List<string> list:
                    item[name] = list.Select(v => v == WebLoadOptions.OriginalLanguage ? original : v).ToList();
                    break;
                case WebLoadOptions.OriginalLanguage:
                    item[name] = original;
                    break;
            }
        }
    }

    /// <summary>Checks the element's text against the label, or against any of several labels separated by '|'.</summary>
    private static bool HasLabel(IElement element, string label)
    {
        var text = Normalize(element.TextContent)?.TrimEnd(':', ' ');
        return text is not null && label.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(l => text.StartsWith(l, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the text that follows a label element, as in <c>&lt;b&gt;Country:&lt;/b&gt; USA&lt;br&gt;</c>
    /// or <c>&lt;td class="header"&gt;Size&lt;/td&gt;&lt;td&gt;1 GB&lt;/td&gt;</c>.
    /// The value ends at the next line break or the next label. If the label is directly followed
    /// by a line break, the value is taken from the next line.
    /// </summary>
    private static string? TextAfterLabel(IElement label, string labelSelector)
    {
        var text = new StringBuilder();
        var skippedBreak = false;

        for (var node = label.NextSibling; node is not null; node = node.NextSibling)
        {
            if (node is IText textNode)
            {
                text.Append(textNode.Data);
                continue;
            }

            if (node is not IElement element)
            {
                continue;
            }

            if (element.LocalName == "br")
            {
                if (!string.IsNullOrWhiteSpace(StripColon(text.ToString())) || skippedBreak)
                {
                    break;
                }

                skippedBreak = true;
                continue;
            }

            if (element.Matches(labelSelector))
            {
                break;
            }

            text.Append(GetText(element));
        }

        return Normalize(StripColon(text.ToString()));
    }

    /// <summary>Removes the colon that follows a label written as <c>&lt;b&gt;Label&lt;/b&gt;: value</c>.</summary>
    private static string StripColon(string text) => text.TrimStart().TrimStart(':');

    /// <summary>
    /// Narrows the value with a regular expression: the first capture group if the pattern has one,
    /// otherwise the whole match. No match gives <c>null</c>.
    /// </summary>
    private static string? ApplyPattern(string? value, string? pattern)
    {
        if (value is null || string.IsNullOrEmpty(pattern))
        {
            return value;
        }

        var match = Regex.Match(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            return null;
        }

        return Normalize(match.Groups.Count > 1 ? match.Groups[1].Value : match.Value);
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
