using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebLoadMcp.Models;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

public class PageLoaderTests
{
    private static PageLoader Loader(HttpMessageHandler handler)
    {
        var options = PageParserTests.Options();
        return new PageLoader(
            new FakeHttpClientFactory(handler), new PageParser(options, new PathResolver(options.BaseUrl)), NullLogger<PageLoader>.Instance);
    }

    [Fact]
    public async Task GetList_LoadsFromListPrefix()
    {
        var handler = FakeHandler.Returning("""<div class="item"><span class="year">2020</span><a href="/site/items/7/slug">t</a></div>""");

        var result = await Loader(handler).LoadListAsync("1/0/0/0", TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://example.com/site/list/1/0/0/0"), Assert.Single(handler.Requests));
        Assert.Equal(PageKind.List, result.Kind);
        Assert.Equal("1/0/0/0", result.Path);
        Assert.Equal("7/slug", Assert.Single(result.Items!)["link"]);
    }

    [Fact]
    public async Task GetItem_LoadsFromItemPrefix()
    {
        var handler = FakeHandler.Returning("""<h1>Film (2001)</h1>""");

        var result = await Loader(handler).LoadItemAsync("7/slug", TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://example.com/site/items/7/slug"), Assert.Single(handler.Requests));
        Assert.Equal(PageKind.Item, result.Kind);
        Assert.Equal("7/slug", result.Path);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("1/../../other")]
    public async Task PathsLeavingThePrefix_AreRejected(string path)
    {
        var handler = FakeHandler.Returning("");

        await Assert.ThrowsAsync<ArgumentException>(() => Loader(handler).LoadItemAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HttpError_Throws()
    {
        var handler = FakeHandler.Returning("nope", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader(handler).LoadListAsync("missing", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Contains("'missing'", ex.Message);
        Assert.DoesNotContain("example.com", ex.Message);
    }

    [Fact]
    public async Task ResultNeverContainsBaseServer()
    {
        var handler = FakeHandler.Returning("""
            <div class="item"><span class="year">2020</span><a href="https://example.com/site/items/1">t</a></div>
            """);

        var result = await Loader(handler).LoadListAsync("list", TestContext.Current.CancellationToken);

        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("example.com", json);
        Assert.DoesNotContain("\"url\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1", result.Items![0]["link"]);
    }

    [Fact]
    public async Task UnreachableServer_ErrorHidesBaseServer()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("No such host is known. (example.com:443)"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader(handler).LoadListAsync("list", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("example.com", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task Timeout_ErrorHidesBaseServer()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("Timed out calling https://example.com/site/list"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Loader(handler).LoadListAsync("list", TestContext.Current.CancellationToken));
        Assert.Contains("timed out", ex.Message);
        Assert.DoesNotContain("example.com", ex.Message);
    }

    [Fact]
    public async Task AbsoluteUrl_IsRejectedWithoutRequest()
    {
        var handler = FakeHandler.Returning("");

        await Assert.ThrowsAsync<ArgumentException>(() => Loader(handler).LoadListAsync("https://evil.com/", TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }
}
