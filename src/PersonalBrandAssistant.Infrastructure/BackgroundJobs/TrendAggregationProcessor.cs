using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class TrendAggregationProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TrendMonitoringOptions _options;
    private readonly BackgroundJobsOptions _jobsOptions;
    private readonly ILogger<TrendAggregationProcessor> _logger;

    public TrendAggregationProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<TrendMonitoringOptions> options,
        IOptions<BackgroundJobsOptions> jobsOptions,
        ILogger<TrendAggregationProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _jobsOptions = jobsOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_jobsOptions.TrendAggregationEnabled)
        {
            _logger.LogInformation("TrendAggregation processor is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.AggregationIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during trend aggregation cycle");
            }
        }
    }

    internal async Task ProcessCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var trendMonitor = scope.ServiceProvider.GetRequiredService<ITrendMonitor>();

        try
        {
            var result = await trendMonitor.RefreshTrendsAsync(ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Trend refresh failed: {Errors}",
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in trend aggregation cycle");
        }
    }
}
