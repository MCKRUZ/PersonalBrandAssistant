using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class Notification : EntityBase
{
    private Notification() { }

    public Guid UserId { get; private init; }
    public NotificationType Type { get; private init; }
    public string Title { get; private init; } = string.Empty;
    public string Message { get; private init; } = string.Empty;
    public Guid? ContentId { get; private init; }
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    public void MarkAsRead() => IsRead = true;

    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        Guid? contentId = null) =>
        new()
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ContentId = contentId,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
