using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications");

        group.MapGet("/", ListNotifications);
        group.MapGet("/pending", ListPending);
        group.MapPost("/{id:guid}/read", MarkRead);
        group.MapPost("/{id:guid}/acknowledge", Acknowledge);
        group.MapPost("/read-all", MarkAllRead);
    }

    private static async Task<IResult> ListNotifications(
        IApplicationDbContext dbContext,
        bool? isRead = null,
        int pageSize = 20)
    {
        var query = dbContext.Notifications.AsNoTracking();

        if (isRead.HasValue)
            query = query.Where(n => n.IsRead == isRead.Value);

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(pageSize, 1, 50))
            .ToListAsync();

        return Results.Ok(notifications);
    }

    private static async Task<IResult> MarkRead(
        INotificationService notificationService,
        Guid id)
    {
        await notificationService.MarkReadAsync(id);
        return Results.Ok();
    }

    private static async Task<IResult> MarkAllRead(
        INotificationService notificationService)
    {
        await notificationService.MarkAllReadAsync();
        return Results.Ok();
    }

    private static async Task<IResult> ListPending(
        INotificationService notificationService,
        Guid? contentId = null,
        CancellationToken ct = default)
    {
        var pending = await notificationService.GetPendingAsync(contentId, ct);
        return Results.Ok(pending);
    }

    private static async Task<IResult> Acknowledge(
        INotificationService notificationService,
        Guid id,
        CancellationToken ct = default)
    {
        await notificationService.AcknowledgeAsync(id, ct);
        return Results.Ok();
    }
}
