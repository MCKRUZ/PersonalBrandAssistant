using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Security;
using PBA.Infrastructure.Security.OAuthProviders;
using Xunit;

namespace PBA.Infrastructure.Tests.Security;

// Boundary test: proves exactly one IOAuthProvider resolves per platform via keyed DI, and that an
// unregistered platform resolves to null (which the coordinator turns into NotSupportedException).
public class OAuthProviderMapTests
{
    private static ServiceProvider BuildProviderMap()
    {
        var services = new ServiceCollection();

        services.AddSingleton(Mock.Of<IHttpClientFactory>());
        services.AddSingleton(Mock.Of<ITokenEncryptor>());
        services.AddLogging();
        services.Configure<LinkedInOptions>(_ => { });
        services.Configure<TwitterOptions>(_ => { });

        // The two registrations under test, identical to DependencyInjection.cs.
        services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
        services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ResolvesOneProviderPerPlatform_ViaKeyedDI()
    {
        using var provider = BuildProviderMap();

        var linkedIn = provider.GetKeyedService<IOAuthProvider>(Platform.LinkedIn);
        var twitter = provider.GetKeyedService<IOAuthProvider>(Platform.Twitter);
        var unregistered = provider.GetKeyedService<IOAuthProvider>(Platform.Blog);

        Assert.IsType<LinkedInOAuthProvider>(linkedIn);
        Assert.IsType<TwitterOAuthProvider>(twitter);
        Assert.Equal(Platform.LinkedIn, linkedIn!.Platform);
        Assert.Equal(Platform.Twitter, twitter!.Platform);
        Assert.Null(unregistered);
    }
}
