using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class ContentPlatformStatus : AuditableEntityBase
{
    public Guid ContentId { get; set; }
    public PlatformType Platform { get; set; }
    public PlatformPublishStatus Status { get; set; } = PlatformPublishStatus.Pending;
    public string? PlatformPostId { get; set; }
    public string? PostUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public string? IdempotencyKey { get; init; }
    public int RetryCount { get; set; } = 0;
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public uint Version { get; set; }
}
