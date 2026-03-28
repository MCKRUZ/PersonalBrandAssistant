using System.Globalization;
using Google.Analytics.Data.V1Beta;
using Google.Apis.SearchConsole.v1.Data;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

internal sealed class GoogleAnalyticsService : IGoogleAnalyticsService
{
    private static readonly PredicateBuilder<object> TransientExceptionPredicate = new PredicateBuilder()
        .Handle<RpcException>(ex =>
            ex.StatusCode is StatusCode.Unavailable
            or StatusCode.DeadlineExceeded
            or StatusCode.ResourceExhausted)
        .Handle<Google.GoogleApiException>()
        .Handle<HttpRequestException>();

    private readonly IGa4Client _ga4Client;
    private readonly ISearchConsoleClient _searchConsoleClient;
    private readonly GoogleAnalyticsOptions _options;
    private readonly ILogger<GoogleAnalyticsService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public GoogleAnalyticsService(
        IGa4Client ga4Client,
        ISearchConsoleClient searchConsoleClient,
        IOptions<GoogleAnalyticsOptions> options,
        ILogger<GoogleAnalyticsService> logger)
    {
        _ga4Client = ga4Client;
        _searchConsoleClient = searchConsoleClient;
        _options = options.Value;
        _logger = logger;

        // 15s total timeout caps the entire operation including retries (intentional for single-user dashboard)
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(15))
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = TransientExceptionPredicate
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = TransientExceptionPredicate
            })
            .Build();
    }

    public async Task<Result<WebsiteOverview>> GetOverviewAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var request = new RunReportRequest
            {
                Property = $"properties/{_options.PropertyId}",
                DateRanges =
                {
                    new DateRange
                    {
                        StartDate = FormatDate(from),
                        EndDate = FormatDate(to)
                    }
                },
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

            var response = await _resiliencePipeline.ExecuteAsync(
                async token => await _ga4Client.RunReportAsync(request, token), ct);

            if (response.Rows is null || response.Rows.Count == 0)
            {
                return Result<WebsiteOverview>.Success(
                    new WebsiteOverview(0, 0, 0, 0, 0, 0));
            }

            var row = response.Rows[0];
            var overview = new WebsiteOverview(
                ActiveUsers: ParseInt(row.MetricValues[0].Value),
                Sessions: ParseInt(row.MetricValues[1].Value),
                PageViews: ParseInt(row.MetricValues[2].Value),
                AvgSessionDuration: ParseDouble(row.MetricValues[3].Value),
                BounceRate: ParseDouble(row.MetricValues[4].Value),
                NewUsers: ParseInt(row.MetricValues[5].Value));

            return Result<WebsiteOverview>.Success(overview);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "GA4 circuit breaker is open — overview request rejected");
            return Result<WebsiteOverview>.Failure(
                ErrorCode.InternalError, "GA4 service temporarily unavailable");
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "GA4 overview request timed out after retries");
            return Result<WebsiteOverview>.Failure(
                ErrorCode.InternalError, "GA4 request timed out");
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "GA4 API error fetching overview");
            return Result<WebsiteOverview>.Failure(
                ErrorCode.InternalError, $"GA4 API error: {ex.Status.Detail}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching GA4 overview");
            return Result<WebsiteOverview>.Failure(
                ErrorCode.InternalError, $"GA4 error: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        try
        {
            var request = new RunReportRequest
            {
                Property = $"properties/{_options.PropertyId}",
                DateRanges =
                {
                    new DateRange
                    {
                        StartDate = FormatDate(from),
                        EndDate = FormatDate(to)
                    }
                },
                Dimensions = { new Dimension { Name = "pagePath" } },
                Metrics =
                {
                    new Metric { Name = "screenPageViews" },
                    new Metric { Name = "activeUsers" }
                },
                OrderBys =
                {
                    new OrderBy
                    {
                        Metric = new OrderBy.Types.MetricOrderBy { MetricName = "screenPageViews" },
                        Desc = true
                    }
                },
                Limit = limit
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async token => await _ga4Client.RunReportAsync(request, token), ct);

            var pages = (response.Rows ?? Enumerable.Empty<Row>())
                .Select(row => new PageViewEntry(
                    PagePath: row.DimensionValues[0].Value,
                    Views: ParseInt(row.MetricValues[0].Value),
                    Users: ParseInt(row.MetricValues[1].Value)))
                .ToList();

            return Result<IReadOnlyList<PageViewEntry>>.Success(pages);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "GA4 circuit breaker is open — top pages request rejected");
            return Result<IReadOnlyList<PageViewEntry>>.Failure(
                ErrorCode.InternalError, "GA4 service temporarily unavailable");
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "GA4 top pages request timed out after retries");
            return Result<IReadOnlyList<PageViewEntry>>.Failure(
                ErrorCode.InternalError, "GA4 request timed out");
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "GA4 API error fetching top pages");
            return Result<IReadOnlyList<PageViewEntry>>.Failure(
                ErrorCode.InternalError, $"GA4 API error: {ex.Status.Detail}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching GA4 top pages");
            return Result<IReadOnlyList<PageViewEntry>>.Failure(
                ErrorCode.InternalError, $"GA4 error: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var request = new RunReportRequest
            {
                Property = $"properties/{_options.PropertyId}",
                DateRanges =
                {
                    new DateRange
                    {
                        StartDate = FormatDate(from),
                        EndDate = FormatDate(to)
                    }
                },
                Dimensions = { new Dimension { Name = "sessionDefaultChannelGroup" } },
                Metrics =
                {
                    new Metric { Name = "sessions" },
                    new Metric { Name = "activeUsers" }
                }
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async token => await _ga4Client.RunReportAsync(request, token), ct);

            var sources = (response.Rows ?? Enumerable.Empty<Row>())
                .Select(row => new TrafficSourceEntry(
                    Channel: row.DimensionValues[0].Value,
                    Sessions: ParseInt(row.MetricValues[0].Value),
                    Users: ParseInt(row.MetricValues[1].Value)))
                .ToList();

            return Result<IReadOnlyList<TrafficSourceEntry>>.Success(sources);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "GA4 circuit breaker is open — traffic sources request rejected");
            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
                ErrorCode.InternalError, "GA4 service temporarily unavailable");
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "GA4 traffic sources request timed out after retries");
            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
                ErrorCode.InternalError, "GA4 request timed out");
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "GA4 API error fetching traffic sources");
            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
                ErrorCode.InternalError, $"GA4 API error: {ex.Status.Detail}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching GA4 traffic sources");
            return Result<IReadOnlyList<TrafficSourceEntry>>.Failure(
                ErrorCode.InternalError, $"GA4 error: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        try
        {
            var request = new SearchAnalyticsQueryRequest
            {
                StartDate = FormatDate(from),
                EndDate = FormatDate(to),
                Dimensions = ["query"],
                RowLimit = limit
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async token => await _searchConsoleClient.QueryAsync(
                    _options.SiteUrl, request, token), ct);

            var queries = (response.Rows ?? Enumerable.Empty<ApiDataRow>())
                .Select(row => new SearchQueryEntry(
                    Query: row.Keys[0],
                    Clicks: (int)(row.Clicks ?? 0),
                    Impressions: (int)(row.Impressions ?? 0),
                    Ctr: row.Ctr ?? 0,
                    Position: row.Position ?? 0))
                .ToList();

            return Result<IReadOnlyList<SearchQueryEntry>>.Success(queries);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Search Console circuit breaker is open — query request rejected");
            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
                ErrorCode.InternalError, "Search Console service temporarily unavailable");
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "Search Console query request timed out after retries");
            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
                ErrorCode.InternalError, "Search Console request timed out");
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogError(ex, "Search Console API error fetching top queries");
            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
                ErrorCode.InternalError, $"Search Console API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching Search Console queries");
            return Result<IReadOnlyList<SearchQueryEntry>>.Failure(
                ErrorCode.InternalError, $"Search Console error: {ex.Message}");
        }
    }

    private static string FormatDate(DateTimeOffset dto) =>
        dto.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
