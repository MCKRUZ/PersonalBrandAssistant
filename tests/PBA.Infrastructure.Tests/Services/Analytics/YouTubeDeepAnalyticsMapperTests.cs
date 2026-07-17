using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics;
using Xunit;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class YouTubeDeepAnalyticsMapperTests
{
    [Fact]
    public void MapsReportsRows_ToLabeledSeries()
    {
        // Day-dimensioned Analytics v2 report: one series per METRIC column, points labeled by the DIMENSION.
        var report = new YouTubeReportResult(
            Columns:
            [
                new YouTubeReportColumn("day", "DIMENSION"),
                new YouTubeReportColumn("views", "METRIC"),
                new YouTubeReportColumn("averageViewDuration", "METRIC")
            ],
            Rows:
            [
                ["2026-07-15", "100", "42.5"],
                ["2026-07-16", "150", "48.0"]
            ]);

        var series = YouTubeDeepAnalyticsMapper.MapToSeries(report);

        Assert.Equal(2, series.Count);   // views + averageViewDuration (day is the label, not a series)

        var views = series.Single(s => s.Metric == "views");
        Assert.Equal("2026-07-15", views.Points[0].Day);
        Assert.Equal(100, views.Points[0].Value);
        Assert.Equal(150, views.Points[1].Value);

        // Deep-path values may be fractional (unlike snapshot bags).
        var avg = series.Single(s => s.Metric == "averageViewDuration");
        Assert.Equal(42.5, avg.Points[0].Value);
        Assert.Equal(48.0, avg.Points[1].Value);
    }

    [Fact]
    public void MapsNonDayDimension_LabelsByThatDimension()
    {
        // Traffic-source variant: no `day` column. The dimension column must become the label, not a
        // bogus all-zero series (the pre-generalization bug).
        var report = new YouTubeReportResult(
            Columns:
            [
                new YouTubeReportColumn("insightTrafficSourceType", "DIMENSION"),
                new YouTubeReportColumn("views", "METRIC")
            ],
            Rows:
            [
                ["YT_SEARCH", "800"],
                ["SUGGESTED_VIDEO", "1200"]
            ]);

        var series = YouTubeDeepAnalyticsMapper.MapToSeries(report);

        var views = Assert.Single(series);   // only the metric column is a series
        Assert.Equal("views", views.Metric);
        Assert.Equal("YT_SEARCH", views.Points[0].Day);
        Assert.Equal(800, views.Points[0].Value);
        Assert.Equal("SUGGESTED_VIDEO", views.Points[1].Day);
    }
}
