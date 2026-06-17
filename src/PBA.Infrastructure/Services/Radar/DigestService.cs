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
    private readonly TimeOnly _runAt = TimeOnly.ParseExact(options.Value.RunAtLocalTime, "HH:mm", CultureInfo.InvariantCulture);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DigestService scheduled daily at {Time} (host TZ {Tz})", _options.RunAtLocalTime, TimeZoneInfo.Local.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                if (TimeOnly.FromDateTime(now.DateTime) >= _runAt)
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
        var date = DateOnly.FromDateTime(now.UtcDateTime);

        // Generate both briefs for the day. Microsoft only delivers in-app (no email/Discord push) and
        // is skipped when no Microsoft-source items exist yet.
        foreach (var kind in new[] { DigestKind.Main, DigestKind.Microsoft })
            await GenerateForKindAsync(scope, db, date, now, kind, ct);
    }

    private async Task GenerateForKindAsync(
        IServiceScope scope, ApplicationDbContext db, DateOnly date, DateTimeOffset now, DigestKind kind, CancellationToken ct)
    {
        // Guard: one digest per (calendar day UTC, kind)
        if (await db.Digests.AnyAsync(d => d.Date == date && d.Kind == kind, ct))
            return;

        var since = now.AddHours(-_options.LookbackHours);
        var query = db.Ideas
            .Where(i => i.ScoredAt != null && i.Score != null && i.DuplicateOfId == null && i.DetectedAt >= since);

        // "Pure Microsoft" = items whose source is tagged Microsoft (set in IdeaSourceSeedService).
        if (kind == DigestKind.Microsoft)
            query = query.Where(i => i.IdeaSource != null && i.IdeaSource.Category == "Microsoft");

        var top = await query
            .OrderByDescending(i => i.Score)
            .Take(_options.TopN)
            .ToListAsync(ct);

        if (top.Count == 0) return;

        // Resolve writer from scope (scoped service — never inject into singleton constructor)
        var writer = scope.ServiceProvider.GetRequiredService<IDigestWriter>();

        var inputs = top
            .Select((idea, idx) => new DigestInput(idx, idea.Title, idea.Summary ?? string.Empty, idea.Score ?? 0, idea.Url))
            .ToList();

        var copy = await writer.WriteAsync(inputs, kind, ct);
        if (copy is null) return;

        var digest = new Digest
        {
            Date = date,
            Kind = kind,
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

        // In-app feed notification + external delivery are for the Main brief only; the Microsoft brief
        // is surfaced beside it on the Daily Brief page, so a second ping would just be noise.
        if (kind == DigestKind.Main)
        {
            db.FeedItems.Add(new FeedItem
            {
                Type = FeedItemType.SystemNotification,
                Title = copy.Title,
                Summary = copy.Intro.Length > 280 ? copy.Intro[..280] : copy.Intro,
                Data = JsonSerializer.Serialize(new { digestId = digest.Id }),
                Priority = FeedItemPriority.Normal,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Generated {Kind} digest {Date} with {Count} items", kind, date, top.Count);

        if (kind != DigestKind.Main) return;

        var dispatcher = scope.ServiceProvider.GetRequiredService<IDeliveryDispatcher>();
        var deliveryItems = digest.Items
            .OrderBy(i => i.Rank)
            .Join(top, di => di.IdeaId, idea => idea.Id,
                (di, idea) => new DeliveryItem(di.Rank, di.Score, idea.Title, di.WhyItMatters, idea.Url))
            .ToList();
        await dispatcher.DispatchAsync(
            new DeliveryNotification(DeliveryKind.Digest, copy.Title, copy.Intro, deliveryItems), ct);
    }
}
