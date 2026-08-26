using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PBA.Api.Authentication;
using PBA.Api.Endpoints;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class ExternalEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ValidKey = "test-external-key-123";
    private readonly TestWebApplicationFactory _factory;

    public ExternalEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Client whose host has a configured key. Optionally sends a key header.
    private HttpClient AuthorizedHost(string? keyHeader, Action<IServiceCollection>? configure = null)
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"{ExternalApiOptions.SectionName}:ApiKey", ValidKey);
            if (configure is not null)
                builder.ConfigureTestServices(configure);
        }).CreateClient();

        if (keyHeader is not null)
            client.DefaultRequestHeaders.Add(ApiKeyEndpointFilter.HeaderName, keyHeader);

        return client;
    }

    [Fact]
    public async Task Feed_NoApiKey_Returns401()
    {
        var client = AuthorizedHost(keyHeader: null);
        var response = await client.GetAsync("/api/external/feed");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Feed_WrongApiKey_Returns401()
    {
        var client = AuthorizedHost(keyHeader: "wrong-key");
        var response = await client.GetAsync("/api/external/feed");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Feed_KeyNotConfigured_FailsClosed_Returns401()
    {
        // Base factory has no ExternalApi:ApiKey set — even a plausible header must be rejected.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyEndpointFilter.HeaderName, ValidKey);

        var response = await client.GetAsync("/api/external/feed");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Feed_ValidKey_Returns200()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.GetAsync("/api/external/feed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ideas_ValidKey_Returns200()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.GetAsync("/api/external/ideas");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Content_ValidKey_Returns200()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.GetAsync("/api/external/content");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Trending_ValidKey_Returns200()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.GetAsync("/api/external/feed/trending");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DigestLatest_ValidKey_PassesAuthAndReachesHandler()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.GetAsync("/api/external/digests/latest");
        // No digest seeded -> handler returns 404/400. The point: auth passed (not 401).
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnalyticsWebsite_ValidKey_Returns200()
    {
        var ga = new Mock<IGoogleAnalyticsService>();
        ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WebsiteOverview>.Success(new WebsiteOverview(1, 1, 1, 1, 0.1, 1)));
        ga.Setup(g => g.GetTopPagesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PageViewEntry>>.Success([]));
        ga.Setup(g => g.GetTrafficSourcesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TrafficSourceEntry>>.Success([]));
        ga.Setup(g => g.GetTopQueriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success([]));

        var client = AuthorizedHost(ValidKey, services =>
        {
            services.RemoveAll<IGoogleAnalyticsService>();
            services.AddScoped(_ => ga.Object);
        });

        var response = await client.GetAsync("/api/external/analytics/website?period=7d");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateIdea_NoApiKey_Returns401()
    {
        var client = AuthorizedHost(keyHeader: null);
        var response = await client.PostAsJsonAsync("/api/external/ideas",
            new CreateExternalIdeaRequest { Title = "Should not be created" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateIdea_ValidKey_Returns201WithId()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.PostAsJsonAsync("/api/external/ideas",
            new CreateExternalIdeaRequest
            {
                Title = "Avatar-sourced idea " + Guid.NewGuid(),
                Description = "Injected by project-avatar",
                Category = "AI",
                Tags = ["agents", "branding"]
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatedIdeaResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);
    }

    // A scheduledAt that cannot be parsed must stop the request, not fall through to an immediate
    // post. Silently posting now would put a clip on a public account days early, and the caller
    // would see a 200 telling it everything went to plan.
    [Theory]
    [InlineData("not-a-date")]
    [InlineData("08/07/2026 10:15")]
    public async Task SocialClipPublish_UnparseableScheduledAt_Returns400AndNamesTheFormat(string bad)
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.PostAsync("/api/external/social-clip/publish", ClipForm(bad));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ISO-8601", await response.Content.ReadAsStringAsync());
    }

    // An offset-less timestamp is the dangerous case: it PARSES, but as SERVER-local time. The API
    // container runs UTC while the campaign queues are written in America/New_York, so accepting
    // one would shift every slot by four or five hours with nothing logged and no error to notice —
    // the mistake only shows up once the clip is already public at the wrong hour.
    [Fact]
    public async Task SocialClipPublish_ScheduledAtWithoutOffset_IsRejected()
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.PostAsync(
            "/api/external/social-clip/publish", ClipForm("2026-08-07T10:15:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The other half of the rule: the formats AIVP actually sends must get THROUGH the parse.
    // Python's datetime.isoformat() emits "+00:00"; the campaign queues carry "-04:00". A check
    // strict enough to reject these would take the whole TikTok lane down.
    [Theory]
    [InlineData("2026-08-07T10:15:00-04:00")]
    [InlineData("2026-08-07T14:15:00+00:00")]
    [InlineData("2026-08-07T14:15:00Z")]
    public async Task SocialClipPublish_ValidOffsetTimestamp_IsAcceptedByTheParse(string good)
    {
        var client = AuthorizedHost(ValidKey);
        var response = await client.PostAsync("/api/external/social-clip/publish", ClipForm(good));

        // Publishing itself fails here (no Buffer channel in a test host) — the assertion is that
        // the request got PAST the parse, which a 400 would mean it had not.
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static MultipartFormDataContent ClipForm(string scheduledAt)
    {
        var file = new ByteArrayContent([0x00, 0x01, 0x02]);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        return new MultipartFormDataContent
        {
            { file, "file", "clip.mp4" },
            { new StringContent("caption"), "caption" },
            { new StringContent(scheduledAt), "scheduledAt" }
        };
    }

    private sealed record CreatedIdeaResponse(Guid Id);
}
