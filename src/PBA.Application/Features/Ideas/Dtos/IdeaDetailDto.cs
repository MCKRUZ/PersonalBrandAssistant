using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Dtos;

public record IdeaDetailDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? Summary { get; init; }
    public string? ThumbnailUrl { get; init; }
    public IdeaStatus Status { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset DetectedAt { get; init; }
    public bool HasSavedDetails { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<IdeaConnectionDto>? AIConnections { get; init; }
    public SavedIdeaDetailDto? SavedDetails { get; init; }
    public IdeaSourceInfoDto? SourceInfo { get; init; }
}

public record SavedIdeaDetailDto
{
    public string? Notes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> SuggestedPlatforms { get; init; } = [];
    public string? SuggestedAngle { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}

public record IdeaSourceInfoDto
{
    public string Name { get; init; } = string.Empty;
    public IdeaSourceType Type { get; init; }
    public string? FeedUrl { get; init; }
}
