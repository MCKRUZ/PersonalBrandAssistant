namespace PersonalBrandAssistant.Application.Common.Models;

public record SubstackRssEntry(
    string Guid,
    string Title,
    string Link,
    DateTimeOffset PublishedAt,
    string? ContentEncoded,
    string ContentHash);

public record FeedFetchResult(
    bool NotModified,
    string? ETag,
    DateTimeOffset? LastModified,
    IReadOnlyList<SubstackRssEntry> Entries);
