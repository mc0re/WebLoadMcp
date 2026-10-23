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

    [McpServerTool(Name = "load_page", ReadOnly = true, Idempotent = true)]
    [Description(
        "Loads a page from the predefined server and parses it. " +
        "Returns kind='list' with 'items' (each with 'year', 'title' and 'link') when the page is a list, " +
        "or kind='item' with 'item' details otherwise. " +
        "An item's 'link' is a path relative to the server; pass it back to this tool to load that item's details.")]
    public async Task<PageResult> LoadPage(
        [Description("Path relative to the server's base URL, e.g. 'news/' or 'news/42?lang=en'.")] string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _loader.LoadAsync(path, cancellationToken);
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
