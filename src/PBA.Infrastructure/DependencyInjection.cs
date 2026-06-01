using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Publishing;
using PBA.Infrastructure.Seeding;
using PBA.Infrastructure.Security;
using PBA.Infrastructure.Services;
using PBA.Infrastructure.Transformers;

namespace PBA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                dataSource,
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddHttpClient();

        services.Configure<RssPollingOptions>(configuration.GetSection(RssPollingOptions.SectionName));
        services.AddHttpClient<RssFeedReader>();
        services.AddScoped<IRssFeedReader, RssFeedReader>();
        services.AddHostedService<RssPollingService>();

        services.Configure<SidecarOptions>(configuration.GetSection(SidecarOptions.SectionName));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        // Drafting routes through OpenRouter (fast, length-controllable) instead of the
        // agentic claude CLI. ProcessRunner/SidecarClient stay registered as a fallback.
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));
        services.AddHttpClient<ISidecarClient, OpenRouterClient>();
        services.AddHostedService<AiConnectionsService>();

        services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));

        services.AddScoped<IContentPublisher, ContentPublisher>();
        services.AddScoped<IContentScheduler, HangfireContentScheduler>();
        services.AddHostedService<ScheduledPublishReconciler>();

        services.AddPublishingDependencies(configuration);

        services.AddScoped<IFeedSeedService, FeedSeedService>();
        services.AddScoped<IIdeaSourceSeedService, IdeaSourceSeedService>();

        return services;
    }

    internal static IServiceCollection AddPublishingDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<MediumOptions>(configuration.GetSection(MediumOptions.SectionName));
        services.Configure<SubstackOptions>(configuration.GetSection(SubstackOptions.SectionName));
        services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
        services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));
        services.Configure<TransformerOptions>(configuration.GetSection(TransformerOptions.SectionName));

        // Security
        services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
        services.AddScoped<IOAuthService, OAuthService>();

        // Content transformation
        services.AddScoped<IContentTransformer, ContentTransformer>();

        // Keyed connectors
        services.AddKeyedScoped<IPlatformConnector, BlogConnector>(Platform.Blog);
        services.AddKeyedScoped<IPlatformConnector, MediumConnector>(Platform.Medium);
        services.AddKeyedScoped<IPlatformConnector, LinkedInConnector>(Platform.LinkedIn);
        services.AddKeyedScoped<IPlatformConnector, TwitterConnector>(Platform.Twitter);
        services.AddKeyedScoped<IPlatformConnector, SubstackConnector>(Platform.Substack);

        // Keyed formatters
        services.AddKeyedScoped<IPlatformFormatter, BlogFormatter>(Platform.Blog);
        services.AddKeyedScoped<IPlatformFormatter, MediumFormatter>(Platform.Medium);
        services.AddKeyedScoped<IPlatformFormatter, LinkedInFormatter>(Platform.LinkedIn);
        services.AddKeyedScoped<IPlatformFormatter, TwitterFormatter>(Platform.Twitter);
        services.AddKeyedScoped<IPlatformFormatter, SubstackFormatter>(Platform.Substack);

        // HttpClient factories
        services.AddHttpClient<MediumConnector>(client =>
        {
            client.BaseAddress = new Uri("https://api.medium.com");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("Accept-Charset", "utf-8");
        });

        services.AddHttpClient<LinkedInConnector>(client =>
        {
            client.BaseAddress = new Uri("https://api.linkedin.com");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddStandardResilienceHandler();

        services.AddHttpClient<TwitterConnector>(client =>
        {
            client.BaseAddress = new Uri("https://api.x.com");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddHttpClient<SubstackConnector>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<SubstackOptions>>().CurrentValue;
            var slug = string.IsNullOrEmpty(options.PublicationSlug) ? "default" : options.PublicationSlug;
            client.BaseAddress = new Uri($"https://{slug}.substack.com");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Retry handler
        services.AddScoped<IPublishRetryHandler, PublishRetryHandler>();

        return services;
    }
}
