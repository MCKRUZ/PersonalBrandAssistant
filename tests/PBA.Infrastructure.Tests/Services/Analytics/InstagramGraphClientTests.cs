using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

// Client-level tests for the real map-by-returned-name drop logic and the per-media values[0].value parse.
// These exercise the seam the facade tests mock — the plan calls IG deprecation tolerance "critical".
public class InstagramGraphClientTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly HttpClient _http;

    public InstagramGraphClientTests()
    {
        _http = new HttpClient(_handler.Object) { BaseAddress = new Uri("https://graph.instagram.com/") };
    }

    private InstagramGraphClient CreateClient() =>
        new(_http, NullLogger<InstagramGraphClient>.Instance);

    // Route each request to a canned JSON body by URL path.
    private void RouteByPath(Func<string, string, string> respond)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                var query = req.RequestUri.Query;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(respond(path, query), System.Text.Encoding.UTF8, "application/json")
                });
            });
    }

    [Fact]
    public async Task GetAccountMetricsAsync_MapsByReturnedName_DropsMetricsNotInResponse()
    {
        RouteByPath((path, _) => path switch
        {
            "/me" => """{"user_id":"123"}""",
            "/123" => """{"followers_count":5000,"id":"123"}""",
            // The API returned reach + views but silently DROPPED the requested "saves" (deprecated).
            "/123/insights" => """{"data":[{"name":"reach","total_value":{"value":1200}},{"name":"views","total_value":{"value":3400}}]}""",
            _ => "{}"
        });

        var result = await CreateClient().GetAccountMetricsAsync(
            "token", ["reach", "views", "saves"], CancellationToken.None);

        Assert.Equal(5000, result["followers"]);
        Assert.Equal(1200, result["reach"]);
        Assert.Equal(3400, result["views"]);
        Assert.False(result.ContainsKey("saves"));   // dropped by the API -> simply absent, no failure
    }

    [Fact]
    public async Task GetRecentMediaAsync_ParsesPerMediaValuesArray()
    {
        RouteByPath((path, _) => path switch
        {
            "/me/media" => """{"data":[{"id":"m1","caption":"hello"}]}""",
            "/m1/insights" => """{"data":[{"name":"views","values":[{"value":900}]},{"name":"likes","values":[{"value":40}]}]}""",
            _ => "{}"
        });

        var media = await CreateClient().GetRecentMediaAsync("token", ["views", "likes"], 10, CancellationToken.None);

        var item = Assert.Single(media);
        Assert.Equal("m1", item.MediaId);
        Assert.Equal("hello", item.Caption);
        Assert.Equal(900, item.Metrics["views"]);
        Assert.Equal(40, item.Metrics["likes"]);
    }
}
