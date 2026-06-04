using Google.Analytics.Data.V1Beta;
using Google.Apis.SearchConsole.v1.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class GoogleAnalyticsService(
    IGa4Client ga4,
    ISearchConsoleClient searchConsole,
    IOptions<GoogleAnalyticsOptions> options,
    ILogger<GoogleAnalyticsService> logger) : IGoogleAnalyticsService
{
    private readonly GoogleAnalyticsOptions _options = options.Value;

    public async Task<Result<WebsiteOverview>> GetOverviewAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var request = new RunReportRequest
        {
            Property = $"properties/{_options.PropertyId}",
            DateRanges = { Range(from, to) },
            Metrics =
            {
                new Metric { Name = "activeUsers" },
                new Metric { Name = "sessions" },
                new Metric { Name = "screenPageViews" },
                new Metric { Name = "averageSessionDuration" },
                new Metric { Name = "bounceRate" },
                new Metric { Name = "newUsers" }
            }
        };

        try
        {
            var response = await ga4.RunReportAsync(request, ct);
            var row = response.Rows.FirstOrDefault();
            if (row is null)
                return Result<WebsiteOverview>.Success(new WebsiteOverview(0, 0, 0, 0, 0, 0));

            return Result<WebsiteOverview>.Success(new WebsiteOverview(
                ActiveUsers: ParseInt(row.MetricValues[0].Value),
                Sessions: ParseInt(row.MetricValues[1].Value),
                PageViews: ParseInt(row.MetricValues[2].Value),
                AvgSessionDuration: ParseDouble(row.MetricValues[3].Value),
                BounceRate: ParseDouble(row.MetricValues[4].Value),
                NewUsers: ParseInt(row.MetricValues[5].Value)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 GetOverview failed");
            return Result<WebsiteOverview>.Fail($"GA4 request failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        var request = new RunReportRequest
        {
            Property = $"properties/{_options.PropertyId}",
            DateRanges = { Range(from, to) },
            Dimensions = { new Dimension { Name = "pagePath" } },
            Metrics = { new Metric { Name = "screenPageViews" }, new Metric { Name = "totalUsers" } },
            OrderBys = { new OrderBy { Metric = new OrderBy.Types.MetricOrderBy { MetricName = "screenPageViews" }, Desc = true } },
            Limit = limit
        };

        try
        {
            var response = await ga4.RunReportAsync(request, ct);
            var rows = response.Rows
                .Select(r => new PageViewEntry(
                    r.DimensionValues[0].Value,
                    ParseInt(r.MetricValues[0].Value),
                    ParseInt(r.MetricValues[1].Value)))
                .ToList();
            return Result<IReadOnlyList<PageViewEntry>>.Success(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 GetTopPages failed");
            return Result<IReadOnlyList<PageViewEntry>>.Fail($"GA4 request failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var request = new RunReportRequest
        {
            Property = $"properties/{_options.PropertyId}",
            DateRanges = { Range(from, to) },
            Dimensions = { new Dimension { Name = "sessionDefaultChannelGroup" } },
            Metrics = { new Metric { Name = "sessions" }, new Metric { Name = "totalUsers" } },
            OrderBys = { new OrderBy { Metric = new OrderBy.Types.MetricOrderBy { MetricName = "sessions" }, Desc = true } }
        };

        try
        {
            var response = await ga4.RunReportAsync(request, ct);
            var rows = response.Rows
                .Select(r => new TrafficSourceEntry(
                    r.DimensionValues[0].Value,
                    ParseInt(r.MetricValues[0].Value),
                    ParseInt(r.MetricValues[1].Value)))
                .ToList();
            return Result<IReadOnlyList<TrafficSourceEntry>>.Success(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 GetTrafficSources failed");
            return Result<IReadOnlyList<TrafficSourceEntry>>.Fail($"GA4 request failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        var request = new SearchAnalyticsQueryRequest
        {
            StartDate = from.ToString("yyyy-MM-dd"),
            EndDate = to.ToString("yyyy-MM-dd"),
            Dimensions = ["query"],
            RowLimit = limit
        };

        try
        {
            var response = await searchConsole.QueryAsync(_options.SiteUrl, request, ct);
            var rows = (response.Rows ?? [])
                .Select(r => new SearchQueryEntry(
                    r.Keys[0],
                    (int)(r.Clicks ?? 0),
                    (int)(r.Impressions ?? 0),
                    r.Ctr ?? 0,
                    r.Position ?? 0))
                .ToList();
            return Result<IReadOnlyList<SearchQueryEntry>>.Success(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search Console GetTopQueries failed");
            return Result<IReadOnlyList<SearchQueryEntry>>.Fail($"Search Console request failed: {ex.Message}");
        }
    }

    private static DateRange Range(DateTimeOffset from, DateTimeOffset to) => new()
    {
        StartDate = from.ToString("yyyy-MM-dd"),
        EndDate = to.ToString("yyyy-MM-dd")
    };

    private static int ParseInt(string? v) =>
        int.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 0;

    private static double ParseDouble(string? v) =>
        double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
}
