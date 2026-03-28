using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class EventEndpoints
{
    private static readonly ContentStatus[] TerminalStatuses =
        [ContentStatus.Published, ContentStatus.Archived];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events").WithTags("Events");
        group.MapGet("/pipeline", StreamPipelineEvents);
    }

    private static async Task StreamPipelineEvents(
        HttpContext context,
        IPipelineEventBroadcaster broadcaster,
        IApplicationDbContext dbContext,
        CancellationToken ct)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var activeItems = await dbContext.Contents
            .AsNoTracking()
            .Where(c => !TerminalStatuses.Contains(c.Status))
            .Select(c => new
            {
                c.Id,
                c.Title,
                Platform = c.TargetPlatforms.Length > 0 ? c.TargetPlatforms[0].ToString() : "Unknown",
                Stage = c.Status.ToString(),
                ContentType = c.ContentType.ToString(),
                c.UpdatedAt
            })
            .ToListAsync(ct);

        var snapshotJson = JsonSerializer.Serialize(activeItems, JsonOptions);
        await WriteSseEvent(context.Response, "pipeline:snapshot", snapshotJson, ct);

        var reader = broadcaster.Subscribe();
        try
        {
            await foreach (var pipelineEvent in reader.ReadAllAsync(ct))
            {
                await WriteSseEvent(context.Response, pipelineEvent.EventType, pipelineEvent.Data, ct);
            }
        }
        finally
        {
            broadcaster.Unsubscribe(reader);
        }
    }

    private static async Task WriteSseEvent(
        HttpResponse response, string eventType, string data, CancellationToken ct)
    {
        await response.WriteAsync($"event: {eventType}\n", ct);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
