using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class BlogEntityTests
{
    [Fact]
    public void MatchConfidence_HasExpectedValues()
    {
        var values = Enum.GetValues<MatchConfidence>();
        Assert.Equal(4, values.Length);
        Assert.Contains(MatchConfidence.High, values);
        Assert.Contains(MatchConfidence.Medium, values);
        Assert.Contains(MatchConfidence.Low, values);
        Assert.Contains(MatchConfidence.None, values);
    }

    [Fact]
    public void NotificationStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<NotificationStatus>();
        Assert.Equal(3, values.Length);
        Assert.Contains(NotificationStatus.Pending, values);
        Assert.Contains(NotificationStatus.Acknowledged, values);
        Assert.Contains(NotificationStatus.Acted, values);
    }

    [Fact]
    public void BlogPublishStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<BlogPublishStatus>();
        Assert.Equal(4, values.Length);
        Assert.Contains(BlogPublishStatus.Staged, values);
        Assert.Contains(BlogPublishStatus.Publishing, values);
        Assert.Contains(BlogPublishStatus.Published, values);
        Assert.Contains(BlogPublishStatus.Failed, values);
    }

    [Fact]
    public void ChatMessage_ConstructsCorrectly()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var message = new ChatMessage("user", "Hello world", timestamp);

        Assert.Equal("user", message.Role);
        Assert.Equal("Hello world", message.Content);
        Assert.Equal(timestamp, message.Timestamp);
    }

    [Fact]
    public void SubstackDetection_InitializesWithCorrectDefaults()
    {
        var detection = new SubstackDetection();

        Assert.Null(detection.ContentId);
        Assert.Equal(string.Empty, detection.RssGuid);
        Assert.Equal(string.Empty, detection.Title);
        Assert.Equal(string.Empty, detection.SubstackUrl);
        Assert.Equal(string.Empty, detection.ContentHash);
        Assert.NotEqual(Guid.Empty, detection.Id);
    }

    [Fact]
    public void UserNotification_StatusTransitions()
    {
        var notification = new UserNotification
        {
            Type = "SubstackDetected",
            Message = "New post detected",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal(NotificationStatus.Pending, notification.Status);

        notification.Status = NotificationStatus.Acknowledged;
        notification.AcknowledgedAt = DateTimeOffset.UtcNow;
        Assert.Equal(NotificationStatus.Acknowledged, notification.Status);
        Assert.NotNull(notification.AcknowledgedAt);

        notification.Status = NotificationStatus.Acted;
        Assert.Equal(NotificationStatus.Acted, notification.Status);
    }

    [Fact]
    public void NotificationType_IncludesSubstackDetectedAndBlogReady()
    {
        var values = Enum.GetValues<NotificationType>();
        Assert.Contains(NotificationType.SubstackDetected, values);
        Assert.Contains(NotificationType.BlogReady, values);
    }
}
