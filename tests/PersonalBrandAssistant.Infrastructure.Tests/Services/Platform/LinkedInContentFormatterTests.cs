using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class LinkedInContentFormatterTests
{
    private readonly LinkedInContentFormatter _sut = new();

    [Fact]
    public void Platform_IsLinkedIn() =>
        Assert.Equal(PlatformType.LinkedIn, _sut.Platform);

    [Fact]
    public void FormatAndValidate_NormalText_Succeeds()
    {
        var content = Content.Create(ContentType.BlogPost, "A thoughtful LinkedIn post about leadership.");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Contains("leadership", result.Value!.Text);
    }

    [Fact]
    public void FormatAndValidate_TextAt3000Chars_Succeeds()
    {
        var body = new string('A', 3000);
        var content = Content.Create(ContentType.BlogPost, body);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_TextExceeds3000Chars_ReturnsFailure()
    {
        var body = new string('A', 3500);
        var content = Content.Create(ContentType.BlogPost, body);

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_PreservesInlineHashtags()
    {
        var content = Content.Create(ContentType.BlogPost, "Great insights on #leadership and #innovation today.");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Contains("#leadership", result.Value!.Text);
        Assert.Contains("#innovation", result.Value!.Text);
    }

    [Fact]
    public void FormatAndValidate_AppendsTagsNotAlreadyInline()
    {
        var content = Content.Create(ContentType.BlogPost, "Post about #leadership today.");
        content.Metadata.Tags.AddRange(["leadership", "tech"]);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        // "leadership" is already inline, should not be duplicated
        Assert.Contains("#tech", result.Value!.Text);
        // Count occurrences of #leadership — should be exactly 1
        var count = result.Value.Text.Split("#leadership").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void FormatAndValidate_TagsPushOver3000_ReturnsFailure()
    {
        var body = new string('A', 2995);
        var content = Content.Create(ContentType.BlogPost, body);
        content.Metadata.Tags.AddRange(["verylongtag"]);

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_PreservesTitle()
    {
        var content = Content.Create(ContentType.BlogPost, "Body text", title: "My Article");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Equal("My Article", result.Value!.Title);
    }

    [Fact]
    public void FormatAndValidate_EmptyBody_ReturnsFailure()
    {
        var content = Content.Create(ContentType.BlogPost, "");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }
}
