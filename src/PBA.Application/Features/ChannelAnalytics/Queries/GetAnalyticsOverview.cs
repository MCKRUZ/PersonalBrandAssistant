using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ChannelAnalytics.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.ChannelAnalytics.Queries;

public static class GetAnalyticsOverview
{
    private static readonly Platform[] Platforms = [Platform.YouTube, Platform.Instagram, Platform.TikTok];

    public record Query(DateOnly From, DateOnly To) : IRequest<Result<OverviewDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<OverviewDto>>
    {
        public async Task<Result<OverviewDto>> Handle(Query request, CancellationToken ct)
        {
            var channels = new List<OverviewChannel>();
            long totalAudience = 0;

            foreach (var platform in Platforms)
            {
                var status = await ChannelAnalyticsReadHelper.ResolveStatusAsync(db, platform, ct);
                var accounts = await ChannelAnalyticsReadHelper.LoadAccountSnapshotsAsync(db, platform, request.From, request.To, ct);

                long? followers = accounts.Count > 0
                    ? ChannelAnalyticsReadHelper.AudienceValue(accounts[^1].Metrics)
                    : null;
                var sparkline = accounts.Count > 0
                    ? ChannelAnalyticsReadHelper.DeltaSeries(accounts, AudienceKey(accounts[^1].Metrics))
                    : [];

                // TotalAudience = latest followers/subscribers across CONNECTED channels only.
                if (status == ConnectionStatus.Connected && followers is not null)
                    totalAudience += followers.Value;

                channels.Add(new OverviewChannel(platform, status, followers, sparkline));
            }

            var combinedKpis = new List<KpiCard>
            {
                new("total_audience", "Total Audience", totalAudience, null, null)
            };

            return Result<OverviewDto>.Success(new OverviewDto(totalAudience, combinedKpis, channels));
        }

        private static string AudienceKey(IReadOnlyDictionary<string, long> bag) =>
            bag.ContainsKey("followers") ? "followers" : "subscribers";
    }
}
