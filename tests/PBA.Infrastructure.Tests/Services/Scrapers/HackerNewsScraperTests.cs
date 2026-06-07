using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Scrapers;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Scrapers;

public class HackerNewsScraperTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();

    private HackerNewsScraper Build(HackerNewsOptions opts)
    {
        var http = new HttpClient(_handler.Object);
        return new HackerNewsScraper(http, Options.Create(opts), NullLogger<HackerNewsScraper>.Instance);
    }

    private void Route(Func<string, string?> responder)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                var body = responder(req.RequestUri!.ToString());
                return body is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            });
    }

    private static IdeaSource Source() => new() { Name = "HN", Type = IdeaSourceType.HackerNews, Category = "Tech" };

    [Fact]
    public async Task FetchAsync_ReturnsStoriesAtOrAboveMinScore_WithCommentsFolded()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Route(url =>
        {
            if (url.Contains("topstories")) return "[1, 2]";
            if (url.EndsWith("/item/1.json"))
                return $"{{\"id\":1,\"type\":\"story\",\"title\":\"Big AI\",\"url\":\"https://ex/ai\",\"score\":250,\"time\":{now},\"kids\":[11]}}";
            if (url.EndsWith("/item/2.json"))
                return $"{{\"id\":2,\"type\":\"story\",\"title\":\"Low\",\"url\":\"https://ex/low\",\"score\":10,\"time\":{now}}}";
            if (url.EndsWith("/item/11.json"))
                return "{\"id\":11,\"type\":\"comment\",\"text\":\"great insight\",\"by\":\"alice\"}";
            return null;
        });
        var scraper = Build(new HackerNewsOptions { MinScore = 100, FetchTopStories = 30, TopComments = 5 });

        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UnixEpoch, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Big AI", item.Title);
        Assert.Equal("https://ex/ai", item.Url);
        Assert.Contains("great insight", item.Description);
    }

    [Fact]
    public async Task FetchAsync_FiltersStoriesOlderThanSince()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();
        Route(url =>
        {
            if (url.Contains("topstories")) return "[1]";
            if (url.EndsWith("/item/1.json"))
                return $"{{\"id\":1,\"type\":\"story\",\"title\":\"Old\",\"url\":\"https://ex/o\",\"score\":500,\"time\":{old}}}";
            return null;
        });
        var scraper = Build(new HackerNewsOptions { MinScore = 100 });
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);
        Assert.Empty(items);
    }

    [Fact]
    public async Task FetchAsync_SelfPost_UsesHnDiscussionUrl()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Route(url =>
        {
            if (url.Contains("topstories")) return "[5]";
            if (url.EndsWith("/item/5.json"))
                return $"{{\"id\":5,\"type\":\"story\",\"title\":\"Ask HN\",\"score\":150,\"time\":{now},\"text\":\"question?\"}}";
            return null;
        });
        var scraper = Build(new HackerNewsOptions { MinScore = 100 });
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal("https://news.ycombinator.com/item?id=5", Assert.Single(items).Url);
    }

    [Fact]
    public async Task FetchAsync_HttpError_ReturnsEmpty()
    {
        _handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));
        var scraper = Build(new HackerNewsOptions());
        var items = await scraper.FetchAsync(Source(), DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Empty(items);
    }
}
