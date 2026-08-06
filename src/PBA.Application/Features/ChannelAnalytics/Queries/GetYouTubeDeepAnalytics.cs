using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Application.Features.ChannelAnalytics.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.ChannelAnalytics.Queries;

// The one LIVE read path: YouTube Analytics v2 reports.query for the selected window (its own history, not
// snapshotted). Wrapped in Result so the YouTube tab degrades gracefully if the live call fails.
public static class GetYouTubeDeepAnalytics
{
    private static readonly string[] DayMetrics =
    [
        "views", "estimatedMinutesWatched", "averageViewDuration",
        "subscribersGained", "subscribersLost", "likes", "comments", "shares"
    ];

    public record Query(DateOnly From, DateOnly To) : IRequest<Result<YouTubeDeepAnalyticsDto>>;

    public sealed class Handler(IYouTubeApiClient youtube, IPlatformTokenProvider tokens)
        : IRequestHandler<Query, Result<YouTubeDeepAnalyticsDto>>
    {
        public async Task<Result<YouTubeDeepAnalyticsDto>> Handle(Query request, CancellationToken ct)
        {
            // Ensure a fresh token on demand — the stored one expires ~1h after the daily poll.
            var tokenResult = await tokens.GetFreshAccessTokenAsync(Platform.YouTube, CredentialPurpose.Analytics, ct);
            if (!tokenResult.IsSuccess)
                return Result<YouTubeDeepAnalyticsDto>.Fail(
                    tokenResult.Errors.FirstOrDefault() ?? "YouTube analytics is unavailable.");

            var accessToken = tokenResult.Value!;

            try
            {
                var day = await RunAsync(request, "day", DayMetrics, accessToken, ct);
                var traffic = await RunAsync(request, "insightTrafficSourceType", ["views"], accessToken, ct);
                var geography = await RunAsync(request, "country", ["views"], accessToken, ct);
                var demographics = await RunAsync(request, "ageGroup", ["viewerPercentage"], accessToken, ct);

                return Result<YouTubeDeepAnalyticsDto>.Success(
                    new YouTubeDeepAnalyticsDto(day, traffic, geography, demographics));
            }
            catch (Exception ex)
            {
                return Result<YouTubeDeepAnalyticsDto>.Fail($"YouTube deep analytics failed: {ex.Message}");
            }
        }

        private async Task<IReadOnlyList<YouTubeMetricSeries>> RunAsync(
            Query request, string dimensions, IReadOnlyList<string> metrics, string accessToken, CancellationToken ct)
        {
            var report = await youtube.RunAnalyticsReportAsync(
                new YouTubeReportRequest("channel==MINE", dimensions, metrics, request.From, request.To), accessToken, ct);
            return YouTubeDeepAnalyticsMapper.MapToSeries(report);
        }
    }
}
