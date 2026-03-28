using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISubstackContentMatcher
{
    Task<ContentMatchResult> MatchAsync(SubstackRssEntry entry, CancellationToken ct);
}

public record ContentMatchResult(Guid? ContentId, MatchConfidence Confidence, string MatchReason);
