using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class TrendItem : AuditableEntityBase
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public TrendSourceType SourceType { get; set; }
    public Guid? TrendSourceId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? DeduplicationKey { get; set; }
    public string? Category { get; set; }
    public string? Summary { get; set; }
}
