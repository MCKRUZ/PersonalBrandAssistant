using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class TrendEndpoints
{
    public static void MapTrendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trends").WithTags("Trends");

        group.MapGet("/suggestions", GetSuggestions);
        group.MapPost("/suggestions/{id:guid}/accept", AcceptSuggestion);
        group.MapPost("/suggestions/{id:guid}/dismiss", DismissSuggestion);
        group.MapPost("/refresh", RefreshTrends);
    }

    private static async Task<IResult> GetSuggestions(
        ITrendMonitor monitor,
        int limit = 20,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var result = await monitor.GetSuggestionsAsync(clampedLimit, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AcceptSuggestion(
        ITrendMonitor monitor,
        Guid id,
        CancellationToken ct)
    {
        var result = await monitor.AcceptSuggestionAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DismissSuggestion(
        ITrendMonitor monitor,
        Guid id,
        CancellationToken ct)
    {
        var result = await monitor.DismissSuggestionAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RefreshTrends(
        ITrendMonitor monitor,
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var autonomy = await db.AutonomyConfigurations.FirstOrDefaultAsync(ct)
                       ?? AutonomyConfiguration.CreateDefault();

        if (autonomy.GlobalLevel == AutonomyLevel.Manual)
            return Results.Problem(statusCode: 403, detail: "Trend refresh requires SemiAuto or higher autonomy level.");

        var result = await monitor.RefreshTrendsAsync(ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();
        return Results.Accepted();
    }
}
