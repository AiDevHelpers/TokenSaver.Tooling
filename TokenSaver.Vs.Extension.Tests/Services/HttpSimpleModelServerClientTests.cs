#pragma warning disable CA2000 // Test factories hand disposables to the client under test; each test is short-lived.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TokenSaver.Vs.Extension.Services;
using Xunit;

namespace TokenSaver.Vs.Extension.Tests.Services;

public sealed class HttpSimpleModelServerClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static HttpSimpleModelServerClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var httpClient = new HttpClient(new FakeHandler(respond)) { BaseAddress = new Uri("http://localhost:11435") };
        return new HttpSimpleModelServerClient(httpClient, ownsHttpClient: true);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task CheckHealthAsync_ReturnsRouterReportedStatus()
    {
        using var client = CreateClient(_ => JsonResponse("""{"status":"warming_up"}"""));
        var health = await client.CheckHealthAsync();
        Assert.Equal(SimpleModelServerHealthStatus.WarmingUp, health.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnreachable_WhenRequestThrows()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("ECONNREFUSED"));
        var health = await client.CheckHealthAsync();
        Assert.Equal(SimpleModelServerHealthStatus.Unreachable, health.Status);
    }

    [Fact]
    public async Task ListModelsAsync_MapsOpenAiStyleBody()
    {
        using var client = CreateClient(_ =>
            JsonResponse("""{"object":"list","data":[{"id":"HelperModel","object":"model"}]}"""));

        var models = await client.ListModelsAsync();

        Assert.Single(models);
        Assert.Equal("HelperModel", models[0].Id);
        Assert.Equal("HelperModel", models[0].Name);
    }

    [Fact]
    public async Task ListModelsAsync_Throws_OnNonOkResponse()
    {
        using var client = CreateClient(_ => JsonResponse("{}", HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListModelsAsync());
    }

    [Fact]
    public async Task StreamChatAsync_YieldsTextDeltas_AndStopsAtDone()
    {
        var sse = string.Join("\n\n",
        [
            """data: {"choices":[{"delta":{"content":"Hel"}}]}""",
            """data: {"choices":[{"delta":{"content":"lo"}}]}""",
            """data: {"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "data: [DONE]",
        ]) + "\n\n";

        using var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });

        var deltas = new List<string>();
        var sawDone = false;
        await foreach (var chunk in client.StreamChatAsync(
            [new SimpleModelServerChatMessage(SimpleModelServerChatRole.User, "hi")]))
        {
            if (chunk.ContentDelta.Length > 0) deltas.Add(chunk.ContentDelta);
            if (chunk.Done)
            {
                sawDone = true;
                break;
            }
        }

        Assert.Equal("Hello", string.Concat(deltas));
        Assert.True(sawDone);
    }

    [Fact]
    public async Task StreamChatAsync_Throws_OnErrorStatus()
    {
        using var client = CreateClient(_ => JsonResponse("{}", HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.StreamChatAsync([new SimpleModelServerChatMessage(SimpleModelServerChatRole.User, "hi")]))
            {
            }
        });
    }

    [Fact]
    public void EstimateTokenCount_IsStableRoughEstimate()
    {
        using var client = new HttpSimpleModelServerClient(new Uri("http://localhost:11435"));
        Assert.Equal(1, client.EstimateTokenCount(""));
        Assert.Equal(2, client.EstimateTokenCount(new string('a', 8)));
    }
}
