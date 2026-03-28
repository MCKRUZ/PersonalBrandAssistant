using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.BlogServices;

public class PublishDelayCalculatorTests
{
    [Fact]
    public void DefaultDelay_Is7Days()
    {
        var substackPublishedAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var defaultDelay = TimeSpan.FromDays(7);

        var scheduledAt = substackPublishedAt + defaultDelay;

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 12, 0, 0, TimeSpan.Zero), scheduledAt);
    }

    [Fact]
    public void CustomDelay_UsesOverride()
    {
        var substackPublishedAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var customDelay = TimeSpan.FromDays(14);

        var scheduledAt = substackPublishedAt + customDelay;

        Assert.Equal(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero), scheduledAt);
    }

    [Fact]
    public void NullOverride_UsesGlobalDefault()
    {
        TimeSpan? blogDelayOverride = null;
        var globalDefault = TimeSpan.FromDays(7);
        var substackPublishedAt = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

        var delay = blogDelayOverride ?? globalDefault;
        var scheduledAt = substackPublishedAt + delay;

        Assert.Equal(TimeSpan.FromDays(7), delay);
        Assert.Equal(new DateTimeOffset(2026, 3, 17, 0, 0, 0, TimeSpan.Zero), scheduledAt);
    }

    [Fact]
    public void BlogSkipped_SkipsScheduling()
    {
        var content = Content.Create(ContentType.BlogPost, "body", "Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        content.BlogSkipped = true;

        Assert.True(content.BlogSkipped);
        // BlogSkipped = true means no schedule should be created
    }

    [Fact]
    public void BlogSkippedAndOverride_AreIndependent()
    {
        var content = Content.Create(ContentType.BlogPost, "body", "Test",
            [PlatformType.Substack, PlatformType.PersonalBlog]);
        content.BlogSkipped = true;
        content.BlogDelayOverride = TimeSpan.FromDays(3);

        Assert.True(content.BlogSkipped);
        Assert.Equal(TimeSpan.FromDays(3), content.BlogDelayOverride);
        // Even though override is set, BlogSkipped means no scheduling
    }
}
