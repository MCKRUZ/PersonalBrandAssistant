using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class ChannelAnalyticsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public ChannelAnalyticsEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChannelAnalyticsEndpoints_Overview_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/analytics/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChannelAnalyticsEndpoints_Channel_InvalidPlatform_ReturnsBadRequest()
    {
        // LinkedIn is not an analytics platform.
        var response = await _client.GetAsync("/api/analytics/channel/LinkedIn");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("7d", HttpStatusCode.OK)]
    [InlineData("30d", HttpStatusCode.OK)]
    [InlineData("90d", HttpStatusCode.OK)]
    [InlineData("bogus", HttpStatusCode.BadRequest)]
    public async Task ChannelAnalyticsEndpoints_Channel_ParsesPeriod(string period, HttpStatusCode expected)
    {
        var response = await _client.GetAsync($"/api/analytics/channel/youtube?period={period}");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ChannelAnalyticsEndpoints_MapsResultFailure_ViaToApiResult()
    {
        // No YouTube analytics credential in the test DB -> GetYouTubeDeepAnalytics returns Result.Fail
        // (General) -> ToApiResult -> Problem (500). Proves failure mapping, not an unhandled exception.
        var response = await _client.GetAsync("/api/analytics/youtube/deep");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public void TestFactory_DoesNotStartChannelMetricPollingService()
    {
        var hostedServices = _factory.Services.GetServices<IHostedService>();
        Assert.DoesNotContain(hostedServices, h => h is ChannelMetricPollingService);
    }
}
