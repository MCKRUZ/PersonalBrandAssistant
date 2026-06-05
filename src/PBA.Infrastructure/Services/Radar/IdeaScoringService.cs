using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaScoringService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdeaScoringOptions> options,
    ILogger<IdeaScoringService> logger) : BackgroundService
{
    private readonly IdeaScoringOptions _options = options.Value;
    private DateTimeOffset _startedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _startedAt = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = _options.BackfillEnabled ? (DateTimeOffset?)null : _startedAt;
                await ScoreBatchAsync(cutoff, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idea scoring sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task ScoreBatchAsync(DateTimeOffset? backfillCutoff, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var analyzer = scope.ServiceProvider.GetRequiredService<IIdeaAnalyzer>();

        var query = db.Ideas.Where(i => i.ScoredAt == null);
        if (backfillCutoff is { } cutoff)
            query = query.Where(i => i.DetectedAt >= cutoff);

        var batch = await query
            .OrderByDescending(i => i.DetectedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        var scored = 0;
        foreach (var idea in batch)
        {
            var analysis = await analyzer.AnalyzeAsync(
                idea.Title, idea.Description, idea.Url, idea.SourceName, ct);

            if (analysis is not null)
            {
                idea.Score = analysis.Score;
                idea.ScoreReason = analysis.Reason;
                idea.Summary = analysis.Summary;
                idea.Category ??= analysis.Category;
                if (analysis.Tags.Count > 0) idea.Tags = analysis.Tags.ToList();
                idea.ScoredAt = DateTimeOffset.UtcNow;
                scored++;
            }

            if (_options.ThrottleMs > 0)
                await Task.Delay(_options.ThrottleMs, ct);
        }

        if (scored > 0)
            await db.SaveChangesAsync(ct);
        logger.LogInformation("Scored {Scored}/{Total} ideas", scored, batch.Count);
    }
}
