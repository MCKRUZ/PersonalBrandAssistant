using System.Text.Json;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class TwitterFormatterTests
{
    private readonly TwitterFormatter _formatter = new();

    private static PreprocessedContent CreateContent(
        string body,
        string? canonicalUrl = null) => new(
        Title: "Test Post",
        Body: body,
        CanonicalUrl: canonicalUrl,
        Tags: [],
        Images: []);

    [Fact]
    public async Task Format_Under280Chars_ReturnsSingleSegment()
    {
        var content = CreateContent("Short tweet about .NET 10");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Equal("Short tweet about .NET 10", result);
        Assert.DoesNotContain("[", result);
    }

    [Fact]
    public async Task Format_Over280Chars_SplitsIntoThreadSegments()
    {
        var body = string.Join(". ", Enumerable.Range(1, 30)
            .Select(i => $"This is sentence number {i} which adds length")) + ".";

        var content = CreateContent(body);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        var segments = JsonSerializer.Deserialize<string[]>(result);
        Assert.NotNull(segments);
        Assert.True(segments.Length > 1);
        foreach (var segment in segments)
            Assert.True(segment.Length <= 280, $"Segment too long ({segment.Length}): {segment[..50]}...");
    }

    [Fact]
    public async Task Format_SplitsAtSentenceBoundaries()
    {
        var sentences = Enumerable.Range(1, 20)
            .Select(i => $"Sentence {i} has some words in it here")
            .ToList();
        var body = string.Join(". ", sentences) + ".";

        var content = CreateContent(body);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        var segments = JsonSerializer.Deserialize<string[]>(result);
        Assert.NotNull(segments);

        foreach (var segment in segments)
        {
            var trimmed = segment.TrimEnd();
            if (trimmed.Contains('.') && !trimmed.EndsWith('.'))
            {
                var afterLastPeriod = trimmed[(trimmed.LastIndexOf('.') + 1)..].Trim();
                Assert.True(afterLastPeriod.Length < 50,
                    "Segment should split near sentence boundaries");
            }
        }
    }

    [Fact]
    public async Task Format_IncludesArticleLinkInLastSegment()
    {
        var body = string.Join(". ", Enumerable.Range(1, 30)
            .Select(i => $"This is sentence number {i} which adds length")) + ".";
        var url = "https://matthewkruczek.ai/posts/my-post";

        var content = CreateContent(body, url);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        var segments = JsonSerializer.Deserialize<string[]>(result);
        Assert.NotNull(segments);
        Assert.Contains(url, segments[^1]);
    }

    [Fact]
    public async Task Format_StripMarkdown_ToPlainText()
    {
        var body = "**Bold text** and *italic text* with [a link](https://example.com) and `code`\n## Heading";

        var content = CreateContent(body);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*italic text*", result);
        Assert.DoesNotContain("[a link]", result);
        Assert.DoesNotContain("`code`", result);
        Assert.DoesNotContain("## ", result);
        Assert.Contains("Bold text", result);
        Assert.Contains("italic text", result);
        Assert.Contains("code", result);
        Assert.Contains("Heading", result);
    }

    [Fact]
    public async Task Format_PreservesHashtags()
    {
        var body = "Exploring #dotnet and #AI for enterprise solutions";

        var content = CreateContent(body);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("#dotnet", result);
        Assert.Contains("#AI", result);
    }

    [Fact]
    public void Platform_ReturnsTwitter()
    {
        Assert.Equal(Platform.Twitter, _formatter.Platform);
    }

    [Fact]
    public async Task Format_SingleTweetWithUrl_BudgetsTcoLength()
    {
        var textPart = new string('A', 250);
        var url = "https://matthewkruczek.ai/posts/very-long-slug-name-here";

        var content = CreateContent(textPart, url);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains(url, result);
        Assert.DoesNotContain("[", result);
    }
}
