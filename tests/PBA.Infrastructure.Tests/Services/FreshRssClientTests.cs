using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class FreshRssClientTests
{
    private readonly FreshRssOptions _options = new()
    {
        BaseUrl = "http://freshrss.local",
        Username = "admin",
        ApiPassword = "secret",
        BatchSize = 200,
    };

    private FreshRssClient CreateClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var optionsMonitor = Mock.Of<IOptionsMonitor<FreshRssOptions>>(
            o => o.CurrentValue == _options);
        return new FreshRssClient(httpClient, optionsMonitor, NullLogger<FreshRssClient>.Instance);
    }

    [Fact]
    public async Task AuthenticateAsync_CachesToken()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("*/accounts/ClientLogin", "SID=unused\nLSID=unused\nAuth=test-token-123");
        handler.AddResponse("*/subscription/list*", """{"subscriptions":[]}""");

        var client = CreateClient(handler);
        var token = await client.AuthenticateAsync();

        Assert.Equal("test-token-123", token);

        await client.GetSubscriptionsAsync();

        var loginRequests = handler.Requests.Count(r =>
            r.RequestUri!.PathAndQuery.Contains("ClientLogin"));
        Assert.Equal(1, loginRequests);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsFeedList()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("*/accounts/ClientLogin", "Auth=token");
        handler.AddResponse("*/subscription/list*", """
        {
            "subscriptions": [
                {"id":"feed/1","title":"Tech Blog","url":"https://techblog.com/rss","categories":[{"label":"Tech"}]},
                {"id":"feed/2","title":"Science Daily","url":"https://science.com/rss","categories":[]}
            ]
        }
        """);

        var client = CreateClient(handler);
        var feeds = await client.GetSubscriptionsAsync();

        Assert.Equal(2, feeds.Count);
        Assert.Equal("Tech Blog", feeds[0].Title);
        Assert.Equal("https://techblog.com/rss", feeds[0].Url);
        Assert.Equal("Tech", feeds[0].Category);
        Assert.Null(feeds[1].Category);
    }

    [Fact]
    public async Task GetEntriesAsync_ReturnsEntriesNewerThanTimestamp()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("*/accounts/ClientLogin", "Auth=token");
        handler.AddResponse("*/stream/contents*", """
        {
            "items": [
                {
                    "id": "123",
                    "title": "Article 1",
                    "summary": {"content": "Content 1"},
                    "canonical": [{"href": "https://example.com/1"}],
                    "origin": {"title": "Blog"},
                    "categories": ["tech"],
                    "published": 1700000000
                },
                {
                    "id": "456",
                    "title": "Article 2",
                    "summary": {"content": "Content 2"},
                    "canonical": [{"href": "https://example.com/2"}],
                    "origin": {"title": "Blog"},
                    "categories": [],
                    "published": 1700001000
                }
            ]
        }
        """);

        var client = CreateClient(handler);
        var entries = await client.GetEntriesAsync(newerThan: DateTimeOffset.FromUnixTimeSeconds(1699999000));

        Assert.Equal(2, entries.Count);
        Assert.Equal("Article 1", entries[0].Title);
        Assert.Equal("https://example.com/1", entries[0].Url);
        Assert.Equal("Blog", entries[0].FeedTitle);

        var streamRequest = handler.Requests.First(r => r.RequestUri!.PathAndQuery.Contains("stream/contents"));
        Assert.Contains("ot=1699999000", streamRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task GetEntriesAsync_RespectsBatchSize()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("*/accounts/ClientLogin", "Auth=token");
        handler.AddResponse("*/stream/contents*", """{"items":[]}""");

        var client = CreateClient(handler);
        await client.GetEntriesAsync();

        var streamRequest = handler.Requests.First(r => r.RequestUri!.PathAndQuery.Contains("stream/contents"));
        Assert.Contains("n=200", streamRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task GetEntriesAsync_FollowsContinuationToken()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("*/accounts/ClientLogin", "Auth=token");
        handler.AddSequentialResponses("*/stream/contents*",
            """{"items":[{"id":"1","title":"A","summary":{"content":""},"canonical":[{"href":"u1"}],"origin":{"title":"F"},"published":1000}],"continuation":"abc"}""",
            """{"items":[{"id":"2","title":"B","summary":{"content":""},"canonical":[{"href":"u2"}],"origin":{"title":"F"},"published":2000}]}""");

        var client = CreateClient(handler);
        var entries = await client.GetEntriesAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal("A", entries[0].Title);
        Assert.Equal("B", entries[1].Title);

        var streamRequests = handler.Requests
            .Where(r => r.RequestUri!.PathAndQuery.Contains("stream/contents"))
            .ToList();
        Assert.Equal(2, streamRequests.Count);
        Assert.Contains("c=abc", streamRequests[1].RequestUri!.Query);
    }

    [Theory]
    [InlineData("tag:google.com,2005:reader/item/00000000075bcd15", "123456789")]
    [InlineData("tag:google.com,2005:reader/item/123456789", "123456789")]
    [InlineData("123456789", "123456789")]
    public void NormalizeItemId_ConvertsFormats(string input, string expected)
    {
        Assert.Equal(expected, FreshRssClient.NormalizeItemId(input));
    }

    [Fact]
    public async Task SendWithReauthAsync_ReauthenticatesOn401()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("*/accounts/ClientLogin", "Auth=new-token");
        handler.AddSequentialResponses("*/subscription/list*",
            (HttpStatusCode.Unauthorized, ""),
            (HttpStatusCode.OK, """{"subscriptions":[]}"""));

        var client = CreateClient(handler);
        await client.AuthenticateAsync();
        var feeds = await client.GetSubscriptionsAsync();

        Assert.Empty(feeds);
        var loginCount = handler.Requests.Count(r =>
            r.RequestUri!.PathAndQuery.Contains("ClientLogin"));
        Assert.Equal(2, loginCount);
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    private readonly List<(Func<string, bool> Match, Queue<(HttpStatusCode Status, string Body)> Responses)> _rules = [];

    public void AddResponse(string pattern, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var matcher = CreateMatcher(pattern);
        _rules.Add((matcher, new Queue<(HttpStatusCode, string)>([(status, body)])));
    }

    public void AddSequentialResponses(string pattern, params string[] bodies)
    {
        var matcher = CreateMatcher(pattern);
        var queue = new Queue<(HttpStatusCode, string)>(bodies.Select(b => (HttpStatusCode.OK, b)));
        _rules.Add((matcher, queue));
    }

    public void AddSequentialResponses(string pattern, params (HttpStatusCode Status, string Body)[] responses)
    {
        var matcher = CreateMatcher(pattern);
        _rules.Add((matcher, new Queue<(HttpStatusCode, string)>(responses)));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var url = request.RequestUri!.ToString();

        foreach (var (match, responses) in _rules)
        {
            if (match(url) && responses.Count > 0)
            {
                var (status, body) = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Func<string, bool> CreateMatcher(string pattern)
    {
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
            return url => url.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*'))
            return url => url.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*'))
            return url => url.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return url => url.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
