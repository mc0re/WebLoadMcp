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

    public bool IsList => Multiple || !string.IsNullOrEmpty(Separator);
}
