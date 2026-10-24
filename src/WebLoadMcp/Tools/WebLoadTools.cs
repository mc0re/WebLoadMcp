using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WebLoadMcp.Models;
using WebLoadMcp.Services;

namespace WebLoadMcp.Tools;

[McpServerToolType]
public sealed class WebLoadTools
{
    private readonly PageLoader _loader;
    private readonly ReadyChecker _readyChecker;

    public WebLoadTools(PageLoader loader, ReadyChecker readyChecker)
    {
        _loader = loader;
        _readyChecker = readyChecker;
    }

    [McpServerTool(Name = "get_list", ReadOnly = true, Idempotent = true)]
    [Description(
        "Loads a list page and returns its 'items', each with 'year', 'title' and 'link'. " +
        "Pass an item's 'link' to get_item to load that item's details.")]
    public Task<PageResult> GetList(
        [Description("Path of the list page, e.g. '0/1/0/0'.")] string path,
        CancellationToken cancellationToken) =>
        Run(() => _loader.LoadListAsync(path, cancellationToken));

    [McpServerTool(Name = "get_item", ReadOnly = true, Idempotent = true)]
    [Description(
        "Loads an item page and returns its details in 'item': title, year, country, description, genres, " +
        "audio and subtitle languages, size, image and a magnet link in 'reference'.")]
    public Task<PageResult> GetItem(
        [Description("Path of the item, as given in a list item's 'link', e.g. '1045135/mystery-train-1989'.")] string path,
        CancellationToken cancellationToken) =>
        Run(() => _loader.LoadItemAsync(path, cancellationToken));

    private static async Task<PageResult> Run(Func<Task<PageResult>> load)
    {
        try
        {
            return await load();
        }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "ready", ReadOnly = true)]
    [Description(
        "Checks the country of this machine's public IP address as seen from the Internet. " +
        "Returns true if it is NOT in the configured country, false if it is.")]
    public async Task<bool> Ready(CancellationToken cancellationToken)
    {
        try
        {
            return await _readyChecker.IsReadyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.Text.Json.JsonException)
        {
            throw new McpException($"Ready check failed: {ex.Message}", ex);
        }
    }
}
