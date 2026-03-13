using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Enums;

public class EnumTests
{
    [Fact]
    public void ContentType_HasExactly4Values()
    {
        var values = Enum.GetValues<ContentType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(ContentType.BlogPost, values);
        Assert.Contains(ContentType.SocialPost, values);
        Assert.Contains(ContentType.Thread, values);
        Assert.Contains(ContentType.VideoDescription, values);
    }

    [Fact]
    public void ContentStatus_HasExactly8Values()
    {
        var values = Enum.GetValues<ContentStatus>();
        Assert.Equal(8, values.Length);
    }

    [Fact]
    public void PlatformType_HasExactly4Values()
    {
        var values = Enum.GetValues<PlatformType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(PlatformType.TwitterX, values);
        Assert.Contains(PlatformType.LinkedIn, values);
        Assert.Contains(PlatformType.Instagram, values);
        Assert.Contains(PlatformType.YouTube, values);
    }

    [Fact]
    public void AutonomyLevel_HasExactly4Values()
    {
        var values = Enum.GetValues<AutonomyLevel>();
        Assert.Equal(4, values.Length);
        Assert.Contains(AutonomyLevel.Manual, values);
        Assert.Contains(AutonomyLevel.Assisted, values);
        Assert.Contains(AutonomyLevel.SemiAuto, values);
        Assert.Contains(AutonomyLevel.Autonomous, values);
    }
}
