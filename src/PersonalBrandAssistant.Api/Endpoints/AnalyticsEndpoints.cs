using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/content/{id:guid}", GetPerformance);
        group.MapGet("/top", GetTopContent);
        group.MapPost("/content/{id:guid}/refresh", RefreshEngagement);
    }

    private static async Task<IResult> GetPerformance(
        IEngagementAggregator aggregator,
        Guid id,
        CancellationToken ct)
    {
        var result = await aggregator.GetPerformanceAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetTopContent(
        IEngagementAggregator aggregator,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit = 10,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var result = await aggregator.GetTopContentAsync(from, to, clampedLimit, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RefreshEngagement(
        IEngagementAggregator aggregator,
        Guid id,
        CancellationToken ct)
    {
        var result = await aggregator.FetchLatestAsync(id, ct);
        if (result.IsSuccess)
            return Results.Accepted(value: result.Value);
        return result.ToHttpResult();
    }
}
