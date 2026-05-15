namespace PBA.Application.Features.Feed.Dtos;

public record TrendingTopicDto
{
    public string Topic { get; init; } = string.Empty;
    public int Count { get; init; }
    public DateTimeOffset LatestAt { get; init; }
}
