using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class RetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<RetentionCleanupService> _logger;
    private readonly int _workflowLogRetentionDays;
    private readonly int _notificationRetentionDays;

    public RetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        IConfiguration configuration,
        ILogger<RetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _workflowLogRetentionDays = configuration.GetValue("Retention:WorkflowTransitionLogDays", 180);
        _notificationRetentionDays = configuration.GetValue("Retention:NotificationDays", 90);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during retention cleanup");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    internal async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var workflowCutoff = _dateTimeProvider.UtcNow.AddDays(-_workflowLogRetentionDays);
        var deletedLogs = await context.WorkflowTransitionLogs
            .Where(l => l.Timestamp < workflowCutoff)
            .ExecuteDeleteAsync(ct);

        if (deletedLogs > 0)
            _logger.LogInformation("Deleted {Count} workflow transition logs older than {Days} days",
                deletedLogs, _workflowLogRetentionDays);

        var notificationCutoff = _dateTimeProvider.UtcNow.AddDays(-_notificationRetentionDays);
        var deletedNotifications = await context.Notifications
            .Where(n => n.CreatedAt < notificationCutoff)
            .ExecuteDeleteAsync(ct);

        if (deletedNotifications > 0)
            _logger.LogInformation("Deleted {Count} notifications older than {Days} days",
                deletedNotifications, _notificationRetentionDays);
    }
}
