using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Feed.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Queries;

public static class GetFeedSummary
{
    public record Query : IRequest<Result<FeedSummaryDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<FeedSummaryDto>>
    {
        public async Task<Result<FeedSummaryDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var baseQuery = db.FeedItems.AsNoTracking()
                .Where(f => f.ExpiresAt == null || f.ExpiresAt >= DateTimeOffset.UtcNow);

            var unreadCount = await baseQuery.CountAsync(f => !f.IsRead, cancellationToken);

            var pendingApprovals = await baseQuery.CountAsync(
                f => (f.Type == FeedItemType.AgentDraft || f.Type == FeedItemType.ApprovalRequest) && !f.IsActedOn,
                cancellationToken);

            var trendingCount = await baseQuery.CountAsync(
                f => f.Type == FeedItemType.TrendAlert && !f.IsRead,
                cancellationToken);

            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var analyticsItems = await baseQuery
                .Where(f => f.Type == FeedItemType.AnalyticsHighlight && f.CreatedAt >= cutoff)
                .ToListAsync(cancellationToken);

            var engagementDelta = 0.0;
            if (analyticsItems.Count > 0)
            {
                var deltas = new List<double>();
                foreach (var item in analyticsItems)
                {
                    if (string.IsNullOrEmpty(item.Data))
                        continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(item.Data);
                        if (doc.RootElement.TryGetProperty("delta", out var deltaProp) && deltaProp.TryGetDouble(out var d))
                            deltas.Add(d);
                    }
                    catch (JsonException)
                    {
                    }
                }

                if (deltas.Count > 0)
                    engagementDelta = deltas.Average();
            }

            return new FeedSummaryDto
            {
                UnreadCount = unreadCount,
                PendingApprovals = pendingApprovals,
                TrendingCount = trendingCount,
                EngagementDelta = engagementDelta
            };
        }
    }
}
