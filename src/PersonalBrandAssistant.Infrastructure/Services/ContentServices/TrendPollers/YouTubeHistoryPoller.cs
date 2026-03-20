using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

/// <summary>
/// Polls YouTube Data API v3 activities endpoint for watch history.
/// Requires OAuth2 token with youtube.readonly scope.
/// </summary>
public class YouTubeHistoryPoller : ITrendSourcePoller
{
    public TrendSourceType SourceType => TrendSourceType.YouTube;

    public Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        // TODO: Implement YouTube Data API v3 integration
        // - Use activities.list endpoint with mine=true
        // - Filter for watch events
        // - Map video metadata (title, description, tags) to TrendItem
        throw new NotImplementedException("YouTube history polling is not yet implemented.");
    }
}
