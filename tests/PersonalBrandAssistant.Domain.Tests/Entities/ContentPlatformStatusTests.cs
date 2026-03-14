using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class ContentPlatformStatusTests
{
    [Fact]
    public void ContentPlatformStatus_DefaultsTo_PendingStatus_And_ZeroRetries()
    {
        var status = new ContentPlatformStatus
        {
            ContentId = Guid.NewGuid(),
            Platform = PlatformType.TwitterX,
        };

        Assert.Equal(PlatformPublishStatus.Pending, status.Status);
        Assert.Equal(0, status.RetryCount);
    }

    [Fact]
    public void IdempotencyKey_CanBeSetViaInitProperty()
    {
        var key = "sha256-hash-value";
        var status = new ContentPlatformStatus
        {
            ContentId = Guid.NewGuid(),
            Platform = PlatformType.LinkedIn,
            IdempotencyKey = key,
        };

        Assert.Equal(key, status.IdempotencyKey);
    }

    [Fact]
    public void ContentPlatformStatus_GetsValidGuidId_FromEntityBase()
    {
        var status = new ContentPlatformStatus
        {
            ContentId = Guid.NewGuid(),
            Platform = PlatformType.Instagram,
        };

        Assert.NotEqual(Guid.Empty, status.Id);
    }

    [Fact]
    public void ContentPlatformStatus_StoresAllPublishOutcomeFields()
    {
        var now = DateTimeOffset.UtcNow;
        var status = new ContentPlatformStatus
        {
            ContentId = Guid.NewGuid(),
            Platform = PlatformType.YouTube,
            PlatformPostId = "yt-video-123",
            PostUrl = "https://youtube.com/watch?v=123",
            PublishedAt = now,
            ErrorMessage = "some error",
        };

        Assert.Equal("yt-video-123", status.PlatformPostId);
        Assert.Equal("https://youtube.com/watch?v=123", status.PostUrl);
        Assert.Equal(now, status.PublishedAt);
        Assert.Equal("some error", status.ErrorMessage);
    }
}
