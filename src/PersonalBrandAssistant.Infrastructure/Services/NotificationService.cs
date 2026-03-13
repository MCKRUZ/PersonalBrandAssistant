using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IApplicationDbContext dbContext,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SendAsync(
        NotificationType type, string title, string message,
        Guid? contentId = null, CancellationToken ct = default)
    {
        // For single-user system, get the first user
        var user = await _dbContext.Users.FirstOrDefaultAsync(ct);
        if (user is null)
        {
            _logger.LogWarning("No user found for notification dispatch");
            return;
        }

        var notification = Notification.Create(user.Id, type, title, message, contentId);
        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Notification persisted: {Type} - {Title}", type, title);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct);

        if (notification is null) return;

        notification.MarkAsRead();
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        await _dbContext.Notifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }
}
