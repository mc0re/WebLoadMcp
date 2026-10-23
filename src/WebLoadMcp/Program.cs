using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebLoadMcp.Configuration;
using WebLoadMcp.Services;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // MCP clients often start the server from an arbitrary directory; read appsettings.json next to the binary.
    ContentRootPath = AppContext.BaseDirectory,
});

// stdout carries the MCP protocol, so all logging must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddOptions<WebLoadOptions>()
    .Bind(builder.Configuration.GetSection(WebLoadOptions.SectionName))
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), $"{WebLoadOptions.SectionName}:BaseUrl must be an absolute URL.")
    .ValidateOnStart();
builder.Services.AddOptions<ReadyOptions>()
    .Bind(builder.Configuration.GetSection(ReadyOptions.SectionName));

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<WebLoadOptions>>().Value);
builder.Services.AddSingleton(sp => new PathResolver(sp.GetRequiredService<WebLoadOptions>().BaseUrl));
builder.Services.AddSingleton<PageParser>();
builder.Services.AddSingleton<PageLoader>();
builder.Services.AddSingleton<ReadyChecker>();

builder.Services.AddHttpClient(PageLoader.HttpClientName, (sp, client) =>
{
    var options = sp.GetRequiredService<WebLoadOptions>();
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    if (!string.IsNullOrWhiteSpace(options.UserAgent))
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }
});
builder.Services.AddHttpClient(ReadyChecker.HttpClientName, (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<ReadyOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("WebLoadMcp/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
