using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.Analytics.Queries;

namespace PBA.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/website", async (
            string? period, DateTimeOffset? from, DateTimeOffset? to,
            ISender sender, CancellationToken ct) =>
        {
            if (!TryResolveRange(period, from, to, out var range))
                return Results.BadRequest("Invalid period or date range. Use period (7d, 30d, 90d) or a from/to range where from <= to.");

            var result = await sender.Send(new GetWebsiteAnalytics.Query(range.From, range.To), ct);
            return result.ToApiResult();
        });

        group.MapGet("/health", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAnalyticsHealth.Query(), ct);
            return result.ToApiResult();
        });
    }

    private static bool TryResolveRange(
        string? period, DateTimeOffset? from, DateTimeOffset? to, out (DateTimeOffset From, DateTimeOffset To) range)
    {
        var today = DateTimeOffset.UtcNow.Date;
        range = default;

        if (!string.IsNullOrWhiteSpace(period))
        {
            int days = period switch { "7d" => 7, "30d" => 30, "90d" => 90, _ => -1 };
            if (days < 0) return false;
            range = (new DateTimeOffset(today.AddDays(-(days - 1)), TimeSpan.Zero), new DateTimeOffset(today, TimeSpan.Zero));
            return true;
        }

        if (from.HasValue && to.HasValue)
        {
            if (from.Value > to.Value) return false;
            range = (from.Value, to.Value);
            return true;
        }

        range = (new DateTimeOffset(today.AddDays(-29), TimeSpan.Zero), new DateTimeOffset(today, TimeSpan.Zero));
        return true;
    }
}
