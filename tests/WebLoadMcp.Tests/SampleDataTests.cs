using Microsoft.Extensions.Configuration;
using WebLoadMcp.Configuration;
using WebLoadMcp.Models;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tests;

/// <summary>
/// Parses the sample pages in the repository's data folder with the shipped appsettings.json.
/// </summary>
public class SampleDataTests
{
    private static readonly WebLoadOptions Options = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
        .Build()
        .GetSection(WebLoadOptions.SectionName)
        .Get<WebLoadOptions>()!;

    private static PageResult Parse(string file, string path)
    {
        var resolver = new PathResolver(Options.BaseUrl);
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", file));
        return new PageParser(Options, resolver).Parse(html, resolver.Resolve(path));
    }

    [Fact]
    public void ListPage()
    {
        var result = Parse("list.html", "browse/113/1/0/0");

        Assert.Equal(PageKind.List, result.Kind);
        Assert.Equal("browse/113/1/0/0", result.Path);
        Assert.Equal(100, result.Items!.Count);

        var first = result.Items[0];
        Assert.Equal("2018", first["year"]);
        Assert.Equal("Сага о чудовище. Сумерки / Wildling", first["title"]);
        Assert.Equal("torrent/1045132/saga-o-chudoviwe-sumerki_wildling-2018-bdremux-1080p-d-p-l2-a", first["link"]);

        Assert.All(result.Items, item =>
        {
            Assert.NotNull(item["title"]);
            Assert.StartsWith("torrent/", (string)item["link"]!);
        });
    }

    [Fact]
    public void ItemPage()
    {
        var result = Parse("item.html", "torrent/1045135/tainstvennyj-poezd_mystery-train-1989-bdremux-1080p-p-a-l1");

        Assert.Equal(PageKind.Item, result.Kind);
        var item = result.Item!;
        Assert.Equal("Таинственный поезд / Mystery Train", item["title"]);
        Assert.Equal("1989", item["year"]);
        Assert.Equal("https://i8.imageban.ru/out/2025/07/10/5f873b2115fe9d96b2e43712319e004c.jpg", item["image"]);
        Assert.Equal("США, Япония", item["country"]);
        Assert.StartsWith("Трилогия о событиях", (string)item["description"]!);
        Assert.EndsWith("самого Элвиса.....", (string)item["description"]!);
        Assert.Equal(["Комедия", "драма"], (IEnumerable<string>)item["genres"]!);
        Assert.Equal(
            [
                "Русский AC3 2.0 / 192 kbps / 48 kHz - Многоголосый закадровый (Кармен Видео)",
                "Русский AC3 1.0 / 128 kbps / 48 kHz - Авторский (А. Алексеев)",
                "Русский AC3 1.0 / 128 kbps / 48 kHz - Одноголосый (женская озвучка)",
                "Английский LPCM 1.0 / 1152 kbps / 48 kHz / 24-bit",
            ],
            (IEnumerable<string>)item["audio"]!);
        Assert.Equal(["Русские", "английские"], (IEnumerable<string>)item["subtitles"]!);
        Assert.Equal("28.35 GB", item["size"]);
        Assert.Equal(
            "magnet:?xt=urn:btih:73e4ebea578584f332dbf64fb1c302b57210b1c4&dn=" + Uri.EscapeDataString("Таинственный поезд / Mystery Train (1989)"),
            item["reference"]);
        Assert.Null(result.Warnings);
    }
}
