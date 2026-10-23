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
                ["content"] = new() { Selector = ".content" },
                ["images"] = new() { Selector = "img[src]", Attribute = "src", Multiple = true },
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
        Assert.Equal("Archive", result.Title);
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
            <html><head><title>Item 1</title></head><body>
              <h1>  First one </h1>
              <div class="content"><p>Some</p><p>text</p><script>var x;</script></div>
              <img src="img/a.png"><img src="https://cdn.org/b.png">
            </body></html>
            """;

        var result = Parser().Parse(html, new Uri("https://example.com/site/items/1"));

        Assert.Equal(PageKind.Item, result.Kind);
        Assert.Null(result.Items);
        Assert.NotNull(result.Item);
        Assert.Equal("First one", result.Item["title"]);
        Assert.Equal("Some text", result.Item["content"]);
        Assert.Equal(new[] { "items/img/a.png", "https://cdn.org/b.png" }, (IEnumerable<string?>)result.Item["images"]!);
    }
}
