using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace PersonalBrandAssistant.Application.Tests.Features.Notifications;

public class NotificationServiceTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<ILogger<NotificationService>> _logger = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _service = new NotificationService(_dbContext.Object, _logger.Object);
    }

    private void SetupUsers(params User[] users)
    {
        var mockDbSet = users.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Users).Returns(mockDbSet.Object);
    }

    private Mock<DbSet<Notification>> SetupNotifications(params Notification[] notifications)
    {
        var mockDbSet = notifications.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(x => x.Notifications).Returns(mockDbSet.Object);
        return mockDbSet;
    }

    [Fact]
    public async Task SendAsync_PersistsNotificationToDatabase()
    {
        var user = new User { DisplayName = "Test", Email = "test@test.com", TimeZoneId = "UTC" };
        SetupUsers(user);
        var notifications = new List<Notification>();
        var mockDbSet = new List<Notification>().AsQueryable().BuildMockDbSet();
        mockDbSet.Setup(x => x.Add(It.IsAny<Notification>()))
            .Callback<Notification>(notifications.Add);
        _dbContext.Setup(x => x.Notifications).Returns(mockDbSet.Object);

        await _service.SendAsync(NotificationType.ContentApproved, "Approved", "Content was approved", Guid.NewGuid());

        Assert.Single(notifications);
        Assert.Equal(NotificationType.ContentApproved, notifications[0].Type);
        Assert.Equal("Approved", notifications[0].Title);
        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_NoUser_DoesNotThrow()
    {
        SetupUsers(); // no users

        await _service.SendAsync(NotificationType.ContentApproved, "Test", "Test");

        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkReadAsync_SetsIsReadToTrue()
    {
        var user = new User { DisplayName = "Test", Email = "test@test.com", TimeZoneId = "UTC" };
        var notification = Notification.Create(user.Id, NotificationType.ContentApproved, "Test", "Test");
        var mockDbSet = notification.Id;
        var dbSet = SetupNotifications(notification);

        await _service.MarkReadAsync(notification.Id);

        Assert.True(notification.IsRead);
        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkReadAsync_NonexistentId_DoesNotThrow()
    {
        SetupNotifications();

        await _service.MarkReadAsync(Guid.NewGuid());

        _dbContext.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
