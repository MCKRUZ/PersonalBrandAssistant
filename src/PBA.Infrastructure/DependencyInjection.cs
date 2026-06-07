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
        services.AddHttpClient<RssFeedReader>(client =>
        {
            // Many feeds (blogs.windows.com, news.microsoft.com, Ars, etc.) 403 a default/bot
            // User-Agent. Present a browser-like UA so RSS fetches aren't blocked.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        });
        services.AddScoped<IRssFeedReader, RssFeedReader>();

        // Keyed source scrapers (one per IdeaSourceType). SourcePollingService dispatches by type.
        services.Configure<HackerNewsOptions>(configuration.GetSection(HackerNewsOptions.SectionName));
        services.Configure<GitHubScraperOptions>(configuration.GetSection(GitHubScraperOptions.SectionName));

        services.AddScoped<PBA.Infrastructure.Services.Scrapers.RssScraper>();
        services.AddKeyedScoped<ISourceScraper>(IdeaSourceType.RSS,
            (sp, _) => sp.GetRequiredService<PBA.Infrastructure.Services.Scrapers.RssScraper>());
        services.AddHttpClient<PBA.Infrastructure.Services.Scrapers.HackerNewsScraper>();
        services.AddKeyedScoped<ISourceScraper>(IdeaSourceType.HackerNews,
            (sp, _) => sp.GetRequiredService<PBA.Infrastructure.Services.Scrapers.HackerNewsScraper>());
        services.AddHttpClient<PBA.Infrastructure.Services.Scrapers.GitHubScraper>(client =>
        {
            client.BaseAddress = new Uri("https://api.github.com");
        });
        services.AddKeyedScoped<ISourceScraper>(IdeaSourceType.GitHub,
            (sp, _) => sp.GetRequiredService<PBA.Infrastructure.Services.Scrapers.GitHubScraper>());

        services.AddHostedService<SourcePollingService>();

        services.Configure<SidecarOptions>(configuration.GetSection(SidecarOptions.SectionName));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        // Drafting routes through OpenRouter (fast, length-controllable) instead of the
        // agentic claude CLI. ProcessRunner/SidecarClient stay registered as a fallback.
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));
        services.AddHttpClient<ISidecarClient, OpenRouterClient>();
        services.AddHostedService<AiConnectionsService>();

        // AI News Radar (Horizon-inspired): scoring -> clustering -> daily digest.
        services.Configure<IdeaScoringOptions>(configuration.GetSection(IdeaScoringOptions.SectionName));
        services.Configure<ClusteringOptions>(configuration.GetSection(ClusteringOptions.SectionName));
        services.Configure<DigestOptions>(configuration.GetSection(DigestOptions.SectionName));

        services.AddScoped<IIdeaAnalyzer, PBA.Infrastructure.Services.Radar.IdeaAnalyzer>();
        services.AddScoped<IIdeaClusterer, PBA.Infrastructure.Services.Radar.IdeaClusterer>();
        services.AddScoped<IDigestWriter, PBA.Infrastructure.Services.Radar.DigestWriter>();

        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaScoringService>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaClusteringService>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.DigestService>();

        // AI News Radar Phase 2: external delivery (email + Discord) + instant high-score alerts.
        services.Configure<DigestDeliveryOptions>(configuration.GetSection(DigestDeliveryOptions.SectionName));
        services.AddScoped<IDigestDeliverySender, PBA.Infrastructure.Services.Radar.Delivery.EmailDigestSender>();
        services.AddHttpClient<PBA.Infrastructure.Services.Radar.Delivery.DiscordDigestSender>();
        services.AddScoped<IDigestDeliverySender>(sp =>
            sp.GetRequiredService<PBA.Infrastructure.Services.Radar.Delivery.DiscordDigestSender>());
        services.AddScoped<IDeliveryDispatcher, PBA.Infrastructure.Services.Radar.Delivery.DeliveryDispatcher>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.HighScoreAlertService>();

        services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));

        services.AddScoped<IContentPublisher, ContentPublisher>();
        services.AddScoped<IContentScheduler, HangfireContentScheduler>();
        services.AddHostedService<ScheduledPublishReconciler>();

        services.AddPublishingDependencies(configuration);

        services.AddScoped<IFeedSeedService, FeedSeedService>();
        services.AddScoped<IIdeaSourceSeedService, IdeaSourceSeedService>();

        services.Configure<GoogleAnalyticsOptions>(
            configuration.GetSection(GoogleAnalyticsOptions.SectionName));
        services.AddSingleton<IGa4Client, PBA.Infrastructure.Services.Analytics.Ga4Client>();
        services.AddSingleton<ISearchConsoleClient, PBA.Infrastructure.Services.Analytics.SearchConsoleClient>();
        services.AddScoped<IGoogleAnalyticsService, PBA.Infrastructure.Services.Analytics.GoogleAnalyticsService>();

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
        services.Configure<ComfyUiOptions>(configuration.GetSection(ComfyUiOptions.SectionName));

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

        // Hero image generation via self-hosted ComfyUI (BaseAddress is per-request from options)
        services.AddHttpClient<IHeroImageGenerator, ComfyUiHeroImageGenerator>();

        // Retry handler
        services.AddScoped<IPublishRetryHandler, PublishRetryHandler>();

        // Pure static-site index weaver (no IO; safe as a singleton)
        services.AddSingleton<IBlogIndexUpdater, BlogIndexUpdater>();

        return services;
    }
}
