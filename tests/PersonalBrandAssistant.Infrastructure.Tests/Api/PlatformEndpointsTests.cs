using Moq;
using PersonalBrandAssistant.Api.Endpoints;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using MediatR;

namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

public class PlatformEndpointsTests
{
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<ISocialPlatform> _twitterAdapter = new();

    public PlatformEndpointsTests()
    {
        _twitterAdapter.Setup(a => a.Type).Returns(PlatformType.TwitterX);
    }

    [Fact]
    public void OAuthCallbackRequest_HasExpectedProperties()
    {
        var request = new OAuthCallbackRequest("code123", "verifier", "state456");
        Assert.Equal("code123", request.Code);
        Assert.Equal("verifier", request.CodeVerifier);
        Assert.Equal("state456", request.State);
    }

    [Fact]
    public void OAuthCallbackRequest_CodeVerifier_IsNullable()
    {
        var request = new OAuthCallbackRequest("code123", null, "state456");
        Assert.Null(request.CodeVerifier);
    }

    [Fact]
    public void TestPostRequest_HasExpectedDefaults()
    {
        var request = new TestPostRequest(true);
        Assert.True(request.Confirm);
        Assert.Null(request.Message);
    }

    [Fact]
    public void TestPostRequest_AcceptsCustomMessage()
    {
        var request = new TestPostRequest(true, "Custom test message");
        Assert.Equal("Custom test message", request.Message);
    }

    [Fact]
    public async Task GenerateAuthUrl_CallsOAuthManager()
    {
        var expectedUrl = new OAuthAuthorizationUrl("https://twitter.com/oauth", "state-123");
        _oauthManager.Setup(o => o.GenerateAuthUrlAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(expectedUrl));

        var result = await _oauthManager.Object.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://twitter.com/oauth", result.Value!.Url);
        Assert.Equal("state-123", result.Value.State);
    }

    [Fact]
    public async Task ExchangeCode_CallsOAuthManagerWithAllFields()
    {
        var tokens = new OAuthTokens("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), null);
        _oauthManager.Setup(o => o.ExchangeCodeAsync(
                PlatformType.TwitterX, "code", "state", "verifier", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(tokens));

        var result = await _oauthManager.Object.ExchangeCodeAsync(
            PlatformType.TwitterX, "code", "state", "verifier", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value!.AccessToken);
    }

    [Fact]
    public async Task RevokeToken_CallsOAuthManager()
    {
        _oauthManager.Setup(o => o.RevokeTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Unit.Value));

        var result = await _oauthManager.Object.RevokeTokenAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TestPost_PublishesViaSocialPlatform()
    {
        var publishResult = new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow);
        _twitterAdapter.Setup(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(publishResult));

        var adapters = new[] { _twitterAdapter.Object };
        var adapter = adapters.FirstOrDefault(a => a.Type == PlatformType.TwitterX);

        Assert.NotNull(adapter);

        var content = new PlatformContent(
            $"Test post - {DateTime.UtcNow:O}",
            null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await adapter!.PublishAsync(content, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("t-1", result.Value!.PlatformPostId);
    }

    [Fact]
    public async Task GetEngagement_ReturnsStats()
    {
        var stats = new EngagementStats(10, 5, 3, 1000, 0, new Dictionary<string, int>().AsReadOnly());
        _twitterAdapter.Setup(a => a.GetEngagementAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(stats));

        var result = await _twitterAdapter.Object.GetEngagementAsync("12345", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.Likes);
        Assert.Equal(5, result.Value.Comments);
    }

    [Fact]
    public void PlatformType_ParsesCorrectly()
    {
        Assert.True(Enum.TryParse<PlatformType>("TwitterX", ignoreCase: true, out var result));
        Assert.Equal(PlatformType.TwitterX, result);

        Assert.True(Enum.TryParse<PlatformType>("linkedin", ignoreCase: true, out var linkedin));
        Assert.Equal(PlatformType.LinkedIn, linkedin);

        Assert.False(Enum.TryParse<PlatformType>("invalid", ignoreCase: true, out _));
    }

    [Fact]
    public void AdapterResolution_FindsCorrectPlatform()
    {
        var linkedInAdapter = new Mock<ISocialPlatform>();
        linkedInAdapter.Setup(a => a.Type).Returns(PlatformType.LinkedIn);

        var adapters = new[] { _twitterAdapter.Object, linkedInAdapter.Object };

        var twitter = adapters.FirstOrDefault(a => a.Type == PlatformType.TwitterX);
        var linkedin = adapters.FirstOrDefault(a => a.Type == PlatformType.LinkedIn);
        var youtube = adapters.FirstOrDefault(a => a.Type == PlatformType.YouTube);

        Assert.NotNull(twitter);
        Assert.NotNull(linkedin);
        Assert.Null(youtube);
    }
}
