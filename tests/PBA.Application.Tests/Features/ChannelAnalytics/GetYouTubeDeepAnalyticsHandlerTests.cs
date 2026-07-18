using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.ChannelAnalytics.Queries;
using PBA.Domain.Common;
using PBA.Domain.Enums;
using Xunit;

namespace PBA.Application.Tests.Features.ChannelAnalytics;

public class GetYouTubeDeepAnalyticsHandlerTests
{
    private static readonly DateOnly Day1 = new(2026, 6, 1);

    private static Mock<IAnalyticsTokenProvider> FreshToken()
    {
        var m = new Mock<IAnalyticsTokenProvider>();
        m.Setup(t => t.GetFreshAccessTokenAsync(Platform.YouTube, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("fresh-token"));
        return m;
    }

    [Fact]
    public async Task GetYouTubeDeepAnalytics_LiveCallFails_ReturnsResultFail_TabDegrades()
    {
        var youtube = new Mock<IYouTubeApiClient>();
        youtube.Setup(c => c.RunAnalyticsReportAsync(It.IsAny<YouTubeReportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("live call blew up"));

        var result = await new GetYouTubeDeepAnalytics.Handler(youtube.Object, FreshToken().Object)
            .Handle(new GetYouTubeDeepAnalytics.Query(Day1, Day1.AddDays(6)), CancellationToken.None);

        Assert.False(result.IsSuccess);   // Result.Fail, not an exception
    }

    [Fact]
    public async Task GetYouTubeDeepAnalytics_TokenUnavailable_ReturnsResultFail_WithoutCallingClient()
    {
        var tokens = new Mock<IAnalyticsTokenProvider>();
        tokens.Setup(t => t.GetFreshAccessTokenAsync(Platform.YouTube, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Fail("YouTube analytics is not connected."));
        var youtube = new Mock<IYouTubeApiClient>();

        var result = await new GetYouTubeDeepAnalytics.Handler(youtube.Object, tokens.Object)
            .Handle(new GetYouTubeDeepAnalytics.Query(Day1, Day1.AddDays(6)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        youtube.Verify(c => c.RunAnalyticsReportAsync(It.IsAny<YouTubeReportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetYouTubeDeepAnalytics_LiveCallSucceeds_MapsSeries()
    {
        var youtube = new Mock<IYouTubeApiClient>();
        youtube.Setup(c => c.RunAnalyticsReportAsync(It.IsAny<YouTubeReportRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YouTubeReportResult(
                [new YouTubeReportColumn("day", "DIMENSION"), new YouTubeReportColumn("views", "METRIC")],
                [["2026-06-01", "100"], ["2026-06-02", "150"]]));

        var result = await new GetYouTubeDeepAnalytics.Handler(youtube.Object, FreshToken().Object)
            .Handle(new GetYouTubeDeepAnalytics.Query(Day1, Day1.AddDays(6)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var views = result.Value!.DaySeries.Single(s => s.Metric == "views");
        Assert.Equal(100, views.Points[0].Value);
        Assert.Equal(150, views.Points[1].Value);
    }
}
