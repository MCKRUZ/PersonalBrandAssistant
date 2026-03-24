using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ITrendMonitor
{
    Task<Result<IReadOnlyList<TrendSuggestion>>> GetSuggestionsAsync(int limit, CancellationToken ct);
    Task<Result<MediatR.Unit>> DismissSuggestionAsync(Guid suggestionId, CancellationToken ct);
    Task<Result<Guid>> AcceptSuggestionAsync(Guid suggestionId, CancellationToken ct, ContentType? contentTypeOverride = null);
    Task<Result<MediatR.Unit>> RefreshTrendsAsync(CancellationToken ct);
}
