using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ChannelAnalytics.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.ChannelAnalytics.Queries;

public static class GetChannelAnalytics
{
    public record Query(Platform Platform, DateOnly From, DateOnly To) : IRequest<Result<ChannelAnalyticsDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<ChannelAnalyticsDto>>
    {
        public async Task<Result<ChannelAnalyticsDto>> Handle(Query request, CancellationToken ct)
        {
            var platform = request.Platform;
            var status = await ChannelAnalyticsReadHelper.ResolveStatusAsync(db, platform, ct);

            // NotConnected -> empty DTO with that status (not an error).
            if (status == ConnectionStatus.NotConnected)
                return Result<ChannelAnalyticsDto>.Success(
                    new ChannelAnalyticsDto(platform, status, AsOf: null, [], [], []));

            var accounts = await ChannelAnalyticsReadHelper.LoadAccountSnapshotsAsync(db, platform, request.From, request.To, ct);
            var kpis = ChannelAnalyticsReadHelper.BuildKpis(accounts);
            var trends = ChannelAnalyticsReadHelper.BuildTrends(accounts);
            var asOf = accounts.Count > 0 ? accounts[^1].SnapshotDate : (DateOnly?)null;
            var recentPosts = await LoadRecentPostsAsync(platform, request.From, request.To, ct);

            return Result<ChannelAnalyticsDto>.Success(
                new ChannelAnalyticsDto(platform, status, asOf, kpis, trends, recentPosts));
        }

        // Latest captured day's video-scope snapshots.
        private async Task<IReadOnlyList<RecentPost>> LoadRecentPostsAsync(
            Platform platform, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var videos = await db.ChannelMetricSnapshots
                .Where(s => s.Platform == platform && s.Scope == SnapshotScope.Video
                    && s.SnapshotDate >= from && s.SnapshotDate <= to)
                .ToListAsync(ct);
            if (videos.Count == 0)
                return [];

            var latestDate = videos.Max(v => v.SnapshotDate);
            return videos
                .Where(v => v.SnapshotDate == latestDate)
                .Select(v => new RecentPost(v.VideoId, v.VideoTitle, v.Metrics))
                .ToList();
        }
    }
}
