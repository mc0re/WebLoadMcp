using WebLoadMcp.Configuration;

namespace WebLoadMcp.Services;

/// <summary>
/// Normalizes a magnet link found on an item page: replaces the display name (<c>dn</c>)
/// with one built from the item's title and year, and removes the trackers (<c>tr</c>),
/// which are kept in configuration instead. Trackers that differ from the configured
/// ones are reported as warnings.
/// </summary>
public sealed class MagnetLinkRewriter
{
    private const string Prefix = "magnet:?";

    private readonly MagnetOptions _options;

    public MagnetLinkRewriter(MagnetOptions options) => _options = options;

    /// <summary>Rewrites the magnet link in the configured field of <paramref name="item"/>, if there is one.</summary>
    public void Rewrite(Dictionary<string, object?> item, List<string> warnings)
    {
        if (string.IsNullOrEmpty(_options.Field) ||
            !item.TryGetValue(_options.Field, out var value) ||
            value is not string link ||
            !link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var kept = new List<string>();
        var trackers = new List<string>();

        foreach (var part in link[Prefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = part.Split('=', 2)[0];
            if (key.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                trackers.Add(Decode(part[(key.Length + 1)..]));
            }
            else if (!key.Equals("dn", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(part);
            }
        }

        var name = BuildName(item);
        if (name is not null)
        {
            kept.Add("dn=" + Uri.EscapeDataString(name));
        }

        item[_options.Field] = Prefix + string.Join('&', kept);
        CompareTrackers(trackers, warnings);
    }

    private static string? BuildName(Dictionary<string, object?> item)
    {
        var title = item.GetValueOrDefault("title") as string;
        var year = item.GetValueOrDefault("year") as string;

        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        return string.IsNullOrEmpty(year) ? title : $"{title} ({year})";
    }

    private void CompareTrackers(List<string> actual, List<string> warnings)
    {
        var expected = _options.Trackers;
        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(expected, StringComparer.Ordinal).ToList();

        if (missing.Count > 0)
        {
            warnings.Add($"Magnet link lacks configured trackers: {string.Join(", ", missing)}");
        }

        if (unexpected.Count > 0)
        {
            warnings.Add($"Magnet link has trackers that are not configured: {string.Join(", ", unexpected)}");
        }
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
