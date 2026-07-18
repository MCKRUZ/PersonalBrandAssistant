using System.Net;
using System.Net.Http.Json;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
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

    [Theory]
    [InlineData("YouTube")]
    [InlineData("Instagram")]
    [InlineData("TikTok")]
    public async Task Authorize_YouTubeInstagramTikTok_Returns302(string platform)
    {
        var response = await _client.GetAsync($"/api/auth/{platform}/authorize");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WithPurposeAnalytics_PassesAnalyticsPurpose()
    {
        var response = await _client.GetAsync("/api/auth/TikTok/authorize?purpose=analytics");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Only this test uses TikTok+analytics, so the verify is not coupled to other tests.
        _factory.OAuthServiceMock.Verify(x => x.GetAuthorizationUrlAsync(
            Platform.TikTok, CredentialPurpose.Analytics, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Authorize_InvalidPurpose_Returns400()
    {
        var response = await _client.GetAsync("/api/auth/YouTube/authorize?purpose=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("LinkedIn")]
    [InlineData("Twitter")]
    public async Task Authorize_AnalyticsPurposeForPublishingPlatform_Returns400(string platform)
    {
        // Analytics OAuth is only valid for the analytics platforms; allowing it for a publishing platform
        // would let a read-only credential shadow the publishing one in Platform-only lookups.
        var response = await _client.GetAsync($"/api/auth/{platform}/authorize?purpose=analytics");

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
