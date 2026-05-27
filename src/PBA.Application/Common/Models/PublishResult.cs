namespace PBA.Application.Common.Models;

public record PublishResult(
    bool PrimarySuccess,
    string? PrimaryUrl,
    IReadOnlyList<PlatformPublishOutcome> SecondaryOutcomes
);
