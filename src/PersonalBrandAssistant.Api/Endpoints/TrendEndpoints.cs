using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class TrendEndpoints
{
    public static void MapTrendEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trends").WithTags("Trends");

        group.MapGet("/suggestions", GetSuggestions);
        group.MapPost("/suggestions/{id:guid}/accept", AcceptSuggestion);
        group.MapPost("/suggestions/{id:guid}/dismiss", DismissSuggestion);
        group.MapPost("/refresh", RefreshTrends);

        group.MapGet("/sources", GetSources);
        group.MapPost("/sources", CreateSource);
        group.MapDelete("/sources/{id:guid}", DeleteSource);
        group.MapPatch("/sources/{id:guid}/toggle", ToggleSource);

        group.MapGet("/keywords", GetKeywords);
        group.MapPost("/keywords", AddKeyword);
        group.MapDelete("/keywords/{id:guid}", RemoveKeyword);

        group.MapPost("/items/{id:guid}/analyze", AnalyzeItem);

        group.MapGet("/saved", GetSavedItems);
        group.MapPost("/saved", SaveItem);
        group.MapDelete("/saved/{id:guid}", RemoveSavedItem);

        group.MapGet("/settings", GetSettings);
        group.MapPut("/settings", UpdateSettings);
    }

    private static async Task<IResult> GetSuggestions(
        ITrendMonitor monitor,
        IApplicationDbContext db,
        int limit = 20,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 1000);
        var result = await monitor.GetSuggestionsAsync(clampedLimit, ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var suggestions = result.Value!;

        // Batch-query source categories for all related trend items
        var sourceIds = suggestions
            .SelectMany(s => s.RelatedTrends)
            .Where(rt => rt.TrendItem?.TrendSourceId is not null)
            .Select(rt => rt.TrendItem!.TrendSourceId!.Value)
            .Distinct()
            .ToList();

        var categoryMap = sourceIds.Count > 0
            ? await db.TrendSources
                .Where(ts => sourceIds.Contains(ts.Id))
                .ToDictionaryAsync(ts => ts.Id, ts => ts.Category, ct)
            : new Dictionary<Guid, string?>();

        var projected = suggestions.Select(s => new
        {
            s.Id,
            s.Topic,
            s.Rationale,
            s.RelevanceScore,
            SuggestedContentType = s.SuggestedContentType.ToString(),
            SuggestedPlatforms = s.SuggestedPlatforms.Select(p => p.ToString()),
            s.Status,
            s.CreatedAt,
            s.UpdatedAt,
            RelatedTrends = s.RelatedTrends.Select(rt => new
            {
                Source = rt.TrendItem?.SourceType.ToString() ?? "Unknown",
                Title = rt.TrendItem?.Title ?? s.Topic,
                Description = rt.TrendItem?.Description,
                Url = rt.TrendItem?.Url,
                SourceName = rt.TrendItem?.SourceName,
                ThumbnailUrl = rt.TrendItem?.ThumbnailUrl,
                SourceCategory = rt.TrendItem?.Category
                    ?? (rt.TrendItem?.TrendSourceId is not null
                        && categoryMap.TryGetValue(rt.TrendItem.TrendSourceId.Value, out var cat)
                        ? cat
                        : null),
                Score = rt.SimilarityScore,
                TrendItemId = rt.TrendItemId,
                Summary = rt.TrendItem?.Summary,
            }),
        });

        return Results.Ok(projected);
    }

    private static async Task<IResult> AcceptSuggestion(
        ITrendMonitor monitor,
        Guid id,
        CancellationToken ct)
    {
        var result = await monitor.AcceptSuggestionAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DismissSuggestion(
        ITrendMonitor monitor,
        Guid id,
        CancellationToken ct)
    {
        var result = await monitor.DismissSuggestionAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RefreshTrends(
        ITrendMonitor monitor,
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var autonomy = await db.AutonomyConfigurations.FirstOrDefaultAsync(ct)
                       ?? AutonomyConfiguration.CreateDefault();

        if (autonomy.GlobalLevel == AutonomyLevel.Manual)
            return Results.Problem(statusCode: 403, detail: "Trend refresh requires SemiAuto or higher autonomy level.");

        var result = await monitor.RefreshTrendsAsync(ct);
        if (!result.IsSuccess)
            return result.ToHttpResult();
        return Results.Accepted();
    }

    private static async Task<IResult> AnalyzeItem(
        IArticleAnalyzer analyzer,
        Guid id,
        CancellationToken ct)
    {
        var result = await analyzer.AnalyzeAsync(id, ct);
        return result.ToHttpResult();
    }

    // --- Sources ---

    private static async Task<IResult> GetSources(
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var sources = await db.TrendSources
            .Select(s => new
            {
                s.Id,
                s.Name,
                Type = s.Type.ToString(),
                s.IsEnabled,
                s.FeedUrl,
                s.Category,
                ItemCount = db.TrendItems.Count(i => i.TrendSourceId == s.Id),
                LastSync = db.TrendItems
                    .Where(i => i.TrendSourceId == s.Id)
                    .OrderByDescending(i => i.DetectedAt)
                    .Select(i => (DateTimeOffset?)i.DetectedAt)
                    .FirstOrDefault(),
                s.LastPolledAt,
                s.LastSuccessAt,
                s.LastError,
                s.ConsecutiveFailures,
                ComingSoon = false,
            })
            .ToListAsync(ct);

        return Results.Ok(sources);
    }

    private static async Task<IResult> ToggleSource(
        IApplicationDbContext db,
        Guid id,
        CancellationToken ct)
    {
        var source = await db.TrendSources.FindAsync([id], ct);
        if (source is null)
            return Results.NotFound();

        source.IsEnabled = !source.IsEnabled;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateSource(
        IApplicationDbContext db,
        CreateSourceRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest("Name is required.");

        if (string.IsNullOrWhiteSpace(request.FeedUrl) ||
            !Uri.TryCreate(request.FeedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return Results.BadRequest("A valid HTTP/HTTPS feed URL is required.");

        var duplicate = await db.TrendSources
            .AnyAsync(s => s.FeedUrl == request.FeedUrl.Trim(), ct);
        if (duplicate)
            return Results.Conflict("A source with this feed URL already exists.");

        var source = new TrendSource
        {
            Name = request.Name.Trim(),
            Type = TrendSourceType.RssFeed,
            FeedUrl = request.FeedUrl.Trim(),
            Category = request.Category?.Trim(),
            PollIntervalMinutes = 60,
            IsEnabled = true,
        };

        db.TrendSources.Add(source);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/trends/sources/{source.Id}", new
        {
            source.Id,
            source.Name,
            Type = source.Type.ToString(),
            source.IsEnabled,
            source.FeedUrl,
            source.Category,
            ItemCount = 0,
            LastSync = (DateTimeOffset?)null,
            ComingSoon = false,
        });
    }

    private static async Task<IResult> DeleteSource(
        IApplicationDbContext db,
        Guid id,
        CancellationToken ct)
    {
        var source = await db.TrendSources.FindAsync([id], ct);
        if (source is null)
            return Results.NotFound();

        db.TrendSources.Remove(source);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // --- Keywords ---

    private static async Task<IResult> GetKeywords(
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var keywords = await db.InterestKeywords
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
        return Results.Ok(keywords);
    }

    private static async Task<IResult> AddKeyword(
        IApplicationDbContext db,
        AddKeywordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return Results.BadRequest("Keyword is required.");

        var existing = await db.InterestKeywords
            .AnyAsync(k => k.Keyword == request.Keyword.Trim(), ct);
        if (existing)
            return Results.Conflict("Keyword already exists.");

        var keyword = new InterestKeyword
        {
            Keyword = request.Keyword.Trim(),
            Weight = request.Weight ?? 1.0,
        };

        db.InterestKeywords.Add(keyword);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/trends/keywords/{keyword.Id}", keyword);
    }

    private static async Task<IResult> RemoveKeyword(
        IApplicationDbContext db,
        Guid id,
        CancellationToken ct)
    {
        var keyword = await db.InterestKeywords.FindAsync([id], ct);
        if (keyword is null)
            return Results.NotFound();

        db.InterestKeywords.Remove(keyword);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // --- Settings ---

    private static async Task<IResult> GetSettings(
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var settings = await db.TrendSettings.FirstOrDefaultAsync(ct)
                       ?? TrendSettings.CreateDefault();

        return Results.Ok(new
        {
            settings.RelevanceFilterEnabled,
            settings.RelevanceScoreThreshold,
            settings.MaxSuggestionsPerCycle,
        });
    }

    private static async Task<IResult> UpdateSettings(
        IApplicationDbContext db,
        UpdateTrendSettingsRequest request,
        CancellationToken ct)
    {
        if (request.RelevanceScoreThreshold < 0f || request.RelevanceScoreThreshold > 1f)
            return Results.BadRequest("RelevanceScoreThreshold must be between 0.0 and 1.0.");

        if (request.MaxSuggestionsPerCycle < 1 || request.MaxSuggestionsPerCycle > 1000)
            return Results.BadRequest("MaxSuggestionsPerCycle must be between 1 and 1000.");

        var settings = await db.TrendSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = TrendSettings.CreateDefault();
            db.TrendSettings.Add(settings);
        }

        settings.RelevanceFilterEnabled = request.RelevanceFilterEnabled;
        settings.RelevanceScoreThreshold = request.RelevanceScoreThreshold;
        settings.MaxSuggestionsPerCycle = request.MaxSuggestionsPerCycle;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // --- Saved Items ---

    private static async Task<IResult> GetSavedItems(
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var items = await db.SavedTrendItems
            .Include(s => s.TrendItem)
            .OrderByDescending(s => s.SavedAt)
            .Select(s => new
            {
                s.Id,
                s.TrendItemId,
                Title = s.TrendItem!.Title,
                Url = s.TrendItem.Url,
                Source = s.TrendItem.SourceType.ToString(),
                s.SavedAt,
                s.Notes,
            })
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> SaveItem(
        IApplicationDbContext db,
        SaveItemRequest request,
        CancellationToken ct)
    {
        var trendItem = await db.TrendItems.FindAsync([request.TrendItemId], ct);
        if (trendItem is null)
            return Results.NotFound("Trend item not found.");

        var existing = await db.SavedTrendItems
            .AnyAsync(s => s.TrendItemId == request.TrendItemId, ct);
        if (existing)
            return Results.Conflict("Item already saved.");

        var saved = new SavedTrendItem
        {
            TrendItemId = request.TrendItemId,
        };

        db.SavedTrendItems.Add(saved);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/trends/saved/{saved.Id}", new
        {
            saved.Id,
            saved.TrendItemId,
            trendItem.Title,
            trendItem.Url,
            Source = trendItem.SourceType.ToString(),
            saved.SavedAt,
            saved.Notes,
        });
    }

    private static async Task<IResult> RemoveSavedItem(
        IApplicationDbContext db,
        Guid id,
        CancellationToken ct)
    {
        var item = await db.SavedTrendItems.FindAsync([id], ct);
        if (item is null)
            return Results.NotFound();

        db.SavedTrendItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

public record CreateSourceRequest(string Name, string FeedUrl, string? Category);
public record AddKeywordRequest(string Keyword, double? Weight);
public record SaveItemRequest(Guid TrendItemId);
public record UpdateTrendSettingsRequest(bool RelevanceFilterEnabled, float RelevanceScoreThreshold, int MaxSuggestionsPerCycle);
