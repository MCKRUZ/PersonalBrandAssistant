using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Services;

public class RssFeedReader : IRssFeedReader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RssFeedReader> _logger;

    public RssFeedReader(HttpClient httpClient, ILogger<RssFeedReader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<RssFeedItem>> ReadFeedAsync(
        string feedUrl, DateTimeOffset? since, CancellationToken ct = default)
    {
        using var stream = await _httpClient.GetStreamAsync(feedUrl, ct);
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
        var feed = SyndicationFeed.Load(xmlReader);

        var items = new List<RssFeedItem>();

        foreach (var item in feed.Items)
        {
            // Normalize to UTC: feed pubDates can carry a non-UTC offset (e.g. -04:00),
            // which Npgsql rejects for 'timestamp with time zone' and fails the whole save.
            var published = (item.PublishDate != default
                ? item.PublishDate
                : item.LastUpdatedTime != default
                    ? item.LastUpdatedTime
                    : DateTimeOffset.UtcNow).ToUniversalTime();

            if (since.HasValue && published <= since.Value)
                continue;

            var url = item.Links.FirstOrDefault()?.Uri?.AbsoluteUri;
            var description = item.Summary?.Text;
            var thumbnail = GetThumbnailUrl(item);
            var category = item.Categories.FirstOrDefault()?.Name;

            items.Add(new RssFeedItem(
                item.Title?.Text ?? "",
                description,
                url,
                thumbnail,
                category,
                published));
        }

        _logger.LogDebug("Read {Count} items from {FeedUrl} (since {Since})",
            items.Count, feedUrl, since);

        return items;
    }

    private static string? GetThumbnailUrl(SyndicationItem item)
    {
        var imageLink = item.Links
            .FirstOrDefault(l => l.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);

        return imageLink?.Uri?.AbsoluteUri;
    }
}
