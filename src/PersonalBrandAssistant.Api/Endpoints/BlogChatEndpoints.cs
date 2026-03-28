using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class BlogChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapBlogChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/{contentId:guid}/chat").WithTags("BlogChat");
        group.MapPost("/", StreamChatMessage);
        group.MapGet("/history", GetChatHistory);
        group.MapPost("/finalize", FinalizeDraft);
    }

    private static async Task StreamChatMessage(
        Guid contentId,
        ChatMessageRequest request,
        HttpContext context,
        IBlogChatService chatService,
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var content = await db.Contents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (content.ContentType != ContentType.BlogPost)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Content must be a BlogPost" }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 10_000)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Message must be 1-10000 characters" }, ct);
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var chunk in chatService.SendMessageAsync(contentId, request.Message, ct))
            {
                var data = JsonSerializer.Serialize(new { text = chunk }, JsonOptions);
                await context.Response.WriteAsync($"event: delta\ndata: {data}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }

            await context.Response.WriteAsync("event: done\ndata: {}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var errorData = JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions);
            await context.Response.WriteAsync($"event: error\ndata: {errorData}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }

    private static async Task<IResult> GetChatHistory(
        Guid contentId,
        IBlogChatService chatService,
        CancellationToken ct)
    {
        var conversation = await chatService.GetConversationAsync(contentId, ct);
        if (conversation is null)
            return Results.Ok(Array.Empty<object>());

        var messages = conversation.Messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            timestamp = m.Timestamp,
        });
        return Results.Ok(messages);
    }

    private static async Task<IResult> FinalizeDraft(
        Guid contentId,
        IBlogChatService chatService,
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var conversation = await chatService.GetConversationAsync(contentId, ct);
        if (conversation is null)
            return Results.BadRequest(new { error = "No conversation exists for this content" });

        var result = await chatService.ExtractFinalDraftAsync(contentId, ct);
        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                ErrorCode.NotFound => Results.NotFound(new { error = result.Errors.FirstOrDefault() }),
                _ => Results.BadRequest(new { error = result.Errors.FirstOrDefault() }),
            };
        }

        return Results.Ok(result.Value);
    }
}

public record ChatMessageRequest(string Message);
