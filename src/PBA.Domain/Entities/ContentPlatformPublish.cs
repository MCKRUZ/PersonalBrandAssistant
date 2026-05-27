namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class ContentPlatformPublish
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ContentId { get; set; }
    public Platform Platform { get; set; }
    public PublishStatus Status { get; set; }
    public string? PublishedUrl { get; set; }
    public string? PlatformPostId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int Shares { get; set; }
    public int Views { get; set; }
    public DateTimeOffset? MetricsRefreshedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }

    public Content? Content { get; set; }
}
