using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public sealed class TrendMonitor : ITrendMonitor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _dbContext;
    private readonly ISidecarClient _sidecar;
    private readonly IEnumerable<ITrendSourcePoller> _pollers;
    private readonly TrendMonitoringOptions _options;
    private readonly ILogger<TrendMonitor> _logger;

    public TrendMonitor(
        IApplicationDbContext dbContext,
        ISidecarClient sidecar,
        IEnumerable<ITrendSourcePoller> pollers,
        IOptions<TrendMonitoringOptions> options,
        ILogger<TrendMonitor> logger)
    {
        _dbContext = dbContext;
        _sidecar = sidecar;
        _pollers = pollers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<TrendSuggestion>>> GetSuggestionsAsync(
        int limit, CancellationToken ct)
    {
        if (limit <= 0)
            return Result<IReadOnlyList<TrendSuggestion>>.Failure(
                ErrorCode.ValidationFailed, "Limit must be greater than zero");

        var suggestions = await _dbContext.TrendSuggestions
            .Where(s => s.Status == TrendSuggestionStatus.Pending)
            .OrderByDescending(s => s.RelevanceScore)
            .ThenByDescending(s => s.CreatedAt)
            .Take(limit)
            .Include(s => s.RelatedTrends)
                .ThenInclude(rt => rt.TrendItem)
            .ToListAsync(ct);

        return Result<IReadOnlyList<TrendSuggestion>>.Success(suggestions);
    }

    public async Task<Result<MediatR.Unit>> DismissSuggestionAsync(
        Guid suggestionId, CancellationToken ct)
    {
        var suggestion = await _dbContext.TrendSuggestions
            .FindAsync([suggestionId], ct);

        if (suggestion is null)
            return Result<MediatR.Unit>.NotFound($"Suggestion {suggestionId} not found");

        suggestion.Status = TrendSuggestionStatus.Dismissed;
        await _dbContext.SaveChangesAsync(ct);

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    public async Task<Result<Guid>> AcceptSuggestionAsync(
        Guid suggestionId, CancellationToken ct)
    {
        var suggestion = await _dbContext.TrendSuggestions
            .FindAsync([suggestionId], ct);

        if (suggestion is null)
            return Result<Guid>.NotFound($"Suggestion {suggestionId} not found");

        if (suggestion.Status == TrendSuggestionStatus.Accepted)
            return Result<Guid>.Conflict($"Suggestion {suggestionId} is already accepted");

        suggestion.Status = TrendSuggestionStatus.Accepted;

        var content = Content.Create(
            suggestion.SuggestedContentType,
            body: "",
            title: suggestion.Topic,
            targetPlatforms: suggestion.SuggestedPlatforms);

        await _dbContext.Contents.AddAsync(content, ct);
        await _dbContext.SaveChangesAsync(ct);

        return Result<Guid>.Success(content.Id);
    }

    public async Task<Result<MediatR.Unit>> RefreshTrendsAsync(CancellationToken ct)
    {
        var dbSettings = await _dbContext.TrendSettings.FirstOrDefaultAsync(ct);
        var maxSuggestions = dbSettings?.MaxSuggestionsPerCycle ?? _options.MaxSuggestionsPerCycle;

        var sources = await _dbContext.TrendSources
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);

        var allItems = new List<TrendItem>();

        foreach (var source in sources)
        {
            var poller = _pollers.FirstOrDefault(p => p.SourceType == source.Type);
            if (poller is null)
            {
                _logger.LogWarning("No poller registered for source type {SourceType}", source.Type);
                continue;
            }

            try
            {
                var items = await poller.PollAsync(source, ct);
                allItems.AddRange(items);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to poll trend source {SourceName} ({SourceType})",
                    source.Name, source.Type);
            }
        }

        if (allItems.Count == 0)
        {
            _logger.LogInformation("No trend items collected from any source");
            return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
        }

        var deduplicated = TrendDeduplicator.Deduplicate(allItems, _options.TitleSimilarityThreshold);

        // Set deduplication keys on items (kept outside the pure deduplicator)
        foreach (var item in deduplicated)
        {
            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                var canonical = TrendDeduplicator.CanonicalizeUrl(item.Url);
                item.DeduplicationKey = TrendDeduplicator.ComputeDeduplicationKey(canonical);
            }
        }

        // Cross-cycle dedup: filter out items already persisted
        var dedupKeys = deduplicated
            .Where(d => d.DeduplicationKey is not null)
            .Select(d => d.DeduplicationKey!)
            .ToHashSet();

        var existingKeys = await _dbContext.TrendItems
            .Where(t => t.DeduplicationKey != null && dedupKeys.Contains(t.DeduplicationKey))
            .Select(t => t.DeduplicationKey!)
            .ToListAsync(ct);

        var existingKeySet = existingKeys.ToHashSet();
        var newItems = deduplicated
            .Where(d => d.DeduplicationKey is null || !existingKeySet.Contains(d.DeduplicationKey))
            .ToList();

        // Score relevance via LLM — falls back to default score if sidecar unavailable
        Dictionary<int, (float Score, string? Category)> scores;
        try
        {
            scores = await ScoreItemsAsync(newItems, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM scoring unavailable, using default scores");
            scores = [];
        }

        // When no LLM scores available, assign a neutral default so items still produce suggestions
        if (scores.Count == 0)
        {
            for (var i = 0; i < newItems.Count; i++)
                scores[i] = (0.5f, null);
        }

        // Apply LLM-assigned categories to items before persistence
        foreach (var (index, (_, category)) in scores)
        {
            if (index >= 0 && index < newItems.Count && category is not null)
                newItems[index].Category = category;
        }

        var suggestions = ClusterAndCreateSuggestions(newItems, scores);

        // Add TrendItems first so FK references from TrendSuggestionItem resolve correctly
        foreach (var item in newItems)
        {
            await _dbContext.TrendItems.AddAsync(item, ct);
        }

        foreach (var suggestion in suggestions.Take(maxSuggestions))
        {
            await _dbContext.TrendSuggestions.AddAsync(suggestion, ct);
        }

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save trend items to database");
            return Result<MediatR.Unit>.Failure(
                Application.Common.Errors.ErrorCode.InternalError,
                "Failed to save trend data — try again shortly");
        }

        _logger.LogInformation(
            "Trend refresh complete: {ItemCount} new items, {SuggestionCount} suggestions",
            newItems.Count, Math.Min(suggestions.Count, maxSuggestions));

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    private async Task<Dictionary<int, (float Score, string? Category)>> ScoreItemsAsync(
        IReadOnlyList<TrendItem> items, CancellationToken ct)
    {
        var scores = new Dictionary<int, (float Score, string? Category)>();

        var profile = await _dbContext.BrandProfiles
            .FirstOrDefaultAsync(p => p.IsActive, ct);

        if (profile is null)
        {
            _logger.LogWarning("No active brand profile found for relevance scoring");
            return scores;
        }

        var prompt = BuildRelevanceScoringPrompt(items, profile);
        var (responseText, error) = await ConsumeEventStreamAsync(prompt, ct);

        if (error is not null)
        {
            _logger.LogWarning("Sidecar scoring failed: {Error}", error);
            return scores;
        }

        foreach (var (index, score, category) in ParseRelevanceScores(responseText ?? ""))
        {
            if (index >= 0 && index < items.Count)
                scores[index] = (score, category);
        }

        return scores;
    }

    private static string BuildRelevanceScoringPrompt(
        IReadOnlyList<TrendItem> items, BrandProfile profile)
    {
        var itemLines = string.Join("\n", items.Select((item, i) =>
            $"{i}. {item.Title} - {(item.Description?.Length > 200 ? item.Description[..200] : item.Description ?? "")}"));

        return $$"""
            You are a brand relevance scorer. Score each item's relevance to the brand profile and assign a category.
            Return ONLY valid JSON array, no markdown fencing.

            Brand Profile:
            - Topics: {{string.Join(", ", profile.Topics)}}
            - Persona: {{profile.PersonaDescription}}

            Items to score:
            {{itemLines}}

            Categories (pick the best fit): AI/ML, .NET/C#, Angular/Frontend, Azure/Cloud, Security, Docker/Infra, General Tech

            Expected JSON: [{"index": 0, "score": 0.0, "category": "AI/ML"}, ...]
            Score is 0.0-1.0 where 1.0 is highly relevant.
            Category must be one of the listed categories.
            """;
    }

    private async Task<(string? Text, string? Error)> ConsumeEventStreamAsync(
        string prompt, CancellationToken ct)
    {
        try
        {
            if (!_sidecar.IsConnected)
                await _sidecar.ConnectAsync(ct);

            var textParts = new List<string>();
            await foreach (var evt in _sidecar.SendTaskAsync(prompt, null, null, ct))
            {
                switch (evt)
                {
                    case ChatEvent { Text: not null } chat:
                        textParts.Add(chat.Text);
                        break;
                    case ErrorEvent error:
                        return (null, error.Message);
                }
            }

            return (string.Join("", textParts), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consuming sidecar event stream");
            return (null, ex.Message);
        }
    }

    internal static List<(int Index, float Score, string? Category)> ParseRelevanceScores(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0)
                cleaned = cleaned[(firstNewline + 1)..];
            if (cleaned.EndsWith("```"))
                cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<RelevanceScoreDto>>(cleaned, JsonOptions);
            return items?.Select(i => (i.Index, i.Score, i.Category)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private List<TrendSuggestion> ClusterAndCreateSuggestions(
        IReadOnlyList<TrendItem> items, Dictionary<int, (float Score, string? Category)> scores)
    {
        var scoredItems = items
            .Select((item, index) => (Item: item, Score: scores.GetValueOrDefault(index, (0f, null)).Score))
            .OrderByDescending(x => x.Score)
            .ToList();

        var clusters = new List<(TrendItem Centroid, float MaxScore, List<TrendItem> Members)>();

        foreach (var (item, score) in scoredItems)
        {
            var merged = false;
            foreach (var cluster in clusters)
            {
                if (TrendDeduplicator.ComputeTitleSimilarity(
                        item.Title, cluster.Centroid.Title) >= _options.TitleSimilarityThreshold)
                {
                    cluster.Members.Add(item);
                    merged = true;
                    break;
                }
            }

            if (!merged)
            {
                clusters.Add((item, score, [item]));
            }
        }

        return clusters.Select(cluster =>
                CreateSuggestion(cluster.Centroid, cluster.MaxScore, cluster.Members))
            .ToList();
    }

    private static TrendSuggestion CreateSuggestion(
        TrendItem centroid, float maxScore, List<TrendItem> members)
    {
        var suggestion = new TrendSuggestion
        {
            Topic = centroid.Title,
            Rationale = $"Detected across {members.Count} source(s) with relevance score {maxScore:F2}",
            RelevanceScore = maxScore,
            SuggestedContentType = ContentType.BlogPost,
            SuggestedPlatforms = [PlatformType.LinkedIn],
            Status = TrendSuggestionStatus.Pending,
        };

        foreach (var member in members)
        {
            suggestion.RelatedTrends.Add(new TrendSuggestionItem
            {
                TrendSuggestionId = suggestion.Id,
                TrendItemId = member.Id,
                SimilarityScore = TrendDeduplicator.ComputeTitleSimilarity(
                    centroid.Title, member.Title),
            });
        }

        return suggestion;
    }

    private sealed class RelevanceScoreDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("score")]
        public float Score { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }
}
