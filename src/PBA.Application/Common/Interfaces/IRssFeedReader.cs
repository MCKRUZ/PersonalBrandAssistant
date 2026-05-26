namespace PBA.Application.Common.Interfaces;

public interface IRssFeedReader
{
    Task<List<RssFeedItem>> ReadFeedAsync(string feedUrl, DateTimeOffset? since, CancellationToken ct = default);
}
