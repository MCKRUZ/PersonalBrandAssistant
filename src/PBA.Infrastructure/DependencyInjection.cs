using System.Net.Http.Headers;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Connectors;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Media;
using PBA.Infrastructure.Publishing;
using PBA.Infrastructure.Seeding;
using PBA.Infrastructure.Security;
using PBA.Infrastructure.Security.OAuthProviders;
using PBA.Infrastructure.Services;
using PBA.Infrastructure.Transformers;
using Npgsql;
using Pgvector.EntityFrameworkCore;

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
        dataSourceBuilder.UseVector(); // pgvector type mapping at the Npgsql data-source level
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                dataSource,
                npgsql => npgsql
                    .UseVector()
                    .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddHttpClient();

        services.Configure<SourcePollingOptions>(configuration.GetSection(SourcePollingOptions.SectionName));
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

        // Brand-anchored feed ranking: embedding model + runtime-tunable ranking thresholds.
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
        services.Configure<RankingOptions>(configuration.GetSection(RankingOptions.SectionName));

        // AI News Radar (Horizon-inspired): scoring -> clustering -> daily digest.
        services.Configure<IdeaScoringOptions>(configuration.GetSection(IdeaScoringOptions.SectionName));
        services.Configure<ClusteringOptions>(configuration.GetSection(ClusteringOptions.SectionName));
        services.Configure<DigestOptions>(configuration.GetSection(DigestOptions.SectionName));

        services.AddScoped<IIdeaAnalyzer, PBA.Infrastructure.Services.Radar.IdeaAnalyzer>();
        services.AddScoped<PBA.Infrastructure.Services.Radar.IdeaEmbeddingService>();
        services.AddScoped<IDigestWriter, PBA.Infrastructure.Services.Radar.DigestWriter>();

        services.Configure<ChannelAnalyticsOptions>(configuration.GetSection(ChannelAnalyticsOptions.SectionName));

        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaScoringService>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.IdeaDedupService>();
        services.AddHostedService<PBA.Infrastructure.Services.Radar.DigestService>();
        services.AddHostedService<PBA.Infrastructure.Services.Analytics.ChannelMetricPollingService>();

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
        services.AddScoped<IBrandRankingProfileSeedService, BrandRankingProfileSeedService>();

        services.Configure<GoogleAnalyticsOptions>(
            configuration.GetSection(GoogleAnalyticsOptions.SectionName));
        services.AddSingleton<IGa4Client, PBA.Infrastructure.Services.Analytics.Ga4Client>();
        services.AddSingleton<ISearchConsoleClient, PBA.Infrastructure.Services.Analytics.SearchConsoleClient>();
        services.AddScoped<IGoogleAnalyticsService, PBA.Infrastructure.Services.Analytics.GoogleAnalyticsService>();

        // Channel analytics thin clients (SDK/HTTP seams) + keyed per-platform facades.
        services.AddScoped<IYouTubeApiClient, PBA.Infrastructure.Services.Analytics.YouTubeApiClient>();
        services.AddHttpClient<IInstagramGraphClient, PBA.Infrastructure.Services.Analytics.InstagramGraphClient>(
            client => client.BaseAddress = new Uri("https://graph.instagram.com/"));
        services.AddHttpClient<ITikTokDisplayClient, PBA.Infrastructure.Services.Analytics.TikTokDisplayClient>(
            client => client.BaseAddress = new Uri("https://open.tiktokapis.com/"));

        services.AddKeyedScoped<IChannelAnalyticsService,
            PBA.Infrastructure.Services.Analytics.YouTubeAnalyticsService>(Platform.YouTube);
        services.AddKeyedScoped<IChannelAnalyticsService,
            PBA.Infrastructure.Services.Analytics.InstagramAnalyticsService>(Platform.Instagram);
        services.AddKeyedScoped<IChannelAnalyticsService,
            PBA.Infrastructure.Services.Analytics.TikTokAnalyticsService>(Platform.TikTok);

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
        services.Configure<YouTubeOAuthOptions>(configuration.GetSection(YouTubeOAuthOptions.SectionName));
        services.Configure<InstagramOAuthOptions>(configuration.GetSection(InstagramOAuthOptions.SectionName));
        services.Configure<TikTokOAuthOptions>(configuration.GetSection(TikTokOAuthOptions.SectionName));
        services.Configure<TransformerOptions>(configuration.GetSection(TransformerOptions.SectionName));
        services.Configure<ComfyUiOptions>(configuration.GetSection(ComfyUiOptions.SectionName));
        services.Configure<BufferOptions>(configuration.GetSection(BufferOptions.SectionName));
        services.Configure<R2Options>(configuration.GetSection(R2Options.SectionName));

        // Security
        services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
        services.AddScoped<IOAuthService, OAuthService>();

        // On-demand token freshness for the live analytics read path.
        services.AddScoped<IAnalyticsTokenProvider, AnalyticsTokenProvider>();

        // Keyed OAuth providers (resolved by the OAuthService coordinator)
        services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
        services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);
        services.AddKeyedScoped<IOAuthProvider, YouTubeOAuthProvider>(Platform.YouTube);
        services.AddKeyedScoped<IOAuthProvider, InstagramOAuthProvider>(Platform.Instagram);
        services.AddKeyedScoped<IOAuthProvider, TikTokOAuthProvider>(Platform.TikTok);

        // Content transformation
        services.AddScoped<IContentTransformer, ContentTransformer>();

        // Keyed connectors
        services.AddKeyedScoped<IPlatformConnector, BlogConnector>(Platform.Blog);
        services.AddKeyedScoped<IPlatformConnector, MediumConnector>(Platform.Medium);
        services.AddKeyedScoped<IPlatformConnector, LinkedInConnector>(Platform.LinkedIn);
        services.AddKeyedScoped<IPlatformConnector, TwitterConnector>(Platform.Twitter);
        services.AddKeyedScoped<IPlatformConnector, SubstackConnector>(Platform.Substack);
        // Resolved through the typed-client factory, NOT as a plain keyed type. AddHttpClient<BufferConnector>
        // below sets BaseAddress on the client it builds; a plain keyed registration constructs the
        // connector straight from the container instead, handing it an unconfigured HttpClient — every
        // Buffer call then throws "An invalid request URI was provided". That made TikTok publishing
        // impossible through ContentPublisher while working fine from a hand-built connector.
        services.AddKeyedScoped<IPlatformConnector>(Platform.TikTok,
            (sp, _) => sp.GetRequiredService<BufferConnector>());

        // Keyed formatters
        services.AddKeyedScoped<IPlatformFormatter, BlogFormatter>(Platform.Blog);
        services.AddKeyedScoped<IPlatformFormatter, MediumFormatter>(Platform.Medium);
        services.AddKeyedScoped<IPlatformFormatter, LinkedInFormatter>(Platform.LinkedIn);
        services.AddKeyedScoped<IPlatformFormatter, TwitterFormatter>(Platform.Twitter);
        services.AddKeyedScoped<IPlatformFormatter, SubstackFormatter>(Platform.Substack);
        services.AddKeyedScoped<IPlatformFormatter, TikTokFormatter>(Platform.TikTok);

        // TikTok-via-Buffer media hosting: videos are uploaded to R2 and served to Buffer from the
        // bucket's public custom domain (Buffer fetches media by URL, never raw bytes, and HEAD-probes
        // it first — so the URL is public and unsigned, not presigned).
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var r2 = sp.GetRequiredService<IOptionsMonitor<R2Options>>().CurrentValue;
            var config = new AmazonS3Config
            {
                ServiceURL = r2.Endpoint,
                ForcePathStyle = true,
                // R2's canonical SigV4 region is "auto". Match the proven boto3 IG-lane config.
                AuthenticationRegion = "auto"
            };
            return new AmazonS3Client(
                new Amazon.Runtime.BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey), config);
        });
        services.AddScoped<IMediaHost, R2MediaHost>();

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

        services.AddHttpClient<BufferConnector>(client =>
        {
            client.BaseAddress = new Uri("https://api.buffer.com");
            // Must exceed the resilience pipeline's total timeout below, or HttpClient cancels first.
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddStandardResilienceHandler(o =>
        {
            // NEVER retry. createPost is not idempotent and Buffer has no request-dedup key, so a
            // retried mutation posts again. The default handler retried a createPost that had in
            // fact succeeded but answered slowly, and published the same clip to TikTok three times
            // (2026-08-03). A duplicate public post is far worse than a failed one: the caller can
            // retry a failure deliberately, but cannot un-publish.
            // This also disables retries for the two read queries, which is an acceptable trade —
            // they are cheap for the caller to repeat.
            o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);

            // createPost carries a video asset and Buffer validates the media URL before accepting,
            // so it routinely runs past the 30s default that caused the timeout in the first place.
            o.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
            // The handler requires SamplingDuration >= 2x AttemptTimeout.
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
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
