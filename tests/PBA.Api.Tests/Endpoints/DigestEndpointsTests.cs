using System.Net;
using System.Net.Http.Json;
using PBA.Application.Features.Digests.Dtos;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class DigestEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DigestEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetLatest_NoDigest_Returns404OrProblem()
    {
        var response = await _client.GetAsync("/api/digests/latest");
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Expected 404 or 400, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task GetDigests_Returns200WithArray()
    {
        var response = await _client.GetAsync("/api/digests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<IReadOnlyList<DigestSummaryDto>>();
        Assert.NotNull(body);
    }
}
