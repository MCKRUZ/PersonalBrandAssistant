using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Feed.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Queries;

public static class GetTrendingTopics
{
    public record Query : IRequest<Result<IReadOnlyList<TrendingTopicDto>>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<IReadOnlyList<TrendingTopicDto>>>
    {
        public async Task<Result<IReadOnlyList<TrendingTopicDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            var trendAlerts = await db.FeedItems.AsNoTracking()
                .Where(f => f.Type == FeedItemType.TrendAlert && f.CreatedAt >= cutoff)
                .Take(1000)
                .ToListAsync(cancellationToken);

            var topicItems = new List<(string Topic, DateTimeOffset CreatedAt)>();
            foreach (var item in trendAlerts)
            {
                if (string.IsNullOrEmpty(item.Data))
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(item.Data);
                    if (doc.RootElement.TryGetProperty("topic", out var topicProp))
                    {
                        var topic = topicProp.GetString();
                        if (!string.IsNullOrEmpty(topic))
                            topicItems.Add((topic, item.CreatedAt));
                    }
                }
                catch (JsonException)
                {
                }
            }

            var topics = topicItems
                .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TrendingTopicDto
                {
                    Topic = g.Key,
                    Count = g.Count(),
                    LatestAt = g.Max(x => x.CreatedAt)
                })
                .OrderByDescending(t => t.Count)
                .ThenByDescending(t => t.LatestAt)
                .Take(10)
                .ToList();

            return Result<IReadOnlyList<TrendingTopicDto>>.Success(topics);
        }
    }
}
