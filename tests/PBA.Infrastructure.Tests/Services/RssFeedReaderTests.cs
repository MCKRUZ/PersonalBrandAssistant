using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class RssFeedReaderTests
{
    private const string FeedWithBackdatedItem = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>TLDR Tech Feed</title>
            <item>
              <title>Today's Story</title>
              <link>https://example.com/todays-story</link>
              <description>A fresh story stamped at midnight.</description>
              <pubDate>Wed, 03 Jun 2026 00:00:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;

    private static RssFeedReader CreateReader(string xml) =>
        new(new HttpClient(new StubHandler(xml)), NullLogger<RssFeedReader>.Instance);

    [Fact]
    public async Task ReadFeedAsync_ReturnsItemsPublishedBeforeNow()
    {
        // Regression: TLDR (and the bullrich.dev proxy) stamps every story in a daily
        // issue with pubDate = 00:00:00 of the issue day. The old `published <= since`
        // filter, fed by LastPolledAt (always "earlier today"), dropped every such item,
        // so no TLDR/Medium ideas were ingested after the first poll. Dedup — not a
        // poll-time watermark — is what prevents re-adds, so the reader must return the
        // item regardless of when we last polled.
        var reader = CreateReader(FeedWithBackdatedItem);

        var items = await reader.ReadFeedAsync("https://example.com/feed");

        Assert.Contains(items, i => i.Url == "https://example.com/todays-story");
    }

    private sealed class StubHandler(string xml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml"),
            });
    }
}
