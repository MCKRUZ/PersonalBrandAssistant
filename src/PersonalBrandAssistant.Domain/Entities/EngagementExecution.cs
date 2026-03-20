using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class EngagementExecution : AuditableEntityBase
{
    public Guid EngagementTaskId { get; set; }
    public EngagementTask EngagementTask { get; set; } = null!;
    public DateTimeOffset ExecutedAt { get; set; }
    public int ActionsAttempted { get; set; }
    public int ActionsSucceeded { get; set; }
    public string? ErrorMessage { get; set; }
    public ICollection<EngagementAction> Actions { get; } = new List<EngagementAction>();
}
