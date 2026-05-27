using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record PlatformPublishDto
{
    public Guid Id { get; init; }
    public Platform Platform { get; init; }
    public PublishStatus PublishStatus { get; init; }
    public string? PublishedUrl { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public int RetryCount { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
}
