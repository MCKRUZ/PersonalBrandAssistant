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
        config.GlobalLevel = AutonomyLevel.Suggest;

        var result = config.ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.Suggest, result);
    }

    [Fact]
    public void ResolveLevel_ReturnsContentTypeOverride_WhenItMatches()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.ContentTypeOverrides =
        [
            new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.Draft),
        ];

        var result = config.ResolveLevel(ContentType.BlogPost, null);

        Assert.Equal(AutonomyLevel.Draft, result);
    }

    [Fact]
    public void ResolveLevel_ReturnsPlatformOverride_WinsOverContentType()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.ContentTypeOverrides =
        [
            new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.Draft),
        ];
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.FullAuto),
        ];

        var result = config.ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.FullAuto, result);
    }

    [Fact]
    public void ResolveLevel_ReturnsContentTypePlatformOverride_WinsOverAll()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Manual;
        config.ContentTypeOverrides =
        [
            new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.Suggest),
        ];
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.Draft),
        ];
        config.ContentTypePlatformOverrides =
        [
            new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.FullAuto),
        ];

        var result = config.ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.FullAuto, result);
    }

    [Fact]
    public void ResolveLevel_FallsThrough_WhenSpecificOverrideMissing()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Manual;
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.Draft),
        ];
        config.ContentTypePlatformOverrides =
        [
            new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.FullAuto),
        ];

        // SocialPost + LinkedIn: no CTP match, falls to Platform override
        var result = config.ResolveLevel(ContentType.SocialPost, PlatformType.LinkedIn);

        Assert.Equal(AutonomyLevel.Draft, result);
    }

    [Fact]
    public void ResolveLevel_FallsToGlobal_WhenNoPlatformProvided_AndNoContentTypeOverride()
    {
        var config = AutonomyConfiguration.CreateDefault();
        config.GlobalLevel = AutonomyLevel.Suggest;
        config.PlatformOverrides =
        [
            new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.FullAuto),
        ];

        var result = config.ResolveLevel(ContentType.SocialPost, null);

        Assert.Equal(AutonomyLevel.Suggest, result);
    }

    [Fact]
    public void OverrideValueObjects_HaveValueEquality()
    {
        var a = new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.Draft);
        var b = new ContentTypeOverride(ContentType.BlogPost, AutonomyLevel.Draft);

        Assert.Equal(a, b);

        var c = new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.FullAuto);
        var d = new PlatformOverride(PlatformType.LinkedIn, AutonomyLevel.FullAuto);

        Assert.Equal(c, d);

        var e = new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.Draft);
        var f = new ContentTypePlatformOverride(ContentType.BlogPost, PlatformType.LinkedIn, AutonomyLevel.Draft);

        Assert.Equal(e, f);
    }
}
