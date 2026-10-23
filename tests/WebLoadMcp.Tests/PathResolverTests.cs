using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

public class PathResolverTests
{
    private readonly PathResolver _resolver = new("https://example.com/site/");

    [Theory]
    [InlineData("", "https://example.com/site/")]
    [InlineData("list", "https://example.com/site/list")]
    [InlineData("/list/2024?page=2", "https://example.com/site/list/2024?page=2")]
    [InlineData("a/../b", "https://example.com/site/b")]
    public void Resolve_RelativePath_StaysUnderBase(string path, string expected) =>
        Assert.Equal(expected, _resolver.Resolve(path).ToString());

    [Theory]
    [InlineData("https://evil.com/x")]
    [InlineData("//evil.com/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("../other")]
    [InlineData("a/../../other")]
    public void Resolve_PathLeavingBase_Throws(string path) =>
        Assert.Throws<ArgumentException>(() => _resolver.Resolve(path));

    [Fact]
    public void BaseUrlWithoutTrailingSlash_IsTreatedAsDirectory() =>
        Assert.Equal("https://example.com/site/list", new PathResolver("https://example.com/site").Resolve("list").ToString());

    [Theory]
    [InlineData("https://example.com/site/item/7?x=1", "item/7?x=1")]
    [InlineData("https://example.com/other", null)]
    [InlineData("https://another.com/site/item", null)]
    public void ToRelative(string url, string? expected) =>
        Assert.Equal(expected, _resolver.ToRelative(new Uri(url)));
}
