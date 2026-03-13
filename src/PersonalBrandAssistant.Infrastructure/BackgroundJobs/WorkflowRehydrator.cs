using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class WorkflowRehydrator : IHostedService
{
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<WorkflowRehydrator> _logger;

    public WorkflowRehydrator(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<WorkflowRehydrator> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RehydrateStuckContentAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal async Task RehydrateStuckContentAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workflowEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var cutoff = _dateTimeProvider.UtcNow.Add(-StuckThreshold);

        var stuckContent = await context.Contents
            .Where(c => c.Status == ContentStatus.Publishing
                        && c.PublishingStartedAt != null
                        && c.PublishingStartedAt < cutoff)
            .ToListAsync(ct);

        foreach (var content in stuckContent)
        {
            var result = await workflowEngine.TransitionAsync(
                content.Id, ContentStatus.Scheduled, "Rehydrated: stuck in Publishing", ActorType.System, ct);

            if (result.IsSuccess)
            {
                content.PublishingStartedAt = null;
                await context.SaveChangesAsync(ct);
                _logger.LogWarning("Rehydrated stuck content {ContentId} from Publishing to Scheduled", content.Id);
            }
            else
            {
                _logger.LogError("Failed to rehydrate content {ContentId}: {Errors}",
                    content.Id, string.Join(", ", result.Errors));
            }
        }

        if (stuckContent.Count > 0)
            _logger.LogInformation("Rehydrated {Count} stuck content items", stuckContent.Count);
    }
}
