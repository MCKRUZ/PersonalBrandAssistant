using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices.TrendPollers;

/// <summary>
/// Accepts browser history via JSON export upload or Chrome extension webhook.
/// Processes visited URLs to extract trend signals.
/// </summary>
public class BrowserHistoryPoller : ITrendSourcePoller
{
    public TrendSourceType SourceType => TrendSourceType.BrowserHistory;

    public Task<List<TrendItem>> PollAsync(TrendSource source, CancellationToken ct)
    {
        // TODO: Implement browser history integration
        // - Accept JSON export (Chrome history format) via webhook
        // - Filter URLs by domain whitelist / content heuristics
        // - Extract page titles and metadata
        // - Map frequently visited topics to TrendItem
        throw new NotImplementedException("Browser history polling is not yet implemented.");
    }
}
