using System.Net;
using System.Net.Http.Json;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using PBA.Infrastructure.Data;
using Xunit;

namespace PBA.Api.Tests.Endpoints;

public class OAuthEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public OAuthEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Authorize_LinkedIn_Returns302WithRedirectUrl()
    {
        var response = await _client.GetAsync("/api/auth/LinkedIn/authorize");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("oauth.test", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Authorize_UnsupportedPlatform_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/Blog/authorize");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Callback_ValidCode_Returns302RedirectToFrontend()
    {
        var response = await _client.GetAsync("/api/auth/LinkedIn/callback?code=abc&state=valid");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("connected=LinkedIn", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Status_NotConfigured_ReturnsNotConfiguredStatus()
    {
        var response = await _client.GetAsync("/api/auth/Twitter/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NotConfigured", body);
    }

    [Fact]
    public async Task Status_ConnectedPlatform_ReturnsConnectedStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.PlatformCredentials.Add(new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            IsActive = true,
            EncryptedAccessToken = "test",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/auth/LinkedIn/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Connected", body);
    }

    [Fact]
    public async Task Delete_ConnectedPlatform_Returns200()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.PlatformCredentials.Add(new PlatformCredential
        {
            Platform = Platform.Medium,
            IsActive = true,
            EncryptedAccessToken = "test"
        });
        await db.SaveChangesAsync();

        var response = await _client.DeleteAsync("/api/auth/Medium");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonexistentPlatform_Returns404()
    {
        var response = await _client.DeleteAsync("/api/auth/Substack");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
