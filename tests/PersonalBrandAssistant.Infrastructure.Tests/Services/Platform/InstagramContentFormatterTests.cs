using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class InstagramContentFormatterTests
{
    private readonly InstagramContentFormatter _sut = new();

    [Fact]
    public void Platform_IsInstagram() =>
        Assert.Equal(PlatformType.Instagram, _sut.Platform);

    [Fact]
    public void FormatAndValidate_NoMedia_ReturnsFailure()
    {
        var content = Content.Create(ContentType.SocialPost, "Beautiful day!");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_WithMedia_Succeeds()
    {
        var content = Content.Create(ContentType.SocialPost, "Beautiful day!");
        content.Metadata.PlatformSpecificData["media_count"] = "1";

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Equal("Beautiful day!", result.Value!.Text);
    }

    [Fact]
    public void FormatAndValidate_WithMediaCount_Succeeds()
    {
        var content = Content.Create(ContentType.SocialPost, "Photo caption");
        content.Metadata.PlatformSpecificData["media_count"] = "3";

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_TruncatesCaptionAt2200()
    {
        var body = new string('A', 2500);
        var content = Content.Create(ContentType.SocialPost, body);
        content.Metadata.PlatformSpecificData["media_count"] = "1";

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Text.Length <= 2200);
        Assert.EndsWith("...", result.Value.Text);
    }

    [Fact]
    public void FormatAndValidate_LimitsHashtagsTo30()
    {
        var content = Content.Create(ContentType.SocialPost, "Post");
        content.Metadata.PlatformSpecificData["media_count"] = "1";
        for (var i = 0; i < 35; i++)
            content.Metadata.Tags.Add($"tag{i}");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        var hashtagCount = result.Value!.Text.Split('#').Length - 1;
        Assert.True(hashtagCount <= 30);
    }

    [Fact]
    public void FormatAndValidate_CarouselOver10_ReturnsFailure()
    {
        var content = Content.Create(ContentType.SocialPost, "Carousel post");
        content.Metadata.PlatformSpecificData["media_count"] = "1";
        content.Metadata.PlatformSpecificData["carousel_count"] = "12";

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_CarouselAt10_Succeeds()
    {
        var content = Content.Create(ContentType.SocialPost, "Carousel post");
        content.Metadata.PlatformSpecificData["media_count"] = "10";
        content.Metadata.PlatformSpecificData["carousel_count"] = "10";

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_HashtagsSeparatedByBlankLine()
    {
        var content = Content.Create(ContentType.SocialPost, "Nice photo");
        content.Metadata.PlatformSpecificData["media_count"] = "1";
        content.Metadata.Tags.AddRange(["travel", "nature"]);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Contains("\n\n#travel", result.Value!.Text);
    }

    [Fact]
    public void FormatAndValidate_EmptyBodyWithMedia_ReturnsFailure()
    {
        var content = Content.Create(ContentType.SocialPost, "");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }
}
