using System.Text.Json;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class AgentEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents").WithTags("Agents");

        group.MapPost("/stream", StreamExecution);
        group.MapPost("/execute", ExecuteAgent);
        group.MapGet("/executions/{id:guid}", GetExecution);
        group.MapGet("/executions", ListExecutions);
        group.MapGet("/usage", GetUsage);
        group.MapGet("/budget", GetBudget);
    }

    private static async Task StreamExecution(
        HttpContext httpContext,
        IAgentOrchestrator orchestrator,
        AgentExecuteRequest request)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        var ct = httpContext.RequestAborted;

        try
        {
            await WriteSseEventAsync(httpContext, new { type = "status", status = "running" });

            var task = new AgentTask(request.Type, request.ContentId, request.Parameters ?? new());
            var result = await orchestrator.ExecuteAsync(task, ct);

            if (result.IsSuccess)
            {
                var output = result.Value!;
                if (output.Output is not null)
                {
                    await WriteSseEventAsync(httpContext, new
                    {
                        type = "token",
                        text = output.Output.GeneratedText,
                    });

                    await WriteSseEventAsync(httpContext, new
                    {
                        type = "usage",
                        inputTokens = output.Output.InputTokens,
                        outputTokens = output.Output.OutputTokens,
                    });
                }

                await WriteSseEventAsync(httpContext, new
                {
                    type = "complete",
                    executionId = output.ExecutionId,
                    createdContentId = output.CreatedContentId,
                });
            }
            else
            {
                var safeMessage = result.ErrorCode == ErrorCode.ValidationFailed
                    ? string.Join("; ", result.Errors)
                    : "Agent execution failed.";
                await WriteSseEventAsync(httpContext, new
                {
                    type = "error",
                    message = safeMessage,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — no action needed
        }
        catch (Exception)
        {
            try
            {
                await WriteSseEventAsync(httpContext, new
                {
                    type = "error",
                    message = "An unexpected error occurred.",
                });
            }
            catch
            {
                // Response may already be closed
            }
        }
    }

    private static async Task<IResult> ExecuteAgent(
        IAgentOrchestrator orchestrator,
        AgentExecuteRequest request,
        bool wait = false,
        CancellationToken ct = default)
    {
        var task = new AgentTask(request.Type, request.ContentId, request.Parameters ?? new());
        var result = await orchestrator.ExecuteAsync(task, ct);

        if (!result.IsSuccess)
            return result.ToHttpResult();

        if (wait)
            return Results.Ok(result.Value);

        return Results.Accepted(
            $"/api/agents/executions/{result.Value!.ExecutionId}",
            new { executionId = result.Value.ExecutionId });
    }

    private static async Task<IResult> GetExecution(
        IAgentOrchestrator orchestrator,
        Guid id,
        CancellationToken ct)
    {
        var result = await orchestrator.GetExecutionStatusAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ListExecutions(
        IAgentOrchestrator orchestrator,
        Guid? contentId = null,
        CancellationToken ct = default)
    {
        var result = await orchestrator.ListExecutionsAsync(contentId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetUsage(
        ITokenTracker tokenTracker,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? DateTimeOffset.UtcNow.Date;
        var toDate = to ?? DateTimeOffset.UtcNow;

        var cost = await tokenTracker.GetCostForPeriodAsync(fromDate, toDate, ct);
        return Results.Ok(new { from = fromDate, to = toDate, totalCost = cost });
    }

    private static async Task<IResult> GetBudget(
        ITokenTracker tokenTracker,
        CancellationToken ct)
    {
        var remaining = await tokenTracker.GetBudgetRemainingAsync(ct);
        var isOverBudget = await tokenTracker.IsOverBudgetAsync(ct);
        return Results.Ok(new
        {
            budgetRemaining = remaining,
            isOverBudget,
        });
    }

    private static async Task WriteSseEventAsync(HttpContext context, object data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await context.Response.Body.WriteAsync(bytes);
        await context.Response.Body.FlushAsync();
    }
}

public record AgentExecuteRequest(
    AgentCapabilityType Type,
    Guid? ContentId,
    Dictionary<string, string>? Parameters);
