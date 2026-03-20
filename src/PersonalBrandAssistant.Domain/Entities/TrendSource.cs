using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class TrendSource : AuditableEntityBase
{
    public string Name { get; set; } = string.Empty;
    public TrendSourceType Type { get; set; }
    public string? ApiUrl { get; set; }
    public string? FeedUrl { get; set; }
    public string? Category { get; set; }
    public int PollIntervalMinutes { get; set; }
    public bool IsEnabled { get; set; } = true;
}
