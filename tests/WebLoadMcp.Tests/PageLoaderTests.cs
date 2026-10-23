using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WebLoadMcp.Models;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

public class PageLoaderTests
{
    private static PageLoader Loader(HttpMessageHandler handler)
    {
        var options = PageParserTests.Options();
        var resolver = new PathResolver(options.BaseUrl);
        return new PageLoader(new FakeHttpClientFactory(handler), resolver, new PageParser(options, resolver), NullLogger<PageLoader>.Instance);
    }

    [Fact]
    public async Task LoadsPageFromBaseServer()
    {
        var handler = FakeHandler.Returning("""<div class="item"><span class="year">2020</span><a href="x">t</a></div>""");

        var result = await Loader(handler).LoadAsync("list", TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://example.com/site/list"), Assert.Single(handler.Requests));
        Assert.Equal(PageKind.List, result.Kind);
        Assert.Equal("x", Assert.Single(result.Items!)["link"]);
    }

    [Fact]
    public async Task HttpError_Throws()
    {
        var handler = FakeHandler.Returning("nope", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader(handler).LoadAsync("missing", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task AbsoluteUrl_IsRejectedWithoutRequest()
    {
        var handler = FakeHandler.Returning("");

        await Assert.ThrowsAsync<ArgumentException>(() => Loader(handler).LoadAsync("https://evil.com/", TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }
}
