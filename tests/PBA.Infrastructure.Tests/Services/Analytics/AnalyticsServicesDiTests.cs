using Microsoft.Extensions.DependencyInjection;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Services.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

// Boundary test: exactly one IChannelAnalyticsService resolves per analytics platform via keyed DI. Mirrors
// the keyed registrations in DependencyInjection.cs.
public class AnalyticsServicesDiTests
{
    [Fact]
    public void AnalyticsServices_ResolveByPlatformKey_ViaKeyedDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IYouTubeApiClient>());
        services.AddSingleton(Mock.Of<IInstagramGraphClient>());
        services.AddSingleton(Mock.Of<ITikTokDisplayClient>());
        services.AddSingleton(Mock.Of<ITokenEncryptor>());
        services.AddLogging();

        services.AddKeyedScoped<IChannelAnalyticsService, YouTubeAnalyticsService>(Platform.YouTube);
        services.AddKeyedScoped<IChannelAnalyticsService, InstagramAnalyticsService>(Platform.Instagram);
        services.AddKeyedScoped<IChannelAnalyticsService, TikTokAnalyticsService>(Platform.TikTok);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<YouTubeAnalyticsService>(provider.GetKeyedService<IChannelAnalyticsService>(Platform.YouTube));
        Assert.IsType<InstagramAnalyticsService>(provider.GetKeyedService<IChannelAnalyticsService>(Platform.Instagram));
        Assert.IsType<TikTokAnalyticsService>(provider.GetKeyedService<IChannelAnalyticsService>(Platform.TikTok));
        Assert.Null(provider.GetKeyedService<IChannelAnalyticsService>(Platform.LinkedIn));
    }
}
