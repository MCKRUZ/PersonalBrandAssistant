namespace PBA.Application.Common.Models;

using PBA.Domain.Enums;

public record PlatformPublishOutcome(
    Platform Platform,
    bool Success,
    string? Url,
    string? Error
);
