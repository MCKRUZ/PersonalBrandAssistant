using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using PersonalBrandAssistant.Infrastructure.Tests.Mocks;

namespace PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection;

public class ContentEngineServiceRegistrationTests
    : IClassFixture<ContentEngineServiceRegistrationTests.DiTestFactory>, IDisposable
{
    private const string TestApiKey = "test-api-key-12345";
    private readonly DiTestFactory _factory;
    private IServiceScope? _scope;

    public ContentEngineServiceRegistrationTests(DiTestFactory factory)
    {
        _factory = factory;
    }

    private IServiceProvider CreateServiceProvider()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISidecarClient>(new MockSidecarClient());
            });
        });
        _scope = factory.Services.CreateScope();
        return _scope.ServiceProvider;
    }

    [Fact]
    public void ISidecarClient_Resolves_AsSingleton()
    {
        var sp = CreateServiceProvider();
        var service1 = sp.GetService<ISidecarClient>();
        var service2 = sp.GetService<ISidecarClient>();
        Assert.NotNull(service1);
        Assert.Same(service1, service2);
    }

    [Fact]
    public void IContentPipeline_Resolves_AsScoped()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IContentPipeline>();
        Assert.NotNull(service);
        Assert.IsType<ContentPipeline>(service);
    }

    [Fact]
    public void IRepurposingService_Resolves_AsScoped()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IRepurposingService>();
        Assert.NotNull(service);
        Assert.IsType<RepurposingService>(service);
    }

    [Fact]
    public void IContentCalendarService_Resolves_AsScoped()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IContentCalendarService>();
        Assert.NotNull(service);
        Assert.IsType<ContentCalendarService>(service);
    }

    [Fact]
    public void IBrandVoiceService_Resolves_AsScoped()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IBrandVoiceService>();
        Assert.NotNull(service);
        Assert.IsType<BrandVoiceService>(service);
    }

    [Fact]
    public void ITrendMonitor_Resolves_AsScoped()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<ITrendMonitor>();
        Assert.NotNull(service);
        Assert.IsType<TrendMonitor>(service);
    }

    [Fact]
    public void IEngagementAggregator_Resolves_AsScoped()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IEngagementAggregator>();
        Assert.NotNull(service);
        Assert.IsType<EngagementAggregator>(service);
    }

    [Fact]
    public void BackgroundServices_AllFourRegistered()
    {
        // DiTestFactory strips hosted services. Use a separate factory that captures
        // hosted service types in ConfigureTestServices before stripping them.
        List<Type?> capturedTypes = [];

        using var factory = new HostedServiceTestFactory(capturedTypes);
        var withBuilder = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISidecarClient>(new MockSidecarClient());
            });
        });

        // Trigger factory initialization
        _ = withBuilder.Services;

        Assert.Contains(typeof(RepurposeOnPublishProcessor), capturedTypes);
        Assert.Contains(typeof(TrendAggregationProcessor), capturedTypes);
        Assert.Contains(typeof(EngagementAggregationProcessor), capturedTypes);
        Assert.Contains(typeof(CalendarSlotProcessor), capturedTypes);
    }

    [Fact]
    public void SidecarOptions_BindsFromConfiguration()
    {
        var sp = CreateServiceProvider();
        var options = sp.GetRequiredService<IOptions<SidecarOptions>>();
        Assert.Equal("ws://test-sidecar:3001/ws", options.Value.WebSocketUrl);
    }

    [Fact]
    public void ContentEngineOptions_BindsFromConfiguration()
    {
        var sp = CreateServiceProvider();
        var options = sp.GetRequiredService<IOptions<ContentEngineOptions>>();
        Assert.Equal(75, options.Value.BrandVoiceScoreThreshold);
    }

    [Fact]
    public void TrendMonitoringOptions_BindsFromConfiguration()
    {
        var sp = CreateServiceProvider();
        var options = sp.GetRequiredService<IOptions<TrendMonitoringOptions>>();
        Assert.Equal(15, options.Value.AggregationIntervalMinutes);
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

            // Content engine config
            builder.UseSetting("Sidecar:WebSocketUrl", "ws://test-sidecar:3001/ws");
            builder.UseSetting("ContentEngine:BrandVoiceScoreThreshold", "75");
            builder.UseSetting("TrendMonitoring:AggregationIntervalMinutes", "15");

            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services
                    .Where(d => d.ServiceType == typeof(IHostedService))
                    .ToList();
                foreach (var svc in hostedServices)
                    services.Remove(svc);
            });
        }
    }

    /// <summary>
    /// Factory that captures hosted service types before removing them.
    /// </summary>
    public class HostedServiceTestFactory(List<Type?> capturedTypes) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiKey", TestApiKey);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=test_di;Username=test;Password=test");

            builder.ConfigureTestServices(services =>
            {
                // Capture hosted service impl types before removing
                var hostedServices = services
                    .Where(d => d.ServiceType == typeof(IHostedService))
                    .ToList();
                capturedTypes.AddRange(hostedServices.Select(d => d.ImplementationType));

                foreach (var svc in hostedServices)
                    services.Remove(svc);
            });
        }
    }
}
