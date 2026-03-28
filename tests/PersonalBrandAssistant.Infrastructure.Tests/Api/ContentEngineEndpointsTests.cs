using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class ContentEngineEndpointsTests : IClassFixture<ContentEngineEndpointsTests.TestFactory>
{
    private const string TestApiKey = "test-api-key-12345";
    private readonly TestFactory _factory;
    private readonly Mock<IContentPipeline> _pipeline = new();
    private readonly Mock<IRepurposingService> _repurposing = new();
    private readonly Mock<IContentCalendarService> _calendar = new();
    private readonly Mock<IBrandVoiceService> _brandVoice = new();
    private readonly Mock<ITrendMonitor> _trendMonitor = new();
    private readonly Mock<IEngagementAggregator> _aggregator = new();

    public ContentEngineEndpointsTests(TestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthClient()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IContentPipeline>(_ => _pipeline.Object);
                services.AddScoped<IRepurposingService>(_ => _repurposing.Object);
                services.AddScoped<IContentCalendarService>(_ => _calendar.Object);
                services.AddScoped<IBrandVoiceService>(_ => _brandVoice.Object);
                services.AddScoped<ITrendMonitor>(_ => _trendMonitor.Object);
                services.AddScoped<IEngagementAggregator>(_ => _aggregator.Object);
            });
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    private HttpClient CreateUnauthClient()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IContentPipeline>(_ => _pipeline.Object);
                services.AddScoped<IRepurposingService>(_ => _repurposing.Object);
                services.AddScoped<IContentCalendarService>(_ => _calendar.Object);
                services.AddScoped<IBrandVoiceService>(_ => _brandVoice.Object);
                services.AddScoped<ITrendMonitor>(_ => _trendMonitor.Object);
                services.AddScoped<IEngagementAggregator>(_ => _aggregator.Object);
            });
        }).CreateClient();
    }

    // --- ContentPipeline ---

    [Fact]
    public async Task ContentPipeline_Create_Returns201()
    {
        var contentId = Guid.NewGuid();
        _pipeline.Setup(p => p.CreateFromTopicAsync(It.IsAny<ContentCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(contentId));

        using var client = CreateAuthClient();
        var response = await client.PostAsJsonAsync("/api/content-pipeline/create", new
        {
            Type = ContentType.BlogPost,
            Topic = "AI trends",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ContentPipeline_Create_NoApiKey_Returns401()
    {
        using var client = CreateUnauthClient();
        var response = await client.PostAsJsonAsync("/api/content-pipeline/create", new
        {
            Type = ContentType.BlogPost,
            Topic = "test",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ContentPipeline_GenerateOutline_Returns200()
    {
        var id = Guid.NewGuid();
        _pipeline.Setup(p => p.GenerateOutlineAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("# Outline\n- Point 1\n- Point 2"));

        using var client = CreateAuthClient();
        var response = await client.PostAsync($"/api/content-pipeline/{id}/outline", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ContentPipeline_GenerateDraft_Returns200()
    {
        var id = Guid.NewGuid();
        _pipeline.Setup(p => p.GenerateDraftAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("Draft body text"));

        using var client = CreateAuthClient();
        var response = await client.PostAsync($"/api/content-pipeline/{id}/draft", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ContentPipeline_SubmitForReview_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _pipeline.Setup(p => p.SubmitForReviewAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound<MediatR.Unit>("Content not found"));

        using var client = CreateAuthClient();
        var response = await client.PostAsync($"/api/content-pipeline/{id}/submit", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Repurposing ---

    [Fact]
    public async Task Repurpose_ValidRequest_Returns200()
    {
        var id = Guid.NewGuid();
        _repurposing.Setup(r => r.RepurposeAsync(id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));

        using var client = CreateAuthClient();
        var response = await client.PostAsJsonAsync($"/api/repurposing/{id}/repurpose", new
        {
            TargetPlatforms = new[] { PlatformType.TwitterX },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Repurpose_NoApiKey_Returns401()
    {
        using var client = CreateUnauthClient();
        var response = await client.PostAsJsonAsync($"/api/content/{Guid.NewGuid()}/repurpose", new
        {
            TargetPlatforms = new[] { PlatformType.TwitterX },
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetContentTree_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _repurposing.Setup(r => r.GetContentTreeAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound<IReadOnlyList<Content>>("Not found"));

        using var client = CreateAuthClient();
        var response = await client.GetAsync($"/api/repurposing/{id}/tree");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Calendar ---

    [Fact]
    public async Task Calendar_GetSlots_Returns200()
    {
        _calendar.Setup(c => c.GetSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<CalendarSlot>>([]));

        using var client = CreateAuthClient();
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(7).ToString("o"));
        var response = await client.GetAsync($"/api/calendar?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Calendar_CreateSeries_Returns201()
    {
        var seriesId = Guid.NewGuid();
        _calendar.Setup(c => c.CreateSeriesAsync(It.IsAny<ContentSeriesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(seriesId));

        using var client = CreateAuthClient();
        var response = await client.PostAsJsonAsync("/api/calendar/series", new
        {
            Name = "Weekly Post",
            RecurrenceRule = "FREQ=WEEKLY",
            TargetPlatforms = new[] { PlatformType.TwitterX },
            ContentType = ContentType.SocialPost,
            TimeZoneId = "UTC",
            StartsAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // --- BrandVoice ---

    [Fact]
    public async Task BrandVoice_GetScore_Returns200()
    {
        var id = Guid.NewGuid();
        _brandVoice.Setup(b => b.ScoreContentAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BrandVoiceScore(85, 80, 90, 85, [], [])));

        using var client = CreateAuthClient();
        var response = await client.GetAsync($"/api/brand-voice/score/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BrandVoice_GetScore_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _brandVoice.Setup(b => b.ScoreContentAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound<BrandVoiceScore>("Content not found"));

        using var client = CreateAuthClient();
        var response = await client.GetAsync($"/api/brand-voice/score/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Trends ---

    [Fact]
    public async Task Trends_GetSuggestions_Returns200()
    {
        _trendMonitor.Setup(t => t.GetSuggestionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<TrendSuggestion>>([]));

        using var client = CreateAuthClient();
        var response = await client.GetAsync("/api/trends/suggestions?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Trends_AcceptSuggestion_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _trendMonitor.Setup(t => t.AcceptSuggestionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.NotFound<Guid>("Suggestion not found"));

        using var client = CreateAuthClient();
        var response = await client.PostAsync($"/api/trends/suggestions/{id}/accept", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Trends_Refresh_NoApiKey_Returns401()
    {
        using var client = CreateUnauthClient();
        var response = await client.PostAsync("/api/trends/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Analytics ---

    [Fact]
    public async Task Analytics_GetPerformance_Returns200()
    {
        var id = Guid.NewGuid();
        _aggregator.Setup(a => a.GetPerformanceAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ContentPerformanceReport(
                id, new Dictionary<PlatformType, EngagementSnapshot>().AsReadOnly(), 100, null, null)));

        using var client = CreateAuthClient();
        var response = await client.GetAsync($"/api/analytics/content/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Analytics_GetTopContent_Returns200()
    {
        _aggregator.Setup(a => a.GetTopContentAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<TopPerformingContent>>([]));

        using var client = CreateAuthClient();
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
        var response = await client.GetAsync($"/api/analytics/top?from={from}&to={to}&limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Analytics_RefreshEngagement_Returns202()
    {
        var id = Guid.NewGuid();
        _aggregator.Setup(a => a.FetchLatestAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EngagementSnapshot()));

        using var client = CreateAuthClient();
        var response = await client.PostAsync($"/api/analytics/content/{id}/refresh", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    public class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiKey", TestApiKey);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=test_content_engine;Username=test;Password=test");

            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services
                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                    .ToList();
                foreach (var svc in hostedServices)
                    services.Remove(svc);
            });
        }
    }
}
