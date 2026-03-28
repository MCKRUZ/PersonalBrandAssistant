using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

public class SubstackServiceTests
{
    private readonly IOptions<SubstackOptions> _options;
    private readonly Mock<ILogger<SubstackService>> _logger = new();

    public SubstackServiceTests()
    {
        _options = Options.Create(new SubstackOptions
        {
            FeedUrl = "https://matthewkruczek.substack.com/feed"
        });
    }

    private SubstackService CreateSut(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://matthewkruczek.substack.com")
        };
        return new SubstackService(client, _options, _logger.Object);
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/xml")
            });
        return handler;
    }

    private static Mock<HttpMessageHandler> CreateThrowingHandler<TException>()
        where TException : Exception, new()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TException());
        return handler;
    }

    private static string BuildRssFeed(params (string title, string link, string pubDate, string description)[] items)
    {
        var itemsXml = string.Join("\n", items.Select(i => $@"
    <item>
      <title>{i.title}</title>
      <link>{i.link}</link>
      <pubDate>{i.pubDate}</pubDate>
      <description><![CDATA[{i.description}]]></description>
    </item>"));

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
  <channel>
    <title>Matt Kruczek's Newsletter</title>
    <link>https://matthewkruczek.substack.com</link>
    {itemsXml}
  </channel>
</rss>";
    }

    [Fact]
    public async Task GetRecentPostsAsync_ParsesValidRssFeed_IntoSubstackPostList()
    {
        var xml = BuildRssFeed(
            ("Post One", "https://matthewkruczek.substack.com/p/post-one",
             "Tue, 10 Mar 2026 12:00:00 GMT", "<p>Summary one</p>"),
            ("Post Two", "https://matthewkruczek.substack.com/p/post-two",
             "Wed, 11 Mar 2026 12:00:00 GMT", "<p>Summary two</p>"),
            ("Post Three", "https://matthewkruczek.substack.com/p/post-three",
             "Thu, 12 Mar 2026 12:00:00 GMT", "<p>Summary three</p>"));

        var sut = CreateSut(CreateMockHandler(xml).Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("Post Three", result.Value[0].Title);
        Assert.Equal("https://matthewkruczek.substack.com/p/post-three", result.Value[0].Url);
        Assert.Equal("Summary three", result.Value[0].Summary);
    }

    [Fact]
    public async Task GetRecentPostsAsync_RespectsLimitParameter()
    {
        var xml = BuildRssFeed(
            ("A", "https://matthewkruczek.substack.com/p/a", "Tue, 10 Mar 2026 12:00:00 GMT", "a"),
            ("B", "https://matthewkruczek.substack.com/p/b", "Wed, 11 Mar 2026 12:00:00 GMT", "b"),
            ("C", "https://matthewkruczek.substack.com/p/c", "Thu, 12 Mar 2026 12:00:00 GMT", "c"),
            ("D", "https://matthewkruczek.substack.com/p/d", "Fri, 13 Mar 2026 12:00:00 GMT", "d"),
            ("E", "https://matthewkruczek.substack.com/p/e", "Sat, 14 Mar 2026 12:00:00 GMT", "e"));

        var sut = CreateSut(CreateMockHandler(xml).Object);

        var result = await sut.GetRecentPostsAsync(2, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task GetRecentPostsAsync_ReturnsFailure_WhenHttpRequestExceptionThrown()
    {
        var sut = CreateSut(CreateThrowingHandler<HttpRequestException>().Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
    }

    [Fact]
    public async Task GetRecentPostsAsync_ReturnsFailure_WhenRssXmlIsMalformed()
    {
        var sut = CreateSut(CreateMockHandler("not xml at all").Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
    }

    [Fact]
    public async Task GetRecentPostsAsync_StripsHtmlFromSummary()
    {
        var xml = BuildRssFeed(
            ("HTML Post", "https://matthewkruczek.substack.com/p/html-post",
             "Tue, 10 Mar 2026 12:00:00 GMT",
             "<p>Hello <b>world</b></p>"));

        var sut = CreateSut(CreateMockHandler(xml).Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello world", result.Value![0].Summary);
    }

    [Fact]
    public async Task GetRecentPostsAsync_OrdersByPublishedAtDescending()
    {
        var xml = BuildRssFeed(
            ("Jan 1", "https://matthewkruczek.substack.com/p/jan1",
             "Thu, 01 Jan 2026 12:00:00 GMT", "first"),
            ("Jan 15", "https://matthewkruczek.substack.com/p/jan15",
             "Thu, 15 Jan 2026 12:00:00 GMT", "second"),
            ("Jan 10", "https://matthewkruczek.substack.com/p/jan10",
             "Sat, 10 Jan 2026 12:00:00 GMT", "third"));

        var sut = CreateSut(CreateMockHandler(xml).Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jan 15", result.Value![0].Title);
        Assert.Equal("Jan 10", result.Value[1].Title);
        Assert.Equal("Jan 1", result.Value[2].Title);
    }

    [Fact]
    public async Task GetRecentPostsAsync_ReturnsFailure_WhenFeedUrlIsNotSubstackDomain()
    {
        var options = Options.Create(new SubstackOptions
        {
            FeedUrl = "https://evil-site.com/feed"
        });
        var xml = BuildRssFeed(
            ("Post", "https://evil-site.com/p/post", "Mon, 10 Mar 2026 12:00:00 GMT", "content"));
        var handler = CreateMockHandler(xml);
        var client = new HttpClient(handler.Object);
        var sut = new SubstackService(client, options, _logger.Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task GetRecentPostsAsync_ReturnsFailure_WhenFeedUrlUsesHttpScheme()
    {
        var options = Options.Create(new SubstackOptions
        {
            FeedUrl = "http://matthewkruczek.substack.com/feed"
        });
        var handler = CreateMockHandler("<rss/>");
        var client = new HttpClient(handler.Object);
        var sut = new SubstackService(client, options, _logger.Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
    }

    [Fact]
    public async Task GetRecentPostsAsync_ThrowsOperationCanceledException_WhenCancelled()
    {
        var cts = new CancellationTokenSource();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage();
            });

        var sut = CreateSut(handler.Object);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.GetRecentPostsAsync(10, cts.Token));
    }

    [Fact]
    public async Task GetRecentPostsAsync_ReturnsEmptyList_WhenFeedHasNoItems()
    {
        var xml = BuildRssFeed();

        var sut = CreateSut(CreateMockHandler(xml).Object);

        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }
}
