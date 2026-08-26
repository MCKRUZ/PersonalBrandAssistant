using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

public class ContentPublisherTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IPlatformConnector> _blogConnector = new();
    private readonly Mock<IPlatformConnector> _mediumConnector = new();
    private readonly Mock<IPlatformConnector> _linkedInConnector = new();
    private readonly Mock<IPlatformConnector> _twitterConnector = new();
    private readonly Mock<IContentTransformer> _transformer = new();
    private readonly Mock<IMediaHost> _mediaHost = new();
    private readonly Mock<IPlatformCapabilityReader> _capabilities = new();
    private readonly Mock<ILogger<ContentPublisher>> _logger = new();

    public ContentPublisherTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _transformer.Setup(t => t.TransformAsync(It.IsAny<Content>(), It.IsAny<Platform>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Content c, Platform _, CancellationToken _) => c.Body);

        // Default to a platform that CAN hold a post for us, which is the common case among the
        // connectors these tests drive. Individual tests override it where the point is a platform
        // that cannot.
        _capabilities.Setup(c => c.SupportsScheduling(It.IsAny<Platform>())).Returns(true);
    }

    private ContentPublisher CreatePublisher()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Blog, _blogConnector.Object);
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Medium, _mediumConnector.Object);
        services.AddKeyedSingleton<IPlatformConnector>(Platform.LinkedIn, _linkedInConnector.Object);
        services.AddKeyedSingleton<IPlatformConnector>(Platform.Twitter, _twitterConnector.Object);
        var sp = services.BuildServiceProvider();

        return new ContentPublisher(
            _dbContext, sp, _transformer.Object, _mediaHost.Object, _capabilities.Object, _logger.Object);
    }

    private Content CreateScheduledContent(Platform platform = Platform.Blog) =>
        new()
        {
            Title = "Test Post",
            Body = "Some content",
            Status = ContentStatus.Scheduled,
            PrimaryPlatform = platform,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

    private void SetupConnectorSuccess(Mock<IPlatformConnector> connector, string url = "https://example.com/post", string postId = "post-1") =>
        connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(true, url, postId, null));

    private void SetupConnectorFailure(Mock<IPlatformConnector> connector, string error = "Publish failed") =>
        connector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformPublishResult(false, null, null, error));

    // --- Migrated existing tests ---

    // The record's ScheduledAt is the ONLY channel by which a caller can ask a platform to hold a
    // post — BufferConnector reads it off the request to choose customScheduled over shareNow.
    // Dropping it here would silently turn every scheduled clip into an immediate post, and the
    // symptom (a clip going out days early) would surface on the platform, not in any log.
    [Fact]
    public async Task PublishAsync_PassesTheRecordsScheduledAt_ToTheConnector()
    {
        var when = DateTimeOffset.UtcNow.AddDays(2);
        var content = CreateScheduledContent();
        content.ScheduledAt = when;
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        PlatformPublishRequest? captured = null;
        _blogConnector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPublishRequest r, CancellationToken _) => captured = r)
            .ReturnsAsync(new PlatformPublishResult(true, "https://example.com/post", "post-1", null));

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        Assert.Equal(when, captured!.ScheduledAt);
    }

    // A post PBA held until its slot has no bytes left — they arrived days earlier. The staged clip
    // on R2 is the only handle on the video, so failing to pass it means the connector publishes
    // nothing at the moment it finally matters.
    [Fact]
    public async Task PublishAsync_HeldPost_HandsTheStagedUrlToTheConnector()
    {
        var content = CreateScheduledContent();
        content.StagedMediaUrl = "https://media.matthewkruczek.ai/ig/held.mp4";
        content.StagedMediaKey = "ig/held.mp4";
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        PlatformPublishRequest? captured = null;
        _blogConnector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPublishRequest r, CancellationToken _) => captured = r)
            .ReturnsAsync(new PlatformPublishResult(true, "https://example.com/post", "post-1", null));

        await CreatePublisher().PublishAsync(content.Id);

        Assert.Equal("https://media.matthewkruczek.ai/ig/held.mp4", captured!.HostedMediaUrl);
    }

    // PBA held this one because the platform cannot schedule at all (Instagram's shape), so this
    // call IS the moment. Passing a time on would ask an API that has no scheduling concept for
    // something it cannot do — and if it could, it would book a moment already in the past.
    [Fact]
    public async Task PublishAsync_HeldPost_ForANonSchedulingPlatform_PassesNoTime()
    {
        _capabilities.Setup(c => c.SupportsScheduling(Platform.Blog)).Returns(false);

        var content = CreateScheduledContent();
        content.ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        content.StagedMediaUrl = "https://media.matthewkruczek.ai/ig/held.mp4";
        content.StagedMediaKey = "ig/held.mp4";
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        PlatformPublishRequest? captured = null;
        _blogConnector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPublishRequest r, CancellationToken _) => captured = r)
            .ReturnsAsync(new PlatformPublishResult(true, "https://example.com/post", "post-1", null));

        await CreatePublisher().PublishAsync(content.Id);

        Assert.Null(captured!.ScheduledAt);
    }

    // YouTube's shape, and the reason this stopped being decided by "was it staged?". PBA holds the
    // clip not because YouTube cannot schedule — it can — but because uploading is rationed, so the
    // handover happens days EARLY. The go-live time still has to travel with it: without it the
    // video is uploaded public and the whole campaign appears at once.
    [Fact]
    public async Task PublishAsync_HeldPost_ForASchedulingPlatform_StillCarriesTheGoLiveTime()
    {
        var goLive = DateTimeOffset.UtcNow.AddDays(3);
        var content = CreateScheduledContent();
        content.ScheduledAt = goLive;
        content.StagedMediaUrl = "https://media.matthewkruczek.ai/clips/held.mp4";
        content.StagedMediaKey = "clips/held.mp4";
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        PlatformPublishRequest? captured = null;
        _blogConnector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback((PlatformPublishRequest r, CancellationToken _) => captured = r)
            .ReturnsAsync(new PlatformPublishResult(true, "https://example.com/post", "post-1", null));

        await CreatePublisher().PublishAsync(content.Id);

        Assert.Equal(goLive, captured!.ScheduledAt);
        Assert.Equal("https://media.matthewkruczek.ai/clips/held.mp4", captured.HostedMediaUrl);
    }

    // The hand-over moment, and the reason a campaign is no longer capped by a storage setting: the
    // clip has been sitting beside its record since the request, and becomes a public URL only now.
    // The lifecycle rule that reaps that URL therefore only has to outlast the platform reading it,
    // instead of the entire wait before the post was due.
    [Fact]
    public async Task PublishAsync_HeldClip_IsPublishedToStorageAtHandoverAndPassedToTheConnector()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        _dbContext.HeldMedia.Add(new HeldMedia
        {
            ContentId = content.Id,
            FileName = "clip.mp4",
            ContentType = "video/mp4",
            Data = [1, 2, 3]
        });
        await _dbContext.SaveChangesAsync();

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/tiktok/now.mp4", "tiktok/now.mp4"));

        PlatformPublishRequest? captured = null;
        _blogConnector.Setup(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformPublishRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new PlatformPublishResult(true, "https://example.com/post", "post-1", null));

        await CreatePublisher().PublishAsync(content.Id);

        _mediaHost.Verify(m => m.UploadAsync(
            It.Is<byte[]>(d => d.SequenceEqual(new byte[] { 1, 2, 3 })),
            "clip.mp4", "video/mp4", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("https://media.matthewkruczek.ai/tiktok/now.mp4", captured!.HostedMediaUrl);
    }

    // Tens of megabytes per clip: once the public copy exists and the post is out, the held bytes
    // are pure weight in the database and the backups it lands in.
    [Fact]
    public async Task PublishAsync_HeldClip_IsDroppedFromTheDatabaseOncePublished()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        _dbContext.HeldMedia.Add(new HeldMedia
        {
            ContentId = content.Id, FileName = "clip.mp4", ContentType = "video/mp4", Data = [1, 2, 3]
        });
        await _dbContext.SaveChangesAsync();

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/tiktok/now.mp4", "tiktok/now.mp4"));
        SetupConnectorSuccess(_blogConnector);

        await CreatePublisher().PublishAsync(content.Id);

        Assert.Empty(_dbContext.HeldMedia);
    }

    // A failed publish is retried, and the retry needs the bytes. Dropping them on failure would
    // make the first failure permanent.
    [Fact]
    public async Task PublishAsync_HeldClipThatFails_KeepsTheBytesForTheRetry()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        _dbContext.HeldMedia.Add(new HeldMedia
        {
            ContentId = content.Id, FileName = "clip.mp4", ContentType = "video/mp4", Data = [1, 2, 3]
        });
        await _dbContext.SaveChangesAsync();

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/tiktok/now.mp4", "tiktok/now.mp4"));
        SetupConnectorFailure(_blogConnector);

        await CreatePublisher().PublishAsync(content.Id);

        Assert.Single(_dbContext.HeldMedia);
    }

    // A retry days after a failure must not reuse the public copy made on the first attempt: it sits
    // on a short lifecycle rule and may well have been reaped by then. Reusing it would turn "the
    // publish failed" into "the publish succeeded and the video is a dead link" — quieter and worse.
    // The held bytes are still there, so a fresh copy is always available.
    [Fact]
    public async Task PublishAsync_WhenTheHandoverFails_DropsThePublicCopySoTheRetryMakesAFreshOne()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        _dbContext.HeldMedia.Add(new HeldMedia
        {
            ContentId = content.Id, FileName = "clip.mp4", ContentType = "video/mp4", Data = [1, 2, 3]
        });
        await _dbContext.SaveChangesAsync();

        _mediaHost.Setup(m => m.UploadAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedMedia("https://media.matthewkruczek.ai/tiktok/first.mp4", "tiktok/first.mp4"));
        SetupConnectorFailure(_blogConnector);

        await CreatePublisher().PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Null(updated!.StagedMediaUrl);
        Assert.Null(updated.StagedMediaKey);
        Assert.Single(_dbContext.HeldMedia);
    }

    // The one cleanup that must NOT happen. Buffer stores the link and follows it when the post
    // fires, up to a week after this line runs — deleting here hands it a URL that 404s on the day,
    // and nothing fails until then. The lifecycle rule is sized for exactly that wait.
    [Fact]
    public async Task PublishAsync_WhenThePlatformFetchesTheMediaAtPostTime_LeavesTheStagedClipInPlace()
    {
        _capabilities.Setup(c => c.FetchesHostedMediaAtPostTime(Platform.Blog)).Returns(true);
        var content = CreateScheduledContent();
        content.StagedMediaUrl = "https://media.matthewkruczek.ai/tiktok/held.mp4";
        content.StagedMediaKey = "tiktok/held.mp4";
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);

        await CreatePublisher().PublishAsync(content.Id);

        _mediaHost.Verify(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal("tiktok/held.mp4", updated!.StagedMediaKey);
    }

    // Storage is billed and the pointer must not outlive the object: a stale StagedMediaUrl on a
    // republish would send the platform at something that has already been reaped.
    [Fact]
    public async Task PublishAsync_HeldPost_ReleasesTheStagedClipOncePublished()
    {
        var content = CreateScheduledContent();
        content.StagedMediaUrl = "https://media.matthewkruczek.ai/ig/held.mp4";
        content.StagedMediaKey = "ig/held.mp4";
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);

        await CreatePublisher().PublishAsync(content.Id);

        _mediaHost.Verify(m => m.DeleteAsync("ig/held.mp4", It.IsAny<CancellationToken>()), Times.Once);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Null(updated!.StagedMediaKey);
        Assert.Null(updated.StagedMediaUrl);
    }

    // A failed publish will be retried, and the retry needs the clip. Releasing it on failure would
    // make the first failure permanent.
    [Fact]
    public async Task PublishAsync_HeldPostThatFails_KeepsTheStagedClipForTheRetry()
    {
        var content = CreateScheduledContent();
        content.StagedMediaUrl = "https://media.matthewkruczek.ai/ig/held.mp4";
        content.StagedMediaKey = "ig/held.mp4";
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorFailure(_blogConnector);

        await CreatePublisher().PublishAsync(content.Id);

        _mediaHost.Verify(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal("ig/held.mp4", updated!.StagedMediaKey);
    }

    [Fact]
    public async Task PublishAsync_PublishesContent_WhenStatusIsScheduled()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
        Assert.NotNull(updated.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_SkipsPublishing_WhenStatusIsNoLongerScheduled()
    {
        var content = CreateScheduledContent();
        content.Status = ContentStatus.Approved;
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Approved, updated!.Status);
        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_InvokesPlatformConnector_ForBlogPlatform()
    {
        var content = CreateScheduledContent(Platform.Blog);
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_CreatesContentPlatformPublishRecord()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var record = await _dbContext.ContentPlatformPublishes.FirstOrDefaultAsync(p => p.ContentId == content.Id);
        Assert.NotNull(record);
        Assert.Equal(PublishStatus.Published, record.Status);
        Assert.Equal("https://matthewkruczek.ai/posts/test-post", record.PublishedUrl);
    }

    // --- New tests ---

    [Fact]
    public async Task PublishAsync_ResolvesConnectorByPlatform_ViaKeyedDI()
    {
        var content = CreateScheduledContent(Platform.Medium);
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_mediumConnector, "https://medium.com/@matt/post", "medium-1");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_PrimaryFails_AbortsWithoutPublishingSecondaries()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorFailure(_blogConnector, "git push failed");

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        Assert.False(result.PrimarySuccess);
        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.NotEqual(ContentStatus.Published, updated!.Status);
    }

    [Fact]
    public async Task PublishAsync_PrimarySucceeds_FiresStateMachineTrigger()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
        Assert.NotNull(updated.PublishedAt);
    }

    [Fact]
    public async Task PublishAsync_SecondaryFails_CreatesFailedContentPlatformPublishRecord()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
        SetupConnectorFailure(_mediumConnector, "Medium API error");

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        Assert.True(result.PrimarySuccess);

        var records = await _dbContext.ContentPlatformPublishes
            .Where(p => p.ContentId == content.Id)
            .ToListAsync();

        var blogRecord = records.Single(r => r.Platform == Platform.Blog);
        Assert.Equal(PublishStatus.Published, blogRecord.Status);

        var mediumRecord = records.Single(r => r.Platform == Platform.Medium);
        Assert.Equal(PublishStatus.Failed, mediumRecord.Status);
        Assert.Equal("Medium API error", mediumRecord.ErrorMessage);
    }

    [Fact]
    public async Task PublishAsync_SecondaryFails_CreatesRecordWithRetryCountZero()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);
        SetupConnectorFailure(_mediumConnector);

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        var mediumRecord = await _dbContext.ContentPlatformPublishes
            .SingleAsync(p => p.ContentId == content.Id && p.Platform == Platform.Medium);
        Assert.Equal(0, mediumRecord.RetryCount);
    }

    [Fact]
    public async Task PublishAsync_SkipsPlatformWithExistingPublishedRecord()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog];
        _dbContext.Contents.Add(content);
        _dbContext.ContentPlatformPublishes.Add(new ContentPlatformPublish
        {
            ContentId = content.Id,
            Platform = Platform.Blog,
            Status = PublishStatus.Published,
            PublishedUrl = "https://matthewkruczek.ai/posts/existing",
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await _dbContext.SaveChangesAsync();

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var records = await _dbContext.ContentPlatformPublishes.Where(p => p.ContentId == content.Id).ToListAsync();
        Assert.Single(records);
    }

    [Fact]
    public async Task PublishAsync_NoTargetPlatforms_UsesContentTargetPlatforms()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.LinkedIn];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");
        SetupConnectorSuccess(_linkedInConnector, "https://linkedin.com/post/1", "li-1");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _linkedInConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_NoContentTargetPlatforms_UsesPrimaryPlatformOnly()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector);

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mediumConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _linkedInConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_GuidOverload_CallsFullMethodWithNullTargets()
    {
        var content = CreateScheduledContent();
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test-post", "test-post");

        var publisher = CreatePublisher();
        await publisher.PublishAsync(content.Id);

        _blogConnector.Verify(c => c.PublishAsync(It.IsAny<PlatformPublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
    }

    [Fact]
    public async Task PublishAsync_ParallelSecondaries_AllPublishIndependently()
    {
        var content = CreateScheduledContent(Platform.Blog);
        content.TargetPlatforms = [Platform.Blog, Platform.Medium, Platform.LinkedIn, Platform.Twitter];
        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync();
        SetupConnectorSuccess(_blogConnector, "https://matthewkruczek.ai/posts/test", "blog-1");
        SetupConnectorFailure(_mediumConnector, "Medium error");
        SetupConnectorSuccess(_linkedInConnector, "https://linkedin.com/post/1", "li-1");
        SetupConnectorFailure(_twitterConnector, "Twitter error");

        var publisher = CreatePublisher();
        var result = await publisher.PublishAsync(content.Id, null, null, CancellationToken.None);

        Assert.True(result.PrimarySuccess);
        Assert.Equal("https://matthewkruczek.ai/posts/test", result.PrimaryUrl);

        var records = await _dbContext.ContentPlatformPublishes
            .Where(p => p.ContentId == content.Id)
            .ToListAsync();
        Assert.Equal(4, records.Count);

        Assert.Equal(PublishStatus.Published, records.Single(r => r.Platform == Platform.Blog).Status);
        Assert.Equal(PublishStatus.Failed, records.Single(r => r.Platform == Platform.Medium).Status);
        Assert.Equal(PublishStatus.Published, records.Single(r => r.Platform == Platform.LinkedIn).Status);
        Assert.Equal(PublishStatus.Failed, records.Single(r => r.Platform == Platform.Twitter).Status);

        var updated = await _dbContext.Contents.FindAsync(content.Id);
        Assert.Equal(ContentStatus.Published, updated!.Status);
    }

    public void Dispose() => _dbContext.Dispose();
}
