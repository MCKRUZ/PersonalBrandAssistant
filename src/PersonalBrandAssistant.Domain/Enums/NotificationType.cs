namespace PersonalBrandAssistant.Domain.Enums;

public enum NotificationType
{
    ContentReadyForReview,
    ContentApproved,
    ContentRejected,
    ContentPublished,
    ContentFailed,
    PlatformDisconnected,
    PlatformTokenExpiring,
    PlatformScopeMismatch
}
