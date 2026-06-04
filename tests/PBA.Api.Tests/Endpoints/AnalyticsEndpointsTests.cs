using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class AnalyticsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AnalyticsEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWith(Mock<IGoogleAnalyticsService> ga) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleAnalyticsService>();
                services.AddScoped(_ => ga.Object);
            })).CreateClient();

    [Fact]
    public async Task GetWebsite_Returns200_WithCompositeShape()
    {
        var ga = new Mock<IGoogleAnalyticsService>();
        ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WebsiteOverview>.Success(new WebsiteOverview(100, 150, 300, 120.5, 0.45, 80)));
        ga.Setup(g => g.GetTopPagesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PageViewEntry>>.Success([new("/blog", 50, 30)]));
        ga.Setup(g => g.GetTrafficSourcesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TrafficSourceEntry>>.Success([new("Organic Search", 100, 80)]));
        ga.Setup(g => g.GetTopQueriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success([new("ai tools", 50, 1000, 0.05, 3.2)]));

        var client = CreateClientWith(ga);

        var response = await client.GetAsync("/api/analytics/website?period=30d");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("overview", out _));
        Assert.True(root.TryGetProperty("topPages", out _));
        Assert.True(root.TryGetProperty("trafficSources", out _));
        Assert.True(root.TryGetProperty("searchQueries", out _));
    }

    [Fact]
    public async Task GetWebsite_InvalidPeriod_Returns400()
    {
        var client = CreateClientWith(new Mock<IGoogleAnalyticsService>());

        var response = await client.GetAsync("/api/analytics/website?period=nonsense");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWebsite_InvertedDateRange_Returns400()
    {
        var client = CreateClientWith(new Mock<IGoogleAnalyticsService>());

        var response = await client.GetAsync("/api/analytics/website?from=2026-02-01&to=2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_Returns200_WithGa4AndSearchConsoleFlags()
    {
        var ga = new Mock<IGoogleAnalyticsService>();
        ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WebsiteOverview>.Success(new WebsiteOverview(1, 1, 1, 1, 0.1, 1)));
        ga.Setup(g => g.GetTopQueriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success([new("ai tools", 1, 1, 0.1, 1.0)]));

        var client = CreateClientWith(ga);

        var response = await client.GetAsync("/api/analytics/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ga4").GetBoolean());
        Assert.True(root.GetProperty("searchConsole").GetBoolean());
    }
}
