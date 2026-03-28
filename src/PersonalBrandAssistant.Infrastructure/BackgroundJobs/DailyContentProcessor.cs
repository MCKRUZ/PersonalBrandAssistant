using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public sealed class DailyContentProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ContentAutomationOptions _options;
    private readonly ILogger<DailyContentProcessor> _logger;

    public DailyContentProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        IOptions<ContentAutomationOptions> options,
        ILogger<DailyContentProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Daily content automation is disabled");
            return;
        }

        CronExpression cronExpression;
        try
        {
            cronExpression = CronExpression.Parse(_options.CronExpression);
        }
        catch (CronFormatException ex)
        {
            _logger.LogError(ex, "Invalid cron expression: {CronExpression}", _options.CronExpression);
            return;
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        }
        catch (TimeZoneNotFoundException ex)
        {
            _logger.LogError(ex, "Invalid timezone: {TimeZone}", _options.TimeZone);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var next = cronExpression.GetNextOccurrence(_dateTimeProvider.UtcNow, timeZone);
                if (next is null) break;

                var delay = next.Value - _dateTimeProvider.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                await ProcessScheduledRunAsync(timeZone, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily content processor loop");
            }
        }
    }

    internal async Task ProcessScheduledRunAsync(TimeZoneInfo timeZone, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Timezone-aware "today" check
        var nowInTz = TimeZoneInfo.ConvertTime(_dateTimeProvider.UtcNow, timeZone);
        var todayStart = new DateTimeOffset(nowInTz.Date, timeZone.GetUtcOffset(nowInTz.Date));
        var todayEnd = todayStart.AddDays(1);

        var existingRun = await dbContext.AutomationRuns
            .AnyAsync(r =>
                (r.Status == AutomationRunStatus.Completed || r.Status == AutomationRunStatus.Running)
                && r.TriggeredAt >= todayStart.UtcDateTime
                && r.TriggeredAt < todayEnd.UtcDateTime,
                ct);

        if (existingRun)
        {
            _logger.LogInformation("Skipping daily content run - already exists for today");
            return;
        }

        var orchestrator = scope.ServiceProvider.GetRequiredService<IDailyContentOrchestrator>();

        try
        {
            var result = await orchestrator.ExecuteAsync(_options, ct);
            _logger.LogInformation(
                "Daily content pipeline {Status}: RunId={RunId}, ContentId={ContentId}, Duration={DurationMs}ms",
                result.Success ? "completed" : "failed",
                result.RunId, result.PrimaryContentId, result.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily content pipeline failed with unhandled exception");

            var failedRun = AutomationRun.Create();
            failedRun.Fail(ex.Message, 0);
            dbContext.AutomationRuns.Add(failedRun);
            await dbContext.SaveChangesAsync(ct);
        }
    }
}
