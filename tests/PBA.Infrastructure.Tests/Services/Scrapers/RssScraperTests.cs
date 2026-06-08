using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Services.Scrapers;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Scrapers;

public class RssScraperTests
{
    private static IdeaSource Source() => new()
    { Name = "Blog", Type = IdeaSourceType.RSS, FeedUrl = "https://x/feed", Category = "Tech" };

    [Fact]
    public async Task FetchAsync_MapsFeedItemsToScrapedItems_AndIgnoresSince()
    {
        var reader = new Mock<IRssFeedReader>();
        reader.Setup(r => r.ReadFeedAsync("https://x/feed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RssFeedItem>
            {
                new("Title A", "Desc A", "https://x/a", "https://x/thumb", "Tech",
                    new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            });
        var scraper = new RssScraper(reader.Object, NullLogger<RssScraper>.Instance);

        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UtcNow.AddYears(10), CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Title A", item.Title);
        Assert.Equal("https://x/a", item.Url);
        Assert.Equal("Desc A", item.Description);
        Assert.Equal("https://x/thumb", item.ThumbnailUrl);
    }

    [Fact]
    public async Task FetchAsync_NoFeedUrl_ReturnsEmpty()
    {
        var reader = new Mock<IRssFeedReader>();
        var scraper = new RssScraper(reader.Object, NullLogger<RssScraper>.Instance);
        var items = await scraper.FetchAsync(new IdeaSource { Name = "x", Type = IdeaSourceType.RSS, Category = "" },
            DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(items);
        reader.Verify(r => r.ReadFeedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
