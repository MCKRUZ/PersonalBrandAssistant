using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class InstagramConnectorTests : IDisposable
{
    private const string IgUserId = "17841446031323328";
    private const string GraphBase = "https://graph.instagram.com/v23.0";

    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly Mock<IMediaHost> _mediaHost = new();
    private readonly Mock<IPlatformTokenProvider> _tokens = new();
    private readonly Mock<ILogger<InstagramConnector>> _logger = new();
    private readonly List<(HttpMethod Method, string Url, string Body)> _calls = [];

    private readonly InstagramPublishingOptions _options = new()
    {
        Enabled = true,
        IgUserId = IgUserId,
        GraphBase = GraphBase,
        ShareToFeed = true,
        // Keep the poll loop instant; the delay is not what these tests are about.
        ContainerPollSeconds = 0,
        ContainerPollMaxAttempts = 3
    };

    public InstagramConnectorTests()
    {
        _httpClient = new HttpClient(_httpHandler.Object);

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/ig/x.mp4", "ig/x.mp4"));

        _tokens.Setup(t => t.GetFreshAccessTokenAsync(
                Platform.Instagram, CredentialPurpose.Publishing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("ig-token"));
    }

    /// <summary>Answers each call in order with the given (status, json) pairs, recording every request.</summary>
    private void RespondInOrder(params (HttpStatusCode Status, string Json)[] responses)
    {
        var index = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken _) =>
            {
                var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(CancellationToken.None);
                _calls.Add((req.Method, req.RequestUri!.ToString(), body));
                var r = responses[Math.Min(index++, responses.Length - 1)];
                return new HttpResponseMessage(r.Status) { Content = new StringContent(r.Json) };
            });
    }

    private InstagramConnector CreateConnector()
    {
        var monitor = new Mock<IOptionsMonitor<InstagramPublishingOptions>>();
        monitor.Setup(o => o.CurrentValue).Returns(_options);
        return new InstagramConnector(
            _httpClient, _mediaHost.Object, _tokens.Object, monitor.Object, _logger.Object);
    }

    private static PlatformPublishRequest Request(
        string caption = "a caption", DateTimeOffset? scheduledAt = null, bool video = true)
    {
        var media = video
            ? new MediaAttachment([1, 2, 3], "clip.mp4", "video/mp4")
            : new MediaAttachment([1, 2, 3], "card.png", "image/png");

        return new PlatformPublishRequest(
            new Content { Id = Guid.NewGuid(), Title = "Clip", Body = caption },
            caption, [], null, PublishMode.Publish, scheduledAt, media);
    }

    private const string ContainerCreated = """{"id":"CONTAINER1"}""";
    private const string Finished = """{"status_code":"FINISHED"}""";
    private const string Published = """{"id":"MEDIA1"}""";

    [Fact]
    public async Task PublishAsync_HappyPath_ReturnsTheInstagramMediaId()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("MEDIA1", result.PlatformPostId);
    }

    // Meta fetches the video from a URL and never accepts raw bytes, so skipping the staging step
    // would mean posting a container that points at nothing.
    [Fact]
    public async Task PublishAsync_StagesTheVideoAndPassesItsUrlToMeta()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        var create = _calls[0];
        Assert.Contains($"{IgUserId}/media", create.Url);
        Assert.Contains("media_type=REELS", create.Body);
        Assert.Contains(Uri.EscapeDataString("https://media.matthewkruczek.ai/ig/x.mp4"), create.Body);
    }

    // The staged object is dead weight once Meta has copied the video, and the bucket is billed.
    [Fact]
    public async Task PublishAsync_UnstagesTheVideoAfterPublishing()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        _mediaHost.Verify(m => m.DeleteAsync("ig/x.mp4", It.IsAny<CancellationToken>()), Times.Once);
    }

    // A failure that leaves the clip staged still costs storage, and the failure path is exactly
    // when nobody is watching. Unstaging belongs in a finally, not on the success branch.
    [Fact]
    public async Task PublishAsync_UnstagesEvenWhenPublishingFails()
    {
        RespondInOrder((HttpStatusCode.BadRequest, """{"error":{"message":"bad media"}}"""));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        _mediaHost.Verify(m => m.DeleteAsync("ig/x.mp4", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Publishing a container before Meta has transcoded it fails, so the poll is not optional.
    // A single early FINISHED check would publish a video Instagram is still processing.
    [Fact]
    public async Task PublishAsync_WaitsForTheContainerBeforePublishing()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, """{"status_code":"IN_PROGRESS"}"""),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        // create, poll(IN_PROGRESS), poll(FINISHED), publish — the publish must come after both polls.
        Assert.Equal(4, _calls.Count);
        Assert.Contains("media_publish", _calls[3].Url);
    }

    [Fact]
    public async Task PublishAsync_ContainerErrors_SurfacesMetasReasonAndPublishesNothing()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, """{"status_code":"ERROR","status":"Video format not supported"}"""));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Video format not supported", result.ErrorMessage);
        Assert.DoesNotContain(_calls, c => c.Url.Contains("media_publish"));
    }

    // Giving up must not look like a failed post that already went out. The caller retries on
    // failure, so a timeout that had in fact published would double-post.
    [Fact]
    public async Task PublishAsync_ContainerNeverFinishes_FailsWithoutPublishing()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, """{"status_code":"IN_PROGRESS"}"""));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("still processing", result.ErrorMessage);
        Assert.DoesNotContain(_calls, c => c.Url.Contains("media_publish"));
    }

    // Meta has no scheduling concept whatsoever. Silently posting now would put a clip out days
    // early and report success, so the caller could not tell the slot had been dropped.
    [Fact]
    public async Task PublishAsync_WithAScheduledSlot_RefusesRatherThanPostingNow()
    {
        var result = await CreateConnector().PublishAsync(
            Request(scheduledAt: DateTimeOffset.UtcNow.AddDays(2)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("cannot schedule", result.ErrorMessage);
        Assert.Empty(_calls);
    }

    // The analytics credential carries insights scopes only and physically cannot post. Asking for
    // the Publishing purpose is what keeps this from failing at Meta with a permissions error.
    [Fact]
    public async Task PublishAsync_AsksForThePublishingCredentialNotTheAnalyticsOne()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        _tokens.Verify(t => t.GetFreshAccessTokenAsync(
            Platform.Instagram, CredentialPurpose.Publishing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_NoPublishingCredential_SaysSoAndUploadsNothing()
    {
        _tokens.Setup(t => t.GetFreshAccessTokenAsync(
                Platform.Instagram, CredentialPurpose.Publishing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Fail("Instagram Publishing is not connected."));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not connected", result.ErrorMessage);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishAsync_NonVideo_IsRejectedBeforeAnyUpload()
    {
        var result = await CreateConnector().PublishAsync(
            Request(video: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("video", result.ErrorMessage);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishAsync_OverlongCaption_IsRejectedBeforeAnyUpload()
    {
        var result = await CreateConnector().PublishAsync(
            Request(caption: new string('x', 2201)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("2200", result.ErrorMessage);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // A bare status code turns a fixable permissions problem into a guessing game — the exact way
    // the previous TikTok lane sat broken for weeks while reporting nothing useful.
    [Fact]
    public async Task PublishAsync_MetaError_SurfacesTheMessageNotJustTheStatus()
    {
        RespondInOrder((HttpStatusCode.Forbidden,
            """{"error":{"message":"(#200) Requires instagram_business_content_publish"}}"""));

        var result = await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("instagram_business_content_publish", result.ErrorMessage);
    }

    // Scheduling is a fact about Meta, not a gap here. Anything routing on this capability must see
    // false, or it will hand Instagram a slot and expect it to be honoured.
    // When PBA held the post it staged the clip days ago and owns that object. Re-uploading here
    // would store the same video twice and orphan the first copy.
    [Fact]
    public async Task PublishAsync_WithAPreStagedUrl_UsesItInsteadOfUploadingAgain()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        var request = new PlatformPublishRequest(
            new Content { Id = Guid.NewGuid(), Title = "Clip", Body = "caption" },
            "caption", [], null, PublishMode.Publish, null,
            Media: null,
            HostedMediaUrl: "https://media.matthewkruczek.ai/ig/held.mp4");

        var result = await CreateConnector().PublishAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        _mediaHost.Verify(m => m.UploadAsync(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Contains(Uri.EscapeDataString("https://media.matthewkruczek.ai/ig/held.mp4"), _calls[0].Body);
    }

    // A pre-staged object belongs to whoever held the post; ContentPublisher releases it only after
    // a successful publish. Deleting it here would destroy the clip a retry depends on.
    [Fact]
    public async Task PublishAsync_WithAPreStagedUrl_DoesNotDeleteSomeoneElsesObject()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        var request = new PlatformPublishRequest(
            new Content { Id = Guid.NewGuid(), Title = "Clip", Body = "caption" },
            "caption", [], null, PublishMode.Publish, null,
            Media: null,
            HostedMediaUrl: "https://media.matthewkruczek.ai/ig/held.mp4");

        await CreateConnector().PublishAsync(request, CancellationToken.None);

        _mediaHost.Verify(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Cover frames are picked per clip by whoever cut it — 2334ms on one beat, 37290ms on another.
    // A duration-based guess lands somewhere else, so every Reel would open on the wrong frame.
    [Fact]
    public async Task PublishAsync_WithAChosenCoverFrame_SendsItRatherThanGuessing()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        var request = new PlatformPublishRequest(
            new Content { Id = Guid.NewGuid(), Title = "Clip", Body = "caption" },
            "caption", [], null, PublishMode.Publish, null,
            Media: new MediaAttachment([1, 2, 3], "clip.mp4", "video/mp4"),
            HostedMediaUrl: null,
            CoverFrameOffsetMs: 37290);

        await CreateConnector().PublishAsync(request, CancellationToken.None);

        Assert.Contains("thumb_offset=37290", _calls[0].Body);
    }

    // The held-post path is where this is easiest to lose: the bytes are gone by the slot, so the
    // connector cannot re-derive a frame even if it wanted to. The chosen value must still arrive.
    [Fact]
    public async Task PublishAsync_ChosenCoverFrameSurvivesWithNoBytesInHand()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        var request = new PlatformPublishRequest(
            new Content { Id = Guid.NewGuid(), Title = "Clip", Body = "caption" },
            "caption", [], null, PublishMode.Publish, null,
            Media: null,
            HostedMediaUrl: "https://media.matthewkruczek.ai/ig/held.mp4",
            CoverFrameOffsetMs: 2334);

        await CreateConnector().PublishAsync(request, CancellationToken.None);

        Assert.Contains("thumb_offset=2334", _calls[0].Body);
    }

    // Frame 0 of these clips is a near-black title card, so falling back to zero would make every
    // cover a black square — the reason an offset is sent at all.
    [Fact]
    public async Task PublishAsync_NoChosenCoverFrame_StillNeverUsesFrameZero()
    {
        RespondInOrder(
            (HttpStatusCode.OK, ContainerCreated),
            (HttpStatusCode.OK, Finished),
            (HttpStatusCode.OK, Published));

        await CreateConnector().PublishAsync(Request(), CancellationToken.None);

        Assert.DoesNotContain("thumb_offset=0&", _calls[0].Body);
        Assert.Contains("thumb_offset=", _calls[0].Body);
    }

    [Fact]
    public void GetCapabilities_ReportsThatInstagramCannotSchedule()
    {
        Assert.False(CreateConnector().GetCapabilities().SupportsScheduling);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_ChecksMetaAcceptsTheToken_NotJustThatOneExists()
    {
        RespondInOrder((HttpStatusCode.Unauthorized, """{"error":{"message":"expired"}}"""));

        Assert.False(await CreateConnector().ValidateCredentialsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ValidateCredentialsAsync_LiveTokenAndAccount_ReportsReady()
    {
        RespondInOrder((HttpStatusCode.OK, """{"id":"17841446031323328","username":"matthewkruczek.ai"}"""));

        Assert.True(await CreateConnector().ValidateCredentialsAsync(CancellationToken.None));
    }

    public void Dispose() => _httpClient.Dispose();
}
