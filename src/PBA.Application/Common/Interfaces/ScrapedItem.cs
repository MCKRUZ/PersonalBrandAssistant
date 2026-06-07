namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Source-neutral item produced by an <see cref="ISourceScraper"/> before it becomes an Idea.
/// Url is the canonical link used both for display and deduplication.
/// </summary>
public record ScrapedItem(
    string Title,
    string? Description,
    string? Url,
    string? ThumbnailUrl,
    DateTimeOffset PublishedAt);
