using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Queries;

public static class ListIdeas
{
    public record Query : IRequest<Result<PagedResult<IdeaDto>>>
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
        public IdeaStatus? Status { get; init; }
        public Guid? IdeaSourceId { get; init; }
        public string? Category { get; init; }
        public IReadOnlyList<string>? Tags { get; init; }
        public DateTimeOffset? DateFrom { get; init; }
        public DateTimeOffset? DateTo { get; init; }
        public string? SearchText { get; init; }
        public string SortBy { get; init; } = "rank"; // brand-anchored rank is the default (R-M2)
        public string SortDirection { get; init; } = "desc";
        public int? MinScore { get; init; }
        public bool IncludeDuplicates { get; init; } = false;
    }

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PagedResult<IdeaDto>>>
    {
        public async Task<Result<PagedResult<IdeaDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.Ideas.AsNoTracking().AsQueryable();

            if (request.Status.HasValue)
                query = query.Where(i => i.Status == request.Status.Value);
            else
                query = query.Where(i => i.Status != IdeaStatus.Dismissed);

            if (request.IdeaSourceId.HasValue)
                query = query.Where(i => i.IdeaSourceId == request.IdeaSourceId.Value);

            if (!string.IsNullOrWhiteSpace(request.Category))
                query = query.Where(i => i.Category != null
                    && i.Category.ToLower().Contains(request.Category.ToLower()));

            if (request.Tags is { Count: > 0 })
                query = query.Where(i => i.Tags.Any(t => request.Tags.Contains(t)));

            if (request.DateFrom.HasValue)
                query = query.Where(i => i.DetectedAt >= request.DateFrom.Value);

            if (request.DateTo.HasValue)
                query = query.Where(i => i.DetectedAt <= request.DateTo.Value);

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                var search = request.SearchText.ToLower();
                query = query.Where(i =>
                    i.Title.ToLower().Contains(search)
                    || (i.Description != null && i.Description.ToLower().Contains(search))
                    || (i.Summary != null && i.Summary.ToLower().Contains(search)));
            }

            if (!request.IncludeDuplicates)
                query = query.Where(i => i.DuplicateOfId == null);

            if (request.MinScore.HasValue)
                query = query.Where(i => i.Score >= request.MinScore.Value);

            // Filters push to SQL (R-C1b). Count over the filtered set before materializing.
            var totalCount = await query.CountAsync(cancellationToken);

            // Snapshot the active profile once per query; the seeder guarantees one in practice. If none is
            // active, every idea ranks 0 (graceful degrade) and the rank sort falls back to its DetectedAt
            // tie-break, preserving recency ordering.
            var profile = await db.BrandRankingProfiles.AsNoTracking()
                .Include(p => p.Pillars)
                .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
            var snapshot = profile is null ? null : BrandRankingProfileSnapshot.FromProfile(profile);

            // Projection MUST NOT select Embedding (R-C1a): select only display fields + ranking inputs.
            // EF only emits the columns named here, so the vector(1536) Embedding column is excluded.
            // Projecting the whole value-converted PillarSubScores property is supported — the EF limit is
            // "querying INTO" a converted property (referencing its members server-side), which we never do;
            // all rank math runs in memory after materialization. (Section-12 Testcontainers confirms the SQL.)
            //
            // Materializing the full FILTERED set is justified at today's scale (~3,800 ideas) per R-C1b, but
            // note the real cost driver: the default Idea Bank view applies NO filters, so the common path
            // materializes the entire Ideas table to return one page. Revisit when the table approaches ~25k
            // rows OR p95 > 200 ms -> precompute a stored brandFit-per-version column and apply only decay live.
            var rows = await query
                .Select(i => new RankRow
                {
                    Id = i.Id,
                    Title = i.Title,
                    Description = i.Description,
                    Url = i.Url,
                    SourceName = i.SourceName,
                    Category = i.Category,
                    Summary = i.Summary,
                    ThumbnailUrl = i.ThumbnailUrl,
                    Status = i.Status,
                    Tags = i.Tags,
                    DetectedAt = i.DetectedAt,
                    HasSavedDetails = i.SavedDetails != null,
                    Score = i.Score,
                    ScoreReason = i.ScoreReason,
                    IsDuplicate = i.DuplicateOfId != null,
                    PillarSubScores = i.PillarSubScores,
                    IsAntiTopic = i.IsAntiTopic,
                    IsAuthorityTopic = i.IsAuthorityTopic,
                    ScoredProfileVersion = i.ScoredProfileVersion
                })
                .ToListAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var dtos = rows.Select(r => ToDto(r, snapshot, now)).ToList();

            // Rank, sort, and page in memory (R-C1b).
            var page = ApplySort(dtos, request.SortBy, request.SortDirection)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            return new PagedResult<IdeaDto>
            {
                Items = page,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }

        private static IdeaDto ToDto(RankRow r, BrandRankingProfileSnapshot? profile, DateTimeOffset now)
        {
            var dto = new IdeaDto
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                Url = r.Url,
                SourceName = r.SourceName,
                Category = r.Category,
                Summary = r.Summary,
                ThumbnailUrl = r.ThumbnailUrl,
                Status = r.Status,
                Tags = r.Tags,
                DetectedAt = r.DetectedAt,
                HasSavedDetails = r.HasSavedDetails,
                Score = r.Score,
                ScoreReason = r.ScoreReason,
                IsDuplicate = r.IsDuplicate,
                IsAntiTopic = r.IsAntiTopic,
                IsAuthorityTopic = r.IsAuthorityTopic
            };

            if (profile is null) return dto; // no active profile -> rank 0, empty breakdown, not stale

            // Sub-scores are keyed by BrandPillarId (R-C3). Last-wins guards a malformed duplicate key.
            var subScores = r.PillarSubScores
                .GroupBy(s => s.PillarId)
                .ToDictionary(g => g.Key, g => g.Last());

            var rank = ComputeRank(subScores, r.IsAntiTopic, r.IsAuthorityTopic,
                r.ScoredProfileVersion, r.DetectedAt, profile, now);

            return dto with
            {
                Rank = rank.Rank,
                BrandFit = rank.BrandFit,
                RecencyFactor = rank.RecencyFactor,
                Stale = rank.Stale,
                PillarBreakdown = rank.Breakdown
            };
        }

        private static IEnumerable<IdeaDto> ApplySort(IEnumerable<IdeaDto> items, string sortBy, string direction)
        {
            var desc = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);
            // String sorts now run in memory (was SQL collation). Use a FIXED ordinal comparer so the
            // ordering is deterministic regardless of the server's current culture.
            var str = StringComparer.OrdinalIgnoreCase;
            return sortBy.ToLowerInvariant() switch
            {
                "title" => desc ? items.OrderByDescending(i => i.Title, str) : items.OrderBy(i => i.Title, str),
                "sourcename" => desc ? items.OrderByDescending(i => i.SourceName, str) : items.OrderBy(i => i.SourceName, str),
                "category" => desc ? items.OrderByDescending(i => i.Category ?? "", str) : items.OrderBy(i => i.Category ?? "", str),
                "status" => desc ? items.OrderByDescending(i => i.Status) : items.OrderBy(i => i.Status),
                "score" => desc ? items.OrderByDescending(i => i.Score ?? -1) : items.OrderBy(i => i.Score ?? -1),
                "detectedat" => desc ? items.OrderByDescending(i => i.DetectedAt) : items.OrderBy(i => i.DetectedAt),
                // Default = rank. Tie-break on DetectedAt desc so equal/zero ranks fall back to recency
                // (e.g. when no profile is active or ideas are unscored).
                _ => desc
                    ? items.OrderByDescending(i => i.Rank).ThenByDescending(i => i.DetectedAt)
                    : items.OrderBy(i => i.Rank).ThenByDescending(i => i.DetectedAt)
            };
        }
    }

    /// <summary>
    /// Pure brand-anchored composite rank: <c>brandFit × recencyDecay × antiTopicMultiplier ×
    /// authorityBoost</c>. No DB, no LLM — deterministic and unit-tested in isolation. brandFit reuses the
    /// shared <see cref="BrandFit.RenormalizedSubScore"/> over the ACTIVE pillars (R-C2a/b); read-time
    /// renormalization means a weight edit re-ranks instantly with zero LLM calls.
    /// </summary>
    internal static RankResult ComputeRank(
        IReadOnlyDictionary<Guid, PillarSubScore> subScores,
        bool? isAntiTopic,
        bool? isAuthorityTopic,
        int? scoredProfileVersion,
        DateTimeOffset detectedAt,
        BrandRankingProfileSnapshot profile,
        DateTimeOffset now)
    {
        // Breakdown + sub-score map cover only pillars present in the ACTIVE profile that the idea has a
        // sub-score for (R-C2a); display name comes from the active pillar (R-C3).
        var breakdown = new List<PillarBreakdownDto>();
        var subById = new Dictionary<Guid, double>();
        foreach (var pillar in profile.Pillars)
        {
            if (!subScores.TryGetValue(pillar.Id, out var sub)) continue;
            subById[pillar.Id] = sub.Score;
            breakdown.Add(new PillarBreakdownDto { Name = pillar.Name, Score = sub.Score, Reason = sub.Reason });
        }

        var brandFit = Math.Clamp(
            BrandFit.RenormalizedSubScore(subById, profile.Pillars.Select(p => (p.Id, p.Weight)).ToList()),
            0, 1); // R-M6

        var ageDays = Math.Max(0, (now - detectedAt).TotalDays);
        var decay = profile.HalfLifeDays > 0
            ? Math.Exp(-Math.Log(2) / profile.HalfLifeDays * ageDays)
            : 0.0;
        var recency = Math.Max(decay, profile.DecayFloor);

        var antiMult = isAntiTopic == true ? profile.AntiTopicMultiplier : 1.0;   // R-L1
        var authBoost = isAuthorityTopic == true ? profile.AuthorityBoost : 1.0;  // R-L1

        var rank = brandFit * recency * antiMult * authBoost;
        var stale = scoredProfileVersion is { } v && v < profile.Version; // R-C2c

        return new RankResult(rank, brandFit, recency, stale, breakdown);
    }

    internal readonly record struct RankResult(
        double Rank, double BrandFit, double RecencyFactor, bool Stale,
        IReadOnlyList<PillarBreakdownDto> Breakdown);

    // Embedding-free read-path projection: every column the rank + DTO need, and deliberately NOT
    // Embedding (R-C1a).
    private sealed class RankRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? Url { get; init; }
        public string SourceName { get; init; } = string.Empty;
        public string? Category { get; init; }
        public string? Summary { get; init; }
        public string? ThumbnailUrl { get; init; }
        public IdeaStatus Status { get; init; }
        public IReadOnlyList<string> Tags { get; init; } = [];
        public DateTimeOffset DetectedAt { get; init; }
        public bool HasSavedDetails { get; init; }
        public int? Score { get; init; }
        public string? ScoreReason { get; init; }
        public bool IsDuplicate { get; init; }
        public IList<PillarSubScore> PillarSubScores { get; init; } = [];
        public bool? IsAntiTopic { get; init; }
        public bool? IsAuthorityTopic { get; init; }
        public int? ScoredProfileVersion { get; init; }
    }
}
