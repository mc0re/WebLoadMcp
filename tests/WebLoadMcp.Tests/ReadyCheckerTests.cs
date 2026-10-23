using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebLoadMcp.Configuration;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

public class ReadyCheckerTests
{
    private static ReadyChecker Checker(string country, string response, string property = "country") =>
        new(new FakeHttpClientFactory(FakeHandler.Returning(response)),
            Options.Create(new ReadyOptions { Country = country, GeoIpUrl = "https://geo.test/json", CountryProperty = property }),
            NullLogger<ReadyChecker>.Instance);

    [Theory]
    [InlineData("DK", """{"ip":"1.2.3.4","country":"DK"}""", false)]
    [InlineData("dk", """{"ip":"1.2.3.4","country":"DK"}""", false)]
    [InlineData("DK", """{"ip":"1.2.3.4","country":"SE"}""", true)]
    public async Task ReturnsTrueOnlyOutsideConfiguredCountry(string configured, string response, bool expected) =>
        Assert.Equal(expected, await Checker(configured, response).IsReadyAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task CustomCountryProperty()
    {
        var checker = Checker("US", """{"countryCode":"US"}""", property: "countryCode");
        Assert.False(await checker.IsReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CountryNotConfigured_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Checker("", """{"country":"DK"}""").IsReadyAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task CountryMissingInResponse_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Checker("DK", """{"ip":"1.2.3.4"}""").IsReadyAsync(TestContext.Current.CancellationToken));
}
