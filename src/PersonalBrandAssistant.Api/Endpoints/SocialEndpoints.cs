using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class SocialEndpoints
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social").WithTags("Social");

        // Stats & Safety
        group.MapGet("/stats", GetStats);
        group.MapGet("/safety-status", GetSafetyStatus);

        // Engagement tasks
        group.MapGet("/tasks", GetTasks);
        group.MapPost("/tasks", CreateTask);
        group.MapPut("/tasks/{id:guid}", UpdateTask);
        group.MapDelete("/tasks/{id:guid}", DeleteTask);
        group.MapPost("/tasks/{id:guid}/execute", ExecuteTask);
        group.MapGet("/tasks/{id:guid}/history", GetHistory);

        // Opportunities
        group.MapPost("/discover", DiscoverOpportunities);
        group.MapPost("/engage", EngageSingle);
        group.MapPost("/opportunities/dismiss", DismissOpportunity);
        group.MapPost("/opportunities/save", SaveOpportunity);
        group.MapGet("/opportunities/saved", GetSavedOpportunities);

        // Inbox
        group.MapGet("/inbox", GetInboxItems);
        group.MapPut("/inbox/{id:guid}/read", MarkRead);
        group.MapPost("/inbox/{id:guid}/draft", DraftReply);
        group.MapPost("/inbox/{id:guid}/reply", SendReply);
    }

    // --- Stats & Safety ---

    private static async Task<IResult> GetStats(
        ISocialEngagementService service,
        CancellationToken ct)
    {
        var result = await service.GetStatsAsync(ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpResult();
    }

    private static async Task<IResult> GetSafetyStatus(
        ISocialEngagementService service,
        CancellationToken ct)
    {
        var result = await service.GetSafetyStatusAsync(ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpResult();
    }

    // --- Engagement Tasks ---

    private static async Task<IResult> GetTasks(
        ISocialEngagementService service,
        CancellationToken ct)
    {
        var result = await service.GetTasksAsync(ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var projected = result.Value!.Select(t => new
        {
            t.Id,
            Platform = t.Platform.ToString(),
            TaskType = t.TaskType.ToString(),
            t.TargetCriteria,
            t.CronExpression,
            t.IsEnabled,
            t.AutoRespond,
            t.LastExecutedAt,
            t.NextExecutionAt,
            t.MaxActionsPerExecution,
            SchedulingMode = t.SchedulingMode.ToString(),
            t.CreatedAt,
        });

        return Results.Ok(projected);
    }

    private static async Task<IResult> CreateTask(
        ISocialEngagementService service,
        CreateEngagementTaskRequest request,
        CancellationToken ct)
    {
        var dto = new CreateEngagementTaskDto(
            request.Platform,
            request.TaskType,
            request.TargetCriteria,
            request.CronExpression,
            request.IsEnabled,
            request.AutoRespond,
            request.MaxActionsPerExecution,
            request.SchedulingMode);

        var result = await service.CreateTaskAsync(dto, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var task = result.Value!;
        return Results.Created($"/api/social/tasks/{task.Id}", new
        {
            task.Id,
            Platform = task.Platform.ToString(),
            TaskType = task.TaskType.ToString(),
            task.TargetCriteria,
            task.CronExpression,
            task.IsEnabled,
            task.AutoRespond,
            task.NextExecutionAt,
            task.MaxActionsPerExecution,
            SchedulingMode = task.SchedulingMode.ToString(),
            task.CreatedAt,
        });
    }

    private static async Task<IResult> UpdateTask(
        ISocialEngagementService service,
        Guid id,
        UpdateEngagementTaskRequest request,
        CancellationToken ct)
    {
        var dto = new UpdateEngagementTaskDto(
            request.TargetCriteria,
            request.CronExpression,
            request.IsEnabled,
            request.AutoRespond,
            request.MaxActionsPerExecution,
            request.SchedulingMode);

        var result = await service.UpdateTaskAsync(id, dto, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteTask(
        ISocialEngagementService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.DeleteTaskAsync(id, ct);
        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> ExecuteTask(
        ISocialEngagementService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.ExecuteTaskAsync(id, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var exec = result.Value!;
        return Results.Ok(new
        {
            exec.Id,
            exec.ExecutedAt,
            exec.ActionsAttempted,
            exec.ActionsSucceeded,
            exec.ErrorMessage,
            Actions = exec.Actions.Select(a => new
            {
                a.Id,
                ActionType = a.ActionType.ToString(),
                a.TargetUrl,
                a.GeneratedContent,
                a.PlatformPostId,
                a.Succeeded,
                a.ErrorMessage,
                a.PerformedAt,
            }),
        });
    }

    private static async Task<IResult> GetHistory(
        ISocialEngagementService service,
        Guid id,
        int limit = 20,
        CancellationToken ct = default)
    {
        var result = await service.GetExecutionHistoryAsync(id, limit, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var projected = result.Value!.Select(e => new
        {
            e.Id,
            e.ExecutedAt,
            e.ActionsAttempted,
            e.ActionsSucceeded,
            e.ErrorMessage,
            Actions = e.Actions.Select(a => new
            {
                a.Id,
                ActionType = a.ActionType.ToString(),
                a.TargetUrl,
                a.GeneratedContent,
                a.Succeeded,
                a.ErrorMessage,
                a.PerformedAt,
            }),
        });

        return Results.Ok(projected);
    }

    // --- Opportunities ---

    private static async Task<IResult> DiscoverOpportunities(
        ISocialEngagementService service,
        CancellationToken ct)
    {
        var result = await service.DiscoverOpportunitiesAsync(ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpResult();
    }

    private static async Task<IResult> EngageSingle(
        ISocialEngagementService service,
        EngageSingleRequest request,
        CancellationToken ct)
    {
        var dto = new EngageSingleDto(
            request.Platform, request.PostId, request.PostUrl,
            request.Title, request.Content, request.Community);

        var result = await service.EngageSingleAsync(dto, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var action = result.Value!;
        return Results.Ok(new
        {
            action.Id,
            ActionType = action.ActionType.ToString(),
            action.TargetUrl,
            action.GeneratedContent,
            action.PlatformPostId,
            action.Succeeded,
            action.ErrorMessage,
            action.PerformedAt,
        });
    }

    private static async Task<IResult> DismissOpportunity(
        ISocialEngagementService service,
        OpportunityActionRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<PlatformType>(request.Platform, ignoreCase: true, out var platform))
            return Results.BadRequest("Invalid platform");

        var result = await service.DismissOpportunityAsync(request.PostUrl, platform, ct);
        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> SaveOpportunity(
        ISocialEngagementService service,
        OpportunityActionRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<PlatformType>(request.Platform, ignoreCase: true, out var platform))
            return Results.BadRequest("Invalid platform");

        var result = await service.SaveOpportunityAsync(request.PostUrl, platform, ct);
        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> GetSavedOpportunities(
        ISocialEngagementService service,
        CancellationToken ct)
    {
        var result = await service.GetSavedOpportunitiesAsync(ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToHttpResult();
    }

    // --- Inbox ---

    private static async Task<IResult> GetInboxItems(
        ISocialInboxService service,
        string? platform = null,
        bool? isRead = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        PlatformType? platformFilter = null;
        if (platform is not null && Enum.TryParse<PlatformType>(platform, ignoreCase: true, out var p))
            platformFilter = p;

        var filter = new InboxFilterDto(platformFilter, isRead, limit);
        var result = await service.GetItemsAsync(filter, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var projected = result.Value!.Select(i => new
        {
            i.Id,
            Platform = i.Platform.ToString(),
            ItemType = i.ItemType.ToString(),
            i.AuthorName,
            i.AuthorProfileUrl,
            i.Content,
            i.SourceUrl,
            i.IsRead,
            i.DraftReply,
            i.ReplySent,
            i.ReceivedAt,
        });

        return Results.Ok(projected);
    }

    private static async Task<IResult> MarkRead(
        ISocialInboxService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.MarkReadAsync(id, ct);
        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
    }

    private static async Task<IResult> DraftReply(
        ISocialInboxService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.DraftReplyAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SendReply(
        ISocialInboxService service,
        Guid id,
        SendReplyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReplyText))
            return Results.BadRequest("Reply text is required.");

        var result = await service.SendReplyAsync(id, request.ReplyText, ct);
        return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
    }
}

public record CreateEngagementTaskRequest(
    string Platform,
    string TaskType,
    string TargetCriteria,
    string CronExpression,
    bool IsEnabled,
    bool AutoRespond = false,
    int MaxActionsPerExecution = 3,
    string? SchedulingMode = null);

public record UpdateEngagementTaskRequest(
    string? TargetCriteria = null,
    string? CronExpression = null,
    bool? IsEnabled = null,
    bool? AutoRespond = null,
    int? MaxActionsPerExecution = null,
    string? SchedulingMode = null);

public record SendReplyRequest(string ReplyText);

public record EngageSingleRequest(
    string Platform,
    string PostId,
    string PostUrl,
    string Title,
    string Content,
    string Community);

public record OpportunityActionRequest(string PostUrl, string Platform);
