namespace PBA.Application.Common.Interfaces;

public interface IFreshRssClient
{
    Task<IReadOnlyList<RssEntry>> GetEntriesAsync(
        DateTimeOffset? newerThan,
        int count = 200,
        CancellationToken ct = default);
}

public record RssEntry(
    string Title,
    string? Description,
    string? Url,
    string FeedTitle,
    string? ThumbnailUrl,
    string? Category,
    DateTimeOffset PublishedAt,
    string EntryId);
