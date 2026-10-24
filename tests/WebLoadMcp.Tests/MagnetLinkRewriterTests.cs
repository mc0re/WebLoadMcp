using WebLoadMcp.Configuration;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

public class MagnetLinkRewriterTests
{
    private const string Hash = "xt=urn:btih:73e4ebea578584f332dbf64fb1c302b57210b1c4";

    private static readonly MagnetLinkRewriter Rewriter = new(new MagnetOptions
    {
        Field = "reference",
        Trackers = ["udp://a.org:6969", "http://b.local/announce"],
    });

    private static (Dictionary<string, object?> Item, List<string> Warnings) Rewrite(string? link, string? title = "Film", string? year = "1999")
    {
        var item = new Dictionary<string, object?> { ["title"] = title, ["year"] = year, ["reference"] = link };
        var warnings = new List<string>();
        Rewriter.Rewrite(item, warnings);
        return (item, warnings);
    }

    [Fact]
    public void ReplacesNameAndDropsMatchingTrackers()
    {
        var (item, warnings) = Rewrite($"magnet:?{Hash}&dn=site_Old+name&tr=http://b.local/announce&tr=udp%3A%2F%2Fa.org%3A6969");

        Assert.Equal($"magnet:?{Hash}&dn=Film%20%281999%29", item["reference"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void KeepsOtherParameters()
    {
        var (item, _) = Rewrite($"magnet:?{Hash}&xl=123&tr=udp://a.org:6969&tr=http://b.local/announce");

        Assert.Equal($"magnet:?{Hash}&xl=123&dn=Film%20%281999%29", item["reference"]);
    }

    [Fact]
    public void NameWithoutYear()
    {
        var (item, _) = Rewrite($"magnet:?{Hash}&tr=udp://a.org:6969&tr=http://b.local/announce", year: null);

        Assert.Equal($"magnet:?{Hash}&dn=Film", item["reference"]);
    }

    [Fact]
    public void DifferentTrackers_Warn()
    {
        var (item, warnings) = Rewrite($"magnet:?{Hash}&tr=udp://a.org:6969&tr=udp://new.org:80");

        Assert.Equal($"magnet:?{Hash}&dn=Film%20%281999%29", item["reference"]);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("lacks") && w.Contains("http://b.local/announce"));
        Assert.Contains(warnings, w => w.Contains("not configured") && w.Contains("udp://new.org:80"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.org/file.torrent")]
    public void NonMagnetValue_IsLeftAlone(string? link)
    {
        var (item, warnings) = Rewrite(link);

        Assert.Equal(link, item["reference"]);
        Assert.Empty(warnings);
    }
}
