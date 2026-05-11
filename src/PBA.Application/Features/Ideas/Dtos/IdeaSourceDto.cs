using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Dtos;

public record IdeaSourceDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IdeaSourceType Type { get; init; }
    public string? FeedUrl { get; init; }
    public string? ApiUrl { get; init; }
    public string Category { get; init; } = string.Empty;
    public int PollIntervalMinutes { get; init; }
    public bool IsEnabled { get; init; }
    public DateTimeOffset? LastPolledAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int IdeaCount { get; init; }
    public bool IsHealthy { get; init; }
}
