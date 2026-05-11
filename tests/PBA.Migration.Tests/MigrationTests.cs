using PBA.Migration;
using Xunit;

namespace PBA.Migration.Tests;

public class MigrationTests
{
    [Fact]
    public void GenerateDeduplicationKey_NormalizesUrlAndHashesSha256()
    {
        var key = DataMigrator.GenerateDeduplicationKey(
            "https://example.com/article?utm_source=rss", "Title");

        var expectedKey = DataMigrator.GenerateDeduplicationKey(
            "https://example.com/article", "Title");

        Assert.Equal(expectedKey, key);
        Assert.Equal(64, key.Length);
    }

    [Fact]
    public void GenerateDeduplicationKey_FallsBackToTitle_WhenNoUrl()
    {
        var key1 = DataMigrator.GenerateDeduplicationKey(null, "  My Article Title  ");
        var key2 = DataMigrator.GenerateDeduplicationKey("", "My Article Title");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GenerateDeduplicationKey_TitleIsCaseInsensitive()
    {
        var key1 = DataMigrator.GenerateDeduplicationKey(null, "HELLO WORLD");
        var key2 = DataMigrator.GenerateDeduplicationKey(null, "hello world");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void NormalizeUrl_StripsTrailingSlash()
    {
        var result = DataMigrator.NormalizeUrl("https://example.com/article/");
        Assert.Equal("https://example.com/article", result);
    }

    [Fact]
    public void NormalizeUrl_RemovesUtmParams()
    {
        var result = DataMigrator.NormalizeUrl(
            "https://example.com/article?utm_source=rss&utm_medium=feed&id=123");
        Assert.Contains("id=123", result);
        Assert.DoesNotContain("utm_source", result);
        Assert.DoesNotContain("utm_medium", result);
    }

    [Fact]
    public void NormalizeUrl_LowercasesUrl()
    {
        var result = DataMigrator.NormalizeUrl("HTTPS://EXAMPLE.COM/Article");
        Assert.Equal("https://example.com/article", result);
    }

    [Fact]
    public void NormalizeUrl_CleansTrailingQuestionMark()
    {
        var result = DataMigrator.NormalizeUrl("https://example.com/article?utm_source=rss");
        Assert.DoesNotMatch(@"\?$", result);
    }

    [Fact]
    public void GenerateDeduplicationKey_UrlTakesPriorityOverTitle()
    {
        var keyWithUrl = DataMigrator.GenerateDeduplicationKey(
            "https://example.com/article", "Some Title");
        var keyTitleOnly = DataMigrator.GenerateDeduplicationKey(
            null, "Some Title");
        Assert.NotEqual(keyWithUrl, keyTitleOnly);
    }

    [Fact]
    public void GenerateDeduplicationKey_SameUrlDifferentTitles_ProducesSameKey()
    {
        var key1 = DataMigrator.GenerateDeduplicationKey(
            "https://example.com/article", "Title A");
        var key2 = DataMigrator.GenerateDeduplicationKey(
            "https://example.com/article", "Title B");
        Assert.Equal(key1, key2);
    }
}
