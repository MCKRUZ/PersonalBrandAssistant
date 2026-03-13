using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface INotificationService
{
    Task SendAsync(NotificationType type, string title, string message, Guid? contentId = null, CancellationToken ct = default);
    Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}
