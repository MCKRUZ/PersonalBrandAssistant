using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class AutonomyConfigurationTests
{
    [Fact]
    public void Constructor_SetsIdToGuidEmpty()
    {
        var config = AutonomyConfiguration.CreateDefault();
        Assert.Equal(Guid.Empty, config.Id);
    }

    [Fact]
    public void ResolveLevel_ReturnsGlobalLevel_WhenNoOverridesExist()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Assisted;

        var result = config.ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.Assisted, result);
    }

    [Fact]
    public void ResolveLevel_ReturnsContentTypeOverride_WhenItMatches()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.ContentTypeOverrides =
        [
            new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.SemiAuto),
        ];

        var result = config.ResolveLevel(ContentType.BlogPost, null);

        Assert.Equal(AutonomyLevel.SemiAuto, result);
    }

    [Fact]
    public void ResolveLevel_ReturnsPlatformOverride_WinsOverContentType()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.ContentTypeOverrides =
        [
            new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.SemiAuto),
        ];
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.Autonomous),
        ];

        var result = config.ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.Autonomous, result);
    }

    [Fact]
    public void ResolveLevel_ReturnsContentTypePlatformOverride_WinsOverAll()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Manual;
        config.ContentTypeOverrides =
        [
            new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.Assisted),
        ];
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.SemiAuto),
        ];
        config.ContentTypePlatformOverrides =
        [
            new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.Autonomous),
        ];

        var result = config.ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.Autonomous, result);
    }

    [Fact]
    public void ResolveLevel_FallsThrough_WhenSpecificOverrideMissing()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Manual;
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.SemiAuto),
        ];
        config.ContentTypePlatformOverrides =
        [
            new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.Autonomous),
        ];

        // SocialPost + LinkedIn: no CTP match, falls to Platform override
        var result = config.ResolveLevel(ContentType.SocialPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.SemiAuto, result);
    }

    [Fact]
    public void ResolveLevel_FallsToGlobal_WhenNoPlatformProvided_AndNoContentTypeOverride()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Assisted;
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.Autonomous),
        ];

        var result = config.ResolveLevel(ContentType.SocialPost, null);

        Assert.Equal(AutonomyLevel.Assisted, result);
    }

    [Fact]
    public void OverrideValueObjects_HaveValueEquality()
    {
        var a = new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.SemiAuto);
        var b = new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.SemiAuto);

        Assert.Equal(a, b);

        var c = new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.Autonomous);
        var d = new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.Autonomous);

        Assert.Equal(c, d);

        var e = new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.SemiAuto);
        var f = new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.SemiAuto);

        Assert.Equal(e, f);
    }
}
