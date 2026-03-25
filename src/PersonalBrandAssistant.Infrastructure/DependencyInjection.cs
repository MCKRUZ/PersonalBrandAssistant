using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Agents;
using PersonalBrandAssistant.Infrastructure.Agents.Capabilities;
using PersonalBrandAssistant.Infrastructure.BackgroundJobs;
using PersonalBrandAssistant.Infrastructure.Data;
using PersonalBrandAssistant.Infrastructure.Data.Interceptors;
using PersonalBrandAssistant.Infrastructure.Services;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices;
using PersonalBrandAssistant.Infrastructure.Services.MediaServices;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;
using PersonalBrandAssistant.Infrastructure.Services.IntegrationServices;
using PersonalBrandAssistant.Infrastructure.Services.ContentAutomation;
using PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;
using PersonalBrandAssistant.Infrastructure.Services.SocialServices;

namespace PersonalBrandAssistant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<AuditableInterceptor>();
        services.AddScoped<AuditLogInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<AuditableInterceptor>(),
                sp.GetRequiredService<AuditLogInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        var dpBuilder = services.AddDataProtection()
            .SetApplicationName("PersonalBrandAssistant");

        var keyPath = configuration["DataProtection:KeyPath"];
        if (!string.IsNullOrWhiteSpace(keyPath) && IsDirectoryWritable(keyPath))
            dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        // else: ephemeral (in-memory) keys — fine for dev/single-instance

        services.AddSingleton<IEncryptionService, EncryptionService>();

        // Agent orchestration
        services.Configure<AgentOrchestrationOptions>(
            configuration.GetSection(AgentOrchestrationOptions.SectionName));
        services.AddSingleton<ISidecarClient, SidecarClient>();
        services.AddSingleton<IPromptTemplateService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgentOrchestrationOptions>>().Value;
            return new PromptTemplateService(
                opts.PromptsPath,
                sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PromptTemplateService>>());
        });
        services.AddScoped<ITokenTracker, TokenTracker>();
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IAgentCapability, WriterAgentCapability>();
        services.AddScoped<IAgentCapability, SocialAgentCapability>();
        services.AddScoped<IAgentCapability, RepurposeAgentCapability>();
        services.AddScoped<IAgentCapability, EngagementAgentCapability>();
        services.AddScoped<IAgentCapability, AnalyticsAgentCapability>();

        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IContentScheduler, ContentScheduler>();
        services.AddScoped<INotificationService, NotificationService>();

        // Content engine options
        services.Configure<SidecarOptions>(
            configuration.GetSection(SidecarOptions.SectionName));
        services.Configure<ContentEngineOptions>(
            configuration.GetSection(ContentEngineOptions.SectionName));
        services.Configure<TrendMonitoringOptions>(
            configuration.GetSection(TrendMonitoringOptions.SectionName));
        services.Configure<FirecrawlOptions>(
            configuration.GetSection(FirecrawlOptions.SectionName));

        // Content engine services
        services.AddScoped<IBrandVoiceService, BrandVoiceService>();
        services.AddScoped<IContentPipeline, ContentPipeline>();
        services.AddScoped<IRepurposingService, RepurposingService>();
        services.AddScoped<IContentCalendarService, ContentCalendarService>();
        services.AddScoped<ITrendMonitor, TrendMonitor>();
        services.AddScoped<IEngagementAggregator, EngagementAggregator>();

        // Trend source pollers
        services.AddScoped<ITrendSourcePoller, FreshRssPoller>();
        services.AddScoped<ITrendSourcePoller, HackerNewsPoller>();
        services.AddScoped<ITrendSourcePoller, RedditPoller>();
        services.AddScoped<ITrendSourcePoller, TrendRadarPoller>();
        services.AddScoped<ITrendSourcePoller, RssFeedPoller>();

        var feedTimeoutSeconds = configuration.GetValue("TrendMonitoring:FeedTimeoutSeconds", 30);
        services.AddHttpClient("RssFeed", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(feedTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
        });

        services.AddHttpClient("Firecrawl", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<FirecrawlOptions>>().Value;
            var baseUrl = opts.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            var apiKey = configuration["Firecrawl:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        });
        services.AddScoped<IArticleScraper, FirecrawlScraper>();
        services.AddScoped<IArticleAnalyzer, ArticleAnalyzer>();
        services.AddScoped<IContentIdeaService, ContentIdeaService>();

        // Platform integration options
        services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
        services.Configure<MediaStorageOptions>(configuration.GetSection("MediaStorage"));

        // Singleton services
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(30),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });
        services.AddSingleton<IMediaStorage, LocalMediaStorage>();

        // Platform adapters with typed HttpClients
        services.AddHttpClient<TwitterPlatformAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:Twitter:BaseUrl"] ?? "https://api.x.com/2");
        });
        services.AddHttpClient<LinkedInPlatformAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:LinkedIn:BaseUrl"] ?? "https://api.linkedin.com/rest");
            client.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
            var apiVersion = configuration["PlatformIntegrations:LinkedIn:ApiVersion"] ?? "202603";
            client.DefaultRequestHeaders.Add("Linkedin-Version", apiVersion);
        });
        services.AddHttpClient<InstagramPlatformAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:Instagram:BaseUrl"] ?? "https://graph.facebook.com/v19.0");
        });
        services.AddHttpClient<YouTubePlatformAdapter>();
        services.AddHttpClient<RedditPlatformAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:Reddit:BaseUrl"] ?? "https://oauth.reddit.com");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                configuration["PlatformIntegrations:Reddit:UserAgent"]
                ?? "PersonalBrandAssistant/1.0 (by /u/personal-brand-bot)");
        });

        // Scoped services
        services.AddScoped<IPublishingPipeline, PublishingPipeline>();
        services.AddScoped<IOAuthManager, OAuthManager>();
        services.AddScoped<IRateLimiter, DatabaseRateLimiter>();

        // Platform adapters (multi-registration for IEnumerable<ISocialPlatform>)
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<TwitterPlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<LinkedInPlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<InstagramPlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<YouTubePlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<RedditPlatformAdapter>());

        // Content formatters (multi-registration)
        services.AddScoped<IPlatformContentFormatter, TwitterContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, LinkedInContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, InstagramContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, YouTubeContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, RedditContentFormatter>();

        // Social engagement adapters
        services.AddScoped<ISocialEngagementAdapter>(sp => sp.GetRequiredService<RedditPlatformAdapter>());
        services.AddHttpClient<TwitterEngagementAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:Twitter:BaseUrl"] ?? "https://api.x.com/2");
        });
        services.AddScoped<ISocialEngagementAdapter>(sp => sp.GetRequiredService<TwitterEngagementAdapter>());
        services.AddHttpClient<LinkedInEngagementAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:LinkedIn:BaseUrl"] ?? "https://api.linkedin.com/rest");
        });
        services.AddScoped<ISocialEngagementAdapter>(sp => sp.GetRequiredService<LinkedInEngagementAdapter>());
        services.AddHttpClient<InstagramEngagementAdapter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PlatformIntegrations:Instagram:BaseUrl"] ?? "https://graph.facebook.com/v19.0");
        });
        services.AddScoped<ISocialEngagementAdapter>(sp => sp.GetRequiredService<InstagramEngagementAdapter>());
        services.AddScoped<IHumanScheduler, HumanScheduler>();
        services.AddScoped<ISocialEngagementService, SocialEngagementService>();
        services.AddScoped<ISocialInboxService, SocialInboxService>();
        services.AddScoped<IIntegrationMonitorService, IntegrationMonitorService>();
        services.AddSingleton<IPipelineEventBroadcaster, PipelineEventBroadcaster>();

        // Background services
        services.AddHostedService<DataSeeder>();
        services.AddHostedService<AuditLogCleanupService>();
        services.AddHostedService<ScheduledPublishProcessor>();
        services.AddHostedService<RetryFailedProcessor>();
        services.AddHostedService<WorkflowRehydrator>();
        services.AddHostedService<RetentionCleanupService>();
        services.AddHostedService<TokenRefreshProcessor>();
        services.AddHostedService<PlatformHealthMonitor>();
        services.AddHostedService<PublishCompletionPoller>();

        // Content engine background services
        services.AddHostedService<RepurposeOnPublishProcessor>();
        services.AddHostedService<TrendAggregationProcessor>();
        services.AddHostedService<EngagementAggregationProcessor>();
        services.AddHostedService<CalendarSlotProcessor>();

        // Social engagement background services
        services.AddHostedService<EngagementScheduler>();
        services.AddHostedService<InboxPoller>();

        // Substack RSS
        services.Configure<SubstackOptions>(
            configuration.GetSection(SubstackOptions.SectionName));
        services.AddHttpClient<ISubstackService, SubstackService>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // Polly handles timeouts
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.UseJitter = true;
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout
                || args.Outcome.Exception is HttpRequestException or TaskCanceledException);
            options.CircuitBreaker.FailureRatio = 1.0;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.MinimumThroughput = 3;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        });

        // Google Analytics / Search Console
        services.Configure<GoogleAnalyticsOptions>(
            configuration.GetSection(GoogleAnalyticsOptions.SectionName));

        var gaCredentialsPath = configuration
            .GetSection(GoogleAnalyticsOptions.SectionName)
            .GetValue<string>("CredentialsPath") ?? "secrets/google-analytics-sa.json";

        if (File.Exists(gaCredentialsPath))
        {
#pragma warning disable CS0618 // GoogleCredential.FromFile deprecated; CredentialFactory not available in this version
            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(gaCredentialsPath);
#pragma warning restore CS0618
            services.AddSingleton<IGa4Client>(sp =>
            {
                var builder = new Google.Analytics.Data.V1Beta.BetaAnalyticsDataClientBuilder
                {
                    GoogleCredential = credential
                };
                return new Ga4ClientWrapper(builder.Build());
            });
            services.AddSingleton<ISearchConsoleClient>(sp =>
            {
                var scoped = credential.CreateScoped(Google.Apis.SearchConsole.v1.SearchConsoleService.Scope.WebmastersReadonly);
                var service = new Google.Apis.SearchConsole.v1.SearchConsoleService(
                    new Google.Apis.Services.BaseClientService.Initializer
                    {
                        HttpClientInitializer = scoped,
                        ApplicationName = "PersonalBrandAssistant"
                    });
                return new SearchConsoleClientWrapper(service);
            });
        }
        else
        {
            services.AddSingleton<IGa4Client>(new NullGa4Client());
            services.AddSingleton<ISearchConsoleClient>(new NullSearchConsoleClient());
        }
        services.AddScoped<IGoogleAnalyticsService, GoogleAnalyticsService>();
        services.AddSingleton<DashboardRefreshLimiter>();
        services.AddScoped<DashboardAggregator>();
        services.AddScoped<CachedDashboardAggregator>(sp =>
            new CachedDashboardAggregator(
                sp.GetRequiredService<DashboardAggregator>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
                sp.GetRequiredService<DashboardRefreshLimiter>(),
                sp.GetRequiredService<ILogger<CachedDashboardAggregator>>(),
                sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IDashboardAggregator>(sp =>
            sp.GetRequiredService<CachedDashboardAggregator>());
        services.AddScoped<IDashboardCacheInvalidator>(sp =>
            sp.GetRequiredService<CachedDashboardAggregator>());

        // Content automation
        services.Configure<ContentAutomationOptions>(
            configuration.GetSection(ContentAutomationOptions.SectionName));
        services.AddScoped<IDailyContentOrchestrator, DailyContentOrchestrator>();
        services.AddHttpClient("ComfyUI", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ContentAutomationOptions>>().Value.ImageGeneration;
            client.BaseAddress = new Uri(opts.ComfyUiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });
        services.AddSingleton<IComfyUiClient, ComfyUiClient>();
        services.AddScoped<IImageGenerationService, ImageGenerationService>();
        services.AddScoped<IImagePromptService, ImagePromptService>();
        services.AddSingleton<IImageResizer, ImageResizer>();
        services.AddHostedService<DailyContentProcessor>();

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>();

        return services;
    }

    private static bool IsDirectoryWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = System.IO.Path.Combine(path, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}
