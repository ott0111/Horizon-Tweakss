using System.Net;
using System.Text;
using Horizon.Services;
using Xunit;

namespace Horizon.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task Check_returns_a_newer_published_release()
    {
        var service = new UpdateService(Client(HttpStatusCode.OK, """{"tag_name":"v99.0.0"}"""));

        var result = await service.CheckAsync();

        Assert.Equal("v99.0.0", result);
    }

    [Fact]
    public async Task Check_treats_no_published_release_as_no_update()
    {
        var service = new UpdateService(Client(HttpStatusCode.NotFound, "{}"));

        var result = await service.CheckAsync();

        Assert.Null(result);
    }

    private static HttpClient Client(HttpStatusCode status, string json) =>
        new(new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
