using System.Net;

namespace WebLoadMcp.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var response = _respond(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }

    public static FakeHandler Returning(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(content) });
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
