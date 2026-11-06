using System.Net;
using System.Text;

namespace GapActivator.App.Tests.Fakes;

/// <summary>An <see cref="HttpMessageHandler"/> that never reaches the network: it records the request and returns the configured response.</summary>
internal sealed class StubHttpHandler(HttpStatusCode status, string body = "", string contentType = "application/json") : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        Requests++;
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
        return Task.FromResult(response);
    }
}
