using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

internal sealed class SubstackContentMatcher : ISubstackContentMatcher
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<SubstackContentMatcher> _logger;

    public SubstackContentMatcher(
        IApplicationDbContext db,
        ILogger<SubstackContentMatcher> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ContentMatchResult> MatchAsync(SubstackRssEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.Title))
            return new ContentMatchResult(null, MatchConfidence.None, "Empty title");

        var candidates = await _db.Contents
            .AsNoTracking()
            .Where(c => c.ContentType == ContentType.BlogPost
                && c.SubstackPostUrl == null)
            .Select(c => new { c.Id, c.Title, c.CreatedAt, c.TargetPlatforms, c.Status })
            .ToListAsync(ct);

        // Filter to content targeting Substack or in Approved+ status
        var eligible = candidates
            .Where(c => c.TargetPlatforms.Contains(PlatformType.Substack)
                || c.Status >= ContentStatus.Approved)
            .ToList();

        // Exact title match (case-insensitive)
        var exactMatch = eligible.FirstOrDefault(c =>
            string.Equals(c.Title, entry.Title, StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
        {
            _logger.LogInformation(
                "Exact title match for RSS entry '{Title}' → Content {ContentId}",
                entry.Title, exactMatch.Id);
            return new ContentMatchResult(exactMatch.Id, MatchConfidence.High, "Exact title match");
        }

        // Fuzzy match: Levenshtein distance < 20% of title length, within 48h window
        var entryTitleLower = entry.Title.ToLowerInvariant();
        var maxDistance = Math.Max(1, (int)(entryTitleLower.Length * 0.2));
        var window48h = TimeSpan.FromHours(48);

        foreach (var candidate in eligible)
        {
            if (string.IsNullOrWhiteSpace(candidate.Title))
                continue;

            var candidateTitleLower = candidate.Title.ToLowerInvariant();
            var distance = LevenshteinDistance(entryTitleLower, candidateTitleLower);

            if (distance <= maxDistance && distance > 0)
            {
                var timeDiff = (entry.PublishedAt - candidate.CreatedAt).Duration();
                if (timeDiff <= window48h)
                {
                    _logger.LogInformation(
                        "Fuzzy title match for RSS entry '{RssTitle}' → Content {ContentId} (distance={Distance})",
                        entry.Title, candidate.Id, distance);
                    return new ContentMatchResult(
                        candidate.Id,
                        MatchConfidence.Medium,
                        $"Fuzzy title match (distance={distance})");
                }
            }
        }

        return new ContentMatchResult(null, MatchConfidence.None, "No matching content found");
    }

    internal static int LevenshteinDistance(string source, string target)
    {
        if (source.Length == 0) return target.Length;
        if (target.Length == 0) return source.Length;

        var dp = new int[source.Length + 1, target.Length + 1];

        for (var i = 0; i <= source.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= target.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[source.Length, target.Length];
    }
}
