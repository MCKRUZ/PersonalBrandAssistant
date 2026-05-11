using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Dtos;

public record IdeaSourceRequest
{
    public string Name { get; init; } = string.Empty;
    public IdeaSourceType Type { get; init; }
    public string? FeedUrl { get; init; }
    public string? ApiUrl { get; init; }
    public string Category { get; init; } = string.Empty;
    public int PollIntervalMinutes { get; init; } = 30;
    public bool? IsEnabled { get; init; }
}
