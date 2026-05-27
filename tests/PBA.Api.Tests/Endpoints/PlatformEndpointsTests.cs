using System.Net;
using System.Net.Http.Json;
using PBA.Application.Features.Content.Dtos;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class PlatformEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PlatformEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPlatforms_Returns200WithPlatformList()
    {
        var response = await _client.GetAsync("/api/platforms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Blog", body);
        Assert.Contains("Medium", body);
    }

    [Fact]
    public async Task PostCredentials_Medium_Returns200()
    {
        var body = new StoreCredentialsRequest { Token = "test-token" };

        var response = await _client.PostAsJsonAsync("/api/platforms/Medium/credentials", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostCredentials_EmptyToken_Returns400()
    {
        var body = new StoreCredentialsRequest { Token = "" };

        var response = await _client.PostAsJsonAsync("/api/platforms/Medium/credentials", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostCredentials_OAuthPlatform_Returns400()
    {
        var body = new StoreCredentialsRequest { Token = "test" };

        var response = await _client.PostAsJsonAsync("/api/platforms/LinkedIn/credentials", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostCredentials_Blog_Returns400_NoCredentialsNeeded()
    {
        var body = new StoreCredentialsRequest { Token = "test" };

        var response = await _client.PostAsJsonAsync("/api/platforms/Blog/credentials", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not require credentials", text);
    }

    [Fact]
    public async Task PostCredentials_Substack_Returns400_NotYetSupported()
    {
        var body = new StoreCredentialsRequest { Email = "test@test.com", Password = "pass" };

        var response = await _client.PostAsJsonAsync("/api/platforms/Substack/credentials", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("not yet supported", text);
    }
}
