using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Services.Scrapers;

/// <summary>RSS source scraper. Reads the whole feed and ignores <c>since</c> (dedup handles repeats),
/// preserving the original RSS polling behavior.</summary>
public sealed class RssScraper(IRssFeedReader reader, ILogger<RssScraper> logger) : ISourceScraper
{
    // logger retained for consistency with other scrapers in this family; used on future error paths
    private readonly ILogger<RssScraper> _logger = logger;

    public async Task<IReadOnlyList<ScrapedItem>> FetchAsync(
        IdeaSource source, DateTimeOffset since, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source.FeedUrl))
            return [];

        var entries = await reader.ReadFeedAsync(source.FeedUrl, ct);
        return entries
            .Select(e => new ScrapedItem(e.Title, e.Description, e.Url, e.ThumbnailUrl, e.PublishedAt))
            .ToList();
    }
}
