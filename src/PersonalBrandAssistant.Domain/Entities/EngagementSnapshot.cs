using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class EngagementSnapshot : AuditableEntityBase
{
    public Guid ContentPlatformStatusId { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int Shares { get; set; }
    public int? Impressions { get; set; }
    public int? Clicks { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
