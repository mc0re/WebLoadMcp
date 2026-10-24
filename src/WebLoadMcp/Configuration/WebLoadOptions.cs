namespace WebLoadMcp.Configuration;

/// <summary>
/// Settings for loading and parsing pages from the predefined server.
/// Bound from the "WebLoad" configuration section.
/// </summary>
public sealed class WebLoadOptions
{
    public const string SectionName = "WebLoad";

    /// <summary>Absolute base URL of the server; all requested paths are resolved against it.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public string? UserAgent { get; set; }

    public ListPageOptions List { get; set; } = new();

    public DetailPageOptions Detail { get; set; } = new();

    public MagnetOptions Magnet { get; set; } = new();

    /// <summary>
    /// Language names by word stem (e.g. "рус" → "Русский", "eng" → "Английский"), used by fields
    /// with <see cref="FieldOptions.Language"/>. A stem mapped to "" means "no language" (e.g. "нет").
    /// </summary>
    public Dictionary<string, string> Languages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// How magnet links on item pages are rewritten (see <c>MagnetLinkRewriter</c>).
/// </summary>
public sealed class MagnetOptions
{
    /// <summary>Item field holding the magnet link. Empty disables the rewrite.</summary>
    public string? Field { get; set; } = "reference";

    /// <summary>
    /// Trackers expected in the site's magnet links. They are removed from the returned link;
    /// a page whose trackers differ produces a warning.
    /// </summary>
    public List<string> Trackers { get; set; } = [];
}

public sealed class ListPageOptions
{
    /// <summary>
    /// CSS selector matching each item on a list page.
    /// A page where this selector matches nothing is treated as a single (detail) item.
    /// </summary>
    public string ItemSelector { get; set; } = "article";

    /// <summary>Fields extracted from each list item, relative to the item element.</summary>
    public Dictionary<string, FieldOptions> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DetailPageOptions
{
    /// <summary>Fields extracted from a detail page, relative to the document.</summary>
    public Dictionary<string, FieldOptions> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FieldOptions
{
    /// <summary>CSS selector of the element holding the value.</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>Attribute to read instead of the element text (e.g. "href", "src").</summary>
    public string? Attribute { get; set; }

    /// <summary>
    /// Only use elements matched by <see cref="Selector"/> whose text starts with this label
    /// (or with any of several labels separated by '|')
    /// (e.g. "Country" for <c>&lt;b&gt;Country:&lt;/b&gt; USA&lt;br&gt;</c>).
    /// The value is then the text following the label element, not the element itself.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Regular expression applied to the value: the first capture group is used if there is one,
    /// otherwise the whole match. No match gives an empty value.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>Return all matches as an array instead of only the first one.</summary>
    public bool Multiple { get; set; }

    /// <summary>
    /// Characters that split each value into several list entries (e.g. "," for "Drama, Comedy").
    /// Setting it makes the field a list, like <see cref="Multiple"/>.
    /// </summary>
    public string? Separator { get; set; }

    /// <summary>
    /// Replace each value with the language it names (see <see cref="WebLoadOptions.Languages"/>),
    /// or with <see cref="Default"/> when it names none.
    /// </summary>
    public bool Language { get; set; }

    /// <summary>Value used when <see cref="Language"/> finds no language name.</summary>
    public string? Default { get; set; }

    /// <summary>Remove duplicate values from a list field.</summary>
    public bool Distinct { get; set; }

    public bool IsList => Multiple || !string.IsNullOrEmpty(Separator);
}
