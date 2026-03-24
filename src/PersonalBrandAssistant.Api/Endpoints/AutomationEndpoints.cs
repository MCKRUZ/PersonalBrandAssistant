using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class AutomationEndpoints
{
    public static void MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/automation").WithTags("Automation");

        group.MapGet("/runs", ListRuns);
        group.MapGet("/runs/{id:guid}", GetRun);
        group.MapDelete("/runs/{id:guid}", DeleteRun);
        group.MapDelete("/runs", ClearRuns);
        group.MapPost("/trigger", TriggerRun);
        group.MapGet("/config", GetConfig);
    }

    private static async Task<IResult> ListRuns(
        IApplicationDbContext db, int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        var runs = await db.AutomationRuns
            .OrderByDescending(r => r.TriggeredAt)
            .Take(limit)
            .Select(r => new
            {
                r.Id,
                r.TriggeredAt,
                Status = r.Status.ToString(),
                r.PrimaryContentId,
                r.ImageFileId,
                r.DurationMs,
                r.PlatformVersionCount,
                r.CompletedAt,
                r.ErrorDetails,
            })
            .ToListAsync(ct);

        return Results.Ok(runs);
    }

    private static async Task<IResult> GetRun(
        Guid id, IApplicationDbContext db, CancellationToken ct)
    {
        var run = await db.AutomationRuns.FindAsync([id], ct);
        if (run is null)
            return Results.NotFound();

        return Results.Ok(new
        {
            run.Id,
            run.TriggeredAt,
            Status = run.Status.ToString(),
            run.SelectedSuggestionId,
            run.PrimaryContentId,
            run.ImageFileId,
            run.ImagePrompt,
            run.SelectionReasoning,
            run.ErrorDetails,
            run.CompletedAt,
            run.DurationMs,
            run.PlatformVersionCount,
        });
    }

    private static async Task<IResult> DeleteRun(
        Guid id, IApplicationDbContext db, CancellationToken ct)
    {
        var run = await db.AutomationRuns.FindAsync([id], ct);
        if (run is null)
            return Results.NotFound();

        if (run.Status == AutomationRunStatus.Running)
            return Results.Problem(statusCode: 409, detail: "Cannot delete a running pipeline.");

        db.AutomationRuns.Remove(run);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ClearRuns(
        IApplicationDbContext db, CancellationToken ct)
    {
        var completed = await db.AutomationRuns
            .Where(r => r.Status != AutomationRunStatus.Running)
            .ToListAsync(ct);

        db.AutomationRuns.RemoveRange(completed);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deleted = completed.Count });
    }

    private static async Task<IResult> TriggerRun(
        IApplicationDbContext db,
        IDailyContentOrchestrator orchestrator,
        IOptions<ContentAutomationOptions> options,
        CancellationToken ct)
    {
        // Check for running pipeline
        var hasRunning = await db.AutomationRuns
            .AnyAsync(r => r.Status == AutomationRunStatus.Running, ct);
        if (hasRunning)
        {
            return Results.Problem(
                statusCode: 429,
                detail: "A pipeline run is already in progress.");
        }

        // Rate limit: 15 min cooldown
        var recentCompleted = await db.AutomationRuns
            .Where(r => r.Status == AutomationRunStatus.Completed && r.CompletedAt != null)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(ct);

        if (recentCompleted?.CompletedAt is not null &&
            DateTimeOffset.UtcNow - recentCompleted.CompletedAt.Value < TimeSpan.FromMinutes(15))
        {
            return Results.Problem(
                statusCode: 429,
                detail: "A pipeline run was completed recently. Please wait before triggering another.");
        }

        var result = await orchestrator.ExecuteAsync(options.Value, ct);
        return Results.Accepted($"/api/automation/runs/{result.RunId}", new { result.RunId, result.Success });
    }

    private static IResult GetConfig(IOptions<ContentAutomationOptions> options)
    {
        var opts = options.Value;
        return Results.Ok(new
        {
            opts.CronExpression,
            opts.TimeZone,
            opts.Enabled,
            opts.AutonomyLevel,
            opts.TopTrendsToConsider,
            TargetPlatforms = opts.TargetPlatforms.Distinct().ToArray(),
            imageGeneration = new
            {
                opts.ImageGeneration.Enabled,
                opts.ImageGeneration.ComfyUiBaseUrl,
                opts.ImageGeneration.TimeoutSeconds,
                opts.ImageGeneration.DefaultWidth,
                opts.ImageGeneration.DefaultHeight,
                opts.ImageGeneration.ModelCheckpoint,
                opts.ImageGeneration.CircuitBreakerThreshold,
            },
        });
    }
}
