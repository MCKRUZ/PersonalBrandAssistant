using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class NotificationTests
{
    [Fact]
    public void Create_SetsIsReadToFalse()
    {
        var notification = Notification.Create(
            Guid.NewGuid(), NotificationType.ContentApproved, "Title", "Message");

        Assert.False(notification.IsRead);
    }

    [Fact]
    public void MarkAsRead_SetsIsReadToTrue()
    {
        var notification = Notification.Create(
            Guid.NewGuid(), NotificationType.ContentApproved, "Title", "Message");

        notification.MarkAsRead();

        Assert.True(notification.IsRead);
    }

    [Fact]
    public void Create_PopulatesAllRequiredFields()
    {
        var userId = Guid.NewGuid();
        var notification = Notification.Create(
            userId, NotificationType.ContentRejected, "Rejected", "Your content was rejected");

        Assert.Equal(userId, notification.UserId);
        Assert.Equal(NotificationType.ContentRejected, notification.Type);
        Assert.Equal("Rejected", notification.Title);
        Assert.Equal("Your content was rejected", notification.Message);
        Assert.NotEqual(default, notification.CreatedAt);
    }

    [Fact]
    public void Create_ContentIdIsNullByDefault()
    {
        var notification = Notification.Create(
            Guid.NewGuid(), NotificationType.ContentApproved, "Title", "Message");

        Assert.Null(notification.ContentId);
    }

    [Fact]
    public void Create_WithContentId_SetsContentId()
    {
        var contentId = Guid.NewGuid();
        var notification = Notification.Create(
            Guid.NewGuid(), NotificationType.ContentPublished, "Published", "Done",
            contentId: contentId);

        Assert.Equal(contentId, notification.ContentId);
    }
}
