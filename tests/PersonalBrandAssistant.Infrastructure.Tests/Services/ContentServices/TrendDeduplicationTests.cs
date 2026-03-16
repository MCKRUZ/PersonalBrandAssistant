using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

public class TrendDeduplicationTests
{
    [Fact]
    public void Deduplicate_SameUrl_DifferentSources_MergesIntoSingleItem()
    {
        var items = new[]
        {
            CreateItem("Article Title", "https://example.com/post", TrendSourceType.Reddit,
                DateTimeOffset.UtcNow),
            CreateItem("Article Title", "https://example.com/post", TrendSourceType.HackerNews,
                DateTimeOffset.UtcNow.AddMinutes(-5)),
        };

        var result = TrendDeduplicator.Deduplicate(items, 0.85f);

        Assert.Single(result);
        Assert.Equal(TrendSourceType.HackerNews, result[0].SourceType); // earliest DetectedAt
    }

    [Fact]
    public void Deduplicate_UrlCanonicalization_RemovesTrailingSlashAndQueryParams()
    {
        var items = new[]
        {
            CreateItem("Post A", "https://example.com/post?utm_source=x", TrendSourceType.Reddit,
                DateTimeOffset.UtcNow),
            CreateItem("Post B", "https://example.com/post/", TrendSourceType.FreshRSS,
                DateTimeOffset.UtcNow.AddMinutes(-1)),
        };

        var result = TrendDeduplicator.Deduplicate(items, 0.85f);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplicate_FuzzyTitleMatch_AboveThreshold_MergesItems()
    {
        // Items without URLs, titles above threshold
        var items = new[]
        {
            CreateItem("Building AI Agents with .NET 10", null, TrendSourceType.TrendRadar,
                DateTimeOffset.UtcNow),
            CreateItem("Building AI Agents with .NET 10 Preview", null, TrendSourceType.HackerNews,
                DateTimeOffset.UtcNow),
        };

        // Jaccard: intersection={"building","ai","agents","with",".net","10"} (6)
        //          union={"building","ai","agents","with",".net","10","preview"} (7)
        //          similarity = 6/7 = 0.857 > 0.85
        var result = TrendDeduplicator.Deduplicate(items, 0.85f);

        Assert.Single(result);
    }

    [Fact]
    public void Deduplicate_FuzzyTitleMatch_BelowThreshold_KeepsBothItems()
    {
        var items = new[]
        {
            CreateItem("Kubernetes Best Practices for Production", null, TrendSourceType.Reddit,
                DateTimeOffset.UtcNow),
            CreateItem("Getting Started with Docker Compose", null, TrendSourceType.HackerNews,
                DateTimeOffset.UtcNow),
        };

        var result = TrendDeduplicator.Deduplicate(items, 0.85f);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DeduplicationKey_IsDeterministic_ForSameUrl()
    {
        var url = "https://example.com/article?utm_source=twitter&id=42";
        var key1 = TrendDeduplicator.ComputeDeduplicationKey(TrendDeduplicator.CanonicalizeUrl(url));
        var key2 = TrendDeduplicator.ComputeDeduplicationKey(TrendDeduplicator.CanonicalizeUrl(url));

        Assert.Equal(key1, key2);
        Assert.NotEmpty(key1);
    }

    [Fact]
    public void CanonicalizeUrl_RemovesAllTrackingParams()
    {
        var url = "https://example.com/post?utm_source=x&utm_medium=y&ref=abc&id=42";
        var canonical = TrendDeduplicator.CanonicalizeUrl(url);

        Assert.DoesNotContain("utm_source", canonical);
        Assert.DoesNotContain("utm_medium", canonical);
        Assert.DoesNotContain("ref=", canonical);
        Assert.Contains("id=42", canonical);
    }

    [Fact]
    public void ComputeTitleSimilarity_IdenticalTitles_ReturnsOne()
    {
        var similarity = TrendDeduplicator.ComputeTitleSimilarity("Hello World", "Hello World");
        Assert.Equal(1f, similarity);
    }

    [Fact]
    public void ComputeTitleSimilarity_EmptyTitle_ReturnsZero()
    {
        var similarity = TrendDeduplicator.ComputeTitleSimilarity("Hello", "");
        Assert.Equal(0f, similarity);
    }

    private static TrendItem CreateItem(string title, string? url, TrendSourceType sourceType,
        DateTimeOffset detectedAt) => new()
    {
        Title = title,
        Url = url,
        SourceType = sourceType,
        SourceName = sourceType.ToString(),
        DetectedAt = detectedAt,
    };
}
