using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Tests.Publishing;
using Xunit;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Infrastructure.Tests.Connectors;

/// <summary>
/// Covers everything the connector decides BEFORE it reaches Google — the guards that stop an
/// upload from burning quota it cannot get back. The upload call itself goes through Google's client
/// and is exercised against the real API, not here.
/// </summary>
public class YouTubeConnectorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly Mock<IPlatformTokenProvider> _tokens = new();
    private readonly Mock<ILogger<YouTubeConnector>> _logger = new();

    public YouTubeConnectorTests()
    {
        _httpClient = new HttpClient(_httpHandler.Object);
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(
                Platform.YouTube, CredentialPurpose.Publishing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("yt-token"));
    }

    private YouTubeConnector CreateConnector()
    {
        var options = new Mock<IOptionsMonitor<YouTubePublishingOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new YouTubePublishingOptions { Enabled = true });
        return new YouTubeConnector(_httpClient, _tokens.Object, options.Object, new FixedClock(Now), _logger.Object);
    }

    private static PlatformPublishRequest Request(
        string title = "Short title",
        DateTimeOffset? scheduledAt = null,
        MediaAttachment? media = null,
        string? hostedUrl = null) =>
        new(
            Content: new ContentEntity { Title = title, Body = "description" },
            TransformedContent: "description",
            Tags: [],
            CanonicalUrl: null,
            Mode: PublishMode.Publish,
            ScheduledAt: scheduledAt,
            Media: media,
            HostedMediaUrl: hostedUrl);

    private static MediaAttachment Clip() => new([1, 2, 3], "clip.mp4", "video/mp4");

    // YouTube holds the video itself once uploaded, which is what makes the lane independent of any
    // machine here. Reporting this wrong in either direction breaks a campaign: false would make PBA
    // sit on every clip and post them by hand.
    [Fact]
    public void Capabilities_SaysYouTubeSchedulesButRationsUploads()
    {
        var capabilities = CreateConnector().GetCapabilities();

        Assert.True(capabilities.SupportsScheduling);
        Assert.True(capabilities.RequiresPacedHandover);
    }

    // YouTube rejects a publish time already gone by. Finding that out from the API costs the 1,600
    // quota units of the attempt, and on a launch day those are the units the long-form needs.
    [Fact]
    public async Task Publish_WithAPublishTimeAlreadyPast_FailsBeforeSpendingQuota()
    {
        var result = await CreateConnector().PublishAsync(
            Request(scheduledAt: Now.AddMinutes(-1), media: Clip()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("already passed", result.ErrorMessage);
        _tokens.Verify(t => t.GetFreshAccessTokenAsync(
            It.IsAny<Platform>(), It.IsAny<CredentialPurpose>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_WithNoVideoAtAll_Fails()
    {
        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("requires a video", result.ErrorMessage);
    }

    // A YouTube video's title is the headline a viewer reads in the feed, and the API will not
    // accept an empty one. Unlike TikTok and Instagram, the caption cannot stand in for it.
    [Fact]
    public async Task Publish_WithNoTitle_Fails()
    {
        var result = await CreateConnector().PublishAsync(
            Request(title: "   ", media: Clip()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("needs a title", result.ErrorMessage);
    }

    [Fact]
    public async Task Publish_WithoutAPublishingCredential_SaysSoRatherThanFailingAtUpload()
    {
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(
                Platform.YouTube, CredentialPurpose.Publishing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Fail("No active YouTube publishing credential."));

        var result = await CreateConnector().PublishAsync(
            Request(media: Clip(), scheduledAt: Now.AddDays(1)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("credential", result.ErrorMessage);
    }

    // The clip PBA held was staged days earlier and storage reaps on a lifecycle rule. If it is gone
    // the upload cannot happen, and the message has to say that — "upload failed" would send someone
    // looking at YouTube for a problem that is in the bucket.
    [Fact]
    public async Task Publish_WhenTheStagedClipIsGone_SaysTheClipCouldNotBeRead()
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateConnector().PublishAsync(
            Request(scheduledAt: Now.AddDays(1),
                    hostedUrl: "https://media.matthewkruczek.ai/clips/gone.mp4"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("could not be read back from storage", result.ErrorMessage);
    }

    // The real failure this lane hit on its first test upload. The credential was valid, the upload
    // succeeded, YouTube returned a real video id — and the video was on a SECOND channel the same
    // Google account owns, carrying the same display name and a different handle. Nothing on the
    // channel anyone watches showed a trace. Consent grants whichever channel was picked in
    // Google's chooser, so "valid credential" and "right channel" are separate facts.
    [Fact]
    public void ChannelMismatch_RefusesAValidCredentialForTheWrongChannel()
    {
        var reason = YouTubeConnector.ChannelMismatch(
            expected: "UCZ3-8txSHf0tsTbrm98p8_w",
            actualId: "UC5oBxdKVePX4cxKIzLAMfZg",
            title: "Matt Kruczek",
            handle: "@mattkruczek2665");

        Assert.NotNull(reason);
        // Both ids belong in the message. The two channels share a display name, so naming only the
        // title would read as "the credential is for Matt Kruczek, not Matt Kruczek".
        Assert.Contains("UC5oBxdKVePX4cxKIzLAMfZg", reason);
        Assert.Contains("UCZ3-8txSHf0tsTbrm98p8_w", reason);
        Assert.Contains("@mattkruczek2665", reason);
    }

    [Fact]
    public void ChannelMismatch_AcceptsTheIntendedChannel()
    {
        Assert.Null(YouTubeConnector.ChannelMismatch(
            "UCZ3-8txSHf0tsTbrm98p8_w", "UCZ3-8txSHf0tsTbrm98p8_w", "Matt Kruczek", "@matthewkruczek"));
    }

    // Unset means "no expectation on file", which cannot be checked. It has to pass rather than
    // block every upload — but it is why the production compose file pins the id.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ChannelMismatch_WithNoConfiguredChannel_DoesNotBlock(string? configured)
    {
        Assert.Null(YouTubeConnector.ChannelMismatch(
            configured, "UC5oBxdKVePX4cxKIzLAMfZg", "Matt Kruczek", "@mattkruczek2665"));
    }

    public void Dispose() => _httpClient.Dispose();
}
