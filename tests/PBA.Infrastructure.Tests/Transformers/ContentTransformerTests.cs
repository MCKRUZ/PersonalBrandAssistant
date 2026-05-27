namespace PBA.Infrastructure.Tests.Transformers;

using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Transformers;

public class ContentTransformerTests
{
    private readonly Mock<IPlatformFormatter> _mockFormatter = new();
    private readonly TransformerOptions _options = new()
    {
        BaseUrl = "https://matthewkruczek.ai"
    };

    private ContentTransformer CreateTransformer(Platform? registerForPlatform = null)
    {
        var services = new ServiceCollection();

        if (registerForPlatform.HasValue)
        {
            services.AddKeyedSingleton<IPlatformFormatter>(
                registerForPlatform.Value, (_, _) => _mockFormatter.Object);
        }

        var provider = services.BuildServiceProvider();
        var optionsMonitor = Mock.Of<IOptionsMonitor<TransformerOptions>>(
            o => o.CurrentValue == _options);

        return new ContentTransformer(provider, optionsMonitor, NullLogger<ContentTransformer>.Instance);
    }

    private static Content CreateContent(string body = "Test content", string title = "Test Title") =>
        new()
        {
            Title = title,
            Body = body,
            ContentType = ContentType.Blog,
            Tags = ["AI", "Tech"]
        };

    [Fact]
    public async Task TransformAsync_WithBlogPlatform_DelegatesToBlogFormatter()
    {
        _mockFormatter
            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("formatted output");

        var transformer = CreateTransformer(Platform.Blog);
        var result = await transformer.TransformAsync(CreateContent(), Platform.Blog, CancellationToken.None);

        Assert.Equal("formatted output", result);
        _mockFormatter.Verify(
            f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TransformAsync_WithUnregisteredPlatform_ThrowsNotSupportedException()
    {
        var transformer = CreateTransformer();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => transformer.TransformAsync(CreateContent(), Platform.Reddit, CancellationToken.None));
    }

    [Fact]
    public async Task TransformAsync_StripsFrontmatter_BeforeDelegating()
    {
        PreprocessedContent? captured = null;
        _mockFormatter
            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync("ok");

        var content = CreateContent("---\ntitle: Hello\ntags: [a, b]\n---\n\nActual content here.");
        var transformer = CreateTransformer(Platform.Blog);

        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.DoesNotContain("---", captured.Body);
        Assert.DoesNotContain("title: Hello", captured.Body);
        Assert.StartsWith("Actual content here.", captured.Body);
    }

    [Fact]
    public async Task TransformAsync_ResolvesRelativeImagePaths_ToAbsoluteUrls()
    {
        PreprocessedContent? captured = null;
        _mockFormatter
            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync("ok");

        var content = CreateContent("![alt](images/photo.png)\n![alt2](/images/other.jpg)");
        var transformer = CreateTransformer(Platform.Blog);

        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(2, captured.Images.Count);
        Assert.All(captured.Images, img => Assert.StartsWith("https://", img.AbsoluteUrl));
        Assert.Contains("https://matthewkruczek.ai/images/photo.png", captured.Body);
        Assert.Contains("https://matthewkruczek.ai/images/other.jpg", captured.Body);
    }

    [Fact]
    public async Task TransformAsync_PreservesContentMetadata_InPreprocessedContent()
    {
        PreprocessedContent? captured = null;
        _mockFormatter
            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync("ok");

        var content = CreateContent(title: "My Title");
        content.Tags = ["AI", "Tech"];
        var transformer = CreateTransformer(Platform.Blog);

        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("My Title", captured.Title);
        Assert.Contains("AI", captured.Tags);
        Assert.Contains("Tech", captured.Tags);
        Assert.Equal("Blog", captured.ContentType);
    }

    [Fact]
    public async Task TransformAsync_EmptyBody_PassesEmptyToFormatter()
    {
        PreprocessedContent? captured = null;
        _mockFormatter
            .Setup(f => f.FormatAsync(It.IsAny<PreprocessedContent>(), It.IsAny<CancellationToken>()))
            .Callback<PreprocessedContent, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync("ok");

        var content = CreateContent(body: "");
        var transformer = CreateTransformer(Platform.Blog);

        await transformer.TransformAsync(content, Platform.Blog, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("", captured.Body);
    }
}
