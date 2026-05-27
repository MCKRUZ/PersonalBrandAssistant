using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PBA.Application.Features.Content.Dtos;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ContentEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<Guid> CreateTestContent(
        string title = "Test Content",
        ContentType contentType = ContentType.Blog,
        Platform platform = Platform.Blog)
    {
        var body = new CreateContentRequest
        {
            Title = title,
            ContentType = contentType,
            PrimaryPlatform = platform
        };
        var response = await _client.PostAsJsonAsync("/api/content", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<ContentDetailDto> GetContent(Guid id)
    {
        var response = await _client.GetAsync($"/api/content/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContentDetailDto>(JsonOptions))!;
    }

    [Fact]
    public async Task PostContent_Returns201_WithNewId()
    {
        var body = new CreateContentRequest
        {
            Title = "Integration Test Content",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };

        var response = await _client.PostAsJsonAsync("/api/content", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = await response.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Contains("/api/content/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostContent_Returns400_ForEmptyTitle()
    {
        var body = new CreateContentRequest
        {
            Title = "",
            ContentType = ContentType.Blog,
            PrimaryPlatform = Platform.Blog
        };

        var response = await _client.PostAsJsonAsync("/api/content", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetContentList_Returns200_WithPaginatedList()
    {
        var response = await _client.GetAsync("/api/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetContentList_RespectsQueryFilters()
    {
        await CreateTestContent("Filter Test", ContentType.Blog, Platform.Blog);

        var response = await _client.GetAsync("/api/content?status=Idea&platform=Blog&contentType=Blog&search=Filter");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetContent_Returns200_WithContentDetail()
    {
        var id = await CreateTestContent();

        var response = await _client.GetAsync($"/api/content/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ContentDetailDto>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(id, detail.Id);
    }

    [Fact]
    public async Task GetContent_Returns404_ForNonExistentId()
    {
        var response = await _client.GetAsync($"/api/content/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutContent_Returns200_OnSuccessfulUpdate()
    {
        var id = await CreateTestContent();
        var detail = await GetContent(id);

        var body = new UpdateContentRequest
        {
            Title = "Updated Title",
            Body = "Updated body content",
            LastUpdatedAt = detail.UpdatedAt
        };

        var response = await _client.PutAsJsonAsync($"/api/content/{id}", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutContent_Returns409_ForStaleConcurrency()
    {
        var id = await CreateTestContent();
        var detail = await GetContent(id);

        var body = new UpdateContentRequest
        {
            Title = "First Update",
            LastUpdatedAt = detail.UpdatedAt
        };
        var firstResponse = await _client.PutAsJsonAsync($"/api/content/{id}", body);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var staleBody = new UpdateContentRequest
        {
            Title = "Stale Update",
            LastUpdatedAt = detail.UpdatedAt.AddSeconds(-5)
        };
        var staleResponse = await _client.PutAsJsonAsync($"/api/content/{id}", staleBody);

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteContent_Returns200_OnSoftDelete()
    {
        var id = await CreateTestContent();

        var response = await _client.DeleteAsync($"/api/content/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostDraft_ReturnsUpdatedContentDetail()
    {
        var id = await CreateTestContent();

        var body = new DraftContentRequest
        {
            Action = "draft"
        };

        var response = await _client.PostAsJsonAsync($"/api/content/{id}/draft", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ContentDetailDto>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(id, detail.Id);
    }

    [Fact]
    public async Task PutApprove_TransitionsDraftToApproved()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });

        var response = await _client.PutAsync($"/api/content/{id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutSubmitReview_TransitionsDraftToReview()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });

        var response = await _client.PutAsync($"/api/content/{id}/submit-review", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutRequestChanges_TransitionsReviewToDraft()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/submit-review", null);

        var response = await _client.PutAsync($"/api/content/{id}/request-changes", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutSchedule_SetsScheduledAtAndCreatesJob()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/approve", null);

        var body = new ScheduleContentRequest
        {
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        var response = await _client.PutAsJsonAsync($"/api/content/{id}/schedule", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutUnschedule_CancelsJobAndTransitions()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/approve", null);
        await _client.PutAsJsonAsync($"/api/content/{id}/schedule",
            new ScheduleContentRequest { ScheduledAt = DateTimeOffset.UtcNow.AddDays(1) });

        var response = await _client.PutAsync($"/api/content/{id}/unschedule", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostPublish_TransitionsToPublished()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/approve", null);

        var response = await _client.PostAsync($"/api/content/{id}/publish", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutUnpublish_TransitionsPublishedToDraft()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/approve", null);
        await _client.PostAsync($"/api/content/{id}/publish", null);

        var response = await _client.PutAsync($"/api/content/{id}/unpublish", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutRestore_TransitionsArchivedToDraft()
    {
        var id = await CreateTestContent();
        await _client.DeleteAsync($"/api/content/{id}");

        var response = await _client.PutAsync($"/api/content/{id}/restore", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetVoiceCheck_ReturnsVoiceCheckDto()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });

        var response = await _client.GetAsync($"/api/content/{id}/voice-check");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostCrossPost_Returns201_WithChildId()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });

        var body = new CrossPostRequest { TargetPlatform = Platform.LinkedIn };

        var response = await _client.PostAsJsonAsync($"/api/content/{id}/cross-post", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var childId = await response.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, childId);
    }

    [Fact]
    public async Task FullFlow_Create_Draft_Approve_Publish()
    {
        var id = await CreateTestContent("Full Flow Test");

        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });

        await _client.PutAsync($"/api/content/{id}/approve", null);

        var publishResponse = await _client.PostAsync($"/api/content/{id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var detail = await GetContent(id);
        Assert.Equal(ContentStatus.Published, detail.Status);
    }

    [Fact]
    public async Task PostPublish_WithTargetPlatforms_Returns200()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/approve", null);

        var body = new PublishContentRequest { TargetPlatforms = [Platform.Blog] };
        var response = await _client.PostAsJsonAsync($"/api/content/{id}/publish", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPublishStatus_Returns200WithPlatformList()
    {
        var id = await CreateTestContent();
        await _client.PostAsJsonAsync($"/api/content/{id}/draft", new DraftContentRequest { Action = "draft" });
        await _client.PutAsync($"/api/content/{id}/approve", null);
        await _client.PostAsync($"/api/content/{id}/publish", null);

        var response = await _client.GetAsync($"/api/content/{id}/publish-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("platforms", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPublishStatus_Returns404ForNonexistent()
    {
        var response = await _client.GetAsync($"/api/content/{Guid.NewGuid()}/publish-status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostRetry_InvalidPlatform_Returns400()
    {
        var id = await CreateTestContent();

        var response = await _client.PostAsync($"/api/content/{id}/retry/InvalidPlatform", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
