using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.ChannelAnalytics.Queries;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class ChannelAnalyticsEndpoints
{
    // Analytics OAuth exists only for these platforms; a channel request for anything else is a 400.
    private static readonly HashSet<Platform> AnalyticsPlatforms =
        [Platform.YouTube, Platform.Instagram, Platform.TikTok];

    public static void MapChannelAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // Reuses the existing /api/analytics group prefix (website + health are untouched).
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/overview", async (string? period, ISender sender, CancellationToken ct) =>
        {
            if (!TryResolvePeriod(period, out var window))
                return Results.BadRequest("Invalid period. Use 7d, 30d, or 90d.");

            var result = await sender.Send(new GetAnalyticsOverview.Query(window.From, window.To), ct);
            return result.ToApiResult();
        });

        group.MapGet("/channel/{platform}", async (string platform, string? period, ISender sender, CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var p) || !AnalyticsPlatforms.Contains(p))
                return Results.BadRequest($"'{platform}' is not an analytics platform. Use youtube, instagram, or tiktok.");
            if (!TryResolvePeriod(period, out var window))
                return Results.BadRequest("Invalid period. Use 7d, 30d, or 90d.");

            var result = await sender.Send(new GetChannelAnalytics.Query(p, window.From, window.To), ct);
            return result.ToApiResult();
        });

        group.MapGet("/youtube/deep", async (string? period, ISender sender, CancellationToken ct) =>
        {
            if (!TryResolvePeriod(period, out var window))
                return Results.BadRequest("Invalid period. Use 7d, 30d, or 90d.");

            var result = await sender.Send(new GetYouTubeDeepAnalytics.Query(window.From, window.To), ct);
            return result.ToApiResult();
        });
    }

    // Same 7d|30d|90d vocabulary as the Website endpoint (default 30d), resolved to a host-local DateOnly
    // window matching the poller's SnapshotDate clock.
    private static bool TryResolvePeriod(string? period, out (DateOnly From, DateOnly To) window)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        window = default;

        var days = string.IsNullOrWhiteSpace(period)
            ? 30
            : period switch { "7d" => 7, "30d" => 30, "90d" => 90, _ => -1 };
        if (days < 0)
            return false;

        window = (today.AddDays(-(days - 1)), today);
        return true;
    }
}
