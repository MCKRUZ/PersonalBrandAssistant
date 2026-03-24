using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentAutomation;

public class ContentImagePropertiesTests
{
    [Fact]
    public void ImageFileId_DefaultsToNull()
    {
        var content = Content.Create(ContentType.SocialPost, "test body");
        Assert.Null(content.ImageFileId);
    }

    [Fact]
    public void ImageRequired_DefaultsToFalse()
    {
        var content = Content.Create(ContentType.SocialPost, "test body");
        Assert.False(content.ImageRequired);
    }

    [Fact]
    public void ImageFileId_CanBeSet()
    {
        var content = Content.Create(ContentType.SocialPost, "test body");
        content.ImageFileId = "img-abc-123";
        Assert.Equal("img-abc-123", content.ImageFileId);
    }

    [Fact]
    public void ImageRequired_CanBeSetToTrue()
    {
        var content = Content.Create(ContentType.SocialPost, "test body");
        content.ImageRequired = true;
        Assert.True(content.ImageRequired);
    }
}
