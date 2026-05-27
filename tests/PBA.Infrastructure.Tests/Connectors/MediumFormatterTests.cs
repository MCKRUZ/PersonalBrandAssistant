using PBA.Application.Common.Models;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using Xunit;

namespace PBA.Infrastructure.Tests.Connectors;

public class MediumFormatterTests
{
    private readonly MediumFormatter _formatter = new();

    [Fact]
    public async Task FormatAsync_InjectsCanonicalUrlFooter()
    {
        var content = new PreprocessedContent(
            "Test Post", "Some body text.",
            "https://matthewkruczek.ai/posts/my-post",
            [], []);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.EndsWith(
            "\n\n---\n*Originally published at [matthewkruczek.ai](https://matthewkruczek.ai/posts/my-post)*",
            result);
    }

    [Fact]
    public async Task FormatAsync_NullCanonicalUrl_OmitsFooter()
    {
        var content = new PreprocessedContent(
            "Test Post", "Some body text.", null, [], []);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.DoesNotContain("Originally published", result);
        Assert.Equal("Some body text.", result);
    }

    [Fact]
    public async Task FormatAsync_ConvertsSvgReferences_ToPng()
    {
        var content = new PreprocessedContent(
            "Test", "![diagram](https://example.com/chart.svg)",
            null, [], []);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("https://example.com/chart.png", result);
        Assert.DoesNotContain(".svg", result);
    }

    [Fact]
    public async Task FormatAsync_ResolvesRelativeImageUrls_ToAbsolute()
    {
        var content = new PreprocessedContent(
            "Test", "![alt](images/photo.png)",
            "https://matthewkruczek.ai/posts/my-post",
            [], [new ImageReference("images/photo.png", "https://matthewkruczek.ai/images/photo.png", "alt")]);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Contains("![alt](https://matthewkruczek.ai/images/photo.png)", result);
    }

    [Fact]
    public async Task FormatAsync_PreservesMarkdownFormat()
    {
        var body = "# Heading\n\n**bold** and *italic*\n\n```csharp\nvar x = 1;\n```\n\n[link](https://example.com)";
        var content = new PreprocessedContent("Test", body, null, [], []);

        var result = await _formatter.FormatAsync(content, CancellationToken.None);

        Assert.Equal(body, result);
    }

    [Fact]
    public void Platform_ReturnsMedium()
    {
        Assert.Equal(Platform.Medium, _formatter.Platform);
    }
}
