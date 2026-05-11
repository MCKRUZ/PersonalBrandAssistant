using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services;

public partial class RssPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FreshRssClient _freshRssClient;
    private readonly IOptionsMonitor<FreshRssOptions> _options;
    private readonly ILogger<RssPollingService> _logger;

    public RssPollingService(
        IServiceScopeFactory scopeFactory,
        FreshRssClient freshRssClient,
        IOptionsMonitor<FreshRssOptions> options,
        ILogger<RssPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _freshRssClient = freshRssClient;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _freshRssClient.AuthenticateAsync(stoppingToken);
            _logger.LogInformation("FreshRSS authenticated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FreshRSS initial authentication failed, will retry on first poll");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(_options.CurrentValue.PollIntervalMinutes);
            await Task.Delay(interval, stoppingToken);

            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RSS polling cycle failed");
            }
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sources = await dbContext.IdeaSources
            .Where(s => s.IsEnabled && s.Type == IdeaSourceType.RSS)
            .ToListAsync(ct);

        if (sources.Count == 0)
        {
            _logger.LogDebug("No enabled RSS sources to poll");
            return;
        }

        var oldestPoll = sources
            .Where(s => s.LastPolledAt.HasValue)
            .Select(s => s.LastPolledAt!.Value)
            .DefaultIfEmpty(DateTimeOffset.UtcNow.AddDays(-1))
            .Min();

        IReadOnlyList<RssEntry> entries;
        try
        {
            entries = await _freshRssClient.GetEntriesAsync(newerThan: oldestPoll, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch entries from FreshRSS");
            return;
        }

        var existingKeys = await dbContext.Ideas
            .Where(i => i.DeduplicationKey != "")
            .Select(i => i.DeduplicationKey)
            .ToHashSetAsync(ct);

        var processedEntryIds = new List<string>();

        foreach (var source in sources)
        {
            try
            {
                var sourceEntries = entries
                    .Where(e => MatchesSource(e, source))
                    .ToList();

                var newCount = 0;
                foreach (var entry in sourceEntries)
                {
                    var dedupKey = GenerateDeduplicationKey(entry.Url, entry.Title);
                    if (existingKeys.Contains(dedupKey))
                        continue;

                    dbContext.Ideas.Add(new Idea
                    {
                        Title = entry.Title,
                        Description = TruncateContent(entry.Content, 2000),
                        Url = entry.Url,
                        SourceName = entry.FeedTitle,
                        IdeaSourceId = source.Id,
                        ThumbnailUrl = entry.ThumbnailUrl,
                        Category = source.Category,
                        Tags = entry.Categories.ToList(),
                        Status = IdeaStatus.New,
                        DetectedAt = entry.PublishedAt,
                        DeduplicationKey = dedupKey,
                    });

                    existingKeys.Add(dedupKey);
                    processedEntryIds.Add(entry.Id);
                    newCount++;
                }

                source.LastPolledAt = DateTimeOffset.UtcNow;
                source.LastSuccessAt = DateTimeOffset.UtcNow;
                source.ConsecutiveFailures = 0;
                source.LastError = null;

                if (newCount > 0)
                    _logger.LogInformation("Source {SourceName}: {Count} new ideas", source.Name, newCount);
            }
            catch (Exception ex)
            {
                source.ConsecutiveFailures++;
                source.LastError = ex.Message;
                source.LastPolledAt = DateTimeOffset.UtcNow;

                var maxFailures = _options.CurrentValue.MaxConsecutiveFailures;
                if (source.ConsecutiveFailures >= maxFailures)
                {
                    source.IsEnabled = false;
                    _logger.LogError("Source {SourceName} disabled after {Count} consecutive failures",
                        source.Name, source.ConsecutiveFailures);
                }
                else
                {
                    _logger.LogWarning(ex, "Source {SourceName} poll failed ({Count}/{Max})",
                        source.Name, source.ConsecutiveFailures, maxFailures);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);

        if (processedEntryIds.Count > 0)
        {
            try
            {
                await _freshRssClient.MarkAsReadAsync(processedEntryIds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark {Count} entries as read in FreshRSS",
                    processedEntryIds.Count);
            }
        }
    }

    private static bool MatchesSource(RssEntry entry, IdeaSource source)
    {
        if (!string.IsNullOrEmpty(source.FeedUrl) && !string.IsNullOrEmpty(entry.Url))
        {
            var sourceDomain = ExtractDomain(source.FeedUrl);
            var entryDomain = ExtractDomain(entry.Url);
            if (!string.IsNullOrEmpty(sourceDomain) && !string.IsNullOrEmpty(entryDomain))
                return string.Equals(sourceDomain, entryDomain, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(source.Name) && !string.IsNullOrEmpty(entry.FeedTitle))
            return entry.FeedTitle.Contains(source.Name, StringComparison.OrdinalIgnoreCase)
                || source.Name.Contains(entry.FeedTitle, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static string? ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;
        return null;
    }

    internal static string GenerateDeduplicationKey(string? url, string title)
    {
        var input = !string.IsNullOrWhiteSpace(url)
            ? NormalizeUrl(url)
            : title.Trim().ToLowerInvariant();

        return ComputeSha256(input);
    }

    private static string NormalizeUrl(string url)
    {
        var normalized = url.Trim().ToLowerInvariant().TrimEnd('/');
        normalized = UtmParamRegex().Replace(normalized, "");
        normalized = normalized.TrimEnd('?').TrimEnd('&');
        return normalized;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static string? TruncateContent(string? content, int maxLength)
    {
        if (string.IsNullOrEmpty(content)) return content;
        return content.Length <= maxLength ? content : content[..maxLength];
    }

    [GeneratedRegex(@"[?&]utm_\w+=[^&]*", RegexOptions.Compiled)]
    private static partial Regex UtmParamRegex();
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<HashSet<T>> ToHashSetAsync<T>(
        this IQueryable<T> source, CancellationToken ct = default)
    {
        var set = new HashSet<T>();
        await foreach (var item in source.AsAsyncEnumerable().WithCancellation(ct))
            set.Add(item);
        return set;
    }
}
