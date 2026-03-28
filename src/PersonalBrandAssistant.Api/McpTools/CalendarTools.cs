using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.McpTools;

[McpServerToolType]
public static class CalendarTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool]
    [Description("Returns scheduled content for a date range. Use when asked 'what's scheduled this week', 'show me tomorrow's posts', or 'what's on the calendar'. Optional platform filter.")]
    public static async Task<string> pba_get_calendar(
        IServiceProvider serviceProvider,
        [Description("Start date in ISO 8601 format, e.g. '2026-03-23T00:00:00Z'")] string startDate,
        [Description("End date in ISO 8601 format, e.g. '2026-03-30T00:00:00Z'")] string endDate,
        [Description("Optional platform filter: TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack")] string? platform = null,
        CancellationToken ct = default)
    {
        if (!DateTimeOffset.TryParse(startDate, out var from))
            return Error("Invalid startDate format. Use ISO 8601.");
        if (!DateTimeOffset.TryParse(endDate, out var to))
            return Error("Invalid endDate format. Use ISO 8601.");
        if (from >= to)
            return Error("startDate must be before endDate.");

        using var scope = serviceProvider.CreateScope();
        var calendarService = scope.ServiceProvider.GetRequiredService<IContentCalendarService>();

        var result = await calendarService.GetSlotsAsync(from, to, ct);
        if (!result.IsSuccess)
            return Error(string.Join("; ", result.Errors));

        var slots = result.Value!.AsEnumerable();

        if (platform is not null)
        {
            if (!Enum.TryParse<PlatformType>(platform, ignoreCase: true, out var platformEnum))
                return Error($"Invalid platform '{platform}'. Valid: {string.Join(", ", Enum.GetNames<PlatformType>())}");
            slots = slots.Where(s => s.Platform == platformEnum);
        }

        var items = slots.Select(s => new
        {
            s.Id,
            s.ContentId,
            Platform = s.Platform.ToString(),
            s.ScheduledAt,
            Status = s.Status.ToString()
        }).ToList();

        return Success(new { items, count = items.Count });
    }

    [McpServerTool]
    [Description("Schedules approved content for a specific time slot. Use when asked to 'schedule that post for tomorrow at 9am' or 'put it on the calendar'. Validates no conflicts on the same platform within 30 minutes. Respects autonomy dial.")]
    public static async Task<string> pba_schedule_content(
        IServiceProvider serviceProvider,
        [Description("Content ID (GUID) to schedule")] string contentId,
        [Description("Schedule date/time in ISO 8601 format")] string dateTime,
        [Description("Target platform: TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack")] string platform,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(contentId, out var guid))
            return Error("Invalid contentId format. Must be a GUID.");
        if (!DateTimeOffset.TryParse(dateTime, out var scheduledAt))
            return Error("Invalid dateTime format. Use ISO 8601.");
        if (!Enum.TryParse<PlatformType>(platform, ignoreCase: true, out var platformEnum))
            return Error($"Invalid platform '{platform}'. Valid: {string.Join(", ", Enum.GetNames<PlatformType>())}");

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var calendarService = scope.ServiceProvider.GetRequiredService<IContentCalendarService>();

        var content = await dbContext.Contents.FindAsync([guid], ct);
        if (content is null)
            return Error($"Content {contentId} not found.");

        var autonomy = await dbContext.AutonomyConfigurations.FirstOrDefaultAsync(ct);
        var level = autonomy?.GlobalLevel ?? AutonomyLevel.SemiAuto;

        if (level is AutonomyLevel.Manual or AutonomyLevel.Assisted)
            return Success(new { contentId = guid, status = "queued-for-approval", scheduledAt, platform = platformEnum.ToString() });

        var conflictWindow = TimeSpan.FromMinutes(30);
        var conflictsResult = await calendarService.GetSlotsAsync(scheduledAt - conflictWindow, scheduledAt + conflictWindow, ct);
        if (conflictsResult.IsSuccess)
        {
            var conflict = conflictsResult.Value!.FirstOrDefault(s => s.Platform == platformEnum && s.ContentId != guid);
            if (conflict is not null)
                return Error($"Time slot conflict: existing content scheduled on {platformEnum} at {conflict.ScheduledAt:g}.");
        }

        var slotResult = await calendarService.CreateManualSlotAsync(
            new Application.Common.Models.CalendarSlotRequest(scheduledAt, platformEnum), ct);

        if (!slotResult.IsSuccess)
            return Error(string.Join("; ", slotResult.Errors));

        var assignResult = await calendarService.AssignContentAsync(slotResult.Value, guid, ct);
        if (!assignResult.IsSuccess)
            return Error(string.Join("; ", assignResult.Errors));

        return Success(new { contentId = guid, slotId = slotResult.Value, scheduledAt, platform = platformEnum.ToString(), status = "scheduled" });
    }

    [McpServerTool]
    [Description("Moves already-scheduled content to a new time slot. Use when asked to 'move that post to Thursday' or 'reschedule my LinkedIn article'. Validates the new slot is available.")]
    public static async Task<string> pba_reschedule_content(
        IServiceProvider serviceProvider,
        [Description("Content ID (GUID) to reschedule")] string contentId,
        [Description("New date/time in ISO 8601 format")] string newDateTime,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(contentId, out var guid))
            return Error("Invalid contentId format. Must be a GUID.");
        if (!DateTimeOffset.TryParse(newDateTime, out var newScheduledAt))
            return Error("Invalid newDateTime format. Use ISO 8601.");

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var calendarService = scope.ServiceProvider.GetRequiredService<IContentCalendarService>();

        var slot = await dbContext.CalendarSlots
            .FirstOrDefaultAsync(s => s.ContentId == guid, ct);

        if (slot is null)
            return Error($"No scheduled slot found for content {contentId}.");

        var conflictWindow = TimeSpan.FromMinutes(30);
        var conflictsResult = await calendarService.GetSlotsAsync(newScheduledAt - conflictWindow, newScheduledAt + conflictWindow, ct);
        if (conflictsResult.IsSuccess)
        {
            var conflict = conflictsResult.Value!.FirstOrDefault(s => s.Platform == slot.Platform && s.Id != slot.Id);
            if (conflict is not null)
                return Error($"Time slot conflict at new time: existing content on {slot.Platform} at {conflict.ScheduledAt:g}.");
        }

        var previousTime = slot.ScheduledAt;
        slot.ScheduledAt = newScheduledAt;
        await dbContext.SaveChangesAsync(ct);

        return Success(new { contentId = guid, previousScheduledAt = previousTime, newScheduledAt, platform = slot.Platform.ToString() });
    }

    private static string Success(object data) =>
        JsonSerializer.Serialize(new { success = true, data }, JsonOptions);

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);
}
