using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class ContentCalendarService : IContentCalendarService
{
    private const double OccurrenceMatchToleranceMinutes = 1.0;

    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ContentCalendarService> _logger;

    public ContentCalendarService(
        IApplicationDbContext dbContext,
        ILogger<ContentCalendarService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<CalendarSlot>>> GetSlotsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var activeSeries = await _dbContext.ContentSeries
            .Where(s => s.IsActive && s.StartsAt <= to && (s.EndsAt == null || s.EndsAt >= from))
            .ToListAsync(ct);

        var materializedSlots = await _dbContext.CalendarSlots
            .Where(s => s.ScheduledAt >= from && s.ScheduledAt <= to)
            .ToListAsync(ct);

        var result = new List<CalendarSlot>();

        // Add generated occurrences from active series
        foreach (var series in activeSeries)
        {
            var occurrences = GenerateOccurrences(series, from, to);

            foreach (var occurrence in occurrences)
            {
                // Check if already materialized
                var existing = materializedSlots.FirstOrDefault(s =>
                    s.ContentSeriesId == series.Id &&
                    Math.Abs((s.ScheduledAt - occurrence).TotalMinutes) < OccurrenceMatchToleranceMinutes);

                if (existing is not null)
                {
                    result.Add(existing);
                    materializedSlots.Remove(existing);
                }
                else
                {
                    // Create transient slot
                    result.Add(new CalendarSlot
                    {
                        ScheduledAt = occurrence,
                        Platform = series.TargetPlatforms.Length > 0
                            ? series.TargetPlatforms[0]
                            : PlatformType.TwitterX,
                        ContentSeriesId = series.Id,
                        Status = CalendarSlotStatus.Open,
                    });
                }
            }
        }

        // Add remaining materialized slots (manual slots or orphaned)
        result.AddRange(materializedSlots);

        return Result<IReadOnlyList<CalendarSlot>>.Success(
            result.OrderBy(s => s.ScheduledAt).ToList());
    }

    public async Task<Result<Guid>> CreateSeriesAsync(ContentSeriesRequest request, CancellationToken ct)
    {
        if (!TryParseRRule(request.RecurrenceRule, out _))
        {
            return Result<Guid>.Failure(ErrorCode.ValidationFailed, "Invalid RRULE format");
        }

        var series = new ContentSeries
        {
            Name = request.Name,
            Description = request.Description,
            RecurrenceRule = request.RecurrenceRule,
            TargetPlatforms = request.TargetPlatforms,
            ContentType = request.ContentType,
            ThemeTags = request.ThemeTags,
            TimeZoneId = request.TimeZoneId,
            IsActive = true,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
        };

        _dbContext.ContentSeries.Add(series);
        await _dbContext.SaveChangesAsync(ct);

        return Result<Guid>.Success(series.Id);
    }

    public async Task<Result<Guid>> CreateManualSlotAsync(CalendarSlotRequest request, CancellationToken ct)
    {
        var slot = new CalendarSlot
        {
            ScheduledAt = request.ScheduledAt,
            Platform = request.Platform,
            ContentSeriesId = null,
            Status = CalendarSlotStatus.Open,
        };

        _dbContext.CalendarSlots.Add(slot);
        await _dbContext.SaveChangesAsync(ct);

        return Result<Guid>.Success(slot.Id);
    }

    public async Task<Result<MediatR.Unit>> AssignContentAsync(
        Guid slotId, Guid contentId, CancellationToken ct)
    {
        var slot = await _dbContext.CalendarSlots.FindAsync([slotId], ct);
        if (slot is null)
        {
            return Result<MediatR.Unit>.NotFound($"Slot {slotId} not found");
        }

        if (slot.Status != CalendarSlotStatus.Open)
        {
            return Result<MediatR.Unit>.Conflict($"Slot is already {slot.Status}");
        }

        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null)
        {
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found");
        }

        slot.ContentId = contentId;
        slot.Status = CalendarSlotStatus.Filled;
        await _dbContext.SaveChangesAsync(ct);

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    public async Task<Result<int>> AutoFillSlotsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var openSlots = await _dbContext.CalendarSlots
            .Where(s => s.ScheduledAt >= from && s.ScheduledAt <= to && s.Status == CalendarSlotStatus.Open)
            .ToListAsync(ct);

        // Load all series for theme tag matching
        var seriesIds = openSlots
            .Where(s => s.ContentSeriesId.HasValue)
            .Select(s => s.ContentSeriesId!.Value)
            .Distinct()
            .ToList();
        var seriesMap = await _dbContext.ContentSeries
            .Where(s => seriesIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        // Load approved content not yet assigned to any slot (uses NOT EXISTS subquery)
        var candidates = await _dbContext.Contents
            .Where(c => c.Status == ContentStatus.Approved
                && !_dbContext.CalendarSlots.Any(s => s.ContentId == c.Id))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var filled = 0;
        var usedContentIds = new HashSet<Guid>();

        foreach (var slot in openSlots.OrderBy(s => s.ScheduledAt))
        {
            var matching = candidates
                .Where(c => c.TargetPlatforms.Contains(slot.Platform) && !usedContentIds.Contains(c.Id))
                .ToList();

            if (matching.Count == 0) continue;

            // Score by theme tag affinity
            Content? best = null;
            var bestScore = -1;

            if (slot.ContentSeriesId.HasValue &&
                seriesMap.TryGetValue(slot.ContentSeriesId.Value, out var series))
            {
                foreach (var candidate in matching)
                {
                    var score = series.ThemeTags
                        .Count(tag => candidate.Metadata.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            best ??= matching[0];

            slot.ContentId = best.Id;
            slot.Status = CalendarSlotStatus.Filled;
            usedContentIds.Add(best.Id);
            filled++;
        }

        if (filled > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        return Result<int>.Success(filled);
    }

    private List<DateTimeOffset> GenerateOccurrences(
        ContentSeries series, DateTimeOffset from, DateTimeOffset to)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to generate occurrences for series {SeriesId} with RRULE {RRule}",
                series.Id, series.RecurrenceRule);
            return [];
        }
    }

    private static bool TryParseRRule(string rrule, out RecurrencePattern? pattern)
    {
        try
        {
            pattern = new RecurrencePattern(rrule);
            return true;
        }
        catch
        {
            pattern = null;
            return false;
        }
    }
}
