namespace PBA.Application.Common.Interfaces;

public record RssFeedItem(
    string Title,
    string? Description,
    string? Url,
    string? ThumbnailUrl,
    string? Category,
    DateTimeOffset PublishedAt);
