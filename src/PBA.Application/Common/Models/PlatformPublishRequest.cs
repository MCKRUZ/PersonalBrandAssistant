namespace PBA.Application.Common.Models;

using PBA.Domain.Entities;
using PBA.Domain.Enums;

public record PlatformPublishRequest(
    Content Content,
    string TransformedContent,
    IReadOnlyList<string> Tags,
    string? CanonicalUrl,
    PublishMode Mode,
    DateTimeOffset? ScheduledAt
);
