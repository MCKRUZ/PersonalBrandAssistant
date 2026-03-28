using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class CalendarEndpoints
{
    public record AssignContentRequest(Guid ContentId);

    public static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar").WithTags("Calendar");

        group.MapGet("/", GetSlots);
        group.MapPost("/series", CreateSeries);
        group.MapPost("/slot", CreateManualSlot);
        group.MapPut("/slot/{id:guid}/assign", AssignContent);
        group.MapPost("/auto-fill", AutoFill);
    }

    private static async Task<IResult> GetSlots(
        IContentCalendarService calendar,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        if (from >= to)
            return Results.Problem(statusCode: 400, detail: "'from' must be before 'to'.");

        if ((to - from).TotalDays > 90)
            return Results.Problem(statusCode: 400, detail: "Date range must not exceed 90 days.");

        var result = await calendar.GetSlotsAsync(from, to, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateSeries(
        IContentCalendarService calendar,
        ContentSeriesRequest request,
        CancellationToken ct)
    {
        var result = await calendar.CreateSeriesAsync(request, ct);
        return result.ToCreatedHttpResult("/api/calendar/series");
    }

    private static async Task<IResult> CreateManualSlot(
        IContentCalendarService calendar,
        CalendarSlotRequest request,
        CancellationToken ct)
    {
        var result = await calendar.CreateManualSlotAsync(request, ct);
        return result.ToCreatedHttpResult("/api/calendar/slot");
    }

    private static async Task<IResult> AssignContent(
        IContentCalendarService calendar,
        Guid id,
        AssignContentRequest request,
        CancellationToken ct)
    {
        var result = await calendar.AssignContentAsync(id, request.ContentId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AutoFill(
        IContentCalendarService calendar,
        IApplicationDbContext db,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        if (from >= to)
            return Results.Problem(statusCode: 400, detail: "'from' must be before 'to'.");

        if ((to - from).TotalDays > 90)
            return Results.Problem(statusCode: 400, detail: "Date range must not exceed 90 days.");

        var autonomy = await db.AutonomyConfigurations.FirstOrDefaultAsync(ct)
                       ?? AutonomyConfiguration.CreateDefault();

        if (autonomy.GlobalLevel == AutonomyLevel.Manual)
            return Results.Problem(statusCode: 403, detail: "Operation requires SemiAuto or higher autonomy level.");

        var result = await calendar.AutoFillSlotsAsync(from, to, ct);
        return result.ToHttpResult();
    }
}
