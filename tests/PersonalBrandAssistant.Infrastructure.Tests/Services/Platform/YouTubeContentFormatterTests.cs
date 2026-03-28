using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class YouTubeContentFormatterTests
{
    private readonly YouTubeContentFormatter _sut = new();

    [Fact]
    public void Platform_IsYouTube() =>
        Assert.Equal(PlatformType.YouTube, _sut.Platform);

    [Fact]
    public void FormatAndValidate_ValidTitleAndBody_Succeeds()
    {
        var content = Content.Create(ContentType.VideoDescription, "Video description here", title: "My Video");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.Equal("My Video", result.Value!.Title);
        Assert.Equal("Video description here", result.Value.Text);
    }

    [Fact]
    public void FormatAndValidate_NullTitle_ReturnsFailure()
    {
        var content = Content.Create(ContentType.VideoDescription, "Description only");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_EmptyTitle_ReturnsFailure()
    {
        var content = Content.Create(ContentType.VideoDescription, "Description", title: "   ");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FormatAndValidate_TitleOver100Chars_Truncates()
    {
        var title = new string('T', 150);
        var content = Content.Create(ContentType.VideoDescription, "Description", title: title);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Title!.Length <= 100);
        Assert.EndsWith("...", result.Value.Title);
    }

    [Fact]
    public void FormatAndValidate_DescriptionOver5000Chars_Truncates()
    {
        var body = new string('D', 6000);
        var content = Content.Create(ContentType.VideoDescription, body, title: "Title");

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Text.Length <= 5000);
        Assert.EndsWith("...", result.Value.Text);
    }

    [Fact]
    public void FormatAndValidate_TagsStoredInMetadata()
    {
        var content = Content.Create(ContentType.VideoDescription, "Desc", title: "Title");
        content.Metadata.Tags.AddRange(["csharp", "dotnet", "tutorial"]);

        var result = _sut.FormatAndValidate(content);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Metadata.ContainsKey("tags"));
        Assert.Contains("csharp", result.Value.Metadata["tags"]);
        Assert.Contains("dotnet", result.Value.Metadata["tags"]);
        // Tags should NOT be in the description text
        Assert.DoesNotContain("#csharp", result.Value.Text);
    }

    [Fact]
    public void FormatAndValidate_EmptyBody_ReturnsFailure()
    {
        var content = Content.Create(ContentType.VideoDescription, "", title: "Title");

        var result = _sut.FormatAndValidate(content);

        Assert.False(result.IsSuccess);
    }
}
