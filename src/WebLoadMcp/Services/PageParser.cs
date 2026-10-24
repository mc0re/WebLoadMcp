using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<PageParser> _logger;
    private readonly PathResolver _resolver;
    private readonly PathResolver _listResolver;
    private readonly PathResolver _itemResolver;
    private readonly MagnetLinkRewriter _magnet;
    private readonly List<KeyValuePair<string, string>> _languageStems;
    private readonly Dictionary<string, string> _languageByCountry;
    private readonly HtmlParser _htmlParser = new();

    public PageParser(WebLoadOptions options, PathResolver resolver, ILogger<PageParser>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<PageParser>.Instance;
        _resolver = resolver;
        _listResolver = resolver.WithPrefix(options.List.PathPrefix);
        _itemResolver = resolver.WithPrefix(options.Detail.PathPrefix);
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

    /// <summary>Resolves get_list paths (relative to the list prefix).</summary>
    public PathResolver ListPaths => _listResolver;

    /// <summary>Resolves get_item paths (relative to the item prefix).</summary>
    public PathResolver ItemPaths => _itemResolver;

    /// <summary>Parses a list page; a page without matching rows gives an empty list.</summary>
    public PageResult ParseList(string html, Uri pageUri)
    {
        using var document = _htmlParser.ParseDocument(html);
        var path = _listResolver.ToRelative(pageUri) ?? pageUri.PathAndQuery;

        IReadOnlyList<IElement> rows = string.IsNullOrWhiteSpace(_options.List.ItemSelector)
            ? []
            : document.QuerySelectorAll(_options.List.ItemSelector);

        return new PageResult
        {
            Kind = PageKind.List,
            Path = path,
            Items = rows.Select(e => ExtractFields(e, _options.List.Fields, pageUri, path)).ToList(),
        };
    }

    /// <summary>Parses an item page.</summary>
    public PageResult ParseItem(string html, Uri pageUri)
    {
        using var document = _htmlParser.ParseDocument(html);
        var path = _itemResolver.ToRelative(pageUri) ?? pageUri.PathAndQuery;

        var item = ExtractFields(document.DocumentElement, _options.Detail.Fields, pageUri, path);
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

    private Dictionary<string, object?> ExtractFields(IElement scope, Dictionary<string, FieldOptions> fields, Uri pageUri, string path)
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
                    list = list.Select(v => ToCountry(v, name, path)).OfType<string>();
                }

                if (field.Language)
                {
                    list = list.SelectMany(v => ToLanguages(v, field.Default, name, path)).Where(v => v.Length > 0);
                }

                result[name] = list.ToList();
            }
            else
            {
                var value = values.FirstOrDefault();
                if (field.Country)
                {
                    value = value is null ? null : ToCountry(value, name, path);
                }

                result[name] = field.Language ? ToLanguages(value, field.Default, name, path).FirstOrDefault() : value;
            }
        }

        ResolveOriginalLanguage(result, fields, path);

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
            return TextAfterLabel(element, field.Selector, field.Multiline);
        }

        if (string.IsNullOrEmpty(field.Attribute))
        {
            return Normalize(GetText(element));
        }

        var value = element.GetAttribute(field.Attribute);
        return IsUrlAttribute(field.Attribute) ? MakeLink(value, pageUri, field.ItemLink) : Normalize(value);
    }

    /// <summary>
    /// Links to the base server are returned as relative paths (item links relative to the
    /// item prefix, so they can be passed to get_item); other links stay absolute.
    /// </summary>
    private string? MakeLink(string? value, Uri pageUri, bool itemLink)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(pageUri, value.Trim(), out var target))
        {
            return Normalize(value);
        }

        var relative = (itemLink ? _itemResolver.ToRelative(target) : null) ?? _resolver.ToRelative(target);
        if (relative is not null)
        {
            return relative;
        }

        // Keep absolute links exactly as written (e.g. magnet links with encoded parameters).
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out _) ? value.Trim() : target.ToString();
    }

    /// <summary>
    /// Finds the languages named in <paramref name="value"/>, in order: every word that starts with
    /// one of the configured language stems. Returns <paramref name="defaultLanguage"/> when no
    /// language is named, and an empty string for stems mapped to "" (e.g. "нет").
    /// Short stems (codes like "eng", abbreviations like "рус") must match a whole word,
    /// so that e.g. "per" does not match "performance".
    /// </summary>
    private List<string> ToLanguages(string? value, string? defaultLanguage, string field, string path)
    {
        if (value is null)
        {
            return [];
        }

        var found = new List<string>();
        foreach (var word in Regex.Split(value, @"[^\p{L}]+"))
        {
            foreach (var (stem, language) in _languageStems)
            {
                var matches = stem.Length <= MaxWholeWordStem
                    ? word.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    : word.StartsWith(stem, StringComparison.OrdinalIgnoreCase);
                if (matches)
                {
                    if (!found.Contains(language))
                    {
                        found.Add(language);
                    }

                    break;
                }
            }
        }

        if (found.Count > 0)
        {
            return found;
        }

        _logger.LogInformation(
            "{Path}: no known language in {Field} value \"{Value}\", using \"{Default}\"", path, field, value, defaultLanguage);
        return defaultLanguage is null ? [] : [defaultLanguage];
    }

    /// <summary>Maps a country name as written on the site to its configured name, or <c>null</c> if it is not a known country.</summary>
    private string? ToCountry(string value, string field, string path)
    {
        if (_options.Countries.TryGetValue(value.Trim(), out var country) && !string.IsNullOrWhiteSpace(country.Name))
        {
            return country.Name;
        }

        // Lines like "Выпущено: США, Columbia Pictures" also list studios, which end up here too.
        _logger.LogInformation("{Path}: unknown country \"{Value}\" in {Field}, ignored", path, value.Trim(), field);
        return null;
    }

    /// <summary>
    /// Replaces <see cref="WebLoadOptions.OriginalLanguage"/> in language fields with the language of the
    /// first production country, or with "Original" when that country's language is not known.
    /// </summary>
    private void ResolveOriginalLanguage(Dictionary<string, object?> item, Dictionary<string, FieldOptions> fields, string path)
    {
        var firstCountry = fields
            .Where(f => f.Value.Country)
            .Select(f => item.GetValueOrDefault(f.Key))
            .Select(v => v is List<string> list ? list.FirstOrDefault() : v as string)
            .FirstOrDefault(c => c is not null);
        var original = firstCountry is not null && _languageByCountry.TryGetValue(firstCountry, out var language)
            ? language
            : null;

        foreach (var (name, field) in fields.Where(f => f.Value.Language))
        {
            var value = item.GetValueOrDefault(name);
            var hasOriginal = value is WebLoadOptions.OriginalLanguage ||
                value is List<string> values && values.Contains(WebLoadOptions.OriginalLanguage);
            if (!hasOriginal)
            {
                continue;
            }

            if (firstCountry is null)
            {
                _logger.LogInformation("{Path}: original-language track in {Field}, but no known country", path, name);
            }
            else if (original is null)
            {
                _logger.LogInformation(
                    "{Path}: original-language track in {Field}, but the language of {Country} is not known", path, name, firstCountry);
            }

            switch (value)
            {
                case List<string> list:
                    item[name] = list.Select(v => v == WebLoadOptions.OriginalLanguage ? original ?? "Original" : v).ToList();
                    break;
                case WebLoadOptions.OriginalLanguage:
                    item[name] = original ?? "Original";
                    break;
            }
        }
    }

    /// <summary>Checks the element's text against the label, or against any of several labels separated by '|'.</summary>
    private static bool HasLabel(IElement element, string label)
    {
        var text = Normalize(element.TextContent)?.TrimEnd(':', ' ');
        if (text is null)
        {
            return false;
        }

        text = FoldLookalikes(text);
        return label.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(l => text.StartsWith(FoldLookalikes(l), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Replaces Latin letters that look like Cyrillic ones (as in "Cубтитры" typed with a Latin C)
    /// with their Cyrillic twins, so that such labels still match.
    /// </summary>
    private static string FoldLookalikes(string text)
    {
        const string latin = "AaBCcEeHKMOoPpTXxy";
        const string cyrillic = "АаВСсЕеНКМОоРрТХху";

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var index = latin.IndexOf(chars[i]);
            if (index >= 0)
            {
                chars[i] = cyrillic[index];
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Reads the text that follows a label element, as in <c>&lt;b&gt;Country:&lt;/b&gt; USA&lt;br&gt;</c>
    /// or <c>&lt;td class="header"&gt;Size&lt;/td&gt;&lt;td&gt;1 GB&lt;/td&gt;</c>.
    /// The value ends at the next line break or the next label. If the label is directly followed
    /// by a line break, the value is taken from the next line.
    /// </summary>
    private static string? TextAfterLabel(IElement label, string labelSelector, bool multiline)
    {
        var lines = new List<string>();
        var text = new StringBuilder();
        var skippedBreak = false;

        // Adds the text collected so far as a line; returns false if there was none.
        bool EndLine()
        {
            var line = Normalize(lines.Count == 0 ? StripColon(text.ToString()) : text.ToString());
            text.Clear();
            if (line is null)
            {
                return false;
            }

            lines.Add(line);
            return true;
        }

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
                if (EndLine())
                {
                    if (!multiline)
                    {
                        break;
                    }
                }
                else if (lines.Count > 0 || skippedBreak)
                {
                    // An empty line ends the value; so does a second break right after the label.
                    break;
                }
                else
                {
                    skippedBreak = true;
                }

                continue;
            }

            if (element.Matches(labelSelector))
            {
                break;
            }

            text.Append(GetText(element));
        }

        EndLine();
        return lines.Count == 0 ? null : string.Join('\n', lines);
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

    /// <summary>Splits at any separator character that is not inside brackets, as in "английские (full, SDH)".</summary>
    private static IEnumerable<string> Split(string? value, string? separator)
    {
        if (value is null)
        {
            return [];
        }

        if (string.IsNullOrEmpty(separator))
        {
            return [value];
        }

        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth = Math.Max(0, depth - 1);
                    break;
                default:
                    if (depth == 0 && separator.Contains(value[i]))
                    {
                        parts.Add(value[start..i]);
                        start = i + 1;
                    }

                    break;
            }
        }

        parts.Add(value[start..]);
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0);
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
