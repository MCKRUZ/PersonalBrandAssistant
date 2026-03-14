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
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
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
        services.AddScoped<IPublishingPipeline, PublishingPipelineStub>();

        services.AddHostedService<DataSeeder>();
        services.AddHostedService<AuditLogCleanupService>();
        services.AddHostedService<ScheduledPublishProcessor>();
        services.AddHostedService<RetryFailedProcessor>();
        services.AddHostedService<WorkflowRehydrator>();
        services.AddHostedService<RetentionCleanupService>();

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>();

        return services;
    }
}
