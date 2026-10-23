using System.Text.Json.Serialization;

namespace WebLoadMcp.Models;

public static class PageKind
{
    public const string List = "list";
    public const string Item = "item";
}

/// <summary>
/// Result of loading a page: either a list of items or a single item with details.
/// </summary>
public sealed class PageResult
{
    /// <summary>"list" or "item".</summary>
    public required string Kind { get; init; }

    /// <summary>The requested path, relative to the base URL.</summary>
    public required string Path { get; init; }

    /// <summary>The absolute URL that was loaded.</summary>
    public required string Url { get; init; }

    /// <summary>Content of the HTML &lt;title&gt; element.</summary>
    public string? Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Dictionary<string, object?>>? Items { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Item { get; init; }
}
