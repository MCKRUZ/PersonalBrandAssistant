using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Infrastructure.Tests.Mocks;

namespace PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection;

public class AgentServiceRegistrationTests : IClassFixture<AgentServiceRegistrationTests.DiTestFactory>, IDisposable
{
    private const string TestApiKey = "test-api-key-12345";
    private readonly DiTestFactory _factory;
    private IServiceScope? _scope;

    public AgentServiceRegistrationTests(DiTestFactory factory)
    {
        _factory = factory;
    }

    private IServiceProvider CreateServiceProvider()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Replace ChatClientFactory with mock to avoid real Anthropic API key requirement
                services.AddSingleton<IChatClientFactory>(new MockChatClientFactory());
            });
        });
        _scope = factory.Services.CreateScope();
        return _scope.ServiceProvider;
    }

    [Fact]
    public void IChatClientFactory_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IChatClientFactory>();
        Assert.NotNull(service);
    }

    [Fact]
    public void IPromptTemplateService_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IPromptTemplateService>();
        Assert.NotNull(service);
    }

    [Fact]
    public void ITokenTracker_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<ITokenTracker>();
        Assert.NotNull(service);
    }

    [Fact]
    public void IAgentOrchestrator_Resolves()
    {
        var sp = CreateServiceProvider();
        var service = sp.GetService<IAgentOrchestrator>();
        Assert.NotNull(service);
    }

    [Fact]
    public void AllAgentCapabilities_Resolve()
    {
        var sp = CreateServiceProvider();
        var capabilities = sp.GetServices<IAgentCapability>().ToList();
        Assert.Equal(5, capabilities.Count);
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
