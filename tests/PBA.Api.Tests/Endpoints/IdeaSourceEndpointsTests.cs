using System.Net;
using System.Net.Http.Json;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class IdeaSourceEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public IdeaSourceEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSources_Returns200_WithSourceList()
    {
        var response = await _client.GetAsync("/api/idea-sources");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostSource_Returns201()
    {
        var body = new IdeaSourceRequest
        {
            Name = "Test Source",
            Type = IdeaSourceType.RSS,
            FeedUrl = "https://example.com/feed",
            Category = "Testing",
            PollIntervalMinutes = 60
        };

        var response = await _client.PostAsJsonAsync("/api/idea-sources", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSource_Returns204()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/idea-sources",
            new IdeaSourceRequest { Name = "Delete Me", Type = IdeaSourceType.API, Category = "Test" });
        var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var response = await _client.DeleteAsync($"/api/idea-sources/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PostSource_Returns400_ForInvalidInput()
    {
        var body = new IdeaSourceRequest { Name = "", Category = "Test" };

        var response = await _client.PostAsJsonAsync("/api/idea-sources", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutSource_Returns200()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/idea-sources",
            new IdeaSourceRequest { Name = "Update Me", Type = IdeaSourceType.API, Category = "Test" });
        var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var updateBody = new IdeaSourceRequest { Name = "Updated Name", Category = "Updated" };
        var response = await _client.PutAsJsonAsync($"/api/idea-sources/{id}", updateBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostRefresh_Returns200_WithCount()
    {
        var response = await _client.PostAsync("/api/idea-sources/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
