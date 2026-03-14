using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class SchedulingEndpoints
{
    public record ScheduleRequest(DateTimeOffset ScheduledAt);

    public static void MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scheduling").WithTags("Scheduling");

        group.MapPost("/{id:guid}/schedule", ScheduleContent);
        group.MapPut("/{id:guid}/reschedule", RescheduleContent);
        group.MapDelete("/{id:guid}", CancelSchedule);
    }

    private static async Task<IResult> ScheduleContent(
        IContentScheduler scheduler,
        Guid id,
        ScheduleRequest request)
    {
        var result = await scheduler.ScheduleAsync(id, request.ScheduledAt);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RescheduleContent(
        IContentScheduler scheduler,
        Guid id,
        ScheduleRequest request)
    {
        var result = await scheduler.RescheduleAsync(id, request.ScheduledAt);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CancelSchedule(
        IContentScheduler scheduler,
        Guid id)
    {
        var result = await scheduler.CancelAsync(id);
        return result.ToHttpResult();
    }
}
