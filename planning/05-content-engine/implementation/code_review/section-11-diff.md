diff --git a/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs
new file mode 100644
index 0000000..de4913a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs
@@ -0,0 +1,48 @@
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class AnalyticsEndpoints
+{
+    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/analytics").WithTags("Analytics");
+
+        group.MapGet("/content/{id:guid}", GetPerformance);
+        group.MapGet("/top", GetTopContent);
+        group.MapPost("/content/{id:guid}/refresh", RefreshEngagement);
+    }
+
+    private static async Task<IResult> GetPerformance(
+        IEngagementAggregator aggregator,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await aggregator.GetPerformanceAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetTopContent(
+        IEngagementAggregator aggregator,
+        DateTimeOffset from,
+        DateTimeOffset to,
+        int limit = 10,
+        CancellationToken ct = default)
+    {
+        var clampedLimit = Math.Clamp(limit, 1, 50);
+        var result = await aggregator.GetTopContentAsync(from, to, clampedLimit, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> RefreshEngagement(
+        IEngagementAggregator aggregator,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await aggregator.FetchLatestAsync(id, ct);
+        if (result.IsSuccess)
+            return Results.Accepted(value: result.Value);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/BrandVoiceEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/BrandVoiceEndpoints.cs
new file mode 100644
index 0000000..40ce51a
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/BrandVoiceEndpoints.cs
@@ -0,0 +1,23 @@
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class BrandVoiceEndpoints
+{
+    public static void MapBrandVoiceEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/brand-voice").WithTags("BrandVoice");
+
+        group.MapGet("/score/{contentId:guid}", GetScore);
+    }
+
+    private static async Task<IResult> GetScore(
+        IBrandVoiceService brandVoice,
+        Guid contentId,
+        CancellationToken ct)
+    {
+        var result = await brandVoice.ScoreContentAsync(contentId, ct);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/CalendarEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/CalendarEndpoints.cs
new file mode 100644
index 0000000..85855ca
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/CalendarEndpoints.cs
@@ -0,0 +1,85 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class CalendarEndpoints
+{
+    public record AssignContentRequest(Guid ContentId);
+
+    public static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/calendar").WithTags("Calendar");
+
+        group.MapGet("/", GetSlots);
+        group.MapPost("/series", CreateSeries);
+        group.MapPost("/slot", CreateManualSlot);
+        group.MapPut("/slot/{id:guid}/assign", AssignContent);
+        group.MapPost("/auto-fill", AutoFill);
+    }
+
+    private static async Task<IResult> GetSlots(
+        IContentCalendarService calendar,
+        DateTimeOffset from,
+        DateTimeOffset to,
+        CancellationToken ct)
+    {
+        if (from >= to)
+            return Results.Problem(statusCode: 400, detail: "'from' must be before 'to'.");
+
+        if ((to - from).TotalDays > 90)
+            return Results.Problem(statusCode: 400, detail: "Date range must not exceed 90 days.");
+
+        var result = await calendar.GetSlotsAsync(from, to, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> CreateSeries(
+        IContentCalendarService calendar,
+        ContentSeriesRequest request,
+        CancellationToken ct)
+    {
+        var result = await calendar.CreateSeriesAsync(request, ct);
+        return result.ToCreatedHttpResult("/api/calendar/series");
+    }
+
+    private static async Task<IResult> CreateManualSlot(
+        IContentCalendarService calendar,
+        CalendarSlotRequest request,
+        CancellationToken ct)
+    {
+        var result = await calendar.CreateManualSlotAsync(request, ct);
+        return result.ToCreatedHttpResult("/api/calendar/slot");
+    }
+
+    private static async Task<IResult> AssignContent(
+        IContentCalendarService calendar,
+        Guid id,
+        AssignContentRequest request,
+        CancellationToken ct)
+    {
+        var result = await calendar.AssignContentAsync(id, request.ContentId, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> AutoFill(
+        IContentCalendarService calendar,
+        IApplicationDbContext db,
+        DateTimeOffset from,
+        DateTimeOffset to,
+        CancellationToken ct)
+    {
+        var autonomy = await db.AutonomyConfigurations.FirstOrDefaultAsync(ct)
+                       ?? AutonomyConfiguration.CreateDefault();
+
+        if (autonomy.GlobalLevel == AutonomyLevel.Manual)
+            return Results.Problem(statusCode: 403, detail: "Operation requires SemiAuto or higher autonomy level.");
+
+        var result = await calendar.AutoFillSlotsAsync(from, to, ct);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/ContentPipelineEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/ContentPipelineEndpoints.cs
new file mode 100644
index 0000000..2f6f640
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/ContentPipelineEndpoints.cs
@@ -0,0 +1,64 @@
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class ContentPipelineEndpoints
+{
+    public static void MapContentPipelineEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/content-pipeline").WithTags("ContentPipeline");
+
+        group.MapPost("/create", CreateFromTopic);
+        group.MapPost("/{id:guid}/outline", GenerateOutline);
+        group.MapPost("/{id:guid}/draft", GenerateDraft);
+        group.MapPost("/{id:guid}/validate-voice", ValidateVoice);
+        group.MapPost("/{id:guid}/submit", SubmitForReview);
+    }
+
+    private static async Task<IResult> CreateFromTopic(
+        IContentPipeline pipeline,
+        ContentCreationRequest request,
+        CancellationToken ct)
+    {
+        var result = await pipeline.CreateFromTopicAsync(request, ct);
+        return result.ToCreatedHttpResult("/api/content");
+    }
+
+    private static async Task<IResult> GenerateOutline(
+        IContentPipeline pipeline,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await pipeline.GenerateOutlineAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GenerateDraft(
+        IContentPipeline pipeline,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await pipeline.GenerateDraftAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> ValidateVoice(
+        IBrandVoiceService brandVoice,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await brandVoice.ScoreContentAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> SubmitForReview(
+        IContentPipeline pipeline,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await pipeline.SubmitForReviewAsync(id, ct);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/RepurposingEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/RepurposingEndpoints.cs
new file mode 100644
index 0000000..c3fbe02
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/RepurposingEndpoints.cs
@@ -0,0 +1,47 @@
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class RepurposingEndpoints
+{
+    public record RepurposeRequest(PlatformType[] TargetPlatforms);
+
+    public static void MapRepurposingEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/content").WithTags("Repurposing");
+
+        group.MapPost("/{id:guid}/repurpose", Repurpose);
+        group.MapGet("/{id:guid}/repurpose-suggestions", GetSuggestions);
+        group.MapGet("/{id:guid}/tree", GetContentTree);
+    }
+
+    private static async Task<IResult> Repurpose(
+        IRepurposingService service,
+        Guid id,
+        RepurposeRequest request,
+        CancellationToken ct)
+    {
+        var result = await service.RepurposeAsync(id, request.TargetPlatforms, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetSuggestions(
+        IRepurposingService service,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await service.SuggestRepurposingAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> GetContentTree(
+        IRepurposingService service,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await service.GetContentTreeAsync(id, ct);
+        return result.ToHttpResult();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Endpoints/TrendEndpoints.cs b/src/PersonalBrandAssistant.Api/Endpoints/TrendEndpoints.cs
new file mode 100644
index 0000000..9d9ae45
--- /dev/null
+++ b/src/PersonalBrandAssistant.Api/Endpoints/TrendEndpoints.cs
@@ -0,0 +1,63 @@
+using Microsoft.EntityFrameworkCore;
+using PersonalBrandAssistant.Api.Extensions;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Api.Endpoints;
+
+public static class TrendEndpoints
+{
+    public static void MapTrendEndpoints(this IEndpointRouteBuilder app)
+    {
+        var group = app.MapGroup("/api/trends").WithTags("Trends");
+
+        group.MapGet("/suggestions", GetSuggestions);
+        group.MapPost("/suggestions/{id:guid}/accept", AcceptSuggestion);
+        group.MapPost("/suggestions/{id:guid}/dismiss", DismissSuggestion);
+        group.MapPost("/refresh", RefreshTrends);
+    }
+
+    private static async Task<IResult> GetSuggestions(
+        ITrendMonitor monitor,
+        int limit = 20,
+        CancellationToken ct = default)
+    {
+        var clampedLimit = Math.Clamp(limit, 1, 100);
+        var result = await monitor.GetSuggestionsAsync(clampedLimit, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> AcceptSuggestion(
+        ITrendMonitor monitor,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await monitor.AcceptSuggestionAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> DismissSuggestion(
+        ITrendMonitor monitor,
+        Guid id,
+        CancellationToken ct)
+    {
+        var result = await monitor.DismissSuggestionAsync(id, ct);
+        return result.ToHttpResult();
+    }
+
+    private static async Task<IResult> RefreshTrends(
+        ITrendMonitor monitor,
+        IApplicationDbContext db,
+        CancellationToken ct)
+    {
+        var autonomy = await db.AutonomyConfigurations.FirstOrDefaultAsync(ct)
+                       ?? AutonomyConfiguration.CreateDefault();
+
+        if (autonomy.GlobalLevel == AutonomyLevel.Manual)
+            return Results.Problem(statusCode: 403, detail: "Trend refresh requires SemiAuto or higher autonomy level.");
+
+        var result = await monitor.RefreshTrendsAsync(ct);
+        return Results.Accepted(value: result.IsSuccess ? "Refresh triggered" : string.Join(", ", result.Errors));
+    }
+}
diff --git a/src/PersonalBrandAssistant.Api/Program.cs b/src/PersonalBrandAssistant.Api/Program.cs
index 2ba451e..1ebf289 100644
--- a/src/PersonalBrandAssistant.Api/Program.cs
+++ b/src/PersonalBrandAssistant.Api/Program.cs
@@ -60,6 +60,12 @@ app.MapNotificationEndpoints();
 app.MapAgentEndpoints();
 app.MapMediaEndpoints();
 app.MapPlatformEndpoints();
+app.MapContentPipelineEndpoints();
+app.MapRepurposingEndpoints();
+app.MapCalendarEndpoints();
+app.MapBrandVoiceEndpoints();
+app.MapTrendEndpoints();
+app.MapAnalyticsEndpoints();
 
 app.Run();
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEngineEndpointsTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEngineEndpointsTests.cs
new file mode 100644
index 0000000..3f8188d
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Api/ContentEngineEndpointsTests.cs
@@ -0,0 +1,343 @@
+using System.Net;
+using System.Net.Http.Json;
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.AspNetCore.Mvc.Testing;
+using Microsoft.AspNetCore.TestHost;
+using Microsoft.Extensions.DependencyInjection;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Api;
+
+public class ContentEngineEndpointsTests : IClassFixture<ContentEngineEndpointsTests.TestFactory>
+{
+    private const string TestApiKey = "test-api-key-12345";
+    private readonly TestFactory _factory;
+    private readonly Mock<IContentPipeline> _pipeline = new();
+    private readonly Mock<IRepurposingService> _repurposing = new();
+    private readonly Mock<IContentCalendarService> _calendar = new();
+    private readonly Mock<IBrandVoiceService> _brandVoice = new();
+    private readonly Mock<ITrendMonitor> _trendMonitor = new();
+    private readonly Mock<IEngagementAggregator> _aggregator = new();
+
+    public ContentEngineEndpointsTests(TestFactory factory)
+    {
+        _factory = factory;
+    }
+
+    private HttpClient CreateAuthClient()
+    {
+        var client = _factory.WithWebHostBuilder(builder =>
+        {
+            builder.ConfigureTestServices(services =>
+            {
+                services.AddScoped<IContentPipeline>(_ => _pipeline.Object);
+                services.AddScoped<IRepurposingService>(_ => _repurposing.Object);
+                services.AddScoped<IContentCalendarService>(_ => _calendar.Object);
+                services.AddScoped<IBrandVoiceService>(_ => _brandVoice.Object);
+                services.AddScoped<ITrendMonitor>(_ => _trendMonitor.Object);
+                services.AddScoped<IEngagementAggregator>(_ => _aggregator.Object);
+            });
+        }).CreateClient();
+        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
+        return client;
+    }
+
+    private HttpClient CreateUnauthClient()
+    {
+        return _factory.WithWebHostBuilder(builder =>
+        {
+            builder.ConfigureTestServices(services =>
+            {
+                services.AddScoped<IContentPipeline>(_ => _pipeline.Object);
+                services.AddScoped<IRepurposingService>(_ => _repurposing.Object);
+                services.AddScoped<IContentCalendarService>(_ => _calendar.Object);
+                services.AddScoped<IBrandVoiceService>(_ => _brandVoice.Object);
+                services.AddScoped<ITrendMonitor>(_ => _trendMonitor.Object);
+                services.AddScoped<IEngagementAggregator>(_ => _aggregator.Object);
+            });
+        }).CreateClient();
+    }
+
+    // --- ContentPipeline ---
+
+    [Fact]
+    public async Task ContentPipeline_Create_Returns201()
+    {
+        var contentId = Guid.NewGuid();
+        _pipeline.Setup(p => p.CreateFromTopicAsync(It.IsAny<ContentCreationRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(contentId));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsJsonAsync("/api/content-pipeline/create", new
+        {
+            Type = ContentType.BlogPost,
+            Topic = "AI trends",
+        });
+
+        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ContentPipeline_Create_NoApiKey_Returns401()
+    {
+        using var client = CreateUnauthClient();
+        var response = await client.PostAsJsonAsync("/api/content-pipeline/create", new
+        {
+            Type = ContentType.BlogPost,
+            Topic = "test",
+        });
+
+        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ContentPipeline_GenerateOutline_Returns200()
+    {
+        var id = Guid.NewGuid();
+        _pipeline.Setup(p => p.GenerateOutlineAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success("# Outline\n- Point 1\n- Point 2"));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsync($"/api/content-pipeline/{id}/outline", null);
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ContentPipeline_GenerateDraft_Returns200()
+    {
+        var id = Guid.NewGuid();
+        _pipeline.Setup(p => p.GenerateDraftAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success("Draft body text"));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsync($"/api/content-pipeline/{id}/draft", null);
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task ContentPipeline_SubmitForReview_NotFound_Returns404()
+    {
+        var id = Guid.NewGuid();
+        _pipeline.Setup(p => p.SubmitForReviewAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.NotFound<MediatR.Unit>("Content not found"));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsync($"/api/content-pipeline/{id}/submit", null);
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    // --- Repurposing ---
+
+    [Fact]
+    public async Task Repurpose_ValidRequest_Returns200()
+    {
+        var id = Guid.NewGuid();
+        _repurposing.Setup(r => r.RepurposeAsync(id, It.IsAny<PlatformType[]>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<Guid>>([Guid.NewGuid()]));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsJsonAsync($"/api/content/{id}/repurpose", new
+        {
+            TargetPlatforms = new[] { PlatformType.TwitterX },
+        });
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Repurpose_NoApiKey_Returns401()
+    {
+        using var client = CreateUnauthClient();
+        var response = await client.PostAsJsonAsync($"/api/content/{Guid.NewGuid()}/repurpose", new
+        {
+            TargetPlatforms = new[] { PlatformType.TwitterX },
+        });
+
+        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task GetContentTree_NotFound_Returns404()
+    {
+        var id = Guid.NewGuid();
+        _repurposing.Setup(r => r.GetContentTreeAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.NotFound<IReadOnlyList<Content>>("Not found"));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync($"/api/content/{id}/tree");
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    // --- Calendar ---
+
+    [Fact]
+    public async Task Calendar_GetSlots_Returns200()
+    {
+        _calendar.Setup(c => c.GetSlotsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<CalendarSlot>>([]));
+
+        using var client = CreateAuthClient();
+        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
+        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(7).ToString("o"));
+        var response = await client.GetAsync($"/api/calendar?from={from}&to={to}");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Calendar_CreateSeries_Returns201()
+    {
+        var seriesId = Guid.NewGuid();
+        _calendar.Setup(c => c.CreateSeriesAsync(It.IsAny<ContentSeriesRequest>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(seriesId));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsJsonAsync("/api/calendar/series", new
+        {
+            Name = "Weekly Post",
+            RecurrenceRule = "FREQ=WEEKLY",
+            TargetPlatforms = new[] { PlatformType.TwitterX },
+            ContentType = ContentType.SocialPost,
+            TimeZoneId = "UTC",
+            StartsAt = DateTimeOffset.UtcNow,
+        });
+
+        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
+    }
+
+    // --- BrandVoice ---
+
+    [Fact]
+    public async Task BrandVoice_GetScore_Returns200()
+    {
+        var id = Guid.NewGuid();
+        _brandVoice.Setup(b => b.ScoreContentAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new BrandVoiceScore(85, 80, 90, 85, [], [])));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync($"/api/brand-voice/score/{id}");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task BrandVoice_GetScore_NotFound_Returns404()
+    {
+        var id = Guid.NewGuid();
+        _brandVoice.Setup(b => b.ScoreContentAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.NotFound<BrandVoiceScore>("Content not found"));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync($"/api/brand-voice/score/{id}");
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    // --- Trends ---
+
+    [Fact]
+    public async Task Trends_GetSuggestions_Returns200()
+    {
+        _trendMonitor.Setup(t => t.GetSuggestionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<TrendSuggestion>>([]));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync("/api/trends/suggestions?limit=10");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Trends_AcceptSuggestion_NotFound_Returns404()
+    {
+        var id = Guid.NewGuid();
+        _trendMonitor.Setup(t => t.AcceptSuggestionAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.NotFound<Guid>("Suggestion not found"));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsync($"/api/trends/suggestions/{id}/accept", null);
+
+        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Trends_Refresh_NoApiKey_Returns401()
+    {
+        using var client = CreateUnauthClient();
+        var response = await client.PostAsync("/api/trends/refresh", null);
+
+        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
+    }
+
+    // --- Analytics ---
+
+    [Fact]
+    public async Task Analytics_GetPerformance_Returns200()
+    {
+        var id = Guid.NewGuid();
+        _aggregator.Setup(a => a.GetPerformanceAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new ContentPerformanceReport(
+                id, new Dictionary<PlatformType, EngagementSnapshot>().AsReadOnly(), 100, null, null)));
+
+        using var client = CreateAuthClient();
+        var response = await client.GetAsync($"/api/analytics/content/{id}");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Analytics_GetTopContent_Returns200()
+    {
+        _aggregator.Setup(a => a.GetTopContentAsync(
+                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success<IReadOnlyList<TopPerformingContent>>([]));
+
+        using var client = CreateAuthClient();
+        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("o"));
+        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
+        var response = await client.GetAsync($"/api/analytics/top?from={from}&to={to}&limit=10");
+
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Analytics_RefreshEngagement_Returns202()
+    {
+        var id = Guid.NewGuid();
+        _aggregator.Setup(a => a.FetchLatestAsync(id, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new EngagementSnapshot()));
+
+        using var client = CreateAuthClient();
+        var response = await client.PostAsync($"/api/analytics/content/{id}/refresh", null);
+
+        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
+    }
+
+    public class TestFactory : WebApplicationFactory<Program>
+    {
+        protected override void ConfigureWebHost(IWebHostBuilder builder)
+        {
+            builder.UseEnvironment("Development");
+            builder.UseSetting("ApiKey", TestApiKey);
+            builder.UseSetting("ConnectionStrings:DefaultConnection",
+                "Host=localhost;Database=test_content_engine;Username=test;Password=test");
+
+            builder.ConfigureTestServices(services =>
+            {
+                var hostedServices = services
+                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
+                    .ToList();
+                foreach (var svc in hostedServices)
+                    services.Remove(svc);
+            });
+        }
+    }
+}
