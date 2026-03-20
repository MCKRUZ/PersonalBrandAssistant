using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCrontab;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Data;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class EngagementScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<EngagementScheduler> _logger;

    public EngagementScheduler(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        ILogger<EngagementScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessDueTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during engagement scheduling");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessDueTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var engagementService = scope.ServiceProvider.GetRequiredService<ISocialEngagementService>();
        var now = _dateTimeProvider.UtcNow;

        var dueTasks = await context.EngagementTasks
            .Where(t => t.IsEnabled && t.AutoRespond && t.NextExecutionAt != null && t.NextExecutionAt <= now)
            .ToListAsync(ct);

        var humanScheduler = scope.ServiceProvider.GetRequiredService<IHumanScheduler>();

        foreach (var task in dueTasks)
        {
            if (task.SchedulingMode == SchedulingMode.HumanLike && humanScheduler.ShouldSkipExecution(task))
            {
                task.SkippedLastExecution = true;
                var baseCronNext = CrontabSchedule.Parse(task.CronExpression).GetNextOccurrence(now.DateTime);
                task.NextExecutionAt = humanScheduler.ComputeNextHumanExecution(
                    task, new DateTimeOffset(baseCronNext, TimeSpan.Zero));
                await context.SaveChangesAsync(ct);
                _logger.LogInformation("Skipped engagement task {TaskId} (human-like scheduling)", task.Id);
                continue;
            }

            task.SkippedLastExecution = false;

            _logger.LogInformation("Executing engagement task {TaskId} for {Platform}",
                task.Id, task.Platform);

            var result = await engagementService.ExecuteTaskAsync(task.Id, ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Engagement task {TaskId} failed: {Errors}",
                    task.Id, string.Join(", ", result.Errors));
            }
        }
    }
}
