using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
using PersonalBrandAssistant.Infrastructure.Tests.Mocks;

namespace PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection;

public class PlatformServiceRegistrationTests : IClassFixture<PlatformServiceRegistrationTests.DiTestFactory>, IDisposable
{
    private const string TestApiKey = "test-api-key-12345";
    private readonly DiTestFactory _factory;
    private IServiceScope? _scope;

    public PlatformServiceRegistrationTests(DiTestFactory factory)
    {
        _factory = factory;
    }

    private IServiceProvider CreateServiceProvider()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IChatClientFactory>(new MockChatClientFactory());
            });
        });
        _scope = factory.Services.CreateScope();
        return _scope.ServiceProvider;
    }

    [Fact]
    public void AllSocialPlatformAdapters_Resolve()
    {
        var sp = CreateServiceProvider();
        var adapters = sp.GetServices<ISocialPlatform>().ToList();
        Assert.Equal(4, adapters.Count);
    }

    [Fact]
    public void AllContentFormatters_Resolve()
    {
        var sp = CreateServiceProvider();
        var formatters = sp.GetServices<IPlatformContentFormatter>().ToList();
        Assert.Equal(4, formatters.Count);
    }

    [Fact]
    public void IOAuthManager_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IOAuthManager>();
        Assert.NotNull(service);
        Assert.IsType<OAuthManager>(service);
    }

    [Fact]
    public void IRateLimiter_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IRateLimiter>();
        Assert.NotNull(service);
        Assert.IsType<DatabaseRateLimiter>(service);
    }

    [Fact]
    public void IMediaStorage_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IMediaStorage>();
        Assert.NotNull(service);
    }

    [Fact]
    public void IPublishingPipeline_ResolvesRealImplementation()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IPublishingPipeline>();
        Assert.NotNull(service);
        Assert.IsType<PublishingPipeline>(service);
    }

    [Fact]
    public void PlatformIntegrationOptions_BindsFromConfig()
    {
        var sp = CreateServiceProvider();
        var options = sp.GetRequiredService<IOptions<PlatformIntegrationOptions>>();
        Assert.NotNull(options.Value.Twitter);
        Assert.Equal("http://localhost:4200/platforms/twitter/callback", options.Value.Twitter.CallbackUrl);
    }

    [Fact]
    public void MediaStorageOptions_BindsFromConfig()
    {
        var sp = CreateServiceProvider();
        var options = sp.GetRequiredService<IOptions<MediaStorageOptions>>();
        Assert.Equal("./test-media", options.Value.BasePath);
    }

    public void Dispose()
    {
        _scope?.Dispose();
    }

    public class DiTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiKey", TestApiKey);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=test_di;Username=test;Password=test");

            // Platform integration config
            builder.UseSetting("PlatformIntegrations:Twitter:CallbackUrl", "http://localhost:4200/platforms/twitter/callback");
            builder.UseSetting("PlatformIntegrations:Twitter:BaseUrl", "https://api.x.com/2");
            builder.UseSetting("PlatformIntegrations:LinkedIn:CallbackUrl", "http://localhost:4200/platforms/linkedin/callback");
            builder.UseSetting("PlatformIntegrations:LinkedIn:ApiVersion", "202603");
            builder.UseSetting("PlatformIntegrations:LinkedIn:BaseUrl", "https://api.linkedin.com/rest");
            builder.UseSetting("PlatformIntegrations:Instagram:CallbackUrl", "http://localhost:4200/platforms/instagram/callback");
            builder.UseSetting("PlatformIntegrations:YouTube:CallbackUrl", "http://localhost:4200/platforms/youtube/callback");
            builder.UseSetting("PlatformIntegrations:YouTube:DailyQuotaLimit", "10000");
            builder.UseSetting("MediaStorage:BasePath", "./test-media");
            builder.UseSetting("MediaStorage:SigningKey", "test-signing-key-for-hmac-validation");

            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services
                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                    .ToList();
                foreach (var svc in hostedServices)
                    services.Remove(svc);
            });
        }
    }
}
