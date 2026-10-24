using WebLoadMcp.Configuration;
using WebLoadMcp.Models;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

public class PageParserTests
{
    internal static WebLoadOptions Options() => new()
    {
        BaseUrl = "https://example.com/site/",
        List = new ListPageOptions
        {
            ItemSelector = ".item",
            Fields = new(StringComparer.OrdinalIgnoreCase)
            {
                ["year"] = new() { Selector = ".year" },
                ["title"] = new() { Selector = ".title" },
                ["link"] = new() { Selector = "a[href]", Attribute = "href" },
            },
        },
        Detail = new DetailPageOptions
        {
            Fields = new(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = new() { Selector = "h1" },
                ["country"] = new() { Selector = ".country" },
                ["description"] = new() { Selector = ".description" },
                ["genres"] = new() { Selector = ".genres", Separator = ",/" },
                ["audio"] = new() { Selector = ".audio li", Multiple = true },
                ["subtitles"] = new() { Selector = ".subtitles li", Multiple = true },
                ["size"] = new() { Selector = ".size" },
                ["reference"] = new() { Selector = "a.download[href]", Attribute = "href" },
            },
        },
    };

    private static PageParser Parser()
    {
        var options = Options();
        return new PageParser(options, new PathResolver(options.BaseUrl));
    }

    private const string ListHtml = """
        <html><head><title>Archive</title></head><body>
          <div class="item"><span class="year">2023</span> <a href="items/1"><span class="title">First
             one</span></a></div>
          <div class="item"><span class="year">2024</span> <a href="/site/items/2"><span class="title">Second</span></a></div>
          <div class="item"><span class="title">External</span> <a href="https://other.org/x">x</a></div>
        </body></html>
        """;

    [Fact]
    public void ListPage_ReturnsItemsWithYearTitleAndRelativeLink()
    {
        var result = Parser().Parse(ListHtml, new Uri("https://example.com/site/archive/"));

        Assert.Equal(PageKind.List, result.Kind);
        Assert.Equal("archive/", result.Path);
        Assert.Null(result.Item);
        Assert.NotNull(result.Items);
        Assert.Equal(3, result.Items.Count);

        Assert.Equal("2023", result.Items[0]["year"]);
        Assert.Equal("First one", result.Items[0]["title"]);
        Assert.Equal("archive/items/1", result.Items[0]["link"]);

        Assert.Equal("2024", result.Items[1]["year"]);
        Assert.Equal("items/2", result.Items[1]["link"]);

        Assert.Null(result.Items[2]["year"]);
        Assert.Equal("https://other.org/x", result.Items[2]["link"]);
    }

    [Fact]
    public void PageWithoutListItems_ReturnsSingleItem()
    {
        const string html = """
            <html><head><title>First one</title></head><body>
              <h1>  First one </h1>
              <span class="country">Denmark</span>
              <div class="description"><p>Some</p><p>text</p><script>var x;</script></div>
              <span class="genres">Drama, Comedy / Crime</span>
              <ul class="audio"><li>English</li><li>Danish</li></ul>
              <ul class="subtitles"></ul>
              <span class="size">1.4 GB</span>
              <a class="download" href="../files/1.bin">Download</a>
            </body></html>
            """;

        var result = Parser().Parse(html, new Uri("https://example.com/site/items/1"));

        Assert.Equal(PageKind.Item, result.Kind);
        Assert.Null(result.Items);
        var item = Assert.IsType<Dictionary<string, object?>>(result.Item);
        Assert.Equal("First one", item["title"]);
        Assert.Equal("Denmark", item["country"]);
        Assert.Equal("Some text", item["description"]);
        Assert.Equal(["Drama", "Comedy", "Crime"], (IEnumerable<string>)item["genres"]!);
        Assert.Equal(["English", "Danish"], (IEnumerable<string>)item["audio"]!);
        Assert.Empty((IEnumerable<string>)item["subtitles"]!);
        Assert.Equal("1.4 GB", item["size"]);
        Assert.Equal("files/1.bin", item["reference"]);
    }

    [Fact]
    public void MissingListField_IsEmptyListAndMissingScalarIsNull()
    {
        var result = Parser().Parse("<html><body><h1>Bare</h1></body></html>", new Uri("https://example.com/site/x"));

        Assert.Empty((IEnumerable<string>)result.Item!["genres"]!);
        Assert.Null(result.Item["size"]);
    }

    [Theory]
    [InlineData("<b>Аудио:</b> AC3, 6 ch<br>", "<b>Субтитры</b>: нет<br>", new[] { "Russian" }, new string[0])]
    [InlineData("<b>Аудио 1:</b> Английский AC3<br><b>Аудио 2:</b> AC3, rus<br>", "<b>Субтитры:</b> English, рус.<br>", new[] { "English", "Russian" }, new[] { "English", "Russian" })]
    [InlineData("<b>Аудио:</b> AC3 perfect normal final<br>", "", new[] { "Russian" }, new string[0])]
    [InlineData("<b>Аудио 1:</b> Русский<br><b>Аудио 2:</b> Оригинальная дорожка<br>", "", new[] { "Russian", "French" }, new string[0])]
    [InlineData("<b>Аудио 1:</b> Оригинал<br><b>Аудио 2:</b> Французский<br>", "", new[] { "French" }, new string[0])]
    public void LanguageFields(string audioHtml, string subtitlesHtml, string[] audio, string[] subtitles)
    {
        var result = LanguageParser().Parse(
            $"<html><body><b>Страна</b>: Франция, США<br>{audioHtml}{subtitlesHtml}</body></html>",
            new Uri("https://example.com/site/x"));

        Assert.Equal(audio, (IEnumerable<string>)result.Item!["audio"]!);
        Assert.Equal(subtitles, (IEnumerable<string>)result.Item["subtitles"]!);
    }

    [Theory]
    [InlineData("<b>Выпущено</b>: США, Columbia Pictures, Великобритания<br>", new[] { "United States", "United Kingdom" }, "English")]
    [InlineData("<b>Выпущено</b>: Some Studio<br>", new string[0], "Original")]
    public void CountryField_KeepsKnownCountriesAndDrivesOriginalLanguage(string countryHtml, string[] countries, string original)
    {
        var result = LanguageParser().Parse(
            $"<html><body>{countryHtml}<b>Аудио:</b> оригинал<br></body></html>",
            new Uri("https://example.com/site/x"));

        Assert.Equal(countries, (IEnumerable<string>)result.Item!["country"]!);
        Assert.Equal([original], (IEnumerable<string>)result.Item["audio"]!);
    }

    [Fact]
    public void UnknownCountriesAndLanguagesAreLogged()
    {
        var logger = new ListLogger<PageParser>();
        LanguageParser(logger).Parse(
            "<html><body><b>Выпущено</b>: Атлантида, США<br><b>Аудио 1:</b> Клингонский<br><b>Аудио 2:</b> Русский<br></body></html>",
            new Uri("https://example.com/site/x"));

        Assert.Equal(
            [
                "x: unknown country \"Атлантида\" in country, ignored",
                "x: no known language in audio value \"Клингонский\", using \"Russian\"",
            ],
            logger.Messages);
    }

    [Theory]
    [InlineData("<b>Выпущено</b>: Some Studio<br>", "x: original-language track in audio, but no known country")]
    [InlineData("<b>Страна</b>: Атлантида<br>", "x: original-language track in audio, but no known country")]
    [InlineData("<b>Страна</b>: Утопия<br>", "x: original-language track in audio, but the language of Utopia is not known")]
    public void UnresolvedOriginalLanguageIsLogged(string countryHtml, string expected)
    {
        var logger = new ListLogger<PageParser>();
        LanguageParser(logger).Parse($"<html><body>{countryHtml}<b>Аудио:</b> оригинал<br></body></html>", new Uri("https://example.com/site/x"));

        Assert.Contains(expected, logger.Messages);
    }

    private static PageParser LanguageParser(ListLogger<PageParser>? logger = null)
    {
        var options = Options();
        options.Detail.Fields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["country"] = new() { Selector = "b", Label = "Страна|Выпущено", Separator = ",", Country = true, Distinct = true },
            ["audio"] = new() { Selector = "b", Label = "Аудио", Multiple = true, Language = true, Default = "Russian", Distinct = true },
            ["subtitles"] = new() { Selector = "b", Label = "Субтитры", Separator = ",", Language = true, Default = "Russian", Distinct = true },
        };
        options.Languages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["русск"] = "Russian", ["рус"] = "Russian", ["rus"] = "Russian",
            ["англ"] = "English", ["english"] = "English", ["eng"] = "English",
            ["франц"] = "French", ["per"] = "Persian", ["nor"] = "Norwegian", ["fin"] = "Finnish",
            ["оригинал"] = WebLoadOptions.OriginalLanguage, ["нет"] = "",
        };
        options.Countries = new(StringComparer.OrdinalIgnoreCase)
        {
            ["США"] = new() { Name = "United States", Language = "English" },
            ["Великобритания"] = new() { Name = "United Kingdom", Language = "English" },
            ["Франция"] = new() { Name = "France", Language = "French" },
            ["Утопия"] = new() { Name = "Utopia" },
        };
        return new PageParser(options, new PathResolver(options.BaseUrl), logger);
    }
}
