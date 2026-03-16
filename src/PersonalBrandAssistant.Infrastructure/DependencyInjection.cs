using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

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

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(
                configuration["DataProtection:KeyPath"] ?? "data-protection-keys"))
            .SetApplicationName("PersonalBrandAssistant");

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

        // Content pipeline
        services.Configure<ContentEngineOptions>(
            configuration.GetSection(ContentEngineOptions.SectionName));
        services.AddScoped<IBrandVoiceService, StubBrandVoiceService>();
        services.AddScoped<IContentPipeline, ContentPipeline>();
        services.AddScoped<IRepurposingService, RepurposingService>();
        services.AddScoped<IContentCalendarService, ContentCalendarService>();

        // Platform integration options
        services.Configure<PlatformIntegrationOptions>(configuration.GetSection(PlatformIntegrationOptions.SectionName));
        services.Configure<MediaStorageOptions>(configuration.GetSection("MediaStorage"));

        // Singleton services
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
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

        // Scoped services
        services.AddScoped<IPublishingPipeline, PublishingPipeline>();
        services.AddScoped<IOAuthManager, OAuthManager>();
        services.AddScoped<IRateLimiter, DatabaseRateLimiter>();

        // Platform adapters (multi-registration for IEnumerable<ISocialPlatform>)
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<TwitterPlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<LinkedInPlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<InstagramPlatformAdapter>());
        services.AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<YouTubePlatformAdapter>());

        // Content formatters (multi-registration)
        services.AddScoped<IPlatformContentFormatter, TwitterContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, LinkedInContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, InstagramContentFormatter>();
        services.AddScoped<IPlatformContentFormatter, YouTubeContentFormatter>();

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

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>();

        return services;
    }
}
