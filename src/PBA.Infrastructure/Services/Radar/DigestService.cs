using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

public sealed class DigestService(
    IServiceScopeFactory scopeFactory,
    IOptions<DigestOptions> options,
    ILogger<DigestService> logger) : BackgroundService
{
    private readonly DigestOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runAt = TimeOnly.ParseExact(_options.RunAtLocalTime, "HH:mm", CultureInfo.InvariantCulture);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                if (TimeOnly.FromDateTime(now.DateTime) >= runAt)
                    await GenerateDigestAsync(now, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Digest generation failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    internal async Task GenerateDigestAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Guard: one digest per calendar day (UTC)
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (await db.Digests.AnyAsync(d => d.Date == date, ct))
            return;

        var since = now.AddHours(-_options.LookbackHours);
        var top = await db.Ideas
            .Where(i => i.ScoredAt != null && i.DuplicateOfId == null && i.DetectedAt >= since)
            .OrderByDescending(i => i.Score)
            .Take(_options.TopN)
            .ToListAsync(ct);

        if (top.Count == 0) return;

        // Resolve writer from scope (scoped service — never inject into singleton constructor)
        var writer = scope.ServiceProvider.GetRequiredService<IDigestWriter>();

        var inputs = top
            .Select((idea, idx) => new DigestInput(idx, idea.Title, idea.Summary ?? string.Empty, idea.Score ?? 0, idea.Url))
            .ToList();

        var copy = await writer.WriteAsync(inputs, ct);
        if (copy is null) return;

        var digest = new Digest
        {
            Date = date,
            Title = copy.Title,
            Intro = copy.Intro,
            ItemCount = top.Count,
            CreatedAt = now
        };

        var whyByIndex = copy.Items.ToDictionary(i => i.Index, i => i.WhyItMatters);
        for (var idx = 0; idx < top.Count; idx++)
        {
            digest.Items.Add(new DigestItem
            {
                DigestId = digest.Id,
                IdeaId = top[idx].Id,
                Rank = idx + 1,
                Score = top[idx].Score ?? 0,
                WhyItMatters = whyByIndex.TryGetValue(idx, out var why) ? why : string.Empty
            });
        }

        db.Digests.Add(digest);

        db.FeedItems.Add(new FeedItem
        {
            Type = FeedItemType.SystemNotification,
            Title = copy.Title,
            Summary = copy.Intro.Length > 280 ? copy.Intro[..280] : copy.Intro,
            Data = JsonSerializer.Serialize(new { digestId = digest.Id }),
            Priority = FeedItemPriority.Normal,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Generated digest {Date} with {Count} items", date, top.Count);
    }
}
