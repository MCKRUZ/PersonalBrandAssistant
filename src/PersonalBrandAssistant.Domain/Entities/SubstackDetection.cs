using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class SubstackDetection : EntityBase
{
    public Guid? ContentId { get; set; }
    public string RssGuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SubstackUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public MatchConfidence Confidence { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}
