using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
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

public class CalendarSlotProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ContentEngineOptions _options;
    private readonly ILogger<CalendarSlotProcessor> _logger;

    public CalendarSlotProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        IOptions<ContentEngineOptions> options,
        ILogger<CalendarSlotProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during calendar slot processing");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var calendarService = scope.ServiceProvider.GetRequiredService<IContentCalendarService>();
        var now = _dateTimeProvider.UtcNow;
        var windowEnd = now.AddDays(_options.SlotMaterializationDays);

        var activeSeries = await context.ContentSeries
            .Where(s => s.IsActive && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt > now))
            .ToListAsync(ct);

        var newSlotCount = 0;

        foreach (var series in activeSeries)
        {
            try
            {
                var occurrences = GetOccurrences(series, now, windowEnd);

                var existingSlots = await context.CalendarSlots
                    .Where(s => s.ContentSeriesId == series.Id
                                && s.ScheduledAt >= now && s.ScheduledAt <= windowEnd)
                    .Select(s => s.ScheduledAt)
                    .ToListAsync(ct);

                var existingSet = existingSlots.ToHashSet();

                foreach (var occurrence in occurrences)
                {
                    if (existingSet.Contains(occurrence))
                        continue;

                    foreach (var platform in series.TargetPlatforms)
                    {
                        context.CalendarSlots.Add(new CalendarSlot
                        {
                            ScheduledAt = occurrence,
                            Platform = platform,
                            ContentSeriesId = series.Id,
                            Status = CalendarSlotStatus.Open,
                        });
                        newSlotCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing series {SeriesId}", series.Id);
            }
        }

        if (newSlotCount > 0)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Materialized {Count} calendar slots", newSlotCount);
        }

        // Auto-fill uses GlobalLevel (not per-ContentType ResolveLevel) because
        // auto-fill spans multiple content types and there's no single type to resolve against.
        var autonomyConfig = await context.AutonomyConfigurations.FirstOrDefaultAsync(ct)
                             ?? AutonomyConfiguration.CreateDefault();

        if (autonomyConfig.GlobalLevel == AutonomyLevel.Autonomous)
        {
            var fillResult = await calendarService.AutoFillSlotsAsync(now, windowEnd, ct);

            if (fillResult.IsSuccess)
            {
                _logger.LogInformation("Auto-filled {Count} calendar slots", fillResult.Value);
            }
            else
            {
                _logger.LogWarning("Auto-fill failed: {Errors}", string.Join(", ", fillResult.Errors));
            }
        }
    }

    internal static List<DateTimeOffset> GetOccurrences(
        ContentSeries series, DateTimeOffset from, DateTimeOffset to)
    {
        var calEvent = new CalendarEvent
        {
            DtStart = new CalDateTime(series.StartsAt.DateTime, series.TimeZoneId),
        };

        calEvent.RecurrenceRules.Add(new RecurrencePattern(series.RecurrenceRule));

        var fromCal = new CalDateTime(from.UtcDateTime);

        return calEvent.GetOccurrences(fromCal)
            .TakeWhile(o => o.Period.StartTime.Value <= to.UtcDateTime)
            .Select(o => new DateTimeOffset(o.Period.StartTime.Value, TimeSpan.Zero))
            .ToList();
    }
}
