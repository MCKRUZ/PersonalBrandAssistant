diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index fe107bc..4b70d0c 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -234,6 +234,16 @@ public static class DependencyInjection
         services.AddHostedService<EngagementScheduler>();
         services.AddHostedService<InboxPoller>();
 
+        // Substack RSS
+        services.Configure<SubstackOptions>(
+            configuration.GetSection(SubstackOptions.SectionName));
+        services.AddHttpClient<ISubstackService, SubstackService>(client =>
+        {
+            client.Timeout = TimeSpan.FromSeconds(10);
+            client.DefaultRequestHeaders.UserAgent.ParseAdd(
+                "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
+        });
+
         // Google Analytics / Search Console
         services.Configure<GoogleAnalyticsOptions>(
             configuration.GetSection(GoogleAnalyticsOptions.SectionName));
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackService.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackService.cs
new file mode 100644
index 0000000..f4dc7c5
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SubstackService.cs
@@ -0,0 +1,117 @@
+using System.Net;
+using System.ServiceModel.Syndication;
+using System.Text.RegularExpressions;
+using System.Xml;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+internal sealed partial class SubstackService(
+    HttpClient httpClient,
+    IOptions<SubstackOptions> options,
+    ILogger<SubstackService> logger) : ISubstackService
+{
+    private readonly SubstackOptions _options = options.Value;
+
+    public async Task<Result<IReadOnlyList<SubstackPost>>> GetRecentPostsAsync(
+        int limit, CancellationToken ct)
+    {
+        // SSRF protection: validate the URL host
+        if (!IsValidSubstackHost(_options.FeedUrl))
+        {
+            logger.LogWarning(
+                "Substack feed URL validation failed: {FeedUrl} is not a substack.com domain",
+                _options.FeedUrl);
+            return Result<IReadOnlyList<SubstackPost>>.Failure(
+                ErrorCode.ValidationFailed,
+                "Substack feed URL must be a substack.com domain");
+        }
+
+        try
+        {
+            using var response = await httpClient.GetAsync(_options.FeedUrl, ct);
+            response.EnsureSuccessStatusCode();
+
+            await using var stream = await response.Content.ReadAsStreamAsync(ct);
+            using var reader = XmlReader.Create(stream, new XmlReaderSettings
+            {
+                Async = true,
+                DtdProcessing = DtdProcessing.Ignore
+            });
+
+            var feed = SyndicationFeed.Load(reader);
+            if (feed is null)
+            {
+                logger.LogWarning("Failed to parse Substack RSS feed from {FeedUrl}", _options.FeedUrl);
+                return Result<IReadOnlyList<SubstackPost>>.Failure(
+                    ErrorCode.InternalError,
+                    "Failed to parse Substack RSS feed");
+            }
+
+            var posts = feed.Items
+                .Select(item => new SubstackPost(
+                    Title: item.Title?.Text ?? "",
+                    Url: item.Links.FirstOrDefault()?.Uri?.AbsoluteUri ?? "",
+                    PublishedAt: item.PublishDate != default
+                        ? item.PublishDate
+                        : item.LastUpdatedTime != default
+                            ? item.LastUpdatedTime
+                            : DateTimeOffset.UtcNow,
+                    Summary: StripHtml(item.Summary?.Text)))
+                .OrderByDescending(p => p.PublishedAt)
+                .Take(limit)
+                .ToList();
+
+            return Result<IReadOnlyList<SubstackPost>>.Success(posts);
+        }
+        catch (HttpRequestException ex)
+        {
+            logger.LogError(ex, "Failed to fetch Substack RSS feed from {FeedUrl}", _options.FeedUrl);
+            return Result<IReadOnlyList<SubstackPost>>.Failure(
+                ErrorCode.InternalError,
+                $"Failed to fetch Substack RSS feed: {ex.Message}");
+        }
+        catch (XmlException ex)
+        {
+            logger.LogError(ex, "Failed to parse Substack RSS feed from {FeedUrl}", _options.FeedUrl);
+            return Result<IReadOnlyList<SubstackPost>>.Failure(
+                ErrorCode.InternalError,
+                $"Failed to parse Substack RSS feed: {ex.Message}");
+        }
+        catch (Exception ex)
+        {
+            logger.LogError(ex, "Unexpected error fetching Substack RSS feed from {FeedUrl}", _options.FeedUrl);
+            return Result<IReadOnlyList<SubstackPost>>.Failure(
+                ErrorCode.InternalError,
+                $"Unexpected error: {ex.Message}");
+        }
+    }
+
+    private static bool IsValidSubstackHost(string feedUrl)
+    {
+        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
+            return false;
+
+        return uri.Host.EndsWith(".substack.com", StringComparison.OrdinalIgnoreCase)
+            || uri.Host.Equals("substack.com", StringComparison.OrdinalIgnoreCase);
+    }
+
+    private static string? StripHtml(string? html)
+    {
+        if (html is null) return null;
+        var text = HtmlTagRegex().Replace(html, " ");
+        text = WebUtility.HtmlDecode(text);
+        text = CollapseWhitespaceRegex().Replace(text, " ").Trim();
+        return text.Length == 0 ? null : text;
+    }
+
+    [GeneratedRegex(@"<[^>]+>")]
+    private static partial Regex HtmlTagRegex();
+
+    [GeneratedRegex(@"\s{2,}")]
+    private static partial Regex CollapseWhitespaceRegex();
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/SubstackServiceTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/SubstackServiceTests.cs
new file mode 100644
index 0000000..85d6727
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/SubstackServiceTests.cs
@@ -0,0 +1,202 @@
+using System.Net;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;
+
+public class SubstackServiceTests
+{
+    private readonly IOptions<SubstackOptions> _options;
+    private readonly Mock<ILogger<SubstackService>> _logger = new();
+
+    public SubstackServiceTests()
+    {
+        _options = Options.Create(new SubstackOptions
+        {
+            FeedUrl = "https://matthewkruczek.substack.com/feed"
+        });
+    }
+
+    private SubstackService CreateSut(HttpMessageHandler handler)
+    {
+        var client = new HttpClient(handler)
+        {
+            BaseAddress = new Uri("https://matthewkruczek.substack.com")
+        };
+        return new SubstackService(client, _options, _logger.Object);
+    }
+
+    private static Mock<HttpMessageHandler> CreateMockHandler(
+        string content,
+        HttpStatusCode statusCode = HttpStatusCode.OK)
+    {
+        var handler = new Mock<HttpMessageHandler>();
+        handler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage
+            {
+                StatusCode = statusCode,
+                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/xml")
+            });
+        return handler;
+    }
+
+    private static Mock<HttpMessageHandler> CreateThrowingHandler<TException>()
+        where TException : Exception, new()
+    {
+        var handler = new Mock<HttpMessageHandler>();
+        handler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ThrowsAsync(new TException());
+        return handler;
+    }
+
+    private static string BuildRssFeed(params (string title, string link, string pubDate, string description)[] items)
+    {
+        var itemsXml = string.Join("\n", items.Select(i => $@"
+    <item>
+      <title>{i.title}</title>
+      <link>{i.link}</link>
+      <pubDate>{i.pubDate}</pubDate>
+      <description><![CDATA[{i.description}]]></description>
+    </item>"));
+
+        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
+<rss version=""2.0"">
+  <channel>
+    <title>Matt Kruczek's Newsletter</title>
+    <link>https://matthewkruczek.substack.com</link>
+    {itemsXml}
+  </channel>
+</rss>";
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_ParsesValidRssFeed_IntoSubstackPostList()
+    {
+        var xml = BuildRssFeed(
+            ("Post One", "https://matthewkruczek.substack.com/p/post-one",
+             "Tue, 10 Mar 2026 12:00:00 GMT", "<p>Summary one</p>"),
+            ("Post Two", "https://matthewkruczek.substack.com/p/post-two",
+             "Wed, 11 Mar 2026 12:00:00 GMT", "<p>Summary two</p>"),
+            ("Post Three", "https://matthewkruczek.substack.com/p/post-three",
+             "Thu, 12 Mar 2026 12:00:00 GMT", "<p>Summary three</p>"));
+
+        var sut = CreateSut(CreateMockHandler(xml).Object);
+
+        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(3, result.Value!.Count);
+        Assert.Equal("Post Three", result.Value[0].Title);
+        Assert.Equal("https://matthewkruczek.substack.com/p/post-three", result.Value[0].Url);
+        Assert.Equal("Summary three", result.Value[0].Summary);
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_RespectsLimitParameter()
+    {
+        var xml = BuildRssFeed(
+            ("A", "https://matthewkruczek.substack.com/p/a", "Tue, 10 Mar 2026 12:00:00 GMT", "a"),
+            ("B", "https://matthewkruczek.substack.com/p/b", "Wed, 11 Mar 2026 12:00:00 GMT", "b"),
+            ("C", "https://matthewkruczek.substack.com/p/c", "Thu, 12 Mar 2026 12:00:00 GMT", "c"),
+            ("D", "https://matthewkruczek.substack.com/p/d", "Fri, 13 Mar 2026 12:00:00 GMT", "d"),
+            ("E", "https://matthewkruczek.substack.com/p/e", "Sat, 14 Mar 2026 12:00:00 GMT", "e"));
+
+        var sut = CreateSut(CreateMockHandler(xml).Object);
+
+        var result = await sut.GetRecentPostsAsync(2, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, result.Value!.Count);
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_ReturnsFailure_WhenHttpRequestExceptionThrown()
+    {
+        var sut = CreateSut(CreateThrowingHandler<HttpRequestException>().Object);
+
+        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_ReturnsFailure_WhenRssXmlIsMalformed()
+    {
+        var sut = CreateSut(CreateMockHandler("not xml at all").Object);
+
+        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.InternalError, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_StripsHtmlFromSummary()
+    {
+        var xml = BuildRssFeed(
+            ("HTML Post", "https://matthewkruczek.substack.com/p/html-post",
+             "Tue, 10 Mar 2026 12:00:00 GMT",
+             "<p>Hello <b>world</b></p>"));
+
+        var sut = CreateSut(CreateMockHandler(xml).Object);
+
+        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("Hello world", result.Value![0].Summary);
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_OrdersByPublishedAtDescending()
+    {
+        var xml = BuildRssFeed(
+            ("Jan 1", "https://matthewkruczek.substack.com/p/jan1",
+             "Thu, 01 Jan 2026 12:00:00 GMT", "first"),
+            ("Jan 15", "https://matthewkruczek.substack.com/p/jan15",
+             "Thu, 15 Jan 2026 12:00:00 GMT", "second"),
+            ("Jan 10", "https://matthewkruczek.substack.com/p/jan10",
+             "Sat, 10 Jan 2026 12:00:00 GMT", "third"));
+
+        var sut = CreateSut(CreateMockHandler(xml).Object);
+
+        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("Jan 15", result.Value![0].Title);
+        Assert.Equal("Jan 10", result.Value[1].Title);
+        Assert.Equal("Jan 1", result.Value[2].Title);
+    }
+
+    [Fact]
+    public async Task GetRecentPostsAsync_ReturnsFailure_WhenFeedUrlIsNotSubstackDomain()
+    {
+        var options = Options.Create(new SubstackOptions
+        {
+            FeedUrl = "https://evil-site.com/feed"
+        });
+        var xml = BuildRssFeed(
+            ("Post", "https://evil-site.com/p/post", "Mon, 10 Mar 2026 12:00:00 GMT", "content"));
+        var handler = CreateMockHandler(xml);
+        var client = new HttpClient(handler.Object);
+        var sut = new SubstackService(client, options, _logger.Object);
+
+        var result = await sut.GetRecentPostsAsync(10, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+}
