namespace PBA.Application.Common.Models;

public record PlatformPublishResult(
    bool Success,
    string? PublishedUrl,
    string? PlatformPostId,
    string? ErrorMessage
);
