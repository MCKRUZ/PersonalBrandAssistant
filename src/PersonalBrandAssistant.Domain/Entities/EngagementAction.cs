using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class EngagementAction : AuditableEntityBase
{
    public Guid EngagementExecutionId { get; set; }
    public EngagementExecution EngagementExecution { get; set; } = null!;
    public EngagementTaskType ActionType { get; set; }
    public string TargetUrl { get; set; } = "";
    public string? GeneratedContent { get; set; }
    public string? PlatformPostId { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
}
