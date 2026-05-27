using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class LinkedInFormatterTests
{
    private readonly LinkedInFormatter _formatter = new();

    private static PreprocessedContent CreateContent(string body, string? canonicalUrl = null) => new(
        Title: "Test Post",
        Body: body,
        CanonicalUrl: canonicalUrl,
        Tags: [],
        Images: []);

    [Fact]
    public async Task Format_StripMarkdown_ToPlainText()
    {
        var content = CreateContent(
            "## Heading\n\n**Bold text** and *italic text*.\n\n[A link](https://example.com)\n\n- Item 1\n- Item 2");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("Heading", result);
        Assert.DoesNotContain("##", result);
        Assert.Contains("Bold text", result);
        Assert.DoesNotContain("**", result);
        Assert.Contains("italic text", result);
        Assert.Contains("A link", result);
        Assert.DoesNotContain("](", result);
        Assert.Contains("- Item 1", result);
        Assert.Contains("- Item 2", result);
    }

    [Fact]
    public async Task Format_PreservesLineBreaksAndBullets()
    {
        var content = CreateContent("First paragraph.\n\nSecond paragraph.\n\n- Bullet one\n- Bullet two");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("First paragraph.\n\nSecond paragraph.", result);
        Assert.Contains("- Bullet one\n- Bullet two", result);
    }

    [Fact]
    public async Task Format_TruncatesTo3000Chars_WithEllipsis()
    {
        var longBody = new string('A', 3500);
        var content = CreateContent(longBody, "https://matthewkruczek.ai/posts/long-post");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.True(result.Length <= 3000);
        Assert.Contains("...", result);
        Assert.Contains("Read more: https://matthewkruczek.ai/posts/long-post", result);
    }

    [Fact]
    public async Task Format_AddsReadMoreLink_WhenTruncated()
    {
        var longBody = new string('W', 100) + " " + new string('X', 3400);
        var content = CreateContent(longBody, "https://matthewkruczek.ai/posts/test");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.True(result.Length <= 3000);
        Assert.Contains("Read more: https://matthewkruczek.ai/posts/test", result);
    }

    [Fact]
    public async Task Format_Under3000Chars_NoTruncation()
    {
        var content = CreateContent("Short content here.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Equal("Short content here.", result);
        Assert.DoesNotContain("...", result);
        Assert.DoesNotContain("Read more", result);
    }

    [Fact]
    public async Task Format_NullCanonicalUrl_TruncatesWithoutReadMore()
    {
        var longBody = new string('A', 3500);
        var content = CreateContent(longBody, null);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.True(result.Length <= 3000);
        Assert.EndsWith("...", result);
        Assert.DoesNotContain("Read more", result);
    }

    [Fact]
    public async Task Format_CodeBlocks_ConvertToPlainText()
    {
        var content = CreateContent("Before code.\n\n```csharp\nvar x = 1;\n```\n\nAfter code.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("var x = 1;", result);
        Assert.DoesNotContain("```", result);
        Assert.DoesNotContain("csharp", result);
    }

    [Fact]
    public async Task Format_Images_Stripped()
    {
        var content = CreateContent("Before image.\n\n![alt text](https://example.com/img.png)\n\nAfter image.");

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.DoesNotContain("![", result);
        Assert.DoesNotContain("img.png", result);
        Assert.Contains("Before image.", result);
        Assert.Contains("After image.", result);
    }

    [Fact]
    public void Format_Platform_ReturnsLinkedIn()
    {
        Assert.Equal(Platform.LinkedIn, _formatter.Platform);
    }
}
