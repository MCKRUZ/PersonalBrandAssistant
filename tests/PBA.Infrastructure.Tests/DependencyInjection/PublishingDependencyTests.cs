using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using PBA.Infrastructure.Security;
using PBA.Infrastructure.Transformers;
using Xunit;

namespace PBA.Infrastructure.Tests.DependencyInjection;

public class PublishingDependencyTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public PublishingDependencyTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                ["Publishing:Medium:Enabled"] = "true",
                ["Publishing:Substack:Enabled"] = "true",
                ["Publishing:Substack:PublicationSlug"] = "test",
                ["Publishing:LinkedIn:ClientId"] = "test",
                ["Publishing:LinkedIn:ClientSecret"] = "test",
                ["Publishing:LinkedIn:RedirectUri"] = "https://localhost/callback",
                ["Publishing:Twitter:ClientId"] = "test",
                ["Publishing:Twitter:ClientSecret"] = "test",
                ["Publishing:Twitter:RedirectUri"] = "https://localhost/callback",
                ["BlogConnector:RepoPath"] = "/tmp",
                ["BlogConnector:TemplatePath"] = "/tmp/template.html",
                ["ContentTransformer:BaseUrl"] = "https://test.example.com",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseInMemoryDatabase($"PublishingDI_{Guid.NewGuid()}"));
        services.AddScoped<IAppDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        services.AddSingleton(Mock.Of<IProcessRunner>());
        services.AddSingleton(Mock.Of<ISidecarClient>());
        services.AddSingleton(Mock.Of<IBackgroundJobClient>());
        services.AddLogging();
        services.AddHttpClient();

        services.Configure<BlogConnectorOptions>(config.GetSection(BlogConnectorOptions.SectionName));
        services.AddScoped<IContentPublisher, ContentPublisher>();

        services.AddPublishingDependencies(config);

        _provider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(Platform.Blog, typeof(BlogConnector))]
    [InlineData(Platform.Medium, typeof(MediumConnector))]
    [InlineData(Platform.LinkedIn, typeof(LinkedInConnector))]
    [InlineData(Platform.Twitter, typeof(TwitterConnector))]
    [InlineData(Platform.Substack, typeof(SubstackConnector))]
    public void ResolveKeyedConnector_ReturnsCorrectType(Platform platform, Type expectedType)
    {
        using var scope = _provider.CreateScope();
        var connector = scope.ServiceProvider.GetKeyedService<IPlatformConnector>(platform);

        Assert.NotNull(connector);
        Assert.IsType(expectedType, connector);
    }

    [Theory]
    [InlineData(Platform.Blog, typeof(BlogFormatter))]
    [InlineData(Platform.Medium, typeof(MediumFormatter))]
    [InlineData(Platform.LinkedIn, typeof(LinkedInFormatter))]
    [InlineData(Platform.Twitter, typeof(TwitterFormatter))]
    [InlineData(Platform.Substack, typeof(SubstackFormatter))]
    public void ResolveKeyedFormatter_ReturnsCorrectType(Platform platform, Type expectedType)
    {
        using var scope = _provider.CreateScope();
        var formatter = scope.ServiceProvider.GetKeyedService<IPlatformFormatter>(platform);

        Assert.NotNull(formatter);
        Assert.IsType(expectedType, formatter);
    }

    [Fact]
    public void ResolveContentTransformer_ReturnsContentTransformer()
    {
        using var scope = _provider.CreateScope();
        var transformer = scope.ServiceProvider.GetRequiredService<IContentTransformer>();

        Assert.IsType<ContentTransformer>(transformer);
    }

    [Fact]
    public void ResolveTokenEncryptor_ReturnsSingleton()
    {
        using var scope1 = _provider.CreateScope();
        using var scope2 = _provider.CreateScope();
        var first = scope1.ServiceProvider.GetRequiredService<ITokenEncryptor>();
        var second = scope2.ServiceProvider.GetRequiredService<ITokenEncryptor>();

        Assert.Same(first, second);
        Assert.IsType<TokenEncryptor>(first);
    }

    [Fact]
    public void ResolveOAuthService_ReturnsScoped()
    {
        using var scope1 = _provider.CreateScope();
        using var scope2 = _provider.CreateScope();
        var first = scope1.ServiceProvider.GetRequiredService<IOAuthService>();
        var second = scope2.ServiceProvider.GetRequiredService<IOAuthService>();

        Assert.IsType<OAuthService>(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ResolvePublishRetryHandler_ReturnsHandler()
    {
        using var scope = _provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IPublishRetryHandler>();

        Assert.IsType<PublishRetryHandler>(handler);
    }

    [Fact]
    public void ResolveContentPublisher_ReturnsPublisher()
    {
        using var scope = _provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IContentPublisher>();

        Assert.IsType<ContentPublisher>(publisher);
    }

    [Theory]
    [InlineData(nameof(MediumConnector), "https://api.medium.com")]
    [InlineData(nameof(LinkedInConnector), "https://api.linkedin.com")]
    [InlineData(nameof(TwitterConnector), "https://api.x.com")]
    [InlineData(nameof(SubstackConnector), "https://test.substack.com")]
    public void HttpClientFactory_ConfiguresBaseAddress(string clientName, string expectedBase)
    {
        var factory = _provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(clientName);

        Assert.NotNull(client.BaseAddress);
        Assert.StartsWith(expectedBase, client.BaseAddress!.ToString());
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
