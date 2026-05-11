using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class IdeaEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public IdeaEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetIdeas_Returns200_WithPaginatedList()
    {
        var response = await _client.GetAsync("/api/ideas");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetIdea_Returns404_ForNonExistentId()
    {
        var response = await _client.GetAsync($"/api/ideas/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostIdea_Returns201_WithNewId()
    {
        var body = new CreateIdeaRequest
        {
            Title = "Integration Test Idea",
            Category = "Testing"
        };

        var response = await _client.PostAsJsonAsync("/api/ideas", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var id = await response.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task PostIdea_Returns400_ForInvalidInput()
    {
        var body = new CreateIdeaRequest { Title = "" };

        var response = await _client.PostAsJsonAsync("/api/ideas", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutIdeaSave_Returns200()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/ideas",
            new CreateIdeaRequest { Title = "Save Test" });
        var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var saveBody = new SaveIdeaRequest { Notes = "Test notes" };
        var response = await _client.PutAsJsonAsync($"/api/ideas/{id}/save", saveBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutIdeaDismiss_Returns200()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/ideas",
            new CreateIdeaRequest { Title = "Dismiss Test" });
        var id = await createResponse.Content.ReadFromJsonAsync<Guid>();

        var response = await _client.PutAsync($"/api/ideas/{id}/dismiss", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetIdeaConnections_Returns200()
    {
        var response = await _client.GetAsync("/api/ideas/connections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
